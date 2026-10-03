// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.IO;
using System.Linq;
using System.Windows;
using NXProject.Models;
using NXProject.Services;

namespace NXProject.Views
{
    public partial class DevOpsProjectEditWindow : Window
    {
        public DevOpsProject? Result { get; private set; }

        private readonly string _process;
        private readonly bool? _readOnly;
        private readonly string _admGroup;

        /// <summary>
        /// Origem do projeto (DevOps, GitProject ou Projeto Local) e a pasta de artefatos.
        ///
        /// Editadas aqui, mas guardadas em <see cref="PortfolioProjectConfig"/>, e não na lista de
        /// projetos: a lista pode ser compartilhada em rede, e tanto o destino quanto o caminho da
        /// pasta são de quem usa, naquela máquina. Assim continua havendo UMA fonte da verdade —
        /// a mesma que o TaskBoard, o Importar e o Sincronizar leem.
        /// </summary>
        private readonly TfsConnectionOptions _options = TfsConnectionStore.Load();
        private readonly string _originalName;

        public DevOpsProjectEditWindow(string name = "", int id = 0,
                                       bool isOpex = true, string costCenter = "",
                                       string costCenterSource = "", string process = "",
                                       bool? readOnly = null, string admGroup = "",
                                       bool loadTasksOnImport = false)
        {
            InitializeComponent();
            NameBox.Text = name;
            IdBox.Text   = id > 0 ? id.ToString() : "";
            _process = process ?? "";
            ProcessBox.Text = string.IsNullOrWhiteSpace(_process)
                ? AppStrings.Get("PortEdit_ProcessUnknown") : _process;
            _readOnly = readOnly;
            _admGroup = admGroup ?? "";
            AdmGroupBox.Text = string.IsNullOrWhiteSpace(_admGroup)
                ? AppStrings.Get("PortEdit_AdmGroupNone") : _admGroup;

            TypeBox.Items.Add("OPEX");
            TypeBox.Items.Add("CAPEX");
            TypeBox.Items.Add("EPIC");

            var source = string.IsNullOrWhiteSpace(costCenterSource)
                ? (isOpex ? "OPEX" : "CAPEX")
                : costCenterSource.ToUpperInvariant();

            TypeBox.SelectedIndex = source switch { "CAPEX" => 1, "EPIC" => 2, _ => 0 };

            CcBox.Text = costCenter;
            LoadTasksOnImportBox.IsChecked = loadTasksOnImport;

            _originalName = name ?? "";
            BackendBox.Items.Add(AppStrings.Get("PTgt_Default"));
            BackendBox.Items.Add(AppStrings.Get("Sprint_TargetDevOps"));
            BackendBox.Items.Add(AppStrings.Get("Sprint_TargetGitProject"));
            BackendBox.Items.Add(AppStrings.Get("Sprint_TargetLocal"));
            var cfg = FindConfig();
            BackendBox.SelectedIndex = (cfg?.BackendKind ?? NxBackendKind.Default) switch
            {
                NxBackendKind.AzureDevOps => 1,
                NxBackendKind.GitProject => 2,
                NxBackendKind.Local => 3,
                _ => 0
            };
            FolderBox.Text = cfg?.LocalFolderPath ?? "";
            ApplyBackendState();

            Loaded += (_, _) => NameBox.Focus();
        }

        /// <summary>Configuração deste projeto, achada pelo nome (a chave do cadastro).</summary>
        private PortfolioProjectConfig? FindConfig() =>
            string.IsNullOrWhiteSpace(_originalName) ? null
            : _options.PortfolioProjectConfigs.FirstOrDefault(c =>
                string.Equals(c.ProjectName, _originalName, StringComparison.CurrentCultureIgnoreCase));

        private bool IsLocal => BackendBox.SelectedIndex == 3;

        /// <summary>
        /// Pasta é coisa de Projeto Local: nos outros destinos os artefatos ficam no servidor, e
        /// um campo ligado ali prometeria algo que não acontece.
        /// </summary>
        private void ApplyBackendState()
        {
            if (FolderBox == null) return;
            var local = IsLocal;
            FolderLabel.IsEnabled = FolderBox.IsEnabled = FolderBrowse.IsEnabled = FolderHint.IsEnabled = local;
            FolderBox.Background = local ? System.Windows.Media.Brushes.White
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF3, 0xF5, 0xF8));
            // Sugere a pasta de mesmo nome ao lado do arquivo — o padrão que a pessoa esperaria.
            if (local && string.IsNullOrWhiteSpace(FolderBox.Text) && FindConfig() is { } c
                && !string.IsNullOrWhiteSpace(c.FilePath))
                FolderBox.Text = LocalProjectFolder.Resolve(c.FilePath, null);
        }

        private void OnBackendChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
            => ApplyBackendState();

        private void OnPickFolderClick(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = AppStrings.Get("PTgt_PickFolder"),
                InitialDirectory = Directory.Exists(FolderBox.Text) ? FolderBox.Text : ""
            };
            if (dlg.ShowDialog(this) == true) FolderBox.Text = dlg.FolderName;
        }

        /// <summary>
        /// Grava destino e pasta na configuração do projeto. Renomear o projeto leva a
        /// configuração junto: a chave é o nome, e deixar para trás perderia a escolha.
        /// </summary>
        private bool SaveBackendConfig(string novoNome)
        {
            var kind = BackendBox.SelectedIndex switch
            {
                1 => NxBackendKind.AzureDevOps,
                2 => NxBackendKind.GitProject,
                3 => NxBackendKind.Local,
                _ => NxBackendKind.Default
            };
            var pasta = IsLocal ? (FolderBox.Text?.Trim() ?? "") : "";

            // Projeto Local sem pasta não salva: é onde ficam os artefatos e o log de BLOCK.
            if (kind == NxBackendKind.Local && string.IsNullOrWhiteSpace(pasta))
            {
                MessageBox.Show(this, AppStrings.Get("PTgt_FolderRequired", novoNome),
                    AppStrings.Get("Common_Validation"), MessageBoxButton.OK, MessageBoxImage.Warning);
                FolderBox.Focus();
                return false;
            }

            var cfg = FindConfig();
            if (cfg == null)
            {
                // Projeto ainda sem configuração (nunca importado): nasce aqui, só com o destino.
                if (kind == NxBackendKind.Default && string.IsNullOrEmpty(pasta)) return true;
                cfg = new PortfolioProjectConfig { ProjectName = novoNome };
                _options.PortfolioProjectConfigs.Add(cfg);
            }
            cfg.ProjectName = novoNome;
            cfg.BackendKind = kind;
            cfg.LocalFolderPath = pasta;

            if (kind == NxBackendKind.Local && !LocalProjectFolder.Exists(pasta))
            {
                try { LocalProjectFolder.Create(pasta); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, AppStrings.Get("PTgt_FolderError", novoNome, ex.Message),
                        AppStrings.Get("Common_Validation"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }

            TfsConnectionStore.Save(_options, !string.IsNullOrEmpty(_options.PersonalAccessToken));
            return true;
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            var name = NameBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(AppStrings.Get("PortEdit_NameRequired"), AppStrings.Get("Common_Validation"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(IdBox.Text?.Trim(), out var id) || id <= 0)
            {
                MessageBox.Show(AppStrings.Get("PortEdit_IdInvalid"), AppStrings.Get("Common_Validation"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!SaveBackendConfig(name)) return;

            var src = (TypeBox.SelectedItem as string) ?? "OPEX";
            Result = new DevOpsProject
            {
                Name             = name,
                RootWorkItemId   = id,
                IsOpex           = src != "CAPEX",
                CostCenter       = CcBox.Text?.Trim() ?? "",
                CostCenterSource = src,
                Process          = _process,   // read-only nesta tela; preserva o lido do DevOps
                ReadOnly         = _readOnly,   // preservado (compat.); não editável nesta tela
                AdmGroupName     = _admGroup,   // read-only; vem do campo Adm_NX na importação
                LoadTasksOnImport = LoadTasksOnImportBox.IsChecked == true
            };
            DialogResult = true;
            Close();
        }
    }
}
