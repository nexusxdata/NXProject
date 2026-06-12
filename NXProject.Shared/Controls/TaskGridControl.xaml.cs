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

        private bool _headerMeasured;
        private ScrollViewer? _scrollViewer;
        private bool _suppressScrollNotification;
        private string? _lastRowLayoutSignature;
        private Point _dragStartPoint;
        private TaskViewModel? _dragSourceTask;

        // Estado da edição de predecessoras por clique.
        private TextBox? _predEditBox;
        private bool _inPredecessorEdit;

        public TaskGridControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            TaskGrid.LayoutUpdated += OnLayoutUpdated;
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
            PredecessorColumn.Width = new DataGridLength(expanded ? 260 : 54);
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
            if (_inPredecessorEdit &&
                Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
                TryAppendClickedPredecessor(e.OriginalSource as DependencyObject))
            {
                e.Handled = true;
                _dragSourceTask = null;
                return;
            }

            if (FindParent<ToggleButton>(e.OriginalSource as DependencyObject) != null)
            {
                _dragSourceTask = null;
                return;
            }

            _dragStartPoint = e.GetPosition(TaskGrid);
            _dragSourceTask = FindTaskViewModel(e.OriginalSource as DependencyObject);
        }

        private void OnTaskGridPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (TryAppendClickedPredecessor(e.OriginalSource as DependencyObject))
                e.Handled = true; // suprime o menu de contexto padrão
        }

        private bool TryAppendClickedPredecessor(DependencyObject? source)
        {
            if (!_inPredecessorEdit || _predEditBox == null)
                return false;

            // Clique direito ou Ctrl+clique durante a edição de Pred.: adiciona
            // o DisplayId da linha clicada sem trocar a seleção nem fechar a célula.
            var clicked = FindTaskViewModel(source);
            if (clicked == null || ReferenceEquals(clicked, _predEditBox.DataContext))
                return false;

            var parts = _predEditBox.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            if (!parts.Any(p => string.Equals(p, clicked.DisplayId, StringComparison.OrdinalIgnoreCase)))
                parts.Add(clicked.DisplayId);

            _predEditBox.Text = string.Join(",", parts);
            _predEditBox.CaretIndex = _predEditBox.Text.Length;
            _predEditBox.Focus();
            Keyboard.Focus(_predEditBox);
            return true;
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

        private void OnPredEditGotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                _predEditBox = tb;
                _inPredecessorEdit = true;
            }
        }

        private void OnPredPickerLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox combo)
            {
                EnsurePredEditorFromPicker(combo);
                RefreshPredPicker(combo);
            }
        }

        private void OnPredPickerDropDownOpened(object sender, EventArgs e)
        {
            if (sender is ComboBox combo)
            {
                EnsurePredEditorFromPicker(combo);
                RefreshPredPicker(combo);
            }
        }

        private void OnPredPickerKeyUp(object sender, KeyEventArgs e)
        {
            if (sender is not ComboBox combo)
                return;

            if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right or Key.Enter or Key.Escape or Key.Tab)
                return;

            EnsurePredEditorFromPicker(combo);
            RefreshPredPicker(combo, combo.Text);
            combo.IsDropDownOpen = true;
        }

        private void OnPredPickerKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is ComboBox { SelectedItem: TaskViewModel selected } combo)
            {
                AppendPredecessorToEditor(selected);
                ClearPredPicker(combo);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                _inPredecessorEdit = false;
                _predEditBox = null;
                TaskGrid.CancelEdit(DataGridEditingUnit.Cell);
                e.Handled = true;
            }
        }

        private void OnPredPickerSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox combo || combo.SelectedItem is not TaskViewModel selected)
                return;

            EnsurePredEditorFromPicker(combo);
            AppendPredecessorToEditor(selected);
            ClearPredPicker(combo);
        }

        private void OnPredPickerLostFocus(object sender, RoutedEventArgs e)
        {
            if (_predEditBox == null || sender is not ComboBox combo)
                return;

            var cell = FindParent<DataGridCell>(combo);
            Dispatcher.BeginInvoke(() =>
            {
                if (_predEditBox != null && cell?.IsKeyboardFocusWithin == false)
                    CommitPredEdit(_predEditBox);
            });
        }

        private void RefreshPredPicker(ComboBox combo, string? searchText = null)
        {
            if (combo.DataContext is not TaskViewModel current)
                return;

            var query = (searchText ?? combo.Text ?? string.Empty).Trim();
            var candidates = (Tasks ?? new ObservableCollection<TaskViewModel>())
                .Where(t => !ReferenceEquals(t, current))
                .Where(t => t.Model.Children.Count == 0)
                .Where(t => string.IsNullOrWhiteSpace(query)
                            || t.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                            || t.DisplayId.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(80)
                .ToList();

            combo.ItemsSource = candidates;
        }

        private void EnsurePredEditorFromPicker(ComboBox combo)
        {
            if (_predEditBox?.DataContext == combo.DataContext)
                return;

            var cell = FindParent<DataGridCell>(combo);
            var textBox = cell == null ? null : FindChild<TextBox>(cell);
            if (textBox == null)
                return;

            _predEditBox = textBox;
            _inPredecessorEdit = true;
        }

        private void AppendPredecessorToEditor(TaskViewModel predecessor)
        {
            if (_predEditBox == null)
                return;

            var parts = _predEditBox.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            if (!parts.Any(p => string.Equals(p, predecessor.DisplayId, StringComparison.OrdinalIgnoreCase)))
                parts.Add(predecessor.DisplayId);

            _predEditBox.Text = string.Join(",", parts);
            _predEditBox.CaretIndex = _predEditBox.Text.Length;
        }

        private static void ClearPredPicker(ComboBox combo)
        {
            combo.SelectedItem = null;
            combo.Text = string.Empty;
            combo.IsDropDownOpen = false;
        }

        private void CommitPredEdit(TextBox tb)
        {
            _inPredecessorEdit = false;
            _predEditBox = null;
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            TaskGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            TaskGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        private void OnPredEditKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox tb)
            {
                CommitPredEdit(tb);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && sender is TextBox tb2)
            {
                _inPredecessorEdit = false;
                _predEditBox = null;
                TaskGrid.CancelEdit(DataGridEditingUnit.Cell);
                e.Handled = true;
            }
        }

        private void OnPredEditLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb) return;
            // Usa Dispatcher para deixar o clique em outra linha terminar. Se o
            // gesto de captura de predecessora devolveu o foco ao TextBox, mantem
            // a edição aberta; se não, confirma o valor digitado.
            Dispatcher.BeginInvoke(() =>
            {
                var cell = FindParent<DataGridCell>(tb);
                if (_predEditBox == tb && cell?.IsKeyboardFocusWithin == false)
                    CommitPredEdit(tb);
            });
        }

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
