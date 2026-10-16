// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using NXProject.Models;
using NXProject.Services;

namespace NXProject.Views
{
    public partial class DevOpsDiscoveryWindow : Window
    {
        private readonly ObservableCollection<DiscoveryItem> _items = [];
        private readonly ICollectionView _itemsView;
        public List<DevOpsProject> SelectedProjects { get; private set; } = [];
        // Processo do Team Project (Agile/Scrum/CMMI/Basic) — mesmo para todos os itens
        // deste projeto; lido uma vez e gravado em cada DevOpsProject selecionado.
        private string _process = "";

        private readonly TfsConnectionOptions _options;
        private readonly HashSet<int> _existingIds;

        public DevOpsDiscoveryWindow(TfsConnectionOptions options, IEnumerable<DevOpsProject> existing)
        {
            InitializeComponent();
            _options = options;
            _existingIds = existing.Select(p => p.RootWorkItemId).ToHashSet();
            _itemsView = CollectionViewSource.GetDefaultView(_items);
            _itemsView.Filter = FilterItem;
            ItemsGrid.ItemsSource = _itemsView;
            _items.CollectionChanged += (_, _) => RefreshCount();
            SubtitleText.Text = AppStrings.Get("Disc_Subtitle", options.OrganizationUrl, options.TeamProject);
            LoadAsync(options, _existingIds);
        }

        /// <summary>
        /// Troca a origem sem fechar a tela: a busca é a mesma pergunta — "que projetos existem
        /// lá?" —, só muda o servidor que responde. Ter dois botões no Portfólio obrigava a
        /// pessoa a decidir antes de ver o que havia de cada lado.
        /// </summary>
        private async void OnOriginChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OriginCombo == null || !IsLoaded) return;
            _items.Clear();
            AddButton.IsEnabled = false;
            StatusText.Text = "";
            LoadingPanel.Visibility = Visibility.Visible;

            if (OriginCombo.SelectedIndex == 0) { LoadAsync(_options, _existingIds); return; }

            // GitProject: sem conexão configurada, abre a tela dela em vez de só recusar.
            var git = TfsConnectionStore.Load().GitProject;
            if (git?.IsConfigured != true)
            {
                // Abre a tela de conexoes JA na aba do GitHub Projects: e de la que falta algo.
                if (new TfsDevOpsConfigWindow(abaInicial: 1) { Owner = this }.ShowDialog() == true)
                    git = TfsConnectionStore.Load().GitProject;
                if (git?.IsConfigured != true) { OriginCombo.SelectedIndex = 0; return; }
            }

            try
            {
                SubtitleText.Text = AppStrings.Get("Disc_SubtitleGit", git!.Owner, git.ProjectNumber.ToString());
                var achados = await GitProjectDiscovery.FindInBoardAsync(git);
                LoadingPanel.Visibility = Visibility.Collapsed;
                foreach (var a in achados)
                {
                    var proj = GitProjectDiscovery.ToPortfolio(a);
                    var item = new DiscoveryItem
                    {
                        Id = proj.RootWorkItemId,
                        Title = a.Title,
                        Type = a.RootIssue > 0 ? "Project" : "Board",
                        Owner = a.Owner,
                        AdmGroup = ""
                    };
                    item.PropertyChanged += (_, _) => RefreshCount();
                    _items.Add(item);
                }
                if (_items.Count == 0) { StatusText.Text = AppStrings.Get("Disc_NoneFound"); return; }
                AddButton.IsEnabled = true;
                RefreshCount();
            }
            catch (Exception ex)
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
                StatusText.Text = AppStrings.Get("Disc_FetchError", ex.Message);
            }
        }

        private bool FilterItem(object obj)
        {
            if (obj is not DiscoveryItem item) return false;

            var nameFilter  = NameFilterBox?.Text?.Trim() ?? "";
            var ownerFilter = OwnerFilterBox?.Text?.Trim() ?? "";

            if (nameFilter.Length > 0 &&
                (item.Title?.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) ?? -1) < 0)
                return false;

            if (ownerFilter.Length > 0 &&
                (item.Owner?.IndexOf(ownerFilter, StringComparison.OrdinalIgnoreCase) ?? -1) < 0)
                return false;

            return true;
        }

        private void OnFilterChanged(object sender, TextChangedEventArgs e)
        {
            _itemsView?.Refresh();
            RefreshCount();
        }

        private void OnClearFilterClick(object sender, RoutedEventArgs e)
        {
            NameFilterBox.Text = "";
            OwnerFilterBox.Text = "";
            _itemsView?.Refresh();
            RefreshCount();
        }

        private async void LoadAsync(TfsConnectionOptions options, HashSet<int> existingIds)
        {
            try
            {
                var found = await TfsImportService.FetchRootWorkItemsAsync(options);
                _process = await TfsImportService.GetProcessAsync(options) ?? "";
                LoadingPanel.Visibility = Visibility.Collapsed;

                if (found.Count == 0)
                {
                    StatusText.Text = AppStrings.Get("Disc_NoneFound");
                    LoadingPanel.Visibility = Visibility.Visible;
                    return;
                }

                foreach (var (id, title, type, owner, admGroup) in found)
                {
                    var item = new DiscoveryItem { Id = id, Title = title, Type = type, Owner = owner, AdmGroup = admGroup };
                    item.PropertyChanged += (_, _) => RefreshCount();
                    _items.Add(item);
                }

                AddButton.IsEnabled = true;
                RefreshCount();
            }
            catch (Exception ex)
            {
                StatusText.Text = AppStrings.Get("Disc_FetchError", ex.Message);
            }
        }

        private void RefreshCount()
        {
            int sel   = _items.Count(i => i.IsSelected);
            int total = _items.Count;
            CountText.Text = AppStrings.Get("Disc_CountSelected", sel, total);
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
                .Select(i => new DevOpsProject { Name = i.Title, RootWorkItemId = i.Id, Owner = i.Owner,
                    // Processo e grupo administrador so existem no DevOps: vindo do GitHub
                    // ficam vazios, em vez de carimbar o processo do outro servidor.
                    Process = OriginCombo?.SelectedIndex == 1 ? "" : _process,
                    AdmGroupName = i.AdmGroup })
                .ToList();

            if (SelectedProjects.Count == 0)
            {
                MessageBox.Show(AppStrings.Get("Disc_SelectAtLeastOne"), AppStrings.Get("Port_DiscoveryTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            GravaOrigemDosProjetos();
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Guarda a ORIGEM de cada projeto trazido: ele veio deste servidor, e é dele que o
        /// Importar, o TaskBoard e o Sincronizar vão buscá-lo.
        ///
        /// Sem isto, um projeto descoberto no GitHub entrava no portfólio sem destino e a tela
        /// de importação continuava oferecendo o Azure DevOps — o NXProject sabia de onde o
        /// projeto tinha vindo e jogava essa informação fora.
        /// </summary>
        private void GravaOrigemDosProjetos()
        {
            var origem = OriginCombo?.SelectedIndex == 1
                ? NxBackendKind.GitProject : NxBackendKind.AzureDevOps;

            var opcoes = TfsConnectionStore.Load();
            foreach (var p in SelectedProjects)
            {
                var cfg = opcoes.PortfolioProjectConfigs.FirstOrDefault(c =>
                    string.Equals(c.ProjectName, p.Name, StringComparison.CurrentCultureIgnoreCase));
                if (cfg == null)
                {
                    cfg = new PortfolioProjectConfig { ProjectName = p.Name };
                    opcoes.PortfolioProjectConfigs.Add(cfg);
                }
                cfg.BackendKind = origem;
                p.BackendLabel = origem == NxBackendKind.GitProject ? "GitProject" : "Azure DevOps";
            }
            // rememberToken: os tokens ja estao salvos; gravar sem eles apagaria o que existe.
            TfsConnectionStore.Save(opcoes, rememberToken: true);
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
        public string Owner { get; set; } = "";
        public string AdmGroup { get; set; } = "";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
