using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using NXProject.Models;
using NXProject.Services;

namespace NXProject.Views
{
    public partial class ResourceCostWindow : Window
    {
        public sealed class CostRow
        {
            public string ResourceName  { get; init; } = "";
            public string CostTypeLabel { get; init; } = "";
            public string FeatureName   { get; init; } = "";
            public string MonthLabel    { get; init; } = "";
            public string GroupKey      { get; init; } = "";
            public string HoursText     { get; init; } = "";
            public string CostText      { get; init; } = "";
            public decimal Cost         { get; init; }
            public double  Hours        { get; init; }
            public int     Year         { get; init; }
            public int     Month        { get; init; }
        }

        private readonly List<CostRow>    _allRows = new();
        private          ICollectionView  _view    = null!;

        private static readonly string[] MonthNames =
            { "", "Jan", "Fev", "Mar", "Abr", "Mai", "Jun",
                  "Jul", "Ago", "Set", "Out", "Nov", "Dez" };

        public ResourceCostWindow(IEnumerable<ProjectTask> allTasks, IEnumerable<Resource> resources)
        {
            InitializeComponent();

            var lines = ResourceCostService.Compute(allTasks, resources);

            foreach (var l in lines)
            {
                _allRows.Add(new CostRow
                {
                    ResourceName  = l.ResourceName,
                    CostTypeLabel = l.CostType == ResourceCostType.Hourly ? "H/hora" : "Mensal",
                    FeatureName   = l.FeatureName,
                    MonthLabel    = $"{MonthNames[l.Month]}/{l.Year % 100:00}",
                    GroupKey      = l.ResourceName,
                    HoursText     = $"{l.Hours:0.#}",
                    CostText      = l.Cost.ToString("N2"),
                    Cost          = l.Cost,
                    Hours         = l.Hours,
                    Year          = l.Year,
                    Month         = l.Month
                });
            }

            // Filtros: recursos e meses únicos
            var resources2 = new List<string> { "(Todos)" };
            resources2.AddRange(_allRows.Select(r => r.ResourceName).Distinct().OrderBy(s => s));
            ResourceFilter.ItemsSource   = resources2;
            ResourceFilter.SelectedIndex = 0;

            var months = new List<string> { "(Todos)" };
            months.AddRange(_allRows
                .Select(r => (r.Year, r.Month))
                .Distinct()
                .OrderBy(t => t.Year).ThenBy(t => t.Month)
                .Select(t => $"{MonthNames[t.Month]}/{t.Year % 100:00}"));
            MonthFilter.ItemsSource   = months;
            MonthFilter.SelectedIndex = 0;

            BuildView("Recurso");
            UpdateSummary();
        }

        private void BuildView(string groupBy)
        {
            var filtered = ApplyFilters();
            var cv = CollectionViewSource.GetDefaultView(filtered);

            cv.GroupDescriptions.Clear();
            cv.SortDescriptions.Clear();

            switch (groupBy)
            {
                case "Recurso":
                    cv.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CostRow.ResourceName)));
                    cv.SortDescriptions.Add(new SortDescription(nameof(CostRow.ResourceName), ListSortDirection.Ascending));
                    cv.SortDescriptions.Add(new SortDescription(nameof(CostRow.Year),  ListSortDirection.Ascending));
                    cv.SortDescriptions.Add(new SortDescription(nameof(CostRow.Month), ListSortDirection.Ascending));
                    break;
                case "Feature":
                    cv.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CostRow.FeatureName)));
                    cv.SortDescriptions.Add(new SortDescription(nameof(CostRow.FeatureName), ListSortDirection.Ascending));
                    cv.SortDescriptions.Add(new SortDescription(nameof(CostRow.ResourceName), ListSortDirection.Ascending));
                    break;
                case "Mês":
                    cv.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CostRow.MonthLabel)));
                    cv.SortDescriptions.Add(new SortDescription(nameof(CostRow.Year),  ListSortDirection.Ascending));
                    cv.SortDescriptions.Add(new SortDescription(nameof(CostRow.Month), ListSortDirection.Ascending));
                    cv.SortDescriptions.Add(new SortDescription(nameof(CostRow.ResourceName), ListSortDirection.Ascending));
                    break;
            }

            _view = cv;
            CostGrid.ItemsSource = cv;
            UpdateSummary(filtered);
        }

        private List<CostRow> ApplyFilters()
        {
            var res   = ResourceFilter.SelectedItem as string ?? "(Todos)";
            var month = MonthFilter.SelectedItem   as string ?? "(Todos)";

            return _allRows.Where(r =>
                (res   == "(Todos)" || r.ResourceName == res) &&
                (month == "(Todos)" || r.MonthLabel   == month))
                .ToList();
        }

        private void UpdateSummary(List<CostRow>? filtered = null)
        {
            filtered ??= ApplyFilters();
            decimal total = filtered.Sum(r => r.Cost);
            double  hours = filtered.Sum(r => r.Hours);
            TotalText.Text  = $"Total: R$ {total:N2}";
            SummaryText.Text = $"{filtered.Count} linhas | {hours:0.#} HH | R$ {total:N2}";
            CountText.Text   = $"{filtered.Count} linhas";
        }

        private void OnGroupByChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (GroupByCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item)
                BuildView(item.Content?.ToString() ?? "Recurso");
        }

        private void OnFilterChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (GroupByCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item)
                BuildView(item.Content?.ToString() ?? "Recurso");
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }
}
