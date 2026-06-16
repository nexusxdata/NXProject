using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NXProject.Services;

namespace NXProject.Views
{
    public partial class ProjectPickerWindow : Window
    {
        private readonly ObservableCollection<ProjectEntry> _all = [];
        private readonly List<string> _initialSelected;

        public List<string> SelectedPaths { get; private set; } = [];

        public ProjectPickerWindow(List<string> knownPaths, List<string> selectedPaths)
        {
            InitializeComponent();
            _initialSelected = selectedPaths;

            foreach (var path in knownPaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                bool exists = File.Exists(path);
                _all.Add(new ProjectEntry
                {
                    FilePath   = path,
                    IsSelected = selectedPaths.Contains(path, StringComparer.OrdinalIgnoreCase),
                    Exists     = exists,
                    DisplayName = exists
                        ? TryReadProjectName(path) ?? Path.GetFileNameWithoutExtension(path)
                        : $"[não encontrado] {Path.GetFileNameWithoutExtension(path)}"
                });
            }

            ApplyFilter();
            RefreshSummary();
        }

        private static string? TryReadProjectName(string path)
        {
            try
            {
                var project = XmlProjectService.Load(path);
                return string.IsNullOrWhiteSpace(project.Name)
                    ? Path.GetFileNameWithoutExtension(path)
                    : project.Name;
            }
            catch { return null; }
        }

        private void ApplyFilter()
        {
            var query = SearchBox.Text.Trim();
            var filtered = string.IsNullOrEmpty(query)
                ? _all
                : _all.Where(e => e.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                               || e.FilePath.Contains(query, StringComparison.OrdinalIgnoreCase));

            ProjectList.Items.Clear();
            foreach (var entry in filtered)
            {
                var cb = new CheckBox
                {
                    Content   = BuildEntryContent(entry),
                    IsChecked = entry.IsSelected,
                    Tag       = entry,
                    Margin    = new Thickness(4, 2, 4, 2),
                    IsEnabled = entry.Exists
                };
                cb.Checked   += (_, _) => { entry.IsSelected = true;  RefreshSummary(); };
                cb.Unchecked += (_, _) => { entry.IsSelected = false; RefreshSummary(); };
                ProjectList.Items.Add(cb);
            }
        }

        private static StackPanel BuildEntryContent(ProjectEntry e)
        {
            var sp = new StackPanel();
            var nameBlock = new TextBlock
            {
                Text       = e.DisplayName,
                FontWeight = FontWeights.SemiBold,
                FontSize   = 12,
                Foreground = e.Exists
                    ? System.Windows.Media.Brushes.Black
                    : System.Windows.Media.Brushes.Gray
            };
            var pathBlock = new TextBlock
            {
                Text       = e.FilePath,
                FontSize   = 10,
                Foreground = System.Windows.Media.Brushes.Gray,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            sp.Children.Add(nameBlock);
            sp.Children.Add(pathBlock);
            return sp;
        }

        private void RefreshSummary()
        {
            int total    = _all.Count;
            int selected = _all.Count(e => e.IsSelected);
            SummaryText.Text = $"{selected} de {total} projeto(s) marcado(s)";
        }

        private void OnSearchChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
            RefreshSummary();
        }

        private void OnClearSearchClick(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = string.Empty;
        }

        private void OnAddFileClick(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title            = "Adicionar projeto ao portfólio",
                Filter           = "Projetos NXProject (*.nxproject)|*.nxproject|Todos os arquivos (*.*)|*.*",
                Multiselect      = true,
                CheckFileExists  = true
            };
            if (dlg.ShowDialog(this) != true) return;

            foreach (var path in dlg.FileNames)
            {
                if (_all.Any(e => string.Equals(e.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var entry = new ProjectEntry
                {
                    FilePath    = path,
                    IsSelected  = true,
                    Exists      = true,
                    DisplayName = TryReadProjectName(path) ?? Path.GetFileNameWithoutExtension(path)
                };
                _all.Add(entry);
            }

            // Persiste os caminhos conhecidos
            SaveKnownPaths();
            ApplyFilter();
            RefreshSummary();
        }

        private void OnRemoveFromListClick(object sender, RoutedEventArgs e)
        {
            if (ProjectList.SelectedItem is not CheckBox cb || cb.Tag is not ProjectEntry entry) return;
            _all.Remove(entry);
            SaveKnownPaths();
            ApplyFilter();
            RefreshSummary();
        }

        private void SaveKnownPaths()
        {
            var opts = TfsConnectionStore.Load();
            opts.PortfolioProjectPaths = _all.Select(e => e.FilePath).ToList();
            TfsConnectionStore.Save(opts, !string.IsNullOrEmpty(opts.PersonalAccessToken));
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            SelectedPaths = _all.Where(x => x.IsSelected).Select(x => x.FilePath).ToList();
            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

        public sealed class ProjectEntry : INotifyPropertyChanged
        {
            private bool _isSelected;
            public string FilePath    { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public bool   Exists      { get; set; } = true;
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
