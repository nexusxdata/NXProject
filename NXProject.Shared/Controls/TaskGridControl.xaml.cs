using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NXProject.Models;
using NXProject.ViewModels;
using NXProject.Views;

namespace NXProject.Controls
{
    public partial class TaskGridControl : UserControl
    {
        public static readonly DependencyProperty TasksProperty =
            DependencyProperty.Register(nameof(Tasks), typeof(ObservableCollection<TaskViewModel>),
                typeof(TaskGridControl), new PropertyMetadata(null, OnTasksChanged));

        public ObservableCollection<TaskViewModel>? Tasks
        {
            get => (ObservableCollection<TaskViewModel>?)GetValue(TasksProperty);
            set
            {
                SetValue(TasksProperty, value);
                // Resetar cache quando tasks mudam, para forçar re-cálculo de layout
                _lastRowLayoutSignature = null;
            }
        }

        public static readonly DependencyProperty AvailableSprintsProperty =
            DependencyProperty.Register(nameof(AvailableSprints), typeof(ObservableCollection<Sprint>),
                typeof(TaskGridControl), new PropertyMetadata(null));

        /// <summary>Sprints disponíveis para escolher na coluna Sprint (vindas do DevOps).</summary>
        public ObservableCollection<Sprint>? AvailableSprints
        {
            get => (ObservableCollection<Sprint>?)GetValue(AvailableSprintsProperty);
            set => SetValue(AvailableSprintsProperty, value);
        }

        public static readonly DependencyProperty AvailableResourcesProperty =
            DependencyProperty.Register(nameof(AvailableResources), typeof(ObservableCollection<Resource>),
                typeof(TaskGridControl), new PropertyMetadata(null));

        /// <summary>Recursos disponíveis para atribuição nas tarefas.</summary>
        public ObservableCollection<Resource>? AvailableResources
        {
            get => (ObservableCollection<Resource>?)GetValue(AvailableResourcesProperty);
            set => SetValue(AvailableResourcesProperty, value);
        }

        /// <summary>Disparado quando o usuário escolhe outra sprint (ou "(sem sprint)" = null) para uma tarefa.</summary>
        public event Action<TaskViewModel, Sprint?>? TaskSprintChangeRequested;

        // Seleção da sprint no momento em que o dropdown abriu, para só aplicar a
        // troca quando o usuário realmente mudou a escolha (evita limpar sem querer).
        private object? _sprintEditOriginalSelection;

        /// <summary>Disparado quando o DataGrid rola verticalmente.</summary>
        public event Action<double>? VerticalScrollChanged;

        /// <summary>Disparado quando a altura real do header do DataGrid e conhecida.</summary>
        public event Action<double>? HeaderHeightMeasured;

        /// <summary>Disparado quando as linhas reais do DataGrid mudam de posição.</summary>
        public event Action<IReadOnlyList<double>>? RowTopsMeasured;
        public event Action<TaskViewModel, TaskViewModel, bool>? TaskMoveRequested;

        /// <summary>Disparado quando o usuário clica no ID de uma tarefa (editar vínculo DevOps).</summary>
        public event Action<TaskViewModel>? TaskIdClicked;

        /// <summary>Disparado quando o usuário quer destacar as predecessoras de uma tarefa no Gantt.</summary>
        public event Action<TaskViewModel>? HighlightPredecessorsRequested;

        private bool _headerMeasured;
        private ScrollViewer? _scrollViewer;
        private bool _suppressScrollNotification;
        private string? _lastRowLayoutSignature;
        private Point _dragStartPoint;
        private TaskViewModel? _dragSourceTask;

        public TaskGridControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            TaskGrid.LayoutUpdated += OnLayoutUpdated;
        }

        /// <summary>Altera a altura do cabeçalho do DataGrid para sincronizar com o Gantt (ex: modo Dia = 60px).</summary>
        public void SetColumnHeaderHeight(double height)
        {
            TaskGrid.ColumnHeaderHeight = height;
            _headerMeasured = false; // permite re-publicar após o resize
        }

        private void OnLayoutUpdated(object? sender, EventArgs e)
        {
            var header = FindChild<DataGridColumnHeadersPresenter>(TaskGrid);
            if (header == null || header.ActualHeight == 0) return;

            if (!_headerMeasured)
            {
                _headerMeasured = true;
                HeaderHeightMeasured?.Invoke(header.ActualHeight);
                // Publicar RowTops apenas na primeira vez que o header é medido
                PublishRowTops(header.ActualHeight);
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _scrollViewer = FindChild<ScrollViewer>(TaskGrid);
            if (_scrollViewer == null) return;

            _scrollViewer.ScrollChanged += (_, args) =>
            {
                if (_suppressScrollNotification) return;
                if (args.VerticalChange != 0 || args.ExtentHeightChange != 0 || args.ViewportHeightChange != 0)
                    VerticalScrollChanged?.Invoke(_scrollViewer.VerticalOffset);
            };
        }

        public void SyncVerticalOffset(double offset)
        {
            if (_scrollViewer == null) return;
            
            _suppressScrollNotification = true;
            _scrollViewer.ScrollToVerticalOffset(offset);
            _suppressScrollNotification = false;
        }

        public void SetPresentationMode(bool expanded)
        {
            SfpColumn.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            PredecessorColumn.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;

            IdColumn.Width = new DataGridLength(expanded ? 54 : 42);
            DevOpsColumn.Width = new DataGridLength(expanded ? 62 : 46);
            NameColumn.MinWidth = expanded ? 280 : 210;
            NameColumn.Width = expanded
                ? new DataGridLength(2.2, DataGridLengthUnitType.Star)
                : new DataGridLength(1, DataGridLengthUnitType.Star);
            DurationColumn.Width = new DataGridLength(expanded ? 70 : 52);
            SfpColumn.Width = new DataGridLength(expanded ? 64 : 52);
            StartColumn.Width = new DataGridLength(expanded ? 96 : 76);
            FinishColumn.Width = new DataGridLength(expanded ? 96 : 76);
            PercentColumn.Width = new DataGridLength(expanded ? 82 : 62);
            PredecessorColumn.Width = new DataGridLength(expanded ? 120 : 54);
            ResourcesColumn.Width = new DataGridLength(expanded ? 190 : 88);
            SprintColumn.Width = new DataGridLength(expanded ? 190 : 118);
        }

        public void FocusSelectedTask()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (TaskGrid.Items.Count == 0)
                    return;

                var item = TaskGrid.SelectedItem ?? TaskGrid.Items[0];
                if (item == null) return;

                TaskGrid.SelectedItem = item;
                TaskGrid.ScrollIntoView(item);
                TaskGrid.CurrentCell = new DataGridCellInfo(item, NameColumn);

                TaskGrid.UpdateLayout();

                var row = TaskGrid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
                if (row != null)
                {
                    var cell = FindCell(row, NameColumn);
                    if (cell != null)
                    {
                        cell.Focus();
                        Keyboard.Focus(cell);
                        return;
                    }
                }

                TaskGrid.Focus();
                Keyboard.Focus(TaskGrid);
            }));
        }

        private static DataGridCell? FindCell(DataGridRow row, DataGridColumn column)
        {
            var presenter = FindChild<DataGridCellsPresenter>(row);
            if (presenter == null) return null;

            var index = column.DisplayIndex;
            return presenter.ItemContainerGenerator.ContainerFromIndex(index) as DataGridCell;
        }

        private void PublishRowTops(double headerHeight)
        {
            var itemCount = TaskGrid.Items.Count;
            if (itemCount == 0) return;

            var rowTops = new double[itemCount];
            var measuredAnyRow = false;

            for (int i = 0; i < itemCount; i++)
            {
                var row = TaskGrid.ItemContainerGenerator.ContainerFromIndex(i) as DataGridRow;
                if (row != null)
                {
                    rowTops[i] = row.TranslatePoint(new Point(0, 0), TaskGrid).Y - headerHeight;
                    measuredAnyRow = true;
                }
                else
                {
                    rowTops[i] = i * TaskGrid.RowHeight;
                }
            }

            if (!measuredAnyRow) return;

            // Apenas comparar assinatura para evitar renders redundantes
            var signature = string.Join("|", rowTops.Select(v => Math.Round(v, 2).ToString("0.##")));
            if (signature == _lastRowLayoutSignature) return;

            _lastRowLayoutSignature = signature;
            RowTopsMeasured?.Invoke(rowTops);
        }

        private static void OnTasksChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctrl = (TaskGridControl)d;
            ctrl._lastRowLayoutSignature = null;
            // Agendar recalcular RowTops após o layout ser atualizado
            ctrl.Dispatcher.BeginInvoke(() =>
            {
                var header = FindChild<DataGridColumnHeadersPresenter>(ctrl.TaskGrid);
                if (header != null && header.ActualHeight > 0)
                    ctrl.PublishRowTops(header.ActualHeight);
            });
        }

        private void OnTaskGridPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (FindParent<ToggleButton>(e.OriginalSource as DependencyObject) != null)
            {
                _dragSourceTask = null;
                return;
            }

            _dragStartPoint = e.GetPosition(TaskGrid);
            _dragSourceTask = FindTaskViewModel(e.OriginalSource as DependencyObject);
        }

        private void OnTaskGridPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragSourceTask == null)
                return;

            var currentPosition = e.GetPosition(TaskGrid);
            if (Math.Abs(currentPosition.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPosition.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            var dragTask = _dragSourceTask;
            _dragSourceTask = null;
            DragDrop.DoDragDrop(TaskGrid, new DataObject(typeof(TaskViewModel), dragTask), DragDropEffects.Move);
        }

        private void OnTaskGridDragOver(object sender, DragEventArgs e)
        {
            var sourceTask = e.Data.GetData(typeof(TaskViewModel)) as TaskViewModel;
            var targetTask = FindTaskViewModel(e.OriginalSource as DependencyObject);
            e.Effects = sourceTask != null && targetTask != null && sourceTask != targetTask
                ? DragDropEffects.Move
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnTaskGridDrop(object sender, DragEventArgs e)
        {
            var sourceTask = e.Data.GetData(typeof(TaskViewModel)) as TaskViewModel;
            var targetTask = FindTaskViewModel(e.OriginalSource as DependencyObject);
            if (sourceTask == null || targetTask == null || sourceTask == targetTask)
                return;

            var row = FindParent<DataGridRow>(e.OriginalSource as DependencyObject);
            var insertAfter = false;
            if (row != null)
            {
                var rowPosition = e.GetPosition(row);
                insertAfter = rowPosition.Y > row.ActualHeight / 2;
            }

            TaskMoveRequested?.Invoke(sourceTask, targetTask, insertAfter);
            e.Handled = true;
        }

        // Só Feature e Story têm sprint: cancela a edição da coluna Sprint para
        // Projeto/Epic (e qualquer tipo sem suporte a sprint).
        private void OnTaskGridBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
        {
            if (e.Column == SprintColumn && e.Row?.Item is TaskViewModel task && !task.SupportsSprint)
                e.Cancel = true;

            if (e.Column == PercentColumn && e.Row?.Item is TaskViewModel percentTask && !percentTask.CanEditPercentComplete)
                e.Cancel = true;

            if (e.Column == PredecessorColumn && e.Row?.Item is TaskViewModel predecessorTask && !predecessorTask.CanEditPredecessors)
                e.Cancel = true;
        }

        private void OnPredecessorCellClick(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: TaskViewModel task })
                return;
            if (!task.CanEditPredecessors)
                return;

            _dragSourceTask = null;
            e.Handled = true;

            var candidates = (Tasks ?? new ObservableCollection<TaskViewModel>())
                .Where(t => !ReferenceEquals(t, task))
                .Where(t => t.Model.Children.Count == 0)
                .OrderBy(t => t.Name)
                .ToList();

            var owner = Window.GetWindow(this);
            var dialog = new PredecessorPickerWindow(task, candidates)
            {
                Owner = owner
            };

            if (dialog.ShowDialog() == true)
            {
                task.PredecessorsText = dialog.SelectedPredecessorsText;
                TaskGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                TaskGrid.CommitEdit(DataGridEditingUnit.Row, true);
            }
        }

        private void OnHighlightPredecessorsClick(object sender, RoutedEventArgs e)
        {
            // O sender pode ser MenuItem dentro do ContextMenu; DataContext vem do PlacementTarget
            TaskViewModel? task = null;
            if (sender is FrameworkElement fe)
            {
                task = fe.DataContext as TaskViewModel;
                if (task == null && fe is MenuItem mi && mi.Parent is ContextMenu cm && cm.PlacementTarget is FrameworkElement pt)
                    task = pt.DataContext as TaskViewModel;
            }
            if (task == null) return;
            e.Handled = true;

            _highlightSourceTaskId = task.Model.Id;
            ClearPredecessorHighlight();
            if (Tasks != null)
            {
                var predIds = task.Model.PredecessorIds.ToHashSet();
                foreach (var t in Tasks)
                    if (predIds.Contains(t.Model.Id))
                        t.IsHighlightedPredecessor = true;
            }
            HighlightPredecessorsRequested?.Invoke(task);
        }

        private void OnTaskGridSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_highlightSourceTaskId.HasValue)
            {
                var selected = TaskGrid.SelectedItem as TaskViewModel;
                if (selected == null || selected.Model.Id != _highlightSourceTaskId.Value)
                {
                    ClearPredecessorHighlight();
                    HighlightPredecessorsRequested?.Invoke(null!);
                }
            }
        }

        private void ClearPredecessorHighlight()
        {
            _highlightSourceTaskId = null;
            if (Tasks == null) return;
            foreach (var t in Tasks)
                t.IsHighlightedPredecessor = false;
        }

        private int? _highlightSourceTaskId;

        private void OnSprintComboDropDownOpened(object? sender, EventArgs e)
        {
            _sprintEditOriginalSelection = (sender as ComboBox)?.SelectedItem;
        }

        // Troca de sprint pela grade: ao fechar o dropdown, se a seleção mudou,
        // notifica o ViewModel (grava o IterationPath / limpa, desliza a barra) e
        // encerra a edição. "(sem sprint)" (Path nulo) é tratado como limpar.
        private void OnSprintComboDropDownClosed(object? sender, EventArgs e)
        {
            if (sender is not ComboBox { DataContext: TaskViewModel task } combo)
                return;
            if (ReferenceEquals(combo.SelectedItem, _sprintEditOriginalSelection))
                return; // usuário não mudou a escolha

            var sprint = combo.SelectedItem as Sprint;
            if (sprint != null && string.IsNullOrEmpty(sprint.Path))
                sprint = null; // opção "(sem sprint)"

            TaskSprintChangeRequested?.Invoke(task, sprint);
            TaskGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            TaskGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        private void OnResourceComboDropDownClosed(object? sender, EventArgs e)
        {
            if (sender is not ComboBox { DataContext: TaskViewModel task } combo)
                return;

            var selected = combo.SelectedItem as Resource;
            var typed = NormalizeManualResourceName(combo.Text);

            if (selected == null)
            {
                if (string.IsNullOrEmpty(typed))
                    return;

                var existing = AvailableResources?.FirstOrDefault(r => string.Equals(r.Name, typed, StringComparison.OrdinalIgnoreCase));
                Resource res;
                if (existing == null)
                {
                    var nextId = (AvailableResources?.Select(r => r.Id).DefaultIfEmpty(0).Max() ?? 0) + 1;
                    res = new Resource { Id = nextId, Name = typed, IsImportedFromTfs = false };
                    AvailableResources?.Add(res);
                }
                else
                {
                    res = existing;
                }

                task.PrimaryResource = res;
            }
            else
            {
                task.PrimaryResource = selected;
            }

            TaskGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            TaskGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        private void OnResourceComboKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (sender is not ComboBox { DataContext: TaskViewModel task } combo) return;
            OnResourceComboDropDownClosed(sender, EventArgs.Empty);
        }

        private static string NormalizeManualResourceName(string? name) =>
            (name ?? string.Empty).Trim().TrimStart('*').Trim();

        private void CommitDurationEdit(TextBox tb)
        {
            if (tb.DataContext is not TaskViewModel vm) return;
            vm.DurationText = tb.Text;
            TaskGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            TaskGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        private void OnDurationEditKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox tb)
            {
                CommitDurationEdit(tb);
                e.Handled = true;
            }
        }

        private void OnDurationEditLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
                CommitDurationEdit(tb);
        }

        private void CommitStartEdit(TextBox tb)
        {
            if (tb.DataContext is not TaskViewModel vm) return;
            vm.StartText = tb.Text;
            TaskGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            TaskGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        private void OnStartEditKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox tb)
            {
                CommitStartEdit(tb);
                e.Handled = true;
            }
        }

        private void OnStartEditLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
                CommitStartEdit(tb);
        }

        private void CommitFinishEdit(TextBox tb)
        {
            if (tb.DataContext is not TaskViewModel vm) return;
            vm.FinishText = tb.Text;
            TaskGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            TaskGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        private void OnFinishEditKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox tb)
            {
                CommitFinishEdit(tb);
                e.Handled = true;
            }
        }

        private void OnFinishEditLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
                CommitFinishEdit(tb);
        }

        private void OnIdCellClick(object sender, RoutedEventArgs e)
        {
            _dragSourceTask = null;
            if (sender is FrameworkElement { DataContext: TaskViewModel task })
            {
                e.Handled = true;
                TaskIdClicked?.Invoke(task);
            }
        }

        private void OnHierarchyTogglePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragSourceTask = null;

            if (sender is ToggleButton { DataContext: TaskViewModel task })
            {
                task.IsExpanded = !task.IsExpanded;
                e.Handled = true;
            }
        }

        private static T? FindChild<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T t) return t;
                var found = FindChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        private static TaskViewModel? FindTaskViewModel(DependencyObject? source)
        {
            return FindParent<DataGridRow>(source)?.Item as TaskViewModel;
        }

        private static T? FindParent<T>(DependencyObject? source) where T : DependencyObject
        {
            var current = source;
            while (current != null)
            {
                if (current is T match)
                    return match;

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }
    }
}
