using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using NXProject.ViewModels;

namespace NXProject.Views
{
    public partial class PredecessorPickerWindow : Window
    {
        private readonly ICollectionView _view;
        private readonly List<string> _externalIds;

        public ObservableCollection<PredecessorRow> CandidateRows { get; } = new();

        public string SelectedPredecessorsText { get; private set; } = string.Empty;

        public PredecessorPickerWindow(TaskViewModel targetTask, IEnumerable<TaskViewModel> candidates)
        {
            InitializeComponent();
            DataContext = this;

            TargetTaskText.Text = $"Atividade: {targetTask.DisplayId} - {targetTask.Name}";

            var selectedIds = targetTask.PredecessorsText
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var task in candidates)
            {
                var row = new PredecessorRow(task.DisplayId, task.Name)
                {
                    IsSelected = selectedIds.Contains(task.DisplayId)
                };
                row.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(PredecessorRow.IsSelected))
                        RefreshSelectedSummary();
                };
                CandidateRows.Add(row);
            }

            var candidateIds = CandidateRows
                .Select(r => r.DisplayId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _externalIds = selectedIds
                .Where(id => !candidateIds.Contains(id))
                .OrderBy(id => id)
                .ToList();

            _view = CollectionViewSource.GetDefaultView(CandidateRows);
            _view.Filter = FilterRow;
            RefreshSelectedSummary();
        }

        private bool FilterRow(object item)
        {
            if (item is not PredecessorRow row)
                return false;

            var query = SearchBox.Text?.Trim() ?? string.Empty;
            return string.IsNullOrWhiteSpace(query)
                   || row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                   || row.DisplayId.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        private void OnSearchTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _view.Refresh();
            RefreshSelectedSummary();
        }

        private void OnClearSearchClick(object sender, RoutedEventArgs e)
        {
            SearchBox.Clear();
            SearchBox.Focus();
        }

        private void RefreshSelectedSummary()
        {
            var selected = CandidateRows
                .Where(r => r.IsSelected)
                .OrderBy(r => r.Name)
                .Select(r => $"{r.DisplayId} - {r.Name}")
                .ToList();

            var allSelected = _externalIds
                .Select(id => $"{id} - fora da lista filtrada")
                .Concat(selected)
                .ToList();

            SelectedSummaryText.Text = allSelected.Count == 0
                ? "Nenhuma predecessora marcada."
                : string.Join(Environment.NewLine, allSelected);

            CountText.Text = $"{allSelected.Count} marcada(s)";
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            var ids = _externalIds
                .Concat(CandidateRows.Where(r => r.IsSelected).Select(r => r.DisplayId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            SelectedPredecessorsText = string.Join(",", ids);
            DialogResult = true;
        }

        public sealed class PredecessorRow : INotifyPropertyChanged
        {
            private bool _isSelected;

            public PredecessorRow(string displayId, string name)
            {
                DisplayId = displayId;
                Name = name;
            }

            public string DisplayId { get; }
            public string Name { get; }

            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected == value)
                        return;

                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }
    }
}
