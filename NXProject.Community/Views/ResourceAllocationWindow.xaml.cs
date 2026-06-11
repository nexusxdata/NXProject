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
                AddCell(resource.Name, row + 1, 0, true, horizontalAlignment: HorizontalAlignment.Left);

                for (int col = 0; col < sprints.Count; col++)
                {
                    var sprint = sprints[col];
                    var hours = GetAllocatedHours(resource, sprint);
                    AddHoursButton(resource, sprint, hours, row + 1, col + 1);
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

        private void AddHoursButton(Resource resource, SprintColumn sprint, double hours, int row, int col)
        {
            var button = new Button
            {
                Content = hours > 0 ? $"{hours:0.##} h" : "-",
                Tag = (resource, sprint),
                BorderThickness = new Thickness(0),
                Background = hours > 0
                    ? new SolidColorBrush(Color.FromRgb(230, 242, 255))
                    : Brushes.White,
                Foreground = hours > 0
                    ? new SolidColorBrush(Color.FromRgb(31, 78, 161))
                    : new SolidColorBrush(Color.FromRgb(130, 130, 130)),
                FontWeight = hours > 0 ? FontWeights.SemiBold : FontWeights.Normal,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = hours > 0 ? "Ver atividades desta alocacao" : "Sem atividades"
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
            DetailsTitle.Text = $"{resource.Name} - {sprint.Header}";
            SelectedDetails.Clear();

            foreach (var task in _vm.FlatTasks.Where(t => BelongsToSprint(t, sprint)))
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

            _vm.Project.IsDirty = true;
            _vm.RefreshTasks();
            BuildMatrix();
            if (_selectedResource != null && _selectedSprint != null)
                ShowDetails(_selectedResource, _selectedSprint);
        }

        private double GetAllocatedHours(Resource resource, SprintColumn sprint)
        {
            return _vm.FlatTasks
                .Where(t => BelongsToSprint(t, sprint))
                .SelectMany(t => t.Model.Resources.Where(r => r.ResourceId == resource.Id)
                    .Select(r => GetAssignmentHours(t, r)))
                .Sum();
        }

        private static double GetAssignmentHours(TaskViewModel task, TaskResource assignment)
        {
            if (assignment.EstimatedHours.HasValue && assignment.EstimatedHours.Value > 0)
                return assignment.EstimatedHours.Value;

            return Math.Max(0, task.DurationDays)
                   * (assignment.Resource?.MaxUnitsPerDay ?? ProjectCalendarService.WorkingHoursPerDay)
                   * Math.Max(0, assignment.AllocationPercent) / 100.0;
        }

        private bool BelongsToSprint(TaskViewModel task, SprintColumn sprint)
        {
            if (sprint.Path != null)
                return string.Equals(task.Model.TfsIterationPath, sprint.Path, StringComparison.OrdinalIgnoreCase);

            return task.SprintNumber == sprint.Number;
        }

        private System.Collections.Generic.IEnumerable<SprintColumn> BuildSprintColumns()
        {
            if (_vm.Project.Sprints.Count > 0)
            {
                foreach (var sprint in _vm.Project.Sprints.OrderBy(s => s.Number).ThenBy(s => s.Start))
                {
                    yield return new SprintColumn(
                        sprint.Number,
                        sprint.Path,
                        string.IsNullOrWhiteSpace(sprint.Name) ? $"Sprint {sprint.Number}" : sprint.Name);
                }
                yield break;
            }

            foreach (var number in _vm.FlatTasks
                         .Where(t => t.SprintNumber > 0)
                         .Select(t => t.SprintNumber)
                         .Distinct()
                         .OrderBy(n => n))
            {
                yield return new SprintColumn(number, null, $"Sprint {number}");
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private sealed record SprintColumn(int Number, string? Path, string Header);

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
                get => GetAssignmentHours(Task, Assignment);
                set
                {
                    var normalized = double.IsNaN(value) || value < 0 ? 0 : value;
                    Assignment.EstimatedHours = normalized;
                    _owner._vm.Project.IsDirty = true;
                    _owner.BuildMatrix();
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
    }
}
