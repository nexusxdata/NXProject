// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NXProject.Services;

namespace NXProject.Views
{
    /// <summary>
    /// Destino de cada projeto do Portfólio: Azure DevOps, GitProject ou Projeto Local.
    ///
    /// A escolha mora aqui, e não nas telas de trabalho, porque é **do projeto**: o TaskBoard, o
    /// Importar e o Sincronizar nascem com o destino definido aqui, em vez de sempre em DevOps.
    ///
    /// Com "Projeto Local", a **pasta do projeto é obrigatória** — é onde ficam os artefatos e o
    /// log de BLOCK, que no DevOps viveriam no servidor. A tela sugere a pasta de mesmo nome ao
    /// lado do `.nxproject` e recusa salvar sem ela: deixar vazio seria prometer um board local
    /// que não tem onde guardar nada.
    /// </summary>
    public partial class PortfolioTargetWindow : Window
    {
        private sealed class Row
        {
            public PortfolioProjectConfig Config { get; init; } = new();
            public ComboBox Target { get; init; } = new();
            public TextBox Folder { get; init; } = new();
            public Button Browse { get; init; } = new();
            public CheckBox Sync { get; init; } = new();
        }

        private readonly List<Row> _rows = new();
        private readonly TfsConnectionOptions _options;

        public PortfolioTargetWindow(TfsConnectionOptions options)
        {
            InitializeComponent();
            _options = options;
            Loaded += (_, _) => BuildRows();
        }

        private void BuildRows()
        {
            var host = new StackPanel();
            foreach (var cfg in _options.PortfolioProjectConfigs.OrderBy(c => c.ProjectName,
                         StringComparer.CurrentCultureIgnoreCase))
            {
                var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });

                var nome = new TextBlock
                {
                    Text = cfg.ProjectName, VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 11.5, Margin = new Thickness(0, 0, 6, 0),
                    ToolTip = string.IsNullOrWhiteSpace(cfg.FilePath) ? null : cfg.FilePath
                };
                Grid.SetColumn(nome, 0); grid.Children.Add(nome);

                var target = new ComboBox { Height = 22, FontSize = 11, Margin = new Thickness(0, 0, 6, 0) };
                target.Items.Add(AppStrings.Get("PTgt_Default"));
                target.Items.Add(AppStrings.Get("Sprint_TargetDevOps"));
                target.Items.Add(AppStrings.Get("Sprint_TargetGitProject"));
                target.Items.Add(AppStrings.Get("Sprint_TargetLocal"));
                target.SelectedIndex = cfg.BackendKind switch
                {
                    NxBackendKind.AzureDevOps => 1,
                    NxBackendKind.GitProject => 2,
                    NxBackendKind.Local => 3,
                    _ => 0
                };
                Grid.SetColumn(target, 1); grid.Children.Add(target);

                var folderPanel = new DockPanel { Margin = new Thickness(0, 0, 6, 0) };
                var browse = new Button { Content = "…", Width = 28, Height = 22, FontSize = 11 };
                DockPanel.SetDock(browse, Dock.Right);
                var folder = new TextBox { Height = 22, FontSize = 11, Text = cfg.LocalFolderPath };
                folderPanel.Children.Add(browse);
                folderPanel.Children.Add(folder);
                Grid.SetColumn(folderPanel, 2); grid.Children.Add(folderPanel);

                var sync = new CheckBox
                {
                    Content = AppStrings.Get("PTgt_SyncLabel"),
                    IsChecked = cfg.SyncLocalBoardToDevOps,
                    FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = AppStrings.Get("PTgt_SyncTip")
                };
                Grid.SetColumn(sync, 3); grid.Children.Add(sync);

                var row = new Row { Config = cfg, Target = target, Folder = folder, Browse = browse, Sync = sync };
                target.SelectionChanged += (_, _) => ApplyRowState(row);
                browse.Click += (_, _) => PickFolder(row);
                _rows.Add(row);
                ApplyRowState(row);

                host.Children.Add(grid);
            }

            RowsList.Items.Clear();
            RowsList.Items.Add(host);
            if (_rows.Count == 0) StatusText.Text = AppStrings.Get("PTgt_Empty");
        }

        /// <summary>
        /// Liga só o que faz sentido para o destino escolhido: pasta é coisa de Projeto Local, e
        /// o "sincronizar no DevOps" só existe para projeto local que veio do DevOps.
        /// </summary>
        private void ApplyRowState(Row row)
        {
            var local = row.Target.SelectedIndex == 3;
            row.Folder.IsEnabled = local;
            row.Browse.IsEnabled = local;
            row.Sync.IsEnabled = local;
            if (!local) return;

            // Sugere a pasta de mesmo nome ao lado do arquivo — o padrão que a pessoa esperaria.
            if (string.IsNullOrWhiteSpace(row.Folder.Text))
                row.Folder.Text = LocalProjectFolder.Resolve(row.Config.FilePath, null);
        }

        private void PickFolder(Row row)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = AppStrings.Get("PTgt_PickFolder"),
                InitialDirectory = Directory.Exists(row.Folder.Text) ? row.Folder.Text : ""
            };
            if (dlg.ShowDialog(this) == true) row.Folder.Text = dlg.FolderName;
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            // Projeto Local sem pasta não salva: o board local precisa dela para artefatos e log.
            var semPasta = _rows.FirstOrDefault(r => r.Target.SelectedIndex == 3
                                                 && string.IsNullOrWhiteSpace(r.Folder.Text));
            if (semPasta != null)
            {
                StatusText.Text = AppStrings.Get("PTgt_FolderRequired", semPasta.Config.ProjectName);
                semPasta.Folder.Focus();
                return;
            }

            foreach (var row in _rows)
            {
                row.Config.BackendKind = row.Target.SelectedIndex switch
                {
                    1 => NxBackendKind.AzureDevOps,
                    2 => NxBackendKind.GitProject,
                    3 => NxBackendKind.Local,
                    _ => NxBackendKind.Default
                };
                row.Config.LocalFolderPath = row.Target.SelectedIndex == 3 ? row.Folder.Text.Trim() : "";
                row.Config.SyncLocalBoardToDevOps = row.Target.SelectedIndex == 3 && row.Sync.IsChecked == true;

                // A pasta é criada na hora de salvar, não ao abrir o board: aqui a pessoa está
                // justamente dizendo onde ela deve ficar.
                if (row.Config.BackendKind == NxBackendKind.Local
                    && !LocalProjectFolder.Exists(row.Config.LocalFolderPath))
                {
                    try { LocalProjectFolder.Create(row.Config.LocalFolderPath); }
                    catch (Exception ex)
                    {
                        StatusText.Text = AppStrings.Get("PTgt_FolderError", row.Config.ProjectName, ex.Message);
                        return;
                    }
                }
            }

            TfsConnectionStore.Save(_options, !string.IsNullOrEmpty(_options.PersonalAccessToken));
            DialogResult = true;
        }
    }
}
