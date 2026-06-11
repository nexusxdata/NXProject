using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NXProject.Models;
using NXProject.Services;
using NXProject.ViewModels;

namespace NXProject.Views
{
    public partial class ResourceAllocationWindow : Window, INotifyPropertyChanged
    {
        private readonly MainViewModel _vm;
        private Resource? _selectedResource;
        private SprintColumn? _selectedSprint;

        public ResourceAllocationWindow(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            AvailableResources = _vm.Project.Resources;
            SelectedDetails.CollectionChanged += (_, _) => OnPropertyChanged(nameof(SelectedDetails));
            DataContext = this;
            BuildMatrix();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<Resource> AvailableResources { get; }
        public ObservableCollection<AllocationDetailRow> SelectedDetails { get; } = new();

        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            BuildMatrix();
            if (_selectedResource != null && _selectedSprint != null)
                ShowDetails(_selectedResource, _selectedSprint);
        }

        private void OnAddResourceClick(object sender, RoutedEventArgs e)
        {
            var name = PromptResourceName();
            if (string.IsNullOrWhiteSpace(name))
                return;

            var normalizedName = NormalizeManualResourceName(name);
            if (string.IsNullOrWhiteSpace(normalizedName))
                return;
            if (AvailableResources.Any(r => string.Equals(r.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(
                    "Ja existe um recurso com esse nome.",
                    "Incluir recurso",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var nextId = AvailableResources.Select(r => r.Id).DefaultIfEmpty(0).Max() + 1;
            AvailableResources.Add(new Resource
            {
                Id = nextId,
                Name = normalizedName,
                MaxUnitsPerDay = ProjectCalendarService.WorkingHoursPerDay,
                IsImportedFromTfs = false
            });

            _vm.Project.IsDirty = true;
            BuildMatrix();
        }

        private void OnDeleteResourceClick(object sender, RoutedEventArgs e)
        {
            if (_selectedResource == null)
            {
                MessageBox.Show(
                    "Selecione uma celula do recurso que deseja excluir.",
                    "Excluir recurso",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var resource = _selectedResource;
            var assignmentCount = _vm.FlatTasks.Count(t => t.Model.Resources.Any(r => r.ResourceId == resource.Id));
            var confirm = MessageBox.Show(
                assignmentCount > 0
                    ? $"Excluir {resource.DisplayName} e remover {assignmentCount} alocacao(oes)?"
                    : $"Excluir {resource.DisplayName}?",
                "Excluir recurso",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
                return;

            foreach (var task in _vm.FlatTasks)
                task.Model.Resources.RemoveAll(r => r.ResourceId == resource.Id);

            AvailableResources.Remove(resource);
            _selectedResource = null;
            SelectedDetails.Clear();
            DetailsTitle.Text = "Selecione uma celula da grade";
            _vm.Project.IsDirty = true;
            _vm.RefreshTasks();
            BuildMatrix();
        }

        private void BuildMatrix()
        {
            AllocationGrid.Children.Clear();
            AllocationGrid.RowDefinitions.Clear();
            AllocationGrid.ColumnDefinitions.Clear();

            var sprints = BuildSprintColumns().ToList();
            AllocationGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            foreach (var _ in sprints)
                AllocationGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(125) });

            AllocationGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
            AddCell("Recurso", 0, 0, true);
            for (int i = 0; i < sprints.Count; i++)
                AddCell(sprints[i].Header, 0, i + 1, true);

            var resources = _vm.Project.Resources.OrderBy(r => r.Name).ToList();
            for (int row = 0; row < resources.Count; row++)
            {
                var resource = resources[row];
                AllocationGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
                AddCell(resource.DisplayName, row + 1, 0, true, horizontalAlignment: HorizontalAlignment.Left);

                for (int col = 0; col < sprints.Count; col++)
                {
                    var sprint = sprints[col];
                    var hours = GetAllocatedHours(resource, sprint);
                    var allocationPercent = GetAverageAllocationPercent(resource, sprint);
                    var capacityHours = GetSprintCapacityHours(resource, sprint, allocationPercent);
                    var isOverAllocated = hours > capacityHours + 0.0001;
                    AddHoursButton(resource, sprint, hours, allocationPercent, capacityHours, isOverAllocated, row + 1, col + 1);
                }
            }
        }

        private void AddCell(
            string text,
            int row,
            int col,
            bool header,
            HorizontalAlignment horizontalAlignment = HorizontalAlignment.Center)
        {
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(219, 225, 234)),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Background = header
                    ? new SolidColorBrush(Color.FromRgb(235, 239, 246))
                    : Brushes.White,
                Child = new TextBlock
                {
                    Text = text,
                    FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = horizontalAlignment,
                    Margin = new Thickness(8, 0, 8, 0)
                }
            };

            Grid.SetRow(border, row);
            Grid.SetColumn(border, col);
            AllocationGrid.Children.Add(border);
        }

        private void AddHoursButton(
            Resource resource,
            SprintColumn sprint,
            double hours,
            double? allocationPercent,
            double capacityHours,
            bool isOverAllocated,
            int row,
            int col)
        {
            var normalForeground = new SolidColorBrush(Color.FromRgb(31, 78, 161));
            var overAllocatedForeground = new SolidColorBrush(Color.FromRgb(178, 34, 34));
            var button = new Button
            {
                Content = hours > 0
                    ? new StackPanel
                    {
                        Children =
                        {
                            new TextBlock
                            {
                                Text = $"{hours:0.##} h",
                                HorizontalAlignment = HorizontalAlignment.Center,
                                Foreground = isOverAllocated ? overAllocatedForeground : normalForeground
                            },
                            new TextBlock
                            {
                                Text = $"{allocationPercent ?? 0:0.##}%",
                                FontSize = 10,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                Foreground = normalForeground
                            }
                        }
                    }
                    : "-",
                Tag = (resource, sprint),
                BorderThickness = new Thickness(0),
                Background = hours > 0
                    ? isOverAllocated
                        ? new SolidColorBrush(Color.FromRgb(255, 235, 235))
                        : new SolidColorBrush(Color.FromRgb(230, 242, 255))
                    : Brushes.White,
                Foreground = hours > 0
                    ? isOverAllocated ? overAllocatedForeground : normalForeground
                    : new SolidColorBrush(Color.FromRgb(130, 130, 130)),
                FontWeight = hours > 0 ? FontWeights.SemiBold : FontWeights.Normal,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = hours > 0
                    ? isOverAllocated
                        ? $"Sobrealocado: {hours:0.##} h de {capacityHours:0.##} h disponiveis"
                        : $"Ver atividades desta alocacao ({hours:0.##} h de {capacityHours:0.##} h disponiveis)"
                    : "Sem atividades"
            };
            button.Click += OnHoursCellClick;

            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(219, 225, 234)),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Child = button
            };

            Grid.SetRow(border, row);
            Grid.SetColumn(border, col);
            AllocationGrid.Children.Add(border);
        }

        private void OnHoursCellClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: ValueTuple<Resource, SprintColumn> tuple })
                ShowDetails(tuple.Item1, tuple.Item2);
        }

        private void ShowDetails(Resource resource, SprintColumn sprint)
        {
            _selectedResource = resource;
            _selectedSprint = sprint;
            DetailsTitle.Text = $"{resource.DisplayName} - {sprint.Header}";
            SelectedDetails.Clear();

            foreach (var task in _vm.FlatTasks.Where(t => IsLeafTask(t) && BelongsToSprint(t, sprint)))
            {
                var assignment = task.Model.Resources.FirstOrDefault(r => r.ResourceId == resource.Id);
                if (assignment == null)
                    continue;

                SelectedDetails.Add(new AllocationDetailRow(this, task, assignment, resource));
            }
        }

        private void MoveAssignment(AllocationDetailRow row, Resource newResource)
        {
            var task = row.Task.Model;
            var oldAssignment = row.Assignment;
            if (oldAssignment.ResourceId == newResource.Id)
                return;

            task.Resources.Remove(oldAssignment);
            var existing = task.Resources.FirstOrDefault(r => r.ResourceId == newResource.Id);
            if (existing != null)
            {
                existing.EstimatedHours = (existing.EstimatedHours ?? 0) + row.Hours;
                existing.AllocationPercent = Math.Max(existing.AllocationPercent, oldAssignment.AllocationPercent);
                existing.Resource = newResource;
            }
            else
            {
                task.Resources.Add(new TaskResource
                {
                    ResourceId = newResource.Id,
                    Resource = newResource,
                    AllocationPercent = oldAssignment.AllocationPercent,
                    EstimatedHours = oldAssignment.EstimatedHours
                });
            }

            TaskScheduleService.RecalculateFinishFromAssignments(task);
            RecalcSummaryChain(task.Parent);
            _vm.Project.IsDirty = true;
            _vm.RefreshTasks();
            BuildMatrix();
            if (_selectedResource != null && _selectedSprint != null)
                ShowDetails(_selectedResource, _selectedSprint);
        }

        private double GetAllocatedHours(Resource resource, SprintColumn sprint)
        {
            return _vm.FlatTasks
                .Where(t => IsLeafTask(t) && BelongsToSprint(t, sprint))
                .SelectMany(t => t.Model.Resources.Where(r => r.ResourceId == resource.Id)
                    .Select(r => TaskScheduleService.GetAssignmentHours(t.Model, r)))
                .Sum();
        }

        private double? GetAverageAllocationPercent(Resource resource, SprintColumn sprint)
        {
            var assignments = _vm.FlatTasks
                .Where(t => IsLeafTask(t) && BelongsToSprint(t, sprint))
                .SelectMany(t => t.Model.Resources.Where(r => r.ResourceId == resource.Id)
                    .Select(r => new
                    {
                        Hours = TaskScheduleService.GetAssignmentHours(t.Model, r),
                        Percent = TaskScheduleService.NormalizeAllocationPercent(r.AllocationPercent)
                    }))
                .ToList();

            if (assignments.Count == 0)
                return null;

            var totalHours = assignments.Sum(a => a.Hours);
            return totalHours > 0
                ? assignments.Sum(a => a.Percent * a.Hours) / totalHours
                : assignments.Average(a => a.Percent);
        }

        private double GetSprintCapacityHours(Resource resource, SprintColumn sprint, double? allocationPercent)
        {
            var fullCapacityHours = sprint.CapacityHours > 0
                ? sprint.CapacityHours * Math.Max(0.0, resource.MaxUnitsPerDay) / ProjectCalendarService.WorkingHoursPerDay
                : Math.Max(1, _vm.Project.SprintDurationDays) * Math.Max(0.0, resource.MaxUnitsPerDay);

            return fullCapacityHours * (allocationPercent ?? 100.0) / 100.0;
        }

        private bool BelongsToSprint(TaskViewModel task, SprintColumn sprint)
        {
            if (sprint.Path != null)
                return string.Equals(task.Model.TfsIterationPath, sprint.Path, StringComparison.OrdinalIgnoreCase);

            return task.SprintNumber == sprint.Number;
        }

        private static bool IsLeafTask(TaskViewModel task) =>
            task.Model.Children.Count == 0;

        private System.Collections.Generic.IEnumerable<SprintColumn> BuildSprintColumns()
        {
            if (_vm.Project.Sprints.Count > 0)
            {
                foreach (var sprint in _vm.Project.Sprints.OrderBy(s => s.Number).ThenBy(s => s.Start))
                {
                    var capacityHours = sprint.End > sprint.Start
                        ? ProjectCalendarService.CountWorkingHours(sprint.Start, sprint.End)
                        : Math.Max(1, _vm.Project.SprintDurationDays) * ProjectCalendarService.WorkingHoursPerDay;
                    yield return new SprintColumn(
                        sprint.Number,
                        sprint.Path,
                        string.IsNullOrWhiteSpace(sprint.Name) ? $"Sprint {sprint.Number}" : sprint.Name,
                        capacityHours);
                }
                yield break;
            }

            foreach (var number in _vm.FlatTasks
                         .Where(t => t.SprintNumber > 0)
                         .Select(t => t.SprintNumber)
                         .Distinct()
                         .OrderBy(n => n))
            {
                yield return new SprintColumn(
                    number,
                    null,
                    $"Sprint {number}",
                    Math.Max(1, _vm.Project.SprintDurationDays) * ProjectCalendarService.WorkingHoursPerDay);
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private sealed record SprintColumn(int Number, string? Path, string Header, double CapacityHours);

        private void OnDetailsResourceComboDropDownClosed(object? sender, EventArgs e)
        {
            if (sender is not ComboBox { DataContext: AllocationDetailRow row } combo)
                return;

            var selected = combo.SelectedItem as Resource;
            var typed = NormalizeManualResourceName(combo.Text);

            if (selected == null)
            {
                if (string.IsNullOrEmpty(typed))
                    return;

                var existing = AvailableResources.FirstOrDefault(r => string.Equals(r.Name, typed, StringComparison.OrdinalIgnoreCase));
                Resource res;
                if (existing == null)
                {
                    var nextId = AvailableResources.Select(r => r.Id).DefaultIfEmpty(0).Max() + 1;
                    res = new Resource
                    {
                        Id = nextId,
                        Name = typed,
                        MaxUnitsPerDay = ProjectCalendarService.WorkingHoursPerDay,
                        IsImportedFromTfs = false
                    };
                    AvailableResources.Add(res);
                }
                else
                {
                    res = existing;
                }

                row.Resource = res;
            }
            else
            {
                row.Resource = selected;
            }
        }

        private void OnDetailsResourceComboKeyDown(object? sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            if (sender is not ComboBox) return;
            OnDetailsResourceComboDropDownClosed(sender, EventArgs.Empty);
        }

        public sealed class AllocationDetailRow : INotifyPropertyChanged
        {
            private readonly ResourceAllocationWindow _owner;
            private Resource _resource;

            public AllocationDetailRow(
                ResourceAllocationWindow owner,
                TaskViewModel task,
                TaskResource assignment,
                Resource resource)
            {
                _owner = owner;
                Task = task;
                Assignment = assignment;
                _resource = resource;
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            public TaskViewModel Task { get; }
            public TaskResource Assignment { get; }
            public double Hours
            {
                get => TaskScheduleService.GetAssignmentHours(Task.Model, Assignment);
                set
                {
                    var normalized = double.IsNaN(value) || value < 0 ? 0 : value;
                    Assignment.EstimatedHours = normalized;
                    TaskScheduleService.RecalculateFinishFromAssignments(Task.Model);
                    RecalcSummaryChain(Task.Model.Parent);
                    _owner._vm.Project.IsDirty = true;
                    _owner._vm.RefreshTasks();
                    _owner.BuildMatrix();
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Hours)));
                }
            }

            public double AllocationPercent
            {
                get => TaskScheduleService.NormalizeAllocationPercent(Assignment.AllocationPercent);
                set
                {
                    Assignment.AllocationPercent = TaskScheduleService.NormalizeAllocationPercent(value);
                    TaskScheduleService.RecalculateFinishFromAssignments(Task.Model);
                    RecalcSummaryChain(Task.Model.Parent);
                    _owner._vm.Project.IsDirty = true;
                    _owner._vm.RefreshTasks();
                    _owner.BuildMatrix();
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AllocationPercent)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Hours)));
                }
            }

            public Resource Resource
            {
                get => _resource;
                set
                {
                    if (value == null || value.Id == _resource.Id)
                        return;

                    _resource = value;
                    _owner.MoveAssignment(this, value);
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Resource)));
                }
            }
        }

        private static void RecalcSummaryChain(ProjectTask? task)
        {
            var current = task;
            while (current != null)
            {
                current.RecalcSummary();
                current = current.Parent;
            }
        }

        private string? PromptResourceName()
        {
            var dialog = new Window
            {
                Title = "Incluir recurso",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Width = 360,
                Height = 150,
                Background = Brushes.White
            };

            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = "Nome do recurso",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };
            Grid.SetRow(label, 0);
            root.Children.Add(label);

            var textBox = new TextBox
            {
                MinWidth = 300,
                Margin = new Thickness(0, 0, 0, 14)
            };
            Grid.SetRow(textBox, 1);
            root.Children.Add(textBox);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var ok = new Button { Content = "OK", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancelar", Width = 82, IsCancel = true };
            ok.Click += (_, _) =>
            {
                dialog.DialogResult = true;
                dialog.Close();
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);

            dialog.Content = root;
            textBox.Focus();
            return dialog.ShowDialog() == true ? textBox.Text : null;
        }

        private static string NormalizeManualResourceName(string? name) =>
            (name ?? string.Empty).Trim().TrimStart('*').Trim();
    }
}
