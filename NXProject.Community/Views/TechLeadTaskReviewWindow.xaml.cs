using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using NXProject.Models;
using NXProject.Services;

namespace NXProject.Views
{
    public partial class TechLeadTaskReviewWindow : Window
    {
        private readonly Project _project;
        private readonly List<ProjectTask> _stories;
        private readonly List<string> _activityList;
        private readonly ObservableCollection<TaskReviewRow> _allRows = [];
        private ICollectionView? _view;
        private static readonly List<string> KnownStates = ["New", "Active", "Resolved", "Closed", "Blocked"];

        // Drag-drop
        private Point _dragStart;
        private TaskReviewRow? _dragRow;
        private bool _isDragging;

        public bool HasChanges { get; private set; }
        public List<string> ActivityList => _activityList;
        public List<string> ResourceList => _project.Resources.Select(r => r.DisplayName ?? r.Name ?? "").Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        /// <summary>Callback: adiciona rows ao cronograma e retorna a primeira ProjectTask adicionada (para seleção).</summary>
        public Func<IEnumerable<TaskReviewRow>, ProjectTask?>? AddToScheduleCallback { get; set; }
        public Action? ReleaseCallback { get; set; }

        public TechLeadTaskReviewWindow(Project project, List<ProjectTask> stories, List<string>? activityList = null)
        {
            _project = project;
            _activityList = activityList ?? ["Deployment", "Design", "Development", "Documentation", "Requirements", "Testing"];
            _stories = stories;
            InitializeComponent();
            Loaded += async (_, _) => await LoadAsync();
            Loaded += (_, _) =>
            {
                ReleaseButton.Visibility     = ReleaseCallback       != null ? Visibility.Visible : Visibility.Collapsed;
                ExpandAllButton.Visibility   = AddToScheduleCallback != null ? Visibility.Visible : Visibility.Collapsed;
                AddSelectedButton.Visibility = AddToScheduleCallback != null ? Visibility.Visible : Visibility.Collapsed;
            };
        }

        private async Task LoadAsync()
        {
            StatusText.Text = "Buscando Tasks no DevOps...";
            AddSelectedButton.IsEnabled = false;
            SaveChangesButton.IsEnabled = false;

            var options = TfsConnectionStore.Load("NXProject.Community");
            var rows = new List<TaskReviewRow>();

            var inScheduleIds = _stories
                .SelectMany(s => s.Children)
                .Where(c => string.Equals(c.TfsType, "Task", StringComparison.OrdinalIgnoreCase) && c.TfsId.HasValue)
                .Select(c => c.TfsId!.Value)
                .ToHashSet();

            int fetched = 0;
            foreach (var story in _stories)
            {
                if (story.TfsId is not > 0) continue;
                var tasks = await TfsImportService.FetchChildTasksFromDevOpsAsync(options, story.TfsId!.Value);
                if (tasks == null) continue;
                fetched++;
                foreach (var t in tasks)
                {
                    var row = new TaskReviewRow
                    {
                        StoryId         = story.TfsId!.Value,
                        StoryName       = story.Name,
                        StoryTask       = story,
                        TaskId          = t.TfsId,
                        Title           = t.Title,
                        State           = t.State ?? "New",
                        EstimatedHours  = t.EstimatedHours,
                        CompletedHours  = t.CompletedHours,
                        PercentComplete = t.PercentComplete,
                        Priority        = t.Priority,
                        AssignedTo        = t.AssignedTo ?? "",
                        AssignedToDisplay = t.AssignedToDisplay ?? t.AssignedTo ?? "",
                        Activity          = t.Activity ?? "",
                        InSchedule        = inScheduleIds.Contains(t.TfsId),
                    };
                    row.PropertyChanged += OnRowPropertyChanged;
                    rows.Add(row);
                }
            }

            _allRows.Clear();
            foreach (var r in rows) _allRows.Add(r);

            var storyNames = new[] { "(Todas)" }.Concat(rows.Select(r => r.StoryName).Distinct().OrderBy(s => s)).ToList();
            StoryFilterBox.ItemsSource = storyNames;
            StoryFilterBox.SelectedIndex = 0;

            var states = new[] { "(Todos)" }.Concat(rows.Select(r => r.State).Distinct().OrderBy(s => s)).ToList();
            StateFilterBox.ItemsSource = states;
            StateFilterBox.SelectedIndex = 0;

            _view = CollectionViewSource.GetDefaultView(_allRows);
            _view.Filter = ApplyFilter;
            _view.SortDescriptions.Clear();
            _view.SortDescriptions.Add(new SortDescription(nameof(TaskReviewRow.Priority), ListSortDirection.Ascending));
            TasksGrid.ItemsSource = _view;
            RefreshRowNumbers();

            // Preenche duração da story (primeira story selecionada)
            if (_stories.Count > 0)
            {
                var s = _stories[0];
                double h = NXProject.Services.ProjectCalendarService.CountWorkingHours(s.Start, s.Finish);
                StoryDurationBox.Text = h.ToString("0.#");
            }

            UpdateTotals();
            StatusText.Text = $"{fetched} Stories consultadas — {rows.Count} Tasks encontradas no DevOps.";
        }

        private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TaskReviewRow.IsDirty)) return;
            if (sender is TaskReviewRow row && e.PropertyName != nameof(TaskReviewRow.IsSelected)
                                             && e.PropertyName != nameof(TaskReviewRow.InSchedule))
            {
                row.IsDirty = true;
                SaveChangesButton.IsEnabled = _allRows.Any(r => r.IsDirty);
                DirtyHint.Visibility = SaveChangesButton.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
            }
            UpdateTotals();
        }

        private bool ApplyFilter(object obj)
        {
            if (obj is not TaskReviewRow r) return true;
            var storyFilter = StoryFilterBox.SelectedItem as string;
            var stateFilter = StateFilterBox.SelectedItem as string;
            if (!string.IsNullOrEmpty(storyFilter) && storyFilter != "(Todas)" && r.StoryName != storyFilter) return false;
            if (!string.IsNullOrEmpty(stateFilter) && stateFilter != "(Todos)" && r.State != stateFilter) return false;
            return true;
        }

        private void UpdateTotals()
        {
            var visible = _view?.Cast<TaskReviewRow>().ToList() ?? [.. _allRows];
            double totalH = visible.Sum(r => r.EstimatedHours);
            double doneH  = visible.Sum(r => r.CompletedHours);
            int inSched   = visible.Count(r => r.InSchedule);
            int dirty     = _allRows.Count(r => r.IsDirty);
            TotalsText.Text = $"Visível: {visible.Count} Tasks | Est: {totalH:0.#}h | Conc: {doneH:0.#}h | {inSched} no cronograma" +
                              (dirty > 0 ? $" | {dirty} pendentes de sync" : "");
        }

        private void RefreshRowNumbers()
        {
            var visible = (_view?.Cast<TaskReviewRow>() ?? _allRows).ToList();
            for (int i = 0; i < visible.Count; i++)
                visible[i].RowNumber = i + 1;
        }

        private void OnStoryFilterChanged(object sender, SelectionChangedEventArgs e) { _view?.Refresh(); RefreshRowNumbers(); UpdateTotals(); }
        private void OnStateFilterChanged(object sender, SelectionChangedEventArgs e) { _view?.Refresh(); RefreshRowNumbers(); UpdateTotals(); }

        private void OnTasksGridSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateTotals();
            AddSelectedButton.IsEnabled = _allRows.Any(r => r.IsSelected && !r.InSchedule);

            // Atualiza breadcrumb com a story da linha selecionada
            var row = TasksGrid.SelectedItem as TaskReviewRow;
            if (row == null) { BreadcrumbPanel.Visibility = Visibility.Collapsed; return; }

            BreadcrumbPanel.Visibility = Visibility.Visible;
            var story = row.StoryTask;
            var feature = story.Parent;
            var epic = feature?.Parent;

            EpicBreadcrumb.Text    = epic    != null ? $"{epic.Name} › "    : "";
            FeatureBreadcrumb.Text = feature != null ? $"{feature.Name} › " : "";
            StoryBreadcrumb.Text   = $"{story.Name}";
            TaskBreadcrumb.Text    = $" › {row.Title}";
        }

        private void OnStateComboLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox cb)
                cb.ItemsSource = KnownStates;
        }

        private void OnActivityComboLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox cb)
                cb.ItemsSource = _activityList;
        }

        private void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            // Marca dirty quando confirma a edição
            if (e.EditAction == DataGridEditAction.Commit && e.Row.Item is TaskReviewRow row)
            {
                row.IsDirty = true;
                SaveChangesButton.IsEnabled = true;
                DirtyHint.Visibility = Visibility.Visible;
            }
        }

        private async void OnSaveChangesClick(object sender, RoutedEventArgs e)
        {
            var dirty = _allRows.Where(r => r.IsDirty).ToList();
            if (dirty.Count == 0) return;

            SaveChangesButton.IsEnabled = false;
            StatusText.Text = $"Sincronizando {dirty.Count} Task(s) com o DevOps...";

            var options = TfsConnectionStore.Load("NXProject.Community");
            int ok = 0, fail = 0;

            foreach (var row in dirty)
            {
                try
                {
                    await TfsImportService.UpdateTaskFieldsAsync(options, row.TaskId,
                        estimatedHours: row.EstimatedHours,
                        completedHours: row.CompletedHours,
                        priority: row.Priority,
                        assignedTo: row.AssignedTo,
                        state: row.State,
                        title: row.Title,
                        activity: row.Activity);
                    row.IsDirty = false;
                    ok++;
                }
                catch
                {
                    fail++;
                }
            }

            HasChanges = true;
            DirtyHint.Visibility = _allRows.Any(r => r.IsDirty) ? Visibility.Visible : Visibility.Collapsed;
            SaveChangesButton.IsEnabled = _allRows.Any(r => r.IsDirty);
            StatusText.Text = $"Sync concluído: {ok} OK" + (fail > 0 ? $", {fail} com erro" : "");
            UpdateTotals();
        }

        private async void OnReloadClick(object sender, RoutedEventArgs e)
        {
            _allRows.Clear();
            await LoadAsync();
        }

        private void OnAddSelectedClick(object sender, RoutedEventArgs e)
        {
            var toAdd = _allRows.Where(r => r.IsSelected && !r.InSchedule).ToList();
            if (toAdd.Count == 0) return;
            AddToScheduleAndClose(toAdd);
        }

        private void OnExpandAllClick(object sender, RoutedEventArgs e)
        {
            var toAdd = (_view?.Cast<TaskReviewRow>() ?? _allRows).Where(r => !r.InSchedule).ToList();
            if (toAdd.Count == 0) { MessageBox.Show("Todas as Tasks já estão no cronograma.", "Info", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            AddToScheduleAndClose(toAdd);
        }

        private void AddToScheduleAndClose(List<TaskReviewRow> toAdd)
        {
            AddToScheduleCallback?.Invoke(toAdd);
            HasChanges = true;
            Close();
        }

        private void OnReleaseClick(object sender, RoutedEventArgs e)
        {
            ReleaseCallback?.Invoke();
            HasChanges = true;
            Close();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        private void OnResourceSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb && cb.SelectedItem is string name &&
                cb.DataContext is TaskReviewRow row)
            {
                row.AssignedToDisplay = name;
                row.IsDirty = true;
            }
        }

        private void OnGridContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var dataRow = (e.OriginalSource as FrameworkElement)?.DataContext as TaskReviewRow
                       ?? TasksGrid.SelectedItem as TaskReviewRow;
            if (dataRow == null) return;
            var uiRow = TasksGrid.ItemContainerGenerator.ContainerFromItem(dataRow) as DataGridRow;
            var menu  = uiRow?.ContextMenu;
            if (menu == null) return;
            var item = menu.Items.OfType<MenuItem>()
                .FirstOrDefault(m => m.Tag as string == "BlockRowMenuItem");
            if (item != null)
                item.Header = dataRow.IsBlockedState ? "✅ Retirar Block da Task" : "🔴 Adicionar Block na Task";
        }

        private void OnRowToggleBlockClick(object sender, RoutedEventArgs e)
        {
            var row = ((sender as MenuItem)?.Parent as ContextMenu)
                ?.PlacementTarget is FrameworkElement fe
                ? fe.DataContext as TaskReviewRow
                : TasksGrid.SelectedItem as TaskReviewRow;
            if (row == null) return;
            row.ToggleBlock();
            SaveChangesButton.IsEnabled = true;
            DirtyHint.Visibility = Visibility.Visible;
            UpdateTotals();
        }

        private void OnToggleBlockClick(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is TaskReviewRow row)
            {
                row.ToggleBlock();
                SaveChangesButton.IsEnabled = true;
                DirtyHint.Visibility = Visibility.Visible;
                UpdateTotals();
            }
        }

        private void OnRatearClick(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(StoryDurationBox.Text.Replace(",", "."),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double storyHours) || storyHours <= 0)
            {
                MessageBox.Show("Informe a duração da Story em horas (ex: 40).", "Rateio", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var allVisible = (_view?.Cast<TaskReviewRow>() ?? _allRows).ToList();

            // Eligible: tasks sem HH Original E sem HH Atual
            var eligible = allVisible.Where(r => r.EstimatedHours <= 0 && r.CompletedHours <= 0).ToList();
            if (eligible.Count == 0)
            {
                MessageBox.Show("Todas as Tasks já possuem HH Original ou HH Atual.", "Rateio", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Usa HH Original e HH Atual da story (via StoryTask) para ratear separadamente
            var storyTask    = allVisible.FirstOrDefault()?.StoryTask;
            double storyOrig = storyTask?.OriginalEstimatedHours ?? 0;
            double storyCur  = storyTask?.CurrentHours ?? 0;
            int n = eligible.Count;

            // Rateio de HH Original
            double usedOrig   = allVisible.Where(r => r.EstimatedHours > 0).Sum(r => r.EstimatedHours);
            double remainOrig = storyOrig > 0 ? Math.Max(0, storyOrig - usedOrig) : 0;
            double perOrig    = remainOrig > 0 ? remainOrig / n
                              : storyOrig > 0 ? storyOrig / Math.Max(1, allVisible.Count)
                              : storyHours / Math.Max(1, allVisible.Count);

            // Rateio de HH Atual (só se story tem HH Atual)
            double usedCur   = allVisible.Where(r => r.CompletedHours > 0).Sum(r => r.CompletedHours);
            double remainCur = storyCur > 0 ? Math.Max(0, storyCur - usedCur) : 0;
            double perCur    = remainCur > 0 ? remainCur / n
                             : storyCur > 0 ? storyCur / Math.Max(1, allVisible.Count)
                             : 0;

            foreach (var r in eligible)
            {
                r.EstimatedHours = Math.Round(perOrig, 1);
                if (perCur > 0)
                    r.CompletedHours = Math.Round(perCur, 1);
                r.IsDirty = true;
            }

            SaveChangesButton.IsEnabled = true;
            DirtyHint.Visibility = Visibility.Visible;
            UpdateTotals();

            var msg = perCur > 0
                ? $"Rateio aplicado em {n} task(s): HH Original = {perOrig:0.#}h | HH Atual = {perCur:0.#}h"
                : $"Rateio aplicado em {n} task(s): HH Original = {perOrig:0.#}h";
            MessageBox.Show(msg, "Rateio", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ── Drag-drop para reordenar por prioridade ──────────────────────────────

        private void OnGridMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(null);
            _dragRow = GetRowUnderMouse(e);
            _isDragging = false;
        }

        private void OnGridMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragRow == null || _isDragging) return;
            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _isDragging = true;
            DragDrop.DoDragDrop(TasksGrid, _dragRow, DragDropEffects.Move);
        }

        private void OnGridDrop(object sender, DragEventArgs e)
        {
            _isDragging = false;
            if (_dragRow == null) return;

            // Commitar qualquer edição pendente antes de mexer na coleção/sort
            TasksGrid.CommitEdit(DataGridEditingUnit.Row, true);
            TasksGrid.CommitEdit(DataGridEditingUnit.Cell, true);

            var target = GetRowUnderMouse(e);
            if (target == null || ReferenceEquals(target, _dragRow)) { _dragRow = null; return; }

            // Snapshot da ordem visível atual
            var visible = (_view?.Cast<TaskReviewRow>() ?? _allRows).ToList();
            int fromIdx = visible.IndexOf(_dragRow);
            int toIdx   = visible.IndexOf(target);
            if (fromIdx < 0 || toIdx < 0) { _dragRow = null; return; }

            visible.RemoveAt(fromIdx);
            visible.Insert(toIdx, _dragRow);

            // Reatribuir prioridades sequenciais
            for (int i = 0; i < visible.Count; i++)
            {
                int newPri = i + 1;
                if (visible[i].Priority != newPri)
                {
                    visible[i].Priority = newPri;
                    visible[i].IsDirty  = true;
                }
            }

            // Remover sort automático (deve ser feito DEPOIS do CommitEdit)
            _view?.SortDescriptions.Clear();

            // Reordenar _allRows usando Move para evitar reset completo da coleção
            for (int i = 0; i < visible.Count; i++)
            {
                int cur = _allRows.IndexOf(visible[i]);
                if (cur != i) _allRows.Move(cur, i);
            }

            RefreshRowNumbers();
            SaveChangesButton.IsEnabled = _allRows.Any(r => r.IsDirty);
            DirtyHint.Visibility = SaveChangesButton.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
            UpdateTotals();
            _dragRow = null;
        }

        private TaskReviewRow? GetRowUnderMouse(RoutedEventArgs e)
        {
            var el = e.OriginalSource as DependencyObject;
            while (el != null && el is not DataGridRow) el = System.Windows.Media.VisualTreeHelper.GetParent(el);
            return (el as DataGridRow)?.Item as TaskReviewRow;
        }
    }

    public class TaskReviewRow : INotifyPropertyChanged
    {
        public int StoryId { get; set; }
        public string StoryName { get; set; } = "";
        public ProjectTask StoryTask { get; set; } = null!;
        public int TaskId { get; set; }

        private int _rowNumber;
        public int RowNumber { get => _rowNumber; set { if (_rowNumber == value) return; _rowNumber = value; OnPropertyChanged(); } }

        private string _title = "";
        public string Title { get => _title; set { if (_title == value) return; _title = value; OnPropertyChanged(); } }

        private string _state = "New";
        public string State
        {
            get => _state;
            set
            {
                if (_state == value) return;
                _state = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsBlockedState));
                OnPropertyChanged(nameof(BlockButtonLabel));
                OnPropertyChanged(nameof(BlockButtonColor));
            }
        }

        private double _estimatedHours;
        public double EstimatedHours { get => _estimatedHours; set { if (_estimatedHours == value) return; _estimatedHours = value; OnPropertyChanged(); OnPropertyChanged(nameof(EstimatedHoursDisplay)); } }

        private double _completedHours;
        public double CompletedHours { get => _completedHours; set { if (_completedHours == value) return; _completedHours = value; OnPropertyChanged(); } }
        public double PercentComplete { get; set; }

        private int _priority = 5;
        public int Priority { get => _priority; set { if (_priority == value) return; _priority = value; OnPropertyChanged(); } }

        private string _activity = "";
        public string Activity { get => _activity; set { if (_activity == value) return; _activity = value; OnPropertyChanged(); } }

        private string _assignedTo = "";
        public string AssignedTo { get => _assignedTo; set { if (_assignedTo == value) return; _assignedTo = value; OnPropertyChanged(); } }

        // displayName para exibição na grid; editável (sincroniza no AssignedTo se igual ao email)
        private string _assignedToDisplay = "";
        public string AssignedToDisplay { get => _assignedToDisplay; set { if (_assignedToDisplay == value) return; _assignedToDisplay = value; OnPropertyChanged(); } }

        private bool _inSchedule;
        public bool InSchedule
        {
            get => _inSchedule;
            set { _inSchedule = value; OnPropertyChanged(); OnPropertyChanged(nameof(InScheduleDisplay)); }
        }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }

        private bool _isDirty;
        public bool IsDirty { get => _isDirty; set { _isDirty = value; OnPropertyChanged(); } }

        public string EstimatedHoursDisplay => EstimatedHours > 0 ? $"{EstimatedHours:0.#}h" : "-";
        public string InScheduleDisplay => InSchedule ? "✔ Sim" : "Não";

        public bool IsBlockedState => string.Equals(State, "Blocked", StringComparison.OrdinalIgnoreCase);
        public string BlockButtonLabel => IsBlockedState ? "⛔ Block" : "Block";
        public string BlockButtonColor => IsBlockedState ? "#C0392B" : "#AAA";

        public void ToggleBlock()
        {
            State    = IsBlockedState ? "Active" : "Blocked";
            IsDirty  = true;
            OnPropertyChanged(nameof(IsBlockedState));
            OnPropertyChanged(nameof(BlockButtonLabel));
            OnPropertyChanged(nameof(BlockButtonColor));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new(p));
    }
}
