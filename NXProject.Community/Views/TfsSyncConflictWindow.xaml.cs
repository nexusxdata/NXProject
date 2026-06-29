using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NXProject.Services;

namespace NXProject.Views
{
    public partial class TfsSyncConflictWindow : Window
    {
        private readonly ObservableCollection<ConflictRow> _rows;
        private readonly TfsConnectionOptions _options;
        private readonly NXProject.Models.Project _project;

        public TfsSyncConflictWindow(
            IEnumerable<TfsImportService.SyncConflictItem> items,
            NXProject.Models.Project project,
            TfsConnectionOptions options)
        {
            InitializeComponent();
            _options = options;
            _project = project;

            _rows = new ObservableCollection<ConflictRow>(
                items.Select(i => new ConflictRow(i)));

            foreach (var r in _rows)
                r.PropertyChanged += (_, _) => UpdateSummary();

            ConflictGrid.ItemsSource = _rows;
            UpdateSummary();
        }

        private void UpdateSummary()
        {
            var sel = _rows.Count(r => r.IsSelected);
            SummaryText.Text = sel == 0
                ? "Nenhum item selecionado."
                : $"{sel} item(ns) selecionado(s) para sobrescrever.";
            OverwriteButton.IsEnabled = sel > 0;
        }

        private void OnConflictSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ConflictGrid.SelectedItem is not ConflictRow row)
            {
                DiffList.ItemsSource = null;
                DetailHeader.Text = "Selecione um item acima para ver a comparação de atributos";
                return;
            }

            DetailHeader.Text = $"Comparação: {row.TfsType} #{row.TfsId} — {row.LocalTitle}";
            DiffList.ItemsSource = BuildDiffRows(row.Item);
        }

        private static List<DiffRow> BuildDiffRows(TfsImportService.SyncConflictItem item)
        {
            var rows = new List<DiffRow>();

            rows.Add(DiffRow.Build("Título",   item.LocalTitle,                      item.TfsTitle));
            rows.Add(DiffRow.Build("Estado",   item.LocalState,                      item.TfsState));
            rows.Add(DiffRow.Build("Tags",     item.LocalTags,                       item.TfsTags));
            rows.Add(DiffRow.Build("HH Est.",  FormatHours(item.LocalHours),         FormatHours(item.TfsHours)));
            rows.Add(DiffRow.Build("Início",   FormatDate(item.LocalStart),          FormatDate(item.TfsStart)));
            rows.Add(DiffRow.Build("Fim",      FormatDate(item.LocalFinish),         FormatDate(item.TfsFinish)));

            return rows;
        }

        private static string FormatHours(double? h) => h.HasValue ? $"{h.Value:0.#}h" : "—";
        private static string FormatDate(DateTime? d) => d.HasValue ? d.Value.ToString("dd/MM/yyyy") : "—";

        private void OnSelectAll(object sender, RoutedEventArgs e)
        {
            foreach (var r in _rows) r.IsSelected = true;
        }

        private void OnDeselectAll(object sender, RoutedEventArgs e)
        {
            foreach (var r in _rows) r.IsSelected = false;
        }

        private async void OnOverwriteClick(object sender, RoutedEventArgs e)
        {
            var ids = _rows.Where(r => r.IsSelected).Select(r => r.TfsId).ToHashSet();
            if (ids.Count == 0) return;

            OverwriteButton.IsEnabled = false;
            OverwriteButton.Content = "Sincronizando...";

            try
            {
                foreach (var row in _rows.Where(r => r.IsSelected))
                    row.Item.Task.HasSyncConflict = false;

                await TfsImportService.SyncAsync(_project, _options, forceOverwriteIds: ids);

                MessageBox.Show(
                    $"{ids.Count} item(ns) sobrescrito(s) com sucesso no DevOps.",
                    "Sobrescrever", MessageBoxButton.OK, MessageBoxImage.Information);

                foreach (var id in ids)
                {
                    var row = _rows.FirstOrDefault(r => r.TfsId == id);
                    if (row != null) _rows.Remove(row);
                }

                DiffList.ItemsSource = null;
                DetailHeader.Text = "Selecione um item acima para ver a comparação de atributos";

                if (_rows.Count == 0)
                {
                    DialogResult = true;
                    Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao sobrescrever:\n{ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                OverwriteButton.IsEnabled = true;
                OverwriteButton.Content = "Sobrescrever selecionados no DevOps";
                UpdateSummary();
            }
        }

        // ── ViewModels ────────────────────────────────────────────────────────

        public sealed class ConflictRow : INotifyPropertyChanged
        {
            private bool _isSelected;
            public TfsImportService.SyncConflictItem Item { get; }

            public ConflictRow(TfsImportService.SyncConflictItem item)
            {
                Item = item;
            }

            public bool IsSelected
            {
                get => _isSelected;
                set { _isSelected = value; OnPropertyChanged(); }
            }

            public string TfsType      => Item.TfsType;
            public int    TfsId        => Item.TfsId;
            public string ChangedBy    => Item.ChangedBy;
            public string VersionLabel => $"{Item.LocalVersion} → {Item.TfsVersion}";
            public string LocalTitle   => Item.LocalTitle;

            public string DiffSummary
            {
                get
                {
                    var diffs = new List<string>();
                    if (Item.LocalTitle  != Item.TfsTitle)  diffs.Add("Título");
                    if (Item.LocalState  != Item.TfsState)  diffs.Add("Estado");
                    if (Item.LocalTags   != Item.TfsTags)   diffs.Add("Tags");
                    if (!HoursEqual(Item.LocalHours, Item.TfsHours)) diffs.Add("HH");
                    if (Item.LocalStart  != Item.TfsStart)  diffs.Add("Início");
                    if (Item.LocalFinish != Item.TfsFinish) diffs.Add("Fim");
                    return diffs.Count == 0 ? "—" : string.Join(", ", diffs);
                }
            }

            private static bool HoursEqual(double? a, double? b)
                => (a == null && b == null) || (a.HasValue && b.HasValue && Math.Abs(a.Value - b.Value) < 0.01);

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public sealed class DiffRow
        {
            public string Label        { get; init; } = "";
            public string LocalValue   { get; init; } = "";
            public string TfsValue     { get; init; } = "";
            public bool   IsDifferent  { get; init; }

            public string LocalTooltip => IsDifferent ? $"Local: {LocalValue}" : "";
            public string TfsTooltip   => IsDifferent ? $"DevOps: {TfsValue}"  : "";

            private static readonly SolidColorBrush _diffBg = new(Color.FromRgb(0xFF, 0xEB, 0xEB));
            private static readonly SolidColorBrush _normBg = Brushes.Transparent;
            private static readonly SolidColorBrush _rowDiffBg = new(Color.FromRgb(0xFF, 0xF5, 0xF5));
            private static readonly SolidColorBrush _rowNormBg = Brushes.White;

            public SolidColorBrush RowBackground   => IsDifferent ? _rowDiffBg : _rowNormBg;
            public SolidColorBrush LocalBackground => IsDifferent ? _diffBg    : _normBg;
            public SolidColorBrush TfsBackground   => IsDifferent ? _diffBg    : _normBg;
            public string          DiffWeight      => IsDifferent ? "SemiBold"  : "Normal";
            public string          DiffForeground  => IsDifferent ? "#B00020"   : "#222222";

            public static DiffRow Build(string label, string local, string tfs)
            {
                return new DiffRow
                {
                    Label       = label,
                    LocalValue  = string.IsNullOrEmpty(local) ? "—" : local,
                    TfsValue    = string.IsNullOrEmpty(tfs)   ? "—" : tfs,
                    IsDifferent = !string.Equals(local?.Trim(), tfs?.Trim(), StringComparison.Ordinal)
                                  && !(string.IsNullOrEmpty(local) && string.IsNullOrEmpty(tfs))
                };
            }
        }
    }
}
