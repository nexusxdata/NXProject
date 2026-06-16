using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using NXProject.Models;
using NXProject.Services;

namespace NXProject.Views
{
    public partial class ProjectPickerWindow : Window
    {
        private readonly ObservableCollection<ProjectEntry> _all = [];

        /// <summary>Configurações dos projetos selecionados, prontas para salvar.</summary>
        public List<PortfolioProjectConfig> SelectedConfigs { get; private set; } = [];

        public ProjectPickerWindow(List<DevOpsProject> devOpsProjects,
                                   List<PortfolioProjectConfig> existingConfigs)
        {
            InitializeComponent();

            foreach (var dp in devOpsProjects)
            {
                var cfg = existingConfigs.FirstOrDefault(c =>
                    string.Equals(c.ProjectName, dp.Name, StringComparison.OrdinalIgnoreCase));

                _all.Add(new ProjectEntry
                {
                    ProjectName = dp.Name,
                    FilePath    = cfg?.FilePath    ?? string.Empty,
                    IsOpex      = cfg?.IsOpex      ?? true,
                    CostCenter  = cfg?.CostCenter  ?? string.Empty,
                    IsSelected  = cfg != null && !string.IsNullOrWhiteSpace(cfg.FilePath)
                });
            }

            if (_all.Count == 0)
                EmptyMsg.Visibility = Visibility.Visible;

            ApplyFilter();
            RefreshSummary();
        }

        // ── Filtro ────────────────────────────────────────────────────────────
        private void ApplyFilter()
        {
            var q = SearchBox.Text.Trim();
            var filtered = string.IsNullOrEmpty(q)
                ? _all
                : (IEnumerable<ProjectEntry>)_all.Where(e =>
                    e.ProjectName.Contains(q, StringComparison.OrdinalIgnoreCase));

            ProjectList.Items.Clear();
            foreach (var entry in filtered)
                ProjectList.Items.Add(BuildRow(entry));
        }

        private UIElement BuildRow(ProjectEntry entry)
        {
            var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

            // CheckBox
            var cb = new CheckBox
            {
                IsChecked        = entry.IsSelected,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin           = new Thickness(4, 0, 0, 0)
            };
            cb.Checked   += (_, _) => { entry.IsSelected = true;  RefreshSummary(); };
            cb.Unchecked += (_, _) => { entry.IsSelected = false; RefreshSummary(); };
            Grid.SetColumn(cb, 0);

            // Nome do projeto
            var nameBlock = new TextBlock
            {
                Text              = entry.ProjectName,
                FontSize          = 12,
                FontWeight        = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming      = TextTrimming.CharacterEllipsis,
                Margin            = new Thickness(4, 4, 4, 4),
                ToolTip           = entry.ProjectName
            };
            Grid.SetColumn(nameBlock, 1);

            // Caminho do arquivo
            var fileBox = new TextBox
            {
                Text                     = entry.FilePath,
                FontSize                 = 11,
                Foreground               = string.IsNullOrWhiteSpace(entry.FilePath)
                    ? Brushes.Gray : Brushes.Black,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin                   = new Thickness(2, 2, 2, 2),
                IsReadOnly               = true,
                Background               = Brushes.Transparent,
                BorderThickness          = new Thickness(0),
                ToolTip                  = entry.FilePath
            };
            fileBox.MouseDoubleClick += (_, _) => BrowseFile(entry, fileBox, cb);
            Grid.SetColumn(fileBox, 2);

            // Botão "..."
            var browseBtn = new Button
            {
                Content          = "…",
                Width            = 28,
                Height           = 24,
                Margin           = new Thickness(2),
                FontSize         = 12,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip          = "Selecionar arquivo .nxproject"
            };
            browseBtn.Click += (_, _) => BrowseFile(entry, fileBox, cb);
            Grid.SetColumn(browseBtn, 3);

            grid.Children.Add(cb);
            grid.Children.Add(nameBlock);
            grid.Children.Add(fileBox);
            grid.Children.Add(browseBtn);

            // Zebra
            var border = new Border
            {
                Child           = grid,
                BorderBrush     = new SolidColorBrush(Color.FromRgb(230, 230, 230)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            return border;
        }

        private void BrowseFile(ProjectEntry entry, TextBox fileBox, CheckBox cb)
        {
            var dlg = new OpenFileDialog
            {
                Title           = $"Arquivo .nxproject — {entry.ProjectName}",
                Filter          = "Projetos NXProject (*.nxproject)|*.nxproject|Todos (*.*)|*.*",
                CheckFileExists = true
            };

            if (!string.IsNullOrWhiteSpace(entry.FilePath) && File.Exists(entry.FilePath))
                dlg.InitialDirectory = Path.GetDirectoryName(entry.FilePath);

            if (dlg.ShowDialog(this) != true) return;

            entry.FilePath     = dlg.FileName;
            entry.IsSelected   = true;
            fileBox.Text       = dlg.FileName;
            fileBox.Foreground = Brushes.Black;
            fileBox.ToolTip    = dlg.FileName;
            cb.IsChecked       = true;
            RefreshSummary();
        }

        private void RefreshSummary()
        {
            int selected = _all.Count(e => e.IsSelected && !string.IsNullOrWhiteSpace(e.FilePath));
            int semArq   = _all.Count(e => e.IsSelected && string.IsNullOrWhiteSpace(e.FilePath));
            SummaryText.Text = $"{selected} projeto(s) selecionado(s) com arquivo" +
                               (semArq > 0 ? $"  ·  {semArq} sem arquivo (não será carregado)" : "");
        }

        private void OnSearchChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
            RefreshSummary();
        }

        private void OnClearSearchClick(object sender, RoutedEventArgs e) => SearchBox.Text = string.Empty;

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            SelectedConfigs = _all
                .Where(x => x.IsSelected && !string.IsNullOrWhiteSpace(x.FilePath))
                .Select(x => new PortfolioProjectConfig
                {
                    ProjectName = x.ProjectName,
                    FilePath    = x.FilePath,
                    IsOpex      = x.IsOpex,
                    CostCenter  = x.CostCenter
                })
                .ToList();

            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

        // ── Modelo interno ────────────────────────────────────────────────────
        public sealed class ProjectEntry : INotifyPropertyChanged
        {
            private bool   _isSelected;
            private string _filePath = string.Empty;

            public string ProjectName { get; set; } = string.Empty;
            public bool   IsOpex      { get; set; } = true;
            public string CostCenter  { get; set; } = string.Empty;

            public string FilePath
            {
                get => _filePath;
                set { _filePath = value; OnPropertyChanged(); }
            }
            public bool IsSelected
            {
                get => _isSelected;
                set { _isSelected = value; OnPropertyChanged(); }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? n = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }
    }
}
