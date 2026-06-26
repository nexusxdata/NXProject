using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
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

        public bool HasChanges { get; private set; }

        public TechLeadTaskReviewWindow(Project project, List<ProjectTask> stories)
        {
            _project = project;
            _stories = stories;
            InitializeComponent();
            Loaded += async (_, _) => await LoadAsync();
        }

        private async Task LoadAsync()
        {
            StatusText.Text = "Buscando Tasks no DevOps...";
            AddSelectedButton.IsEnabled = false;

            var options = TfsConnectionStore.Load("NXProject.Community");
            var rows = new List<TaskReviewRow>();

            // Set de TfsIds já no cronograma como Tasks
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
                    rows.Add(new TaskReviewRow
                    {
                        StoryId    = story.TfsId!.Value,
                        StoryName  = story.Name,
                        StoryTask  = story,
                        TaskId     = t.TfsId,
                        Title      = t.Title,
                        State      = t.State ?? "",
                        EstimatedHours = t.EstimatedHours,
                        Priority   = t.Priority,
                        AssignedTo = t.AssignedTo ?? "",
                        InSchedule = inScheduleIds.Contains(t.TfsId),
                    });
                }
            }

            foreach (var r in rows) _allRows.Add(r);

            // Popula filtros
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
            int inSched = visible.Count(r => r.InSchedule);
            TotalsText.Text = $"Total visível: {visible.Count} Tasks | {totalH:0.#}h estimadas | {inSched} já no cronograma";
            AddSelectedButton.IsEnabled = _allRows.Any(r => r.IsSelected && !r.InSchedule);
        }

        private void OnStoryFilterChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            _view?.Refresh();
            UpdateTotals();
        }

        private void OnStateFilterChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            _view?.Refresh();
            UpdateTotals();
        }

        private void OnTasksGridSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
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

            foreach (var r in toAdd)
            {
                var pt = new ProjectTask
                {
                    Name           = r.Title,
                    TfsId          = r.TaskId,
                    TfsType        = "Task",
                    EstimatedHours = r.EstimatedHours > 0 ? r.EstimatedHours : null,
                    Priority       = r.Priority > 0 ? r.Priority : 5,
                    TfsState       = r.State,
                    Start          = r.StoryTask.Start,
                    Finish         = r.StoryTask.Finish,
                };
                r.StoryTask.Children.Add(pt);
                r.InSchedule = true;
                r.IsSelected = false;
            }

            HasChanges = true;
            UpdateTotals();
            MessageBox.Show($"{toAdd.Count} Task(s) adicionadas ao cronograma.", "Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }

    public class TaskReviewRow : INotifyPropertyChanged
    {
        public int StoryId { get; set; }
        public string StoryName { get; set; } = "";
        public ProjectTask StoryTask { get; set; } = null!;
        public int TaskId { get; set; }
        public string Title { get; set; } = "";
        public string State { get; set; } = "";
        public double EstimatedHours { get; set; }
        public int Priority { get; set; }
        public string AssignedTo { get; set; } = "";

        private bool _inSchedule;
        public bool InSchedule
        {
            get => _inSchedule;
            set { _inSchedule = value; OnPropertyChanged(); OnPropertyChanged(nameof(InScheduleDisplay)); }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public string EstimatedHoursDisplay => EstimatedHours > 0 ? $"{EstimatedHours:0.#}h" : "-";
        public string InScheduleDisplay => InSchedule ? "✔ Sim" : "Não";

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new(p));
    }
}
