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

        public void RefreshRows() => TaskGrid.Items.Refresh();

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

        private TaskViewModel? _editSnapshotTask;
        private DataGridColumn? _editSnapshotColumn;
        private string _editSnapshotValue = string.Empty;
        private bool _suppressEditorLostFocusCommit;
        private bool _taskIdClickInProgress;

        public static readonly DependencyProperty ShowOriginalHoursColumnProperty =
            DependencyProperty.Register(nameof(ShowOriginalHoursColumn), typeof(bool),
                typeof(TaskGridControl), new PropertyMetadata(false, OnShowOriginalHoursColumnChanged));

        public bool ShowOriginalHoursColumn
        {
            get => (bool)GetValue(ShowOriginalHoursColumnProperty);
            set => SetValue(ShowOriginalHoursColumnProperty, value);
        }

        private static void OnShowOriginalHoursColumnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TaskGridControl ctrl)
                ctrl.OriginalHoursColumn.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Disparado quando o usuário alterna o modo de visualização do Gantt (original/restante).</summary>
        public event Action? GanttViewToggled;

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

        /// <summary>Disparado quando o usuário quer editar o % de alocação de uma tarefa via menu de contexto.</summary>
        public event Action<TaskViewModel>? EditPercAlocRequested;

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
                {
                    VerticalScrollChanged?.Invoke(_scrollViewer.VerticalOffset);
                    var hdr = FindChild<DataGridColumnHeadersPresenter>(TaskGrid);
                    if (hdr != null && hdr.ActualHeight > 0)
                        PublishRowTops(hdr.ActualHeight);
                }
            };
        }

        public void SyncVerticalOffset(double offset)
        {
            if (_scrollViewer == null) return;
            
            _suppressScrollNotification = true;
            _scrollViewer.ScrollToVerticalOffset(offset);
            _suppressScrollNotification = false;
        }

        // Colunas visíveis por padrão (nome = Header da coluna conforme definido no XAML)
        private static readonly HashSet<string> DefaultVisibleColumns = new()
        {
            "ID", "T·E (DevOps)", "Dur.(h)", "Início", "Fim", "% Compl.", "Recursos", "Sprint"
        };

        /// <summary>Disparado quando o usuário salva a configuração de colunas. Arg = nomes das colunas ocultas.</summary>
        public event Action<string>? ColumnSettingsSaved;

        private bool _hasCustomColumnConfig;

        public void ApplyHiddenColumns(string hiddenColumnsCsv)
        {
            var hidden = new HashSet<string>(
                (hiddenColumnsCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            _hasCustomColumnConfig = !string.IsNullOrWhiteSpace(hiddenColumnsCsv);

            foreach (var (label, col) in GetCustomizableColumns())
                col.Visibility = hidden.Contains(label) ? Visibility.Collapsed : Visibility.Visible;

            var orgHHidden = hidden.Contains("OrgH");
            if (ShowOriginalHoursColumn == orgHHidden)
                ShowOriginalHoursColumn = !orgHHidden;
        }

        private IEnumerable<(string Label, DataGridColumn Col)> GetCustomizableColumns() =>
        [
            ("ID",            IdColumn),
            ("T·E (DevOps)",  DevOpsColumn),
            ("Dur.(h)",       DurationColumn),
            ("SFP",           SfpColumn),
            ("OrgH",          OriginalHoursColumn),
            ("HH Atual",      RealizedHoursColumn),
            ("HH Restante",   EstimatedHoursColumn),
            ("Início",        StartColumn),
            ("Fim",           FinishColumn),
            ("% Compl.",      PercentColumn),
            ("Predecessoras", PredecessorColumn),
            ("Recursos",      ResourcesColumn),
            ("Sprint",        SprintColumn),
        ];

        public void ShowColumnCustomizer()
        {
            var cols = GetCustomizableColumns().ToList();

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock
            {
                Text = "Selecione as colunas visíveis:",
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var checks = new List<(CheckBox cb, DataGridColumn col, string label)>();
            foreach (var (label, col) in cols)
            {
                var cb = new CheckBox
                {
                    Content = label,
                    IsChecked = col.Visibility == Visibility.Visible,
                    Margin = new Thickness(0, 4, 0, 4),
                    FontSize = 13
                };
                panel.Children.Add(cb);
                checks.Add((cb, col, label));
            }

            // Botões
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            var btnRestore = new Button
            {
                Content = "Restaurar Padrão",
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 8, 0)
            };
            var btnSave = new Button
            {
                Content = "Salvar",
                Width = 80,
                Padding = new Thickness(0, 6, 0, 6),
                IsDefault = true
            };
            btnPanel.Children.Add(btnRestore);
            btnPanel.Children.Add(btnSave);
            panel.Children.Add(btnPanel);

            var win = new Window
            {
                Title = "Customizar colunas",
                Content = new ScrollViewer { Content = panel },
                Width = 300,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this)
            };

            btnRestore.Click += (_, _) =>
            {
                foreach (var (cb, _, label) in checks)
                    cb.IsChecked = DefaultVisibleColumns.Contains(label);
            };

            btnSave.Click += (_, _) =>
            {
                foreach (var (cb, col, _) in checks)
                    col.Visibility = cb.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

                var orgHCheck = checks.FirstOrDefault(x => x.col == OriginalHoursColumn);
                ShowOriginalHoursColumn = orgHCheck.cb.IsChecked == true;

                var hidden = checks
                    .Where(x => x.cb.IsChecked != true)
                    .Select(x => x.label);
                ColumnSettingsSaved?.Invoke(string.Join(",", hidden));

                win.Close();
            };

            win.ShowDialog();
        }

        public void SetPresentationMode(bool expanded)
        {
            // Só aplica visibilidade padrão de SFP/Predecessoras quando não há configuração
            // salva pelo usuário — evita sobrescrever o Customizar Colunas ao mudar de modo.
            if (!_hasCustomColumnConfig)
            {
                SfpColumn.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                PredecessorColumn.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            }

            IdColumn.Width = new DataGridLength(expanded ? 86 : 42);
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
            PredecessorColumn.Width = new DataGridLength(expanded ? 86 : 54);
            ResourcesColumn.Width = new DataGridLength(expanded ? 190 : 88);
            SprintColumn.Width = new DataGridLength(expanded ? 150 : 118);
        }

        public void SetPrintMode()
        {
            TaskGrid.EnableRowVirtualization = false;
            TaskGrid.EnableColumnVirtualization = false;
            VirtualizingPanel.SetIsVirtualizing(TaskGrid, false);
            ScrollViewer.SetVerticalScrollBarVisibility(TaskGrid, ScrollBarVisibility.Disabled);
            ScrollViewer.SetHorizontalScrollBarVisibility(TaskGrid, ScrollBarVisibility.Disabled);
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

        private void RefreshGridPreservingSelection(object? selectedItem, DataGridColumn? selectedColumn)
        {
            selectedItem ??= TaskGrid.SelectedItem;
            selectedColumn ??= TaskGrid.CurrentCell.Column ?? NameColumn;

            TaskGrid.Items.Refresh();

            if (selectedItem == null || !TaskGrid.Items.Contains(selectedItem))
                return;

            TaskGrid.SelectedItem = selectedItem;
            TaskGrid.ScrollIntoView(selectedItem, selectedColumn);
            TaskGrid.CurrentCell = new DataGridCellInfo(selectedItem, selectedColumn);
            TaskGrid.UpdateLayout();

            if (TaskGrid.ItemContainerGenerator.ContainerFromItem(selectedItem) is not DataGridRow row)
                return;

            row.IsSelected = true;
            var cell = FindCell(row, selectedColumn);
            if (cell != null)
            {
                cell.Focus();
                Keyboard.Focus(cell);
            }
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

            var scrollOffset = _scrollViewer?.VerticalOffset ?? 0;
            for (int i = 0; i < itemCount; i++)
            {
                var row = TaskGrid.ItemContainerGenerator.ContainerFromIndex(i) as DataGridRow;
                if (row != null)
                {
                    // TranslatePoint retorna posição relativa à viewport; somamos o scroll para obter posição absoluta no conteúdo
                    rowTops[i] = row.TranslatePoint(new Point(0, 0), TaskGrid).Y - headerHeight + scrollOffset;
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

            if (e.ClickCount >= 2 && TryHandleComboDoubleClick(e, SprintColumn))
                return;

            if (e.ClickCount >= 2 && TryHandleComboDoubleClick(e, ResourcesColumn))
                return;

            if (e.ClickCount >= 2 && TryHandleReadOnlyDoubleClick(e))
                return;

            _dragStartPoint = e.GetPosition(TaskGrid);
            _dragSourceTask = FindTaskViewModel(e.OriginalSource as DependencyObject);
        }

        private bool TryHandleComboDoubleClick(MouseButtonEventArgs e, DataGridColumn comboColumn)
        {
            var source = e.OriginalSource as DependencyObject;
            var cell = FindParent<DataGridCell>(source);
            var row = FindParent<DataGridRow>(source);
            if (cell?.Column != comboColumn || row?.Item is not TaskViewModel task)
                return false;

            if (!CanEditCell(task, comboColumn))
                return false;

            BeginControlledEditSwitch();
            _dragSourceTask = null;
            TaskGrid.SelectedItem = task;
            TaskGrid.CurrentCell = new DataGridCellInfo(task, comboColumn);
            e.Handled = true;

            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                TaskGrid.BeginEdit();
                TaskGrid.UpdateLayout();

                var rowContainer = TaskGrid.ItemContainerGenerator.ContainerFromItem(task) as DataGridRow;
                var editCell = rowContainer != null ? FindCell(rowContainer, comboColumn) : null;
                var combo = editCell != null ? FindChild<ComboBox>(editCell) : null;
                if (combo == null)
                    return;

                combo.Focus();
                Keyboard.Focus(combo);
                combo.IsDropDownOpen = true;
            }));

            return true;
        }

        private bool TryHandleReadOnlyDoubleClick(MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            var cell = FindParent<DataGridCell>(source);
            var row = FindParent<DataGridRow>(source);
            if (cell?.Column == null || row?.Item is not TaskViewModel task)
                return false;

            if (CanEditCell(task, cell.Column))
                return false;

            BeginControlledEditSwitch();
            _dragSourceTask = null;
            TaskGrid.SelectedItem = task;
            TaskGrid.CurrentCell = new DataGridCellInfo(task, cell.Column);
            cell.Focus();
            Keyboard.Focus(cell);
            e.Handled = true;
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
        private void OnTaskGridCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Column == DurationColumn || e.Column == OriginalHoursColumn)
                return;

            if (e.EditAction == DataGridEditAction.Commit)
            {
                if (IsUnchangedEdit(e.Column, e.EditingElement as FrameworkElement))
                {
                    e.Cancel = true;
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                        new Action(CancelCurrentEdit));
                    return;
                }

                var selectedItem = e.Row?.Item ?? TaskGrid.SelectedItem;
                var selectedColumn = e.Column;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    () => RefreshGridPreservingSelection(selectedItem, selectedColumn));
                ClearEditSnapshot();
            }
        }

        private void OnTaskGridBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
        {
            if (e.Row?.Item is not TaskViewModel task)
                return;

            if (!CanEditCell(task, e.Column))
            {
                e.Cancel = true;
                return;
            }

            CaptureEditSnapshot(task, e.Column);
        }

        private bool CanEditCell(TaskViewModel task, DataGridColumn column)
        {
            if (column == IdColumn ||
                column == DevOpsColumn ||
                column == OriginalHoursColumn)
                return false;

            if (column == DurationColumn)
                return !task.IsDurationReadOnly;

            if (column == StartColumn || column == FinishColumn)
                return task.Model.Children.Count == 0;

            if (column == PercentColumn)
                return task.CanEditPercentComplete;

            if (column == PredecessorColumn)
                return task.CanEditPredecessors;

            if (column == SprintColumn)
                return task.SupportsSprint;

            return true;
        }

        private void CaptureEditSnapshot(TaskViewModel task, DataGridColumn column)
        {
            _editSnapshotTask = task;
            _editSnapshotColumn = column;
            _editSnapshotValue = GetEditValue(task, column, null);
        }

        private bool IsUnchangedEdit(DataGridColumn column, FrameworkElement? editor)
        {
            if (_editSnapshotTask == null || _editSnapshotColumn != column)
                return false;

            var current = GetEditValue(_editSnapshotTask, column, editor);
            return string.Equals(current, _editSnapshotValue, StringComparison.OrdinalIgnoreCase);
        }

        private string GetEditValue(TaskViewModel task, DataGridColumn column, FrameworkElement? editor)
        {
            if (editor != null)
            {
                if (FindChild<ComboBox>(editor) is { } combo)
                    return GetComboEditValue(combo, column);

                if (FindChild<TextBox>(editor) is { } textBox)
                    return GetTextEditValue(textBox, column);

                if (editor is TextBox directTextBox)
                    return GetTextEditValue(directTextBox, column);

                if (editor is ComboBox directCombo)
                    return GetComboEditValue(directCombo, column);
            }

            return GetModelEditValue(task, column);
        }

        private string GetModelEditValue(TaskViewModel task, DataGridColumn column)
        {
            if (column == NameColumn)
                return NormalizeEditText(task.Name);
            if (column == DurationColumn)
                return NormalizeNumber(task.DurationHours);
            if (column == SfpColumn)
                return NormalizeNullableNumber(task.SfpPoints);
            if (column == StartColumn)
                return NormalizeDate(task.Start);
            if (column == FinishColumn)
                return NormalizeDate(task.Finish);
            if (column == PercentColumn)
                return NormalizeNumber(task.PercentComplete);
            if (column == PredecessorColumn)
                return NormalizeEditText(task.PredecessorsText);
            if (column == ResourcesColumn)
                return task.PrimaryResource?.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    ?? NormalizeEditText(task.ResourcesText);
            if (column == SprintColumn)
                return NormalizeEditText(task.SprintPath);

            return string.Empty;
        }

        private string GetComboEditValue(ComboBox combo, DataGridColumn column)
        {
            if (column == SprintColumn)
                return NormalizeEditText(combo.SelectedValue?.ToString());

            if (column == ResourcesColumn)
                return (combo.SelectedItem as Resource)?.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    ?? NormalizeEditText(combo.Text);

            return NormalizeEditText(combo.SelectedValue?.ToString() ?? combo.Text);
        }

        private string GetTextEditValue(TextBox textBox, DataGridColumn column)
        {
            var text = NormalizeEditText(textBox.Text);
            if ((column == StartColumn || column == FinishColumn) &&
                DateTime.TryParse(text, out var date))
            {
                return NormalizeDate(date);
            }

            if ((column == DurationColumn || column == PercentColumn || column == SfpColumn) &&
                double.TryParse(text,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.CurrentCulture,
                    out var number))
            {
                return NormalizeNumber(number);
            }

            return text;
        }

        private static string NormalizeEditText(string? value) => (value ?? string.Empty).Trim();

        private static string NormalizeDate(DateTime value) =>
            value.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        private static string NormalizeNumber(double value) =>
            Math.Round(value, 4).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

        private static string NormalizeNullableNumber(double? value) =>
            value.HasValue ? NormalizeNumber(value.Value) : string.Empty;

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
            if (Tasks != null)
            {
                foreach (var t in Tasks)
                { t.IsHighlightedPredecessor = false; t.IsHighlightSource = false; }
                task.IsHighlightSource = true;
                var predIds = task.Model.PredecessorIds.ToHashSet();
                foreach (var t in Tasks)
                    if (predIds.Contains(t.Model.Id))
                        t.IsHighlightedPredecessor = true;
            }
            HighlightPredecessorsRequested?.Invoke(task);
        }

        private void OnClearPredecessorHighlightClick(object sender, RoutedEventArgs e)
        {
            ClearHighlightState();
            HighlightPredecessorsRequested?.Invoke(null!);
        }

        private void OnTaskGridSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_highlightSourceTaskId.HasValue) return;
            var selected = TaskGrid.SelectedItem as TaskViewModel;
            if (selected == null || selected.Model.Id != _highlightSourceTaskId.Value)
            {
                ClearHighlightState();
                HighlightPredecessorsRequested?.Invoke(null!);
            }
        }

        private void ClearHighlightState()
        {
            _highlightSourceTaskId = null;
            if (Tasks == null) return;
            foreach (var t in Tasks)
            { t.IsHighlightedPredecessor = false; t.IsHighlightSource = false; }
        }

        private int? _highlightSourceTaskId;

        private void OnSprintComboDropDownOpened(object? sender, EventArgs e)
        {
            if (sender is ComboBox { DataContext: TaskViewModel task } combo)
                CaptureEditSnapshot(task, SprintColumn);
        }

        // Troca de sprint pela grade: ao fechar o dropdown, se a seleção mudou,
        // notifica o ViewModel (grava o IterationPath / limpa, desliza a barra) e
        // encerra a edição. "(sem sprint)" (Path nulo) é tratado como limpar.
        private void OnSprintComboDropDownClosed(object? sender, EventArgs e)
        {
            if (sender is not ComboBox { DataContext: TaskViewModel task } combo)
                return;
            if (IsUnchangedEdit(SprintColumn, combo))
            {
                CancelCurrentEdit();
                return; // usuário não mudou a escolha
            }

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
            if (IsUnchangedEdit(ResourcesColumn, combo))
            {
                CancelCurrentEdit();
                return;
            }

            if (selected == null)
            {
                if (string.IsNullOrEmpty(typed))
                {
                    CancelCurrentEdit();
                    return;
                }

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

        private void OnResourceComboDropDownOpened(object? sender, EventArgs e)
        {
            if (sender is ComboBox { DataContext: TaskViewModel task } combo)
                CaptureEditSnapshot(task, ResourcesColumn);
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
            if (IsUnchangedEdit(DurationColumn, tb))
            {
                CancelCurrentEdit();
                return;
            }

            vm.DurationText = tb.Text;
            // Notifica explicitamente as colunas read-only que dependem de DurationHours
            vm.RefreshDerivedDisplayProperties();
            TaskGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            TaskGrid.CommitEdit(DataGridEditingUnit.Row, true);
            ClearEditSnapshot();
        }

        private void OnEditTextBoxGotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
                tb.Dispatcher.BeginInvoke(tb.SelectAll);
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
            if (_suppressEditorLostFocusCommit)
                return;

            if (sender is TextBox tb)
                CommitDurationEdit(tb);
        }

        private void OnToggleOrgHColumnClick(object sender, RoutedEventArgs e)
        {
            ShowOriginalHoursColumn = !ShowOriginalHoursColumn;
        }

        // Guarda a VM capturada no ContextMenuOpening para usar no click (PlacementTarget pode ser null no Click)
        private TaskViewModel? _durationContextMenuVm;

        private void OnToggleGanttOriginalViewClick(object sender, RoutedEventArgs e)
        {
            var vm = _durationContextMenuVm;
            if (vm == null) return;
            vm.SetOriginalHoursView(!vm.UseOriginalHoursView);
            GanttViewToggled?.Invoke();
        }

        // Atualiza os labels dos itens de menu e captura a VM antes de abrir o ContextMenu.
        private void OnDurationContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is not Border { ContextMenu: { } cm }) return;

            _durationContextMenuVm = (sender as System.Windows.FrameworkElement)?.DataContext as TaskViewModel;
            var vm = _durationContextMenuVm;

            var orgHItem = cm.Items.OfType<MenuItem>().FirstOrDefault(m => m.Name == "ToggleOrgHMenuItem");
            if (orgHItem != null)
                orgHItem.Header = ShowOriginalHoursColumn
                    ? "Ocultar coluna Estimativa Original (OrgH)"
                    : "Mostrar coluna Estimativa Original (OrgH)";

            var ganttItem = cm.Items.OfType<MenuItem>().FirstOrDefault(m => m.Name == "ToggleGanttOriginalMenuItem");
            if (ganttItem != null)
                ganttItem.Header = vm?.UseOriginalHoursView == true
                    ? "Voltar Gantt para Estimativa Restante"
                    : "Mostrar Gantt pela Estimativa Original";

        }


        private void CommitStartEdit(TextBox tb)
        {
            if (tb.DataContext is not TaskViewModel vm) return;
            if (IsUnchangedEdit(StartColumn, tb))
            {
                CancelCurrentEdit();
                return;
            }

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
            if (_suppressEditorLostFocusCommit)
                return;

            if (sender is TextBox tb)
                CommitStartEdit(tb);
        }

        private void CommitFinishEdit(TextBox tb)
        {
            if (tb.DataContext is not TaskViewModel vm) return;
            if (IsUnchangedEdit(FinishColumn, tb))
            {
                CancelCurrentEdit();
                return;
            }

            var currentDur = vm.DurationHours;
            var result = System.Windows.MessageBox.Show(
                $"Alterar a data Fim mudará a duração (atual: {currentDur:0.#}h).\nDeseja continuar?",
                "Alterar data Fim",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (result != System.Windows.MessageBoxResult.Yes)
            {
                CancelCurrentEdit();
                return;
            }

            vm.FinishText = tb.Text;
            TaskGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            TaskGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        private void CancelCurrentEdit()
        {
            TaskGrid.CancelEdit(DataGridEditingUnit.Cell);
            TaskGrid.CancelEdit(DataGridEditingUnit.Row);
            ClearEditSnapshot();
        }

        private void BeginControlledEditSwitch()
        {
            _suppressEditorLostFocusCommit = true;
            try
            {
                TaskGrid.CancelEdit(DataGridEditingUnit.Cell);
                TaskGrid.CancelEdit(DataGridEditingUnit.Row);
                ClearEditSnapshot();
            }
            finally
            {
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
                {
                    _suppressEditorLostFocusCommit = false;
                }));
            }
        }

        private void ClearEditSnapshot()
        {
            _editSnapshotTask = null;
            _editSnapshotColumn = null;
            _editSnapshotValue = string.Empty;
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
            if (_suppressEditorLostFocusCommit)
                return;

            if (sender is TextBox tb)
                CommitFinishEdit(tb);
        }

        private void OnIdCellPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount < 2)
                return;

            _dragSourceTask = null;
            if (sender is FrameworkElement { DataContext: TaskViewModel task })
            {
                BeginControlledEditSwitch();
                TaskGrid.SelectedItem = task;
                TaskGrid.CurrentCell = new DataGridCellInfo(task, IdColumn);
            }

            e.Handled = true;
        }

        private void OnIdCellClick(object sender, RoutedEventArgs e)
        {
            _dragSourceTask = null;
            if (_taskIdClickInProgress)
            {
                e.Handled = true;
                return;
            }

            if (sender is FrameworkElement { DataContext: TaskViewModel task })
            {
                e.Handled = true;
                _taskIdClickInProgress = true;
                try
                {
                    BeginControlledEditSwitch();
                    TaskGrid.SelectedItem = task;
                    TaskGrid.CurrentCell = new DataGridCellInfo(task, IdColumn);
                    TaskIdClicked?.Invoke(task);
                }
                finally
                {
                    _taskIdClickInProgress = false;
                }
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

        private void OnEditPercAlocClick(object sender, RoutedEventArgs e)
        {
            TaskViewModel? task = null;
            if (sender is FrameworkElement fe)
            {
                task = fe.DataContext as TaskViewModel;
                if (task == null && fe.ContextMenu?.PlacementTarget is FrameworkElement pt)
                    task = pt.DataContext as TaskViewModel;
            }
            if (task == null || task.Model.Resources.Count == 0) return;

            EditPercAlocRequested?.Invoke(task);
        }
    }
}
