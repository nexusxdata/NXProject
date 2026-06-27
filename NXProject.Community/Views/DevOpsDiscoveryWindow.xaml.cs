using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using NXProject.Models;
using NXProject.Services;

namespace NXProject.Views
{
    public partial class DevOpsDiscoveryWindow : Window
    {
        private readonly ObservableCollection<DiscoveryItem> _items = [];
        public List<DevOpsProject> SelectedProjects { get; private set; } = [];

        public DevOpsDiscoveryWindow(TfsConnectionOptions options, IEnumerable<DevOpsProject> existing)
        {
            InitializeComponent();
            ItemsGrid.ItemsSource = _items;
            _items.CollectionChanged += (_, _) => RefreshCount();
            SubtitleText.Text = $"Organização: {options.OrganizationUrl}  |  Projeto: {options.TeamProject}";
            LoadAsync(options, existing.Select(p => p.RootWorkItemId).ToHashSet());
        }

        private async void LoadAsync(TfsConnectionOptions options, HashSet<int> existingIds)
        {
            try
            {
                var found = await TfsImportService.FetchRootWorkItemsAsync(options);
                LoadingPanel.Visibility = Visibility.Collapsed;

                if (found.Count == 0)
                {
                    StatusText.Text = "Nenhum work item raiz encontrado no projeto.";
                    LoadingPanel.Visibility = Visibility.Visible;
                    return;
                }

                foreach (var (id, title, type) in found)
                {
                    var item = new DiscoveryItem { Id = id, Title = title, Type = type };
                    item.PropertyChanged += (_, _) => RefreshCount();
                    _items.Add(item);
                }

                AddButton.IsEnabled = true;
                RefreshCount();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Erro ao buscar dados: {ex.Message}";
            }
        }

        private void RefreshCount()
        {
            int sel   = _items.Count(i => i.IsSelected);
            int total = _items.Count;
            CountText.Text = $"{sel} de {total} selecionados";
        }

        private void OnSelectAllClick(object sender, RoutedEventArgs e)
        {
            foreach (var i in _items) i.IsSelected = true;
        }

        private void OnDeselectAllClick(object sender, RoutedEventArgs e)
        {
            foreach (var i in _items) i.IsSelected = false;
        }

        private void OnAddClick(object sender, RoutedEventArgs e)
        {
            SelectedProjects = _items
                .Where(i => i.IsSelected)
                .Select(i => new DevOpsProject { Name = i.Title, RootWorkItemId = i.Id })
                .ToList();

            if (SelectedProjects.Count == 0)
            {
                MessageBox.Show("Selecione ao menos um item.", "Discovery", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class DiscoveryItem : INotifyPropertyChanged
    {
        public int    Id    { get; set; }
        public string Title { get; set; } = "";
        public string Type  { get; set; } = "";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
