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
using NXProject.Models;
using NXProject.Services;

namespace NXProject.Views
{
    public partial class TechLeadTaskReviewWindow : Window
    {
        private readonly Project _project;
        private readonly List<ProjectTask> _stories;
        private readonly ObservableCollection<TaskReviewRow> _allRows = [];
        private ICollectionView? _view;
        private static readonly List<string> KnownStates = ["New", "Active", "Resolved", "Closed", "Blocked"];

        public bool HasChanges { get; private set; }
        public Action<IEnumerable<TaskReviewRow>>? AddToScheduleCallback { get; set; }
        public Action? ReleaseCallback { get; set; }

        public TechLeadTaskReviewWindow(Project project, List<ProjectTask> stories)
        {
            _project = project;
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
            TasksGrid.ItemsSource = _view;

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

        private void OnStoryFilterChanged(object sender, SelectionChangedEventArgs e) { _view?.Refresh(); UpdateTotals(); }
        private void OnStateFilterChanged(object sender, SelectionChangedEventArgs e) { _view?.Refresh(); UpdateTotals(); }

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
                        title: row.Title);
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

            if (AddToScheduleCallback != null)
            {
                AddToScheduleCallback.Invoke(toAdd);
                foreach (var r in toAdd) { r.InSchedule = true; r.IsSelected = false; }
                HasChanges = true;
                UpdateTotals();
                MessageBox.Show($"{toAdd.Count} Task(s) adicionadas ao cronograma.", "Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var projectResources = _project.Resources;
            foreach (var r in toAdd)
            {
                var pt = new ProjectTask
                {
                    Name             = r.Title,
                    TfsId            = r.TaskId,
                    TfsType          = "Task",
                    EstimatedHours   = r.EstimatedHours > 0 ? r.EstimatedHours : null,
                    CurrentHours     = r.CompletedHours > 0 ? r.CompletedHours : null,
                    PercentComplete  = r.PercentComplete,
                    Priority         = r.Priority > 0 ? r.Priority : 5,
                    TfsState         = r.State,
                    TfsIterationPath = r.StoryTask.TfsIterationPath,
                    SprintNumber     = r.StoryTask.SprintNumber,
                    Start            = r.StoryTask.Start,
                    Finish           = r.StoryTask.Finish,
                };
                if (!string.IsNullOrWhiteSpace(r.AssignedTo))
                {
                    var res = projectResources.FirstOrDefault(x =>
                        string.Equals(x.Email, r.AssignedTo, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(x.Name,  r.AssignedTo, StringComparison.OrdinalIgnoreCase));
                    if (res != null)
                        pt.Resources.Add(new TaskResource { ResourceId = res.Id, Resource = res, AllocationPercent = 100 });
                }
                r.StoryTask.Children.Add(pt);
                r.InSchedule = true;
                r.IsSelected = false;
            }

            HasChanges = true;
            UpdateTotals();
            MessageBox.Show($"{toAdd.Count} Task(s) adicionadas ao cronograma.", "Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnExpandAllClick(object sender, RoutedEventArgs e)
        {
            var toAdd = (_view?.Cast<TaskReviewRow>() ?? _allRows).Where(r => !r.InSchedule).ToList();
            if (toAdd.Count == 0) { MessageBox.Show("Todas as Tasks já estão no cronograma.", "Info", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            if (AddToScheduleCallback != null)
            {
                AddToScheduleCallback.Invoke(toAdd);
                foreach (var r in toAdd) { r.InSchedule = true; r.IsSelected = false; }
                HasChanges = true;
                UpdateTotals();
                MessageBox.Show($"{toAdd.Count} Task(s) expandidas no cronograma.", "Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OnReleaseClick(object sender, RoutedEventArgs e)
        {
            ReleaseCallback?.Invoke();
            HasChanges = true;
            Close();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }

    public class TaskReviewRow : INotifyPropertyChanged
    {
        public int StoryId { get; set; }
        public string StoryName { get; set; } = "";
        public ProjectTask StoryTask { get; set; } = null!;
        public int TaskId { get; set; }

        private string _title = "";
        public string Title { get => _title; set { if (_title == value) return; _title = value; OnPropertyChanged(); } }

        private string _state = "New";
        public string State { get => _state; set { if (_state == value) return; _state = value; OnPropertyChanged(); } }

        private double _estimatedHours;
        public double EstimatedHours { get => _estimatedHours; set { if (_estimatedHours == value) return; _estimatedHours = value; OnPropertyChanged(); OnPropertyChanged(nameof(EstimatedHoursDisplay)); } }

        public double CompletedHours { get; set; }
        public double PercentComplete { get; set; }

        private int _priority = 5;
        public int Priority { get => _priority; set { if (_priority == value) return; _priority = value; OnPropertyChanged(); } }

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

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new(p));
    }
}
