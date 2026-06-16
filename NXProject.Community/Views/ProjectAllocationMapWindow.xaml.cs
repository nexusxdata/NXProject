using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using NXProject.Models;
using NXProject.Services;

namespace NXProject.Views
{
    public partial class ProjectAllocationMapWindow : Window
    {
        // ── Modelo interno ────────────────────────────────────────────────────
        private sealed class LoadedProject
        {
            public string  FilePath   { get; set; } = string.Empty;
            public string  Name       { get; set; } = string.Empty;
            public bool    IsOpex     { get; set; } = true;
            public string  CostCenter { get; set; } = string.Empty;
            public Project Data       { get; set; } = null!;
        }

        // ── Estado ────────────────────────────────────────────────────────────
        private List<LoadedProject> _projects = [];
        private List<DateTime>      _months   = [];

        private const double RowHeight   = 26;
        private const double ColWidth    = 72;
        private const double LeftWidth   = 220; // projeto
        private const double ResWidth    = 160; // recurso
        private const double TypeWidth   = 80;
        private const double CcWidth     = 120;
        private const double TotalColW   = 72;

        // ── Construtor ────────────────────────────────────────────────────────
        public ProjectAllocationMapWindow()
        {
            InitializeComponent();
            PopulateMonthCombos();

            var opts = TfsConnectionStore.Load();
            LoadProjects(opts);

            Loaded += (_, _) => BuildGrid();
        }

        // ── Período ───────────────────────────────────────────────────────────
        private void PopulateMonthCombos()
        {
            var start = new DateTime(DateTime.Today.Year, 1, 1);
            for (int i = 0; i < 36; i++)
            {
                var m = start.AddMonths(i);
                StartMonthBox.Items.Add(new MonthItem(m));
                EndMonthBox.Items.Add(new MonthItem(m));
            }
            StartMonthBox.SelectedIndex = 0;
            EndMonthBox.SelectedIndex   = 11;
        }

        private sealed class MonthItem(DateTime month)
        {
            public DateTime Month { get; } = month;
            public override string ToString() => Month.ToString("MMM/yyyy");
        }

        private (DateTime Start, DateTime End) GetPeriod()
        {
            var s = (StartMonthBox.SelectedItem as MonthItem)?.Month ?? new DateTime(DateTime.Today.Year, 1, 1);
            var e = (EndMonthBox.SelectedItem   as MonthItem)?.Month ?? new DateTime(DateTime.Today.Year, 12, 1);
            if (e < s) e = s;
            return (s, new DateTime(e.Year, e.Month, DateTime.DaysInMonth(e.Year, e.Month)));
        }

        private static List<DateTime> BuildMonths(DateTime start, DateTime end)
        {
            var list = new List<DateTime>();
            var cur  = new DateTime(start.Year, start.Month, 1);
            var last = new DateTime(end.Year,   end.Month,   1);
            while (cur <= last) { list.Add(cur); cur = cur.AddMonths(1); }
            return list;
        }

        // ── Carregamento ──────────────────────────────────────────────────────
        private void LoadProjects(TfsConnectionOptions opts)
        {
            _projects = [];
            foreach (var cfg in opts.PortfolioProjectConfigs)
            {
                if (string.IsNullOrWhiteSpace(cfg.FilePath) || !System.IO.File.Exists(cfg.FilePath))
                    continue;
                try
                {
                    var project = XmlProjectService.Load(cfg.FilePath);
                    _projects.Add(new LoadedProject
                    {
                        FilePath   = cfg.FilePath,
                        Name       = !string.IsNullOrWhiteSpace(cfg.ProjectName)
                                     ? cfg.ProjectName
                                     : (!string.IsNullOrWhiteSpace(project.Name)
                                         ? project.Name
                                         : System.IO.Path.GetFileNameWithoutExtension(cfg.FilePath)),
                        IsOpex     = cfg.IsOpex,
                        CostCenter = cfg.CostCenter,
                        Data       = project
                    });
                }
                catch { /* arquivo inválido — ignora */ }
            }
        }

        // ── Horas ─────────────────────────────────────────────────────────────
        private static IEnumerable<ProjectTask> GetLeafTasks(IEnumerable<ProjectTask> tasks)
        {
            foreach (var t in tasks)
            {
                if (t.Children.Count == 0) yield return t;
                else foreach (var c in GetLeafTasks(t.Children)) yield return c;
            }
        }

        private static double ComputeHours(Project project, string resourceName,
                                            DateTime monthStart, DateTime monthEnd)
        {
            double total = 0;
            foreach (var task in GetLeafTasks(project.Tasks))
            {
                foreach (var tr in task.Resources)
                {
                    if (!string.Equals(tr.Resource?.Name, resourceName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    double hours = tr.EstimatedHours ?? 0;
                    if (hours <= 0) continue;

                    var tStart = task.Start.Date;
                    var tEnd   = task.Finish.Date;
                    if (tEnd < monthStart || tStart > monthEnd) continue;

                    if (tStart >= tEnd)
                    {
                        total += hours;
                        continue;
                    }

                    var overlapStart   = tStart < monthStart ? monthStart : tStart;
                    var overlapEnd     = tEnd   > monthEnd   ? monthEnd   : tEnd;
                    double overlapDays = Math.Max(0, (overlapEnd - overlapStart).TotalDays + 1);
                    double totalDays   = Math.Max(1, (tEnd - tStart).TotalDays + 1);
                    total += hours * (overlapDays / totalDays);
                }
            }
            return total;
        }

        // ── Construção do grid ────────────────────────────────────────────────
        private void BuildGrid()
        {
            ClearPanels();

            if (_projects.Count == 0)
            {
                StatusText.Text = "Nenhum projeto selecionado. Use '📁 Selecionar Projetos' para adicionar.";
                return;
            }

            var (periodStart, periodEnd) = GetPeriod();
            _months = BuildMonths(periodStart, periodEnd);

            bool hideZero = OnlyWithHoursBox.IsChecked == true;

            // Pré-computa: para cada projeto, quais recursos têm horas no período
            // data[pi][resourceName][mi] = horas
            var projResourceData = new List<(LoadedProject Proj,
                                             List<(string Res, double[] MonthHours)> Rows)>();
            var allResources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var proj in _projects)
            {
                var resources = proj.Data.Resources
                    .Where(r => r.Type == ResourceType.Work && !string.IsNullOrWhiteSpace(r.Name))
                    .Select(r => r.Name!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n)
                    .ToList();

                var rows = new List<(string Res, double[] MonthHours)>();
                foreach (var res in resources)
                {
                    var monthHours = new double[_months.Count];
                    for (int mi = 0; mi < _months.Count; mi++)
                    {
                        var mStart = _months[mi];
                        var mEnd   = new DateTime(mStart.Year, mStart.Month,
                                         DateTime.DaysInMonth(mStart.Year, mStart.Month));
                        monthHours[mi] = ComputeHours(proj.Data, res, mStart, mEnd);
                    }

                    double rowTotal = monthHours.Sum();
                    if (hideZero && rowTotal < 0.01) continue;

                    rows.Add((res, monthHours));
                    allResources.Add(res);
                }

                projResourceData.Add((proj, rows));
            }

            // Quais meses têm dados (para ocultar colunas zeradas)
            var visibleMonths = new List<int>();
            for (int mi = 0; mi < _months.Count; mi++)
            {
                bool hasData = projResourceData.Any(p => p.Rows.Any(r => r.MonthHours[mi] > 0.01));
                if (!hideZero || hasData) visibleMonths.Add(mi);
            }

            BuildHeaders(visibleMonths);
            BuildRows(projResourceData, visibleMonths, hideZero);

            int totalResources = allResources.Count;
            double grandTotal  = projResourceData.Sum(p => p.Rows.Sum(r => r.MonthHours.Sum()));
            StatusText.Text = $"{_projects.Count} projeto(s)  ·  {totalResources} recurso(s) com alocação  ·  " +
                              $"{_months.Count} mês(es)  ·  Total geral: {grandTotal:0.#}h";
        }

        private void ClearPanels()
        {
            MonthHeaderPanel.Items.Clear();
            ResourceHeaderPanel.Items.Clear();
            ProjectNamePanel.Items.Clear();
            ProjectResPanel.Items.Clear();
            ProjectTypePanel.Items.Clear();
            ProjectCcPanel.Items.Clear();
            DataRowsPanel.Items.Clear();
        }

        // ── Cabeçalhos ────────────────────────────────────────────────────────
        private void BuildHeaders(List<int> visibleMonths)
        {
            // Linha 1 (MonthHeaderPanel): meses
            foreach (var mi in visibleMonths)
            {
                MonthHeaderPanel.Items.Add(new Border
                {
                    Width           = ColWidth,
                    Height          = 22,
                    Background      = new SolidColorBrush(Color.FromRgb(43, 87, 154)),
                    BorderBrush     = new SolidColorBrush(Color.FromRgb(29, 63, 115)),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Child           = new TextBlock
                    {
                        Text                = _months[mi].ToString("MMM/yy"),
                        Foreground          = Brushes.White,
                        FontWeight          = FontWeights.SemiBold,
                        FontSize            = 11,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment   = VerticalAlignment.Center
                    }
                });
            }
            // Coluna TOTAL
            MonthHeaderPanel.Items.Add(new Border
            {
                Width           = TotalColW,
                Height          = 22,
                Background      = new SolidColorBrush(Color.FromRgb(25, 60, 120)),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(29, 63, 115)),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Child           = new TextBlock
                {
                    Text                = "TOTAL",
                    Foreground          = Brushes.White,
                    FontWeight          = FontWeights.SemiBold,
                    FontSize            = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center
                }
            });

            // Linha 2 (ResourceHeaderPanel): sub-header vazio (alinha com colunas de mês)
            int totalCols = visibleMonths.Count + 1;
            ResourceHeaderPanel.Items.Add(new Border
            {
                Width           = totalCols * ColWidth,
                Height          = 20,
                Background      = new SolidColorBrush(Color.FromRgb(232, 238, 248)),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(197, 208, 224)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child           = new TextBlock
                {
                    Text       = "← horas alocadas mensalmente →",
                    FontSize   = 10,
                    FontStyle  = FontStyles.Italic,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 120, 160)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center
                }
            });
        }

        // ── Linhas de dados ───────────────────────────────────────────────────
        private void BuildRows(
            List<(LoadedProject Proj, List<(string Res, double[] MonthHours)> Rows)> projData,
            List<int> visibleMonths,
            bool hideZero)
        {
            // Totais por mês (para linha de rodapé)
            var grandByMonth = new double[_months.Count];

            foreach (var (proj, resRows) in projData)
            {
                // ── Cabeçalho do projeto ──────────────────────────────────────
                var projBg = new SolidColorBrush(Color.FromRgb(224, 232, 250));

                ProjectNamePanel.Items.Add(MakeCell(proj.Name, LeftWidth, RowHeight + 2, projBg,
                    bold: true, tooltip: proj.FilePath, leftPad: 6));
                ProjectResPanel.Items.Add(MakeCell("", ResWidth, RowHeight + 2, projBg));
                ProjectTypePanel.Items.Add(MakeTypeCell(proj, projBg));
                ProjectCcPanel.Items.Add(MakeCcCell(proj, projBg));

                // Totais do projeto por mês
                var projByMonth = new double[_months.Count];
                foreach (var (_, mh) in resRows)
                    for (int mi = 0; mi < _months.Count; mi++)
                        projByMonth[mi] += mh[mi];

                double projTotal = projByMonth.Sum();
                DataRowsPanel.Items.Add(BuildDataRow(projByMonth, visibleMonths, projTotal, projBg, bold: true));

                for (int mi = 0; mi < _months.Count; mi++)
                    grandByMonth[mi] += projByMonth[mi];

                // ── Sub-linhas por recurso ────────────────────────────────────
                for (int ri = 0; ri < resRows.Count; ri++)
                {
                    var (resName, monthHours) = resRows[ri];
                    var resBg = new SolidColorBrush(ri % 2 == 0
                        ? Color.FromRgb(255, 255, 255)
                        : Color.FromRgb(248, 250, 255));

                    ProjectNamePanel.Items.Add(MakeCell("", LeftWidth, RowHeight, resBg));
                    ProjectResPanel.Items.Add(new Border
                    {
                        Width           = ResWidth,
                        Height          = RowHeight,
                        Background      = resBg,
                        BorderBrush     = new SolidColorBrush(Color.FromRgb(220, 228, 240)),
                        BorderThickness = new Thickness(0, 0, 1, 1),
                        Padding         = new Thickness(18, 0, 4, 0),
                        ToolTip         = resName,
                        Child           = new TextBlock
                        {
                            Text              = resName,
                            FontSize          = 11,
                            Foreground        = new SolidColorBrush(Color.FromRgb(50, 90, 160)),
                            VerticalAlignment = VerticalAlignment.Center,
                            TextTrimming      = TextTrimming.CharacterEllipsis
                        }
                    });
                    ProjectTypePanel.Items.Add(MakeCell("", TypeWidth, RowHeight, resBg));
                    ProjectCcPanel.Items.Add(MakeCell("", CcWidth, RowHeight, resBg));

                    double resTotal = 0;
                    var resRow = new StackPanel { Orientation = Orientation.Horizontal };
                    foreach (var mi in visibleMonths)
                    {
                        double h = monthHours[mi];
                        resTotal += h;
                        var mStart = _months[mi];
                        var mEnd   = new DateTime(mStart.Year, mStart.Month,
                                         DateTime.DaysInMonth(mStart.Year, mStart.Month));
                        resRow.Children.Add(MakeHoursCellClickable(h, ColWidth, RowHeight,
                            resBg, proj, resName, mStart, mEnd));
                    }
                    resRow.Children.Add(MakeTotalCell(resTotal, TotalColW, RowHeight));
                    DataRowsPanel.Items.Add(resRow);
                }

                // Separador
                var sep = new Border { Height = 3, Background = new SolidColorBrush(Color.FromRgb(180, 200, 230)) };
                ProjectNamePanel.Items.Add(new Border { Height = 3, Background = sep.Background });
                ProjectResPanel.Items.Add(new Border  { Height = 3, Background = sep.Background });
                ProjectTypePanel.Items.Add(new Border { Height = 3, Background = sep.Background });
                ProjectCcPanel.Items.Add(new Border   { Height = 3, Background = sep.Background });
                DataRowsPanel.Items.Add(sep);
            }

            // ── Linha TOTAL GERAL ─────────────────────────────────────────────
            var totalBg = new SolidColorBrush(Color.FromRgb(43, 87, 154));
            ProjectNamePanel.Items.Add(MakeCell("TOTAL GERAL", LeftWidth, RowHeight + 2, totalBg,
                bold: true, fg: Colors.White, leftPad: 6));
            ProjectResPanel.Items.Add(MakeCell("", ResWidth, RowHeight + 2, totalBg));
            ProjectTypePanel.Items.Add(MakeCell("", TypeWidth, RowHeight + 2, totalBg));
            ProjectCcPanel.Items.Add(MakeCell("", CcWidth, RowHeight + 2, totalBg));

            double grandTotal = 0;
            var grandRow = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var mi in visibleMonths)
            {
                grandTotal += grandByMonth[mi];
                grandRow.Children.Add(MakeTotalCell(grandByMonth[mi], ColWidth, RowHeight + 2,
                    bold: true, fg: Colors.White, bg: Color.FromRgb(43, 87, 154)));
            }
            grandRow.Children.Add(MakeTotalCell(grandTotal, TotalColW, RowHeight + 2,
                bold: true, fg: Colors.White, bg: Color.FromRgb(25, 60, 120)));
            DataRowsPanel.Items.Add(grandRow);
        }

        // ── Linha de totais do projeto (sem coluna de recurso — está na coluna fixa) ──
        private static StackPanel BuildDataRow(double[] byMonth, List<int> visibleMonths,
            double rowTotal, SolidColorBrush rowBg, bool bold)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var mi in visibleMonths)
                row.Children.Add(MakeHoursCell(byMonth[mi], ColWidth, RowHeight + 2, rowBg, bold: bold));
            row.Children.Add(MakeTotalCell(rowTotal, TotalColW, RowHeight + 2, bold: bold));
            return row;
        }

        // ── Célula OPEX/CAPEX (leitura — configurado no picker) ──────────────
        private static UIElement MakeTypeCell(LoadedProject proj, Brush bg)
        {
            return new Border
            {
                Width           = TypeWidth,
                Height          = RowHeight + 2,
                Background      = bg,
                BorderBrush     = new SolidColorBrush(Color.FromRgb(180, 200, 230)),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding         = new Thickness(4, 0, 4, 0),
                Child           = new TextBlock
                {
                    Text              = proj.IsOpex ? "OPEX" : "CAPEX",
                    FontSize          = 11,
                    FontWeight        = FontWeights.SemiBold,
                    Foreground        = proj.IsOpex
                        ? new SolidColorBrush(Color.FromRgb(0, 100, 0))
                        : new SolidColorBrush(Color.FromRgb(140, 60, 0)),
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        // ── Célula Centro de Custo (leitura — configurado no picker) ─────────
        private static UIElement MakeCcCell(LoadedProject proj, Brush bg)
        {
            return new Border
            {
                Width           = CcWidth,
                Height          = RowHeight + 2,
                Background      = bg,
                BorderBrush     = new SolidColorBrush(Color.FromRgb(180, 200, 230)),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding         = new Thickness(4, 0, 4, 0),
                ToolTip         = proj.CostCenter,
                Child           = new TextBlock
                {
                    Text             = proj.CostCenter,
                    FontSize         = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming     = TextTrimming.CharacterEllipsis,
                    Foreground       = new SolidColorBrush(Color.FromRgb(60, 60, 60))
                }
            };
        }

        // ── Células genéricas ─────────────────────────────────────────────────
        private static Border MakeCell(string text, double width, double height,
            Brush bg, bool bold = false, Color? fg = null, string? tooltip = null,
            double leftPad = 4)
        {
            var foreground = fg.HasValue
                ? new SolidColorBrush(fg.Value)
                : (Brush)new SolidColorBrush(Color.FromRgb(30, 30, 30));

            return new Border
            {
                Width           = width,
                Height          = height,
                Background      = bg,
                BorderBrush     = new SolidColorBrush(Color.FromRgb(180, 200, 230)),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding         = new Thickness(leftPad, 0, 4, 0),
                ToolTip         = tooltip,
                Child           = new TextBlock
                {
                    Text              = text,
                    FontSize          = 12,
                    FontWeight        = bold ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground        = foreground,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming      = TextTrimming.CharacterEllipsis
                }
            };
        }

        private static Border MakeHoursCell(double hours, double width, double height,
            Brush rowBg, bool bold = false)
        {
            string text = hours < 0.05 ? "–" : $"{hours:0.#}h";
            var fg = hours < 0.05
                ? new SolidColorBrush(Color.FromRgb(200, 200, 200))
                : new SolidColorBrush(bold
                    ? Color.FromRgb(20, 60, 140)
                    : Color.FromRgb(40, 100, 200));

            return new Border
            {
                Width           = width,
                Height          = height,
                Background      = rowBg,
                BorderBrush     = new SolidColorBrush(Color.FromRgb(220, 228, 240)),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Child           = new TextBlock
                {
                    Text                = text,
                    FontSize            = 11,
                    FontWeight          = bold ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground          = fg,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment   = VerticalAlignment.Center,
                    Margin              = new Thickness(0, 0, 6, 0)
                }
            };
        }

        // Versão clicável: abre popup com stories do recurso naquele mês
        private Border MakeHoursCellClickable(double hours, double width, double height,
            Brush rowBg, LoadedProject proj, string resName, DateTime monthStart, DateTime monthEnd)
        {
            string text   = hours < 0.05 ? "–" : $"{hours:0.#}h";
            bool hasHours = hours >= 0.05;
            var fg = hasHours
                ? new SolidColorBrush(Color.FromRgb(40, 100, 200))
                : new SolidColorBrush(Color.FromRgb(200, 200, 200));

            var tb = new TextBlock
            {
                Text                = text,
                FontSize            = 11,
                Foreground          = fg,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment   = VerticalAlignment.Center,
                Margin              = new Thickness(0, 0, 6, 0),
                TextDecorations     = hasHours ? TextDecorations.Underline : null
            };
            var border = new Border
            {
                Width           = width,
                Height          = height,
                Background      = rowBg,
                BorderBrush     = new SolidColorBrush(Color.FromRgb(220, 228, 240)),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Cursor          = hasHours ? System.Windows.Input.Cursors.Hand : null,
                Child           = tb
            };
            if (hasHours)
                border.MouseLeftButtonUp += (_, _) =>
                    ShowStoriesPopup(proj, resName, monthStart, monthEnd);
            return border;
        }

        // ── Lista de stories de um recurso num mês ────────────────────────────
        private static List<ProjectTask> GetStoriesInMonth(Project project, string resName,
            DateTime monthStart, DateTime monthEnd)
        {
            var result = new List<ProjectTask>();
            foreach (var task in GetLeafTasks(project.Tasks))
            {
                bool hasRes = task.Resources.Any(r =>
                    string.Equals(r.Resource?.Name, resName, StringComparison.OrdinalIgnoreCase));
                if (!hasRes) continue;

                var tStart = task.Start.Date;
                var tEnd   = task.Finish.Date;
                if (tEnd < monthStart || tStart > monthEnd) continue;

                result.Add(task);
            }
            return result;
        }

        // ── Popup de stories ──────────────────────────────────────────────────
        private void ShowStoriesPopup(LoadedProject proj, string resName,
            DateTime monthStart, DateTime monthEnd)
        {
            var stories = GetStoriesInMonth(proj.Data, resName, monthStart, monthEnd);

            var opts   = TfsConnectionStore.Load();
            var orgUrl = opts.OrganizationUrl?.TrimEnd('/') ?? "";
            var tp     = opts.TeamProject ?? "";

            var win = new Window
            {
                Title                 = $"{resName}  ·  {monthStart:MMM/yyyy}  ·  {proj.Name}",
                Width                 = 760,
                Height                = 420,
                MinWidth              = 500,
                MinHeight             = 300,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner                 = this,
                Background            = System.Windows.Media.Brushes.White,
                ResizeMode            = ResizeMode.CanResize
            };

            var grid = new Grid { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Título
            var header = new TextBlock
            {
                Text       = $"Stories de {resName}  —  {monthStart:MMMM/yyyy}  —  {proj.Name}",
                FontSize   = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(43, 87, 154)),
                Margin     = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            // Lista
            var sv = new ScrollViewer
            {
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            var panel = new StackPanel();

            // Cabeçalho da tabela
            panel.Children.Add(MakeStoryRow(
                "Story / Tarefa", "HH Est.", "Início", "Fim", "DevOps",
                isHeader: true, devOpsUrl: null));

            if (stories.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text    = "Nenhuma story encontrada para este período.",
                    Margin  = new Thickness(8, 6, 8, 0),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120))
                });
            }
            else
            {
                foreach (var task in stories.OrderBy(t => t.Start))
                {
                    string hh    = task.EstimatedHours.HasValue ? $"{task.EstimatedHours.Value:0.#}h" : "–";
                    string start = task.Start.ToString("dd/MM/yy");
                    string fin   = task.Finish.ToString("dd/MM/yy");
                    string? url  = task.TfsId.HasValue && !string.IsNullOrWhiteSpace(orgUrl)
                        ? $"{orgUrl}/{Uri.EscapeDataString(tp)}/_workitems/edit/{task.TfsId.Value}"
                        : null;
                    panel.Children.Add(MakeStoryRow(task.Name, hh, start, fin, url != null ? "↗" : "", isHeader: false, devOpsUrl: url));
                }
            }

            sv.Content = panel;
            Grid.SetRow(sv, 1);
            grid.Children.Add(sv);

            win.Content = grid;
            win.ShowDialog();
        }

        private static UIElement MakeStoryRow(string name, string hh, string start, string fin,
            string devOps, bool isHeader, string? devOpsUrl)
        {
            var bg = isHeader
                ? new SolidColorBrush(Color.FromRgb(43, 87, 154))
                : (Brush)System.Windows.Media.Brushes.Transparent;
            var fgColor = isHeader ? Colors.White : Color.FromRgb(30, 30, 30);
            var fw = isHeader ? FontWeights.SemiBold : FontWeights.Normal;

            var row = new Border
            {
                Background      = bg,
                BorderBrush     = new SolidColorBrush(Color.FromRgb(210, 220, 240)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };

            UIElement NameCell(string t, double w) => new Border
            {
                Width = w, Padding = new Thickness(6, 4, 6, 4),
                Child = new TextBlock { Text = t, FontSize = 11, FontWeight = fw,
                    Foreground = new SolidColorBrush(fgColor),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center, ToolTip = t }
            };

            sp.Children.Add(NameCell(name, 340));
            sp.Children.Add(NameCell(hh, 70));
            sp.Children.Add(NameCell(start, 76));
            sp.Children.Add(NameCell(fin, 76));

            if (!isHeader && !string.IsNullOrEmpty(devOpsUrl))
            {
                var btn = new Button
                {
                    Content   = "↗ DevOps",
                    FontSize  = 10,
                    Padding   = new Thickness(6, 2, 6, 2),
                    Margin    = new Thickness(4, 2, 4, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor    = System.Windows.Input.Cursors.Hand
                };
                btn.Click += (_, _) =>
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(devOpsUrl) { UseShellExecute = true }); }
                    catch { }
                };
                sp.Children.Add(btn);
            }
            else if (isHeader)
            {
                sp.Children.Add(NameCell(devOps, 90));
            }

            row.Child = sp;
            return row;
        }

        private static Border MakeTotalCell(double hours, double width, double height,
            bool bold = true, Color? fg = null, Color? bg = null)
        {
            string text = hours < 0.05 ? "–" : $"{hours:0.#}h";
            var foreground = fg.HasValue
                ? new SolidColorBrush(fg.Value)
                : new SolidColorBrush(hours < 0.05
                    ? Color.FromRgb(170, 170, 170)
                    : Color.FromRgb(20, 40, 100));

            return new Border
            {
                Width           = width,
                Height          = height,
                Background      = bg.HasValue
                    ? new SolidColorBrush(bg.Value)
                    : new SolidColorBrush(Color.FromRgb(220, 232, 252)),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(180, 200, 230)),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Child           = new TextBlock
                {
                    Text                = text,
                    FontSize            = 11,
                    FontWeight          = bold ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground          = foreground,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment   = VerticalAlignment.Center,
                    Margin              = new Thickness(0, 0, 6, 0)
                }
            };
        }

        // ── Distribuição por Pessoa ───────────────────────────────────────────
        private void BuildDistributionGrid()
        {
            DistProjHeaderPanel.Items.Clear();
            DistTypeHeaderPanel.Items.Clear();
            DistResPanel.Items.Clear();
            DistDataPanel.Items.Clear();

            if (_projects.Count == 0) return;

            var (periodStart, periodEnd) = GetPeriod();
            bool hideZero = OnlyWithHoursBox.IsChecked == true;

            var months = BuildMonths(periodStart, periodEnd);

            // Coleta recursos únicos
            var allResources = _projects
                .SelectMany(p => p.Data.Resources
                    .Where(r => r.Type == ResourceType.Work && !string.IsNullOrWhiteSpace(r.Name))
                    .Select(r => r.Name!))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();

            // dist[projIdx][resName][monthIdx] = horas
            var dist = new List<Dictionary<string, double[]>>();
            foreach (var proj in _projects)
            {
                var d = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
                foreach (var res in allResources)
                {
                    var mh = new double[months.Count];
                    for (int mi = 0; mi < months.Count; mi++)
                    {
                        var ms = months[mi];
                        var me = new DateTime(ms.Year, ms.Month, DateTime.DaysInMonth(ms.Year, ms.Month));
                        mh[mi] = ComputeHours(proj.Data, res, ms, me);
                    }
                    d[res] = mh;
                }
                dist.Add(d);
            }

            if (hideZero)
                allResources = allResources
                    .Where(r => dist.Any(d => d[r].Sum() > 0.01))
                    .ToList();

            // Projetos com alguma hora
            var visibleProjIdx = Enumerable.Range(0, _projects.Count)
                .Where(pi => !hideZero || allResources.Any(r => dist[pi][r].Sum() > 0.01))
                .ToList();

            // Meses com alguma hora
            var visibleMonthIdx = Enumerable.Range(0, months.Count)
                .Where(mi => !hideZero || visibleProjIdx.Any(pi =>
                    allResources.Any(r => dist[pi][r][mi] > 0.01)))
                .ToList();

            const double MonthColW   = 78;   // coluna por mês
            const double ProjTotalW  = 66;   // total do projeto
            const double GrandTotalW = 72;   // total geral
            const double DistRowH    = 36;   // altura maior para duas linhas

            // ── Cabeçalho linha 1: projeto (span = nMeses × MonthColW + ProjTotalW) ──
            foreach (var pi in visibleProjIdx)
            {
                var proj      = _projects[pi];
                double projW  = visibleMonthIdx.Count * MonthColW + ProjTotalW;
                var typeColor = proj.IsOpex ? Color.FromRgb(43, 100, 43) : Color.FromRgb(140, 70, 20);

                DistProjHeaderPanel.Items.Add(new Border
                {
                    Width           = projW,
                    Height          = 22,
                    Background      = new SolidColorBrush(Color.FromRgb(43, 87, 154)),
                    BorderBrush     = new SolidColorBrush(Color.FromRgb(29, 63, 115)),
                    BorderThickness = new Thickness(0, 0, 2, 1),
                    ToolTip         = proj.Name,
                    Child           = new StackPanel
                    {
                        Orientation         = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment   = VerticalAlignment.Center,
                        Children =
                        {
                            new TextBlock
                            {
                                Text       = proj.Name,
                                Foreground = Brushes.White,
                                FontWeight = FontWeights.SemiBold,
                                FontSize   = 11,
                                TextTrimming = TextTrimming.CharacterEllipsis,
                                MaxWidth   = projW - 60,
                                VerticalAlignment = VerticalAlignment.Center,
                                Margin     = new Thickness(4, 0, 6, 0)
                            },
                            new Border
                            {
                                Background      = new SolidColorBrush(typeColor),
                                CornerRadius    = new CornerRadius(3),
                                Padding         = new Thickness(5, 1, 5, 1),
                                VerticalAlignment = VerticalAlignment.Center,
                                Child           = new TextBlock
                                {
                                    Text       = proj.IsOpex ? "OPEX" : "CAPEX",
                                    FontSize   = 9,
                                    FontWeight = FontWeights.SemiBold,
                                    Foreground = Brushes.White
                                }
                            }
                        }
                    }
                });
            }
            DistProjHeaderPanel.Items.Add(new Border
            {
                Width           = GrandTotalW,
                Height          = 22,
                Background      = new SolidColorBrush(Color.FromRgb(25, 60, 120)),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(29, 63, 115)),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Child           = new TextBlock
                {
                    Text                = "TOTAL",
                    Foreground          = Brushes.White,
                    FontWeight          = FontWeights.Bold,
                    FontSize            = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center
                }
            });

            // ── Cabeçalho linha 2: meses de cada projeto + subtotal ──
            foreach (var pi in visibleProjIdx)
            {
                foreach (var mi in visibleMonthIdx)
                {
                    DistTypeHeaderPanel.Items.Add(new Border
                    {
                        Width           = MonthColW,
                        Height          = 20,
                        Background      = new SolidColorBrush(Color.FromRgb(232, 238, 248)),
                        BorderBrush     = new SolidColorBrush(Color.FromRgb(197, 208, 224)),
                        BorderThickness = new Thickness(0, 0, 1, 1),
                        Child           = new TextBlock
                        {
                            Text                = months[mi].ToString("MMM/yy"),
                            FontSize            = 10,
                            FontWeight          = FontWeights.SemiBold,
                            Foreground          = new SolidColorBrush(Color.FromRgb(50, 80, 140)),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment   = VerticalAlignment.Center
                        }
                    });
                }
                DistTypeHeaderPanel.Items.Add(new Border
                {
                    Width           = ProjTotalW,
                    Height          = 20,
                    Background      = new SolidColorBrush(Color.FromRgb(220, 230, 248)),
                    BorderBrush     = new SolidColorBrush(Color.FromRgb(197, 208, 224)),
                    BorderThickness = new Thickness(0, 0, 2, 1),
                    Child           = new TextBlock
                    {
                        Text                = "Total",
                        FontSize            = 10,
                        FontWeight          = FontWeights.SemiBold,
                        Foreground          = new SolidColorBrush(Color.FromRgb(30, 60, 120)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment   = VerticalAlignment.Center
                    }
                });
            }
            DistTypeHeaderPanel.Items.Add(new Border
            {
                Width           = GrandTotalW,
                Height          = 20,
                Background      = new SolidColorBrush(Color.FromRgb(210, 224, 248)),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(197, 208, 224)),
                BorderThickness = new Thickness(0, 0, 1, 1)
            });

            // ── Linhas por recurso ──
            // grandByProjMonth[pi][mi] para linha de totais
            var grandByProjMonth = visibleProjIdx.ToDictionary(
                pi => pi, _ => new double[months.Count]);

            for (int ri = 0; ri < allResources.Count; ri++)
            {
                var resName = allResources[ri];

                // total de horas do recurso por mês (somando todos os projetos) — para calcular %
                var totalByMonth = new double[months.Count];
                foreach (var pi in visibleProjIdx)
                    for (int mi = 0; mi < months.Count; mi++)
                        totalByMonth[mi] += dist[pi][resName][mi];

                double resGrandTotal = totalByMonth.Sum();

                var resBg = new SolidColorBrush(ri % 2 == 0
                    ? Color.FromRgb(255, 255, 255)
                    : Color.FromRgb(248, 250, 255));

                DistResPanel.Items.Add(new Border
                {
                    Width           = 200,
                    Height          = DistRowH,
                    Background      = resBg,
                    BorderBrush     = new SolidColorBrush(Color.FromRgb(220, 228, 240)),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding         = new Thickness(8, 0, 4, 0),
                    ToolTip         = resName,
                    Child           = new TextBlock
                    {
                        Text              = resName,
                        FontSize          = 12,
                        FontWeight        = FontWeights.SemiBold,
                        Foreground        = new SolidColorBrush(Color.FromRgb(40, 70, 130)),
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming      = TextTrimming.CharacterEllipsis
                    }
                });

                var row = new StackPanel { Orientation = Orientation.Horizontal };

                foreach (var pi in visibleProjIdx)
                {
                    double projResTotal = 0;

                    foreach (var mi in visibleMonthIdx)
                    {
                        double h   = dist[pi][resName][mi];
                        double pct = totalByMonth[mi] > 0.01 ? h / totalByMonth[mi] * 100 : 0;
                        projResTotal += h;

                        if (pi == visibleProjIdx[0]) { /* grandByProjMonth handled below */ }
                        grandByProjMonth[pi][mi] += h;

                        string hStr  = h   < 0.05 ? "–"         : $"{h:0.#}h";
                        string pStr  = pct < 0.05 ? ""          : $"{pct:0.#}%";

                        var cellBg = h < 0.05 ? resBg
                            : new SolidColorBrush(Color.FromArgb(
                                (byte)Math.Min(220, 60 + (int)(pct * 1.5)),
                                195, 215, 248));

                        var stack = new StackPanel
                        {
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment   = VerticalAlignment.Center
                        };
                        stack.Children.Add(new TextBlock
                        {
                            Text                = hStr,
                            FontSize            = 11,
                            FontWeight          = FontWeights.SemiBold,
                            Foreground          = h < 0.05
                                ? new SolidColorBrush(Color.FromRgb(200, 200, 200))
                                : new SolidColorBrush(Color.FromRgb(20, 50, 120)),
                            HorizontalAlignment = HorizontalAlignment.Center
                        });
                        if (!string.IsNullOrEmpty(pStr))
                            stack.Children.Add(new TextBlock
                            {
                                Text                = pStr,
                                FontSize            = 9,
                                Foreground          = new SolidColorBrush(Color.FromRgb(60, 100, 180)),
                                HorizontalAlignment = HorizontalAlignment.Center
                            });

                        row.Children.Add(new Border
                        {
                            Width           = MonthColW,
                            Height          = DistRowH,
                            Background      = cellBg,
                            BorderBrush     = new SolidColorBrush(Color.FromRgb(220, 228, 240)),
                            BorderThickness = new Thickness(0, 0, 1, 1),
                            Child           = stack
                        });
                    }

                    // Subtotal do projeto para este recurso
                    double projResPct = resGrandTotal > 0.01 ? projResTotal / resGrandTotal * 100 : 0;
                    string ptH = projResTotal < 0.05 ? "–" : $"{projResTotal:0.#}h";
                    string ptP = projResPct  < 0.05 ? ""  : $"{projResPct:0.#}%";
                    var ptStack = new StackPanel
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment   = VerticalAlignment.Center
                    };
                    ptStack.Children.Add(new TextBlock
                    {
                        Text                = ptH,
                        FontSize            = 11,
                        FontWeight          = FontWeights.Bold,
                        Foreground          = projResTotal < 0.05
                            ? new SolidColorBrush(Color.FromRgb(180, 180, 180))
                            : new SolidColorBrush(Color.FromRgb(20, 40, 100)),
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
                    if (!string.IsNullOrEmpty(ptP))
                        ptStack.Children.Add(new TextBlock
                        {
                            Text                = ptP,
                            FontSize            = 9,
                            Foreground          = new SolidColorBrush(Color.FromRgb(40, 80, 160)),
                            HorizontalAlignment = HorizontalAlignment.Center
                        });

                    row.Children.Add(new Border
                    {
                        Width           = ProjTotalW,
                        Height          = DistRowH,
                        Background      = new SolidColorBrush(Color.FromRgb(220, 232, 252)),
                        BorderBrush     = new SolidColorBrush(Color.FromRgb(180, 200, 230)),
                        BorderThickness = new Thickness(0, 0, 2, 1),
                        Child           = ptStack
                    });
                }

                // Grand total da linha
                row.Children.Add(MakeTotalCell(resGrandTotal, GrandTotalW, DistRowH));
                DistDataPanel.Items.Add(row);
            }

            // ── Linha TOTAL GERAL ──
            var totalBg2 = new SolidColorBrush(Color.FromRgb(43, 87, 154));
            DistResPanel.Items.Add(MakeCell("TOTAL GERAL", 200, DistRowH, totalBg2,
                bold: true, fg: Colors.White, leftPad: 8));

            double grandTotal2 = 0;
            var grandRow2 = new StackPanel { Orientation = Orientation.Horizontal };

            foreach (var pi in visibleProjIdx)
            {
                double projSum = 0;
                foreach (var mi in visibleMonthIdx)
                {
                    double mv = grandByProjMonth[pi][mi];
                    projSum += mv;
                    grandRow2.Children.Add(MakeTotalCell(mv, MonthColW, DistRowH,
                        bold: true, fg: Colors.White, bg: Color.FromRgb(43, 87, 154)));
                }
                grandTotal2 += projSum;
                grandRow2.Children.Add(MakeTotalCell(projSum, ProjTotalW, DistRowH,
                    bold: true, fg: Colors.White, bg: Color.FromRgb(30, 70, 140)));
            }
            grandRow2.Children.Add(MakeTotalCell(grandTotal2, GrandTotalW, DistRowH,
                bold: true, fg: Colors.White, bg: Color.FromRgb(25, 60, 120)));
            DistDataPanel.Items.Add(grandRow2);
        }

        // ── Sincronização de scroll ───────────────────────────────────────────
        private bool _scrolling;
        private void OnMainScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_scrolling) return;
            _scrolling = true;
            MonthHeaderScroll.ScrollToHorizontalOffset(e.HorizontalOffset);
            ResourceHeaderScroll.ScrollToHorizontalOffset(e.HorizontalOffset);
            LeftScroll.ScrollToVerticalOffset(e.VerticalOffset);
            ResScroll.ScrollToVerticalOffset(e.VerticalOffset);
            TypeScroll.ScrollToVerticalOffset(e.VerticalOffset);
            CcScroll.ScrollToVerticalOffset(e.VerticalOffset);
            _scrolling = false;
        }

        private void OnDistScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_scrolling) return;
            _scrolling = true;
            DistProjHeaderScroll.ScrollToHorizontalOffset(e.HorizontalOffset);
            DistTypeHeaderScroll.ScrollToHorizontalOffset(e.HorizontalOffset);
            DistResScroll.ScrollToVerticalOffset(e.VerticalOffset);
            _scrolling = false;
        }

        private void OnTabChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (MainTabControl.SelectedIndex == 1)
                BuildDistributionGrid();
            else if (MainTabControl.SelectedIndex == 2)
                BuildStoriesGrid();
        }

        private void OnSrScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_scrolling) return;
            _scrolling = true;
            SrHeaderScroll.ScrollToHorizontalOffset(e.HorizontalOffset);
            SrLeftScroll.ScrollToVerticalOffset(e.VerticalOffset);
            _scrolling = false;
        }

        private void OnSrLeftScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_scrolling) return;
            _scrolling = true;
            SrMainScroll.ScrollToVerticalOffset(e.VerticalOffset);
            _scrolling = false;
        }

        // ── Aba 3: Stories por Recurso ────────────────────────────────────────
        private const double SrResW   = 160;
        private const double SrProjW  = 160;
        private const double SrStoryW = 260;
        private const double SrMonthW = 72;
        private const double SrTotalW = 72;
        private const double SrCapexW = 72;
        private const double SrOpexW  = 72;
        private const double SrRowH   = 22;

        private void BuildStoriesGrid()
        {
            SrHeaderPanel.Items.Clear();
            SrLeftPanel.Items.Clear();
            SrDataPanel.Items.Clear();

            if (_projects.Count == 0) return;

            var (periodStart, periodEnd) = GetPeriod();
            bool hideZero = OnlyWithHoursBox.IsChecked == true;
            var months = BuildMonths(periodStart, periodEnd);

            // Monta estrutura: recurso → projeto → lista de stories com horas/mês
            // story entry: (task, double[months])
            var byRes = new SortedDictionary<string, List<(LoadedProject Proj, ProjectTask Task, double[] MonthHours)>>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var proj in _projects)
            {
                foreach (var task in GetLeafTasks(proj.Data.Tasks))
                {
                    foreach (var tr in task.Resources)
                    {
                        var rname = tr.Resource?.Name;
                        if (string.IsNullOrWhiteSpace(rname)) continue;

                        var mh = new double[months.Count];
                        bool any = false;
                        for (int mi = 0; mi < months.Count; mi++)
                        {
                            var ms = months[mi];
                            var me = new DateTime(ms.Year, ms.Month, DateTime.DaysInMonth(ms.Year, ms.Month));
                            double h = ComputeHoursForTask(task, tr, ms, me);
                            mh[mi] = h;
                            if (h > 0.01) any = true;
                        }
                        if (hideZero && !any) continue;

                        if (!byRes.TryGetValue(rname, out var list))
                            byRes[rname] = list = [];
                        list.Add((proj, task, mh));
                    }
                }
            }

            // Quais meses têm dados
            var visMi = Enumerable.Range(0, months.Count)
                .Where(mi => !hideZero || byRes.Values.Any(l => l.Any(x => x.MonthHours[mi] > 0.01)))
                .ToList();

            // ── Cabeçalho de meses ──
            foreach (var mi in visMi)
            {
                SrHeaderPanel.Items.Add(new Border
                {
                    Width = SrMonthW, Height = 22,
                    Background = new SolidColorBrush(Color.FromRgb(43, 87, 154)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(29, 63, 115)),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Child = new TextBlock
                    {
                        Text = months[mi].ToString("MMM/yy"), FontSize = 11,
                        FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                });
            }
            SrHeaderPanel.Items.Add(SrMakeMonthHeader("TOTAL",  SrTotalW, Color.FromRgb(25,  60, 120)));
            SrHeaderPanel.Items.Add(SrMakeMonthHeader("CAPEX",  SrCapexW, Color.FromRgb(140, 70,  20)));
            SrHeaderPanel.Items.Add(SrMakeMonthHeader("OPEX",   SrOpexW,  Color.FromRgb(43, 100,  43)));

            // ── Linhas ──
            var grandByMonth = new double[months.Count];
            double grandCapex = 0, grandOpex = 0;

            foreach (var (resName, entries) in byRes)
            {
                // Agrupa por projeto
                var byProj = entries
                    .GroupBy(e => e.Proj.Name, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key);

                // Totais do recurso por mês
                var resByMonth = new double[months.Count];
                foreach (var e in entries)
                    for (int mi = 0; mi < months.Count; mi++)
                        resByMonth[mi] += e.MonthHours[mi];

                bool resFirst = true;
                foreach (var projGroup in byProj)
                {
                    var projEntries = projGroup.ToList();
                    var projByMonth = new double[months.Count];
                    foreach (var e in projEntries)
                        for (int mi = 0; mi < months.Count; mi++)
                            projByMonth[mi] += e.MonthHours[mi];

                    bool projFirst = true;
                    foreach (var (proj, task, mh) in projEntries.OrderBy(e => e.Task.Start))
                    {
                        // Coluna fixa
                        var leftBg = new SolidColorBrush(Color.FromRgb(248, 250, 255));
                        var leftRow = new StackPanel { Orientation = Orientation.Horizontal };

                        // Célula Recurso — só na primeira linha do recurso
                        leftRow.Children.Add(new Border
                        {
                            Width = SrResW, Height = SrRowH,
                            Background = leftBg,
                            BorderBrush = new SolidColorBrush(Color.FromRgb(210, 220, 240)),
                            BorderThickness = new Thickness(0, 0, 1, 1),
                            Padding = new Thickness(6, 0, 4, 0),
                            Child = new TextBlock
                            {
                                Text = resFirst && projFirst ? resName : "",
                                FontSize = 11, FontWeight = FontWeights.SemiBold,
                                Foreground = new SolidColorBrush(Color.FromRgb(43, 87, 154)),
                                VerticalAlignment = VerticalAlignment.Center,
                                TextTrimming = TextTrimming.CharacterEllipsis,
                                ToolTip = resFirst && projFirst ? resName : null
                            }
                        });

                        // Célula Projeto — só na primeira linha do projeto
                        leftRow.Children.Add(new Border
                        {
                            Width = SrProjW, Height = SrRowH,
                            Background = leftBg,
                            BorderBrush = new SolidColorBrush(Color.FromRgb(210, 220, 240)),
                            BorderThickness = new Thickness(0, 0, 1, 1),
                            Padding = new Thickness(4, 0, 4, 0),
                            Child = new TextBlock
                            {
                                Text = projFirst ? projGroup.Key : "",
                                FontSize = 11,
                                Foreground = new SolidColorBrush(Color.FromRgb(60, 100, 170)),
                                VerticalAlignment = VerticalAlignment.Center,
                                TextTrimming = TextTrimming.CharacterEllipsis,
                                ToolTip = projFirst ? projGroup.Key : null
                            }
                        });

                        // Célula Story
                        string storyLabel = task.Name ?? $"#{task.TfsId}";
                        leftRow.Children.Add(new Border
                        {
                            Width = SrStoryW, Height = SrRowH,
                            Background = leftBg,
                            BorderBrush = new SolidColorBrush(Color.FromRgb(210, 220, 240)),
                            BorderThickness = new Thickness(0, 0, 1, 1),
                            Padding = new Thickness(4, 0, 4, 0),
                            ToolTip = $"{storyLabel}\nInício: {task.Start:dd/MM/yy}  Fim: {task.Finish:dd/MM/yy}  HH: {task.EstimatedHours?.ToString("0.#") ?? "–"}h",
                            Child = new TextBlock
                            {
                                Text = storyLabel, FontSize = 10,
                                Foreground = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                                VerticalAlignment = VerticalAlignment.Center,
                                TextTrimming = TextTrimming.CharacterEllipsis
                            }
                        });

                        SrLeftPanel.Items.Add(leftRow);

                        // Dados (meses)
                        var dataRow = new StackPanel { Orientation = Orientation.Horizontal };
                        double rowTotal = 0;
                        foreach (var mi in visMi)
                        {
                            double h = mh[mi];
                            rowTotal += h;
                            dataRow.Children.Add(SrMakeCell(h > 0.01 ? $"{h:0.#}h" : "–",
                                SrMonthW, h > 0.01 ? Color.FromRgb(40, 100, 200) : Color.FromRgb(200, 200, 200),
                                Color.FromRgb(248, 250, 255), bold: false));
                        }
                        dataRow.Children.Add(SrMakeCell(rowTotal > 0.01 ? $"{rowTotal:0.#}h" : "–",
                            SrTotalW, Color.FromRgb(20, 50, 110), Color.FromRgb(230, 238, 252), bold: true));
                        // CAPEX / OPEX por story
                        bool storyIsOpex = proj.IsOpex;
                        dataRow.Children.Add(SrMakeCell(!storyIsOpex && rowTotal > 0.01 ? $"{rowTotal:0.#}h" : "–",
                            SrCapexW, !storyIsOpex && rowTotal > 0.01 ? Color.FromRgb(120, 60, 10) : Color.FromRgb(200, 200, 200),
                            Color.FromRgb(255, 248, 240), bold: false));
                        dataRow.Children.Add(SrMakeCell(storyIsOpex && rowTotal > 0.01 ? $"{rowTotal:0.#}h" : "–",
                            SrOpexW, storyIsOpex && rowTotal > 0.01 ? Color.FromRgb(20, 90, 20) : Color.FromRgb(200, 200, 200),
                            Color.FromRgb(240, 250, 240), bold: false));
                        SrDataPanel.Items.Add(dataRow);

                        projFirst = false;
                        resFirst  = false;
                    }

                    // Sub-total do projeto
                    var projTotalBg = Color.FromRgb(220, 230, 248);
                    var leftProjTotal = new StackPanel { Orientation = Orientation.Horizontal };
                    leftProjTotal.Children.Add(new Border { Width = SrResW, Height = SrRowH, Background = new SolidColorBrush(projTotalBg), BorderBrush = new SolidColorBrush(Color.FromRgb(180,200,230)), BorderThickness = new Thickness(0,0,1,1) });
                    leftProjTotal.Children.Add(new Border
                    {
                        Width = SrProjW, Height = SrRowH,
                        Background = new SolidColorBrush(projTotalBg),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(180, 200, 230)),
                        BorderThickness = new Thickness(0, 0, 1, 1),
                        Padding = new Thickness(4, 0, 4, 0),
                        Child = new TextBlock { Text = projGroup.Key, FontSize = 10, FontWeight = FontWeights.SemiBold,
                            Foreground = new SolidColorBrush(Color.FromRgb(30, 60, 130)),
                            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }
                    });
                    leftProjTotal.Children.Add(new Border
                    {
                        Width = SrStoryW, Height = SrRowH,
                        Background = new SolidColorBrush(projTotalBg),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(180, 200, 230)),
                        BorderThickness = new Thickness(0, 0, 1, 1),
                        Padding = new Thickness(4, 0, 4, 0),
                        Child = new TextBlock { Text = "Subtotal", FontSize = 10, FontStyle = FontStyles.Italic,
                            Foreground = new SolidColorBrush(Color.FromRgb(60, 90, 150)),
                            VerticalAlignment = VerticalAlignment.Center }
                    });
                    SrLeftPanel.Items.Add(leftProjTotal);

                    bool projIsOpex = projEntries[0].Proj.IsOpex;
                    var projDataTotal = new StackPanel { Orientation = Orientation.Horizontal };
                    double ptotal = 0;
                    foreach (var mi in visMi)
                    {
                        double h = projByMonth[mi];
                        ptotal += h;
                        projDataTotal.Children.Add(SrMakeCell(h > 0.01 ? $"{h:0.#}h" : "–",
                            SrMonthW, Color.FromRgb(30, 60, 130), projTotalBg, bold: true));
                    }
                    projDataTotal.Children.Add(SrMakeCell(ptotal > 0.01 ? $"{ptotal:0.#}h" : "–",
                        SrTotalW, Color.FromRgb(20, 40, 100), Color.FromRgb(205, 218, 245), bold: true));
                    projDataTotal.Children.Add(SrMakeCell(!projIsOpex && ptotal > 0.01 ? $"{ptotal:0.#}h" : "–",
                        SrCapexW, !projIsOpex && ptotal > 0.01 ? Color.FromRgb(120, 60, 10) : Color.FromRgb(180, 180, 180),
                        Color.FromRgb(252, 242, 230), bold: true));
                    projDataTotal.Children.Add(SrMakeCell(projIsOpex && ptotal > 0.01 ? $"{ptotal:0.#}h" : "–",
                        SrOpexW, projIsOpex && ptotal > 0.01 ? Color.FromRgb(20, 90, 20) : Color.FromRgb(180, 180, 180),
                        Color.FromRgb(232, 248, 232), bold: true));
                    SrDataPanel.Items.Add(projDataTotal);
                }

                // Total do recurso
                var resTotalBg = Color.FromRgb(43, 87, 154);
                var leftResTotal = new StackPanel { Orientation = Orientation.Horizontal };
                leftResTotal.Children.Add(new Border
                {
                    Width = SrResW, Height = SrRowH + 2,
                    Background = new SolidColorBrush(resTotalBg),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(29, 63, 115)),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(6, 0, 4, 0),
                    Child = new TextBlock { Text = "", FontSize = 11, FontWeight = FontWeights.SemiBold,
                        Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis }
                });
                leftResTotal.Children.Add(new Border { Width = SrProjW, Height = SrRowH + 2, Background = new SolidColorBrush(resTotalBg), BorderBrush = new SolidColorBrush(Color.FromRgb(29,63,115)), BorderThickness = new Thickness(0,0,1,1) });
                leftResTotal.Children.Add(new Border
                {
                    Width = SrStoryW, Height = SrRowH + 2,
                    Background = new SolidColorBrush(resTotalBg),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(29, 63, 115)),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(4, 0, 4, 0),
                    Child = new TextBlock { Text = "TOTAL", FontSize = 11, FontWeight = FontWeights.SemiBold,
                        Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center }
                });
                SrLeftPanel.Items.Add(leftResTotal);

                double resCapex = entries.Where(e => !e.Proj.IsOpex).Sum(e => e.MonthHours.Sum());
                double resOpex  = entries.Where(e =>  e.Proj.IsOpex).Sum(e => e.MonthHours.Sum());
                grandCapex += resCapex;
                grandOpex  += resOpex;

                var resDataTotal = new StackPanel { Orientation = Orientation.Horizontal };
                double rtotal = 0;
                foreach (var mi in visMi)
                {
                    double h = resByMonth[mi];
                    rtotal += h;
                    grandByMonth[mi] += h;
                    resDataTotal.Children.Add(SrMakeCell(h > 0.01 ? $"{h:0.#}h" : "–",
                        SrMonthW, Colors.White, resTotalBg, bold: true, height: SrRowH + 2));
                }
                resDataTotal.Children.Add(SrMakeCell(rtotal > 0.01 ? $"{rtotal:0.#}h" : "–",
                    SrTotalW, Colors.White, Color.FromRgb(25, 60, 120), bold: true, height: SrRowH + 2));
                resDataTotal.Children.Add(SrMakeCell(resCapex > 0.01 ? $"{resCapex:0.#}h" : "–",
                    SrCapexW, Colors.White, Color.FromRgb(140, 70, 20), bold: true, height: SrRowH + 2));
                resDataTotal.Children.Add(SrMakeCell(resOpex > 0.01 ? $"{resOpex:0.#}h" : "–",
                    SrOpexW, Colors.White, Color.FromRgb(43, 100, 43), bold: true, height: SrRowH + 2));
                SrDataPanel.Items.Add(resDataTotal);

                // Separador
                SrLeftPanel.Items.Add(new Border { Height = 2, Background = new SolidColorBrush(Color.FromRgb(180, 200, 230)) });
                SrDataPanel.Items.Add(new Border { Height = 2, Background = new SolidColorBrush(Color.FromRgb(180, 200, 230)) });
            }

            // TOTAL GERAL
            var gtBg = Color.FromRgb(25, 60, 120);
            var gtLeft = new StackPanel { Orientation = Orientation.Horizontal };
            gtLeft.Children.Add(new Border
            {
                Width = SrResW + SrProjW, Height = SrRowH + 2,
                Background = new SolidColorBrush(gtBg),
                BorderBrush = new SolidColorBrush(Color.FromRgb(15, 40, 90)),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(6, 0, 4, 0),
                Child = new TextBlock { Text = "TOTAL GERAL", FontSize = 11, FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center }
            });
            gtLeft.Children.Add(new Border { Width = SrStoryW, Height = SrRowH + 2, Background = new SolidColorBrush(gtBg), BorderBrush = new SolidColorBrush(Color.FromRgb(15,40,90)), BorderThickness = new Thickness(0,0,1,1) });
            SrLeftPanel.Items.Add(gtLeft);

            var gtData = new StackPanel { Orientation = Orientation.Horizontal };
            double gtotal = 0;
            foreach (var mi in visMi)
            {
                double h = grandByMonth[mi];
                gtotal += h;
                gtData.Children.Add(SrMakeCell(h > 0.01 ? $"{h:0.#}h" : "–",
                    SrMonthW, Colors.White, gtBg, bold: true, height: SrRowH + 2));
            }
            gtData.Children.Add(SrMakeCell(gtotal > 0.01 ? $"{gtotal:0.#}h" : "–",
                SrTotalW, Colors.White, Color.FromRgb(15, 40, 90), bold: true, height: SrRowH + 2));
            gtData.Children.Add(SrMakeCell(grandCapex > 0.01 ? $"{grandCapex:0.#}h" : "–",
                SrCapexW, Colors.White, Color.FromRgb(100, 50, 10), bold: true, height: SrRowH + 2));
            gtData.Children.Add(SrMakeCell(grandOpex > 0.01 ? $"{grandOpex:0.#}h" : "–",
                SrOpexW, Colors.White, Color.FromRgb(30, 80, 30), bold: true, height: SrRowH + 2));
            SrDataPanel.Items.Add(gtData);
        }

        private static double ComputeHoursForTask(ProjectTask task, TaskResource tr,
            DateTime monthStart, DateTime monthEnd)
        {
            double hours = tr.EstimatedHours ?? 0;
            if (hours <= 0) return 0;

            var tStart = task.Start.Date;
            var tEnd   = task.Finish.Date;
            if (tEnd < monthStart || tStart > monthEnd) return 0;

            if (tStart >= tEnd) return hours;

            var overlapStart = tStart < monthStart ? monthStart : tStart;
            var overlapEnd   = tEnd   > monthEnd   ? monthEnd   : tEnd;
            double overlapDays = Math.Max(0, (overlapEnd - overlapStart).TotalDays + 1);
            double totalDays   = Math.Max(1, (tEnd - tStart).TotalDays + 1);
            return hours * (overlapDays / totalDays);
        }

        private static Border SrMakeCell(string text, double width, Color fg, Color bg, bool bold, double height = SrRowH)
            => new Border
            {
                Width = width, Height = height,
                Background = new SolidColorBrush(bg),
                BorderBrush = new SolidColorBrush(Color.FromRgb(210, 220, 240)),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Child = new TextBlock
                {
                    Text = text, FontSize = 11,
                    FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = new SolidColorBrush(fg),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                }
            };

        private static Border SrMakeMonthHeader(string text, double width, Color bg)
            => new Border
            {
                Width = width, Height = 22,
                Background = new SolidColorBrush(bg),
                BorderBrush = new SolidColorBrush(Color.FromRgb(29, 63, 115)),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Child = new TextBlock
                {
                    Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

        // ── Handlers ──────────────────────────────────────────────────────────
        private void OnSelectProjectsClick(object sender, RoutedEventArgs e)
        {
            var opts       = TfsConnectionStore.Load();
            var devOpsList = DevOpsProjectListService.Load(opts.DevOpsProjectListPath);

            if (devOpsList.Count == 0)
            {
                MessageBox.Show(
                    "Nenhum projeto DevOps cadastrado.\n\n" +
                    "Acesse Visualizar → Portfólio de Projetos para configurar os projetos.",
                    "Lista vazia", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var win = new ProjectPickerWindow(devOpsList, opts.PortfolioProjectConfigs) { Owner = this };
            if (win.ShowDialog() != true) return;

            // Merge: update OPEX/CC from picker; add new; remove deselected
            foreach (var newCfg in win.SelectedConfigs)
            {
                var existing = opts.PortfolioProjectConfigs.FirstOrDefault(c =>
                    string.Equals(c.ProjectName, newCfg.ProjectName, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.IsOpex     = newCfg.IsOpex;
                    existing.CostCenter = newCfg.CostCenter;
                }
                else
                {
                    opts.PortfolioProjectConfigs.Add(newCfg);
                }
            }

            var selectedNames = win.SelectedConfigs
                .Select(c => c.ProjectName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            opts.PortfolioProjectConfigs.RemoveAll(c =>
                devOpsList.Any(d => string.Equals(d.Name, c.ProjectName, StringComparison.OrdinalIgnoreCase))
                && !selectedNames.Contains(c.ProjectName));

            TfsConnectionStore.Save(opts, !string.IsNullOrEmpty(opts.PersonalAccessToken));

            int count = opts.PortfolioProjectConfigs.Count;
            StatusText.Text = $"{count} projeto(s) selecionado(s). Clique em '☁ Importar do DevOps' para carregar os dados.";
        }

        private async void OnImportFromDevOpsClick(object sender, RoutedEventArgs e)
        {
            var opts = TfsConnectionStore.Load();

            if (string.IsNullOrWhiteSpace(opts.OrganizationUrl) ||
                string.IsNullOrWhiteSpace(opts.PersonalAccessToken))
            {
                MessageBox.Show(
                    "Conexão com o Azure DevOps não configurada.\n\n" +
                    "Acesse Exportar → Sincronizar para configurar o PAT e a URL da organização.",
                    "Configuração necessária", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (opts.PortfolioProjectConfigs.Count == 0)
            {
                MessageBox.Show("Nenhum projeto selecionado. Use '☑ Selecionar Projetos' primeiro.",
                    "Sem projetos", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var devOpsList = DevOpsProjectListService.Load(opts.DevOpsProjectListPath);

            var toImport = new List<(PortfolioProjectConfig Cfg, int RootId)>();
            var skipped  = new List<string>();

            foreach (var cfg in opts.PortfolioProjectConfigs)
            {
                var dp = devOpsList.FirstOrDefault(d =>
                    string.Equals(d.Name, cfg.ProjectName, StringComparison.OrdinalIgnoreCase));
                if (dp == null || dp.RootWorkItemId <= 0)
                    skipped.Add(cfg.ProjectName);
                else
                    toImport.Add((cfg, dp.RootWorkItemId));
            }

            if (toImport.Count == 0)
            {
                MessageBox.Show(
                    "Nenhum projeto selecionado tem ID raiz configurado.\n\n" +
                    "Edite o Portfólio de Projetos (Visualizar → Portfólio de Projetos) e informe o ID raiz de cada projeto.",
                    "ID raiz não configurado", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ImportBtn.IsEnabled = false;
            var errors = new List<string>();
            var imported = new List<LoadedProject>();

            for (int i = 0; i < toImport.Count; i++)
            {
                var (cfg, rootId) = toImport[i];
                StatusText.Text = $"Importando {i + 1}/{toImport.Count}: {cfg.ProjectName}...";
                await Task.Yield(); // permite atualizar a UI

                var importOpts = new TfsConnectionOptions
                {
                    OrganizationUrl      = opts.OrganizationUrl,
                    TeamProject          = opts.TeamProject,
                    PersonalAccessToken  = opts.PersonalAccessToken,
                    RootWorkItemId       = rootId,
                    HoursPerDay          = opts.HoursPerDay,
                    EffortFieldName      = opts.EffortFieldName,
                    StartFieldName       = opts.StartFieldName,
                    FinishFieldName      = opts.FinishFieldName,
                    PercAlocFieldName    = opts.PercAlocFieldName,
                    SyncVersionFieldName = opts.SyncVersionFieldName,
                    SyncNameFieldName    = opts.SyncNameFieldName,
                    FixedStartTagName    = opts.FixedStartTagName,
                    FixedFinishTagName   = opts.FixedFinishTagName,
                    SyncPredecessorLinks = false,
                    FutureSprintDays     = 0
                };

                try
                {
                    var result = await TfsImportService.ImportAsync(importOpts);
                    imported.Add(new LoadedProject
                    {
                        FilePath   = string.Empty,
                        Name       = cfg.ProjectName,
                        IsOpex     = cfg.IsOpex,
                        CostCenter = cfg.CostCenter,
                        Data       = result.Project
                    });
                }
                catch (Exception ex)
                {
                    errors.Add($"{cfg.ProjectName}: {ex.Message}");
                }
            }

            ImportBtn.IsEnabled = true;
            _projects = imported;
            BuildGrid();

            var sb = new StringBuilder();
            sb.Append($"{imported.Count} projeto(s) importado(s) do DevOps");
            if (skipped.Count > 0)
                sb.Append($"  ·  {skipped.Count} sem ID raiz (ignorado)");
            if (errors.Count > 0)
                sb.Append($"  ·  {errors.Count} erro(s)");
            StatusText.Text = sb.ToString();

            if (errors.Count > 0)
                MessageBox.Show("Erros durante a importação:\n\n" + string.Join("\n", errors),
                    "Importação parcial", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void OnPeriodChanged(object sender, SelectionChangedEventArgs e) { }

        private void OnFilterChanged(object sender, RoutedEventArgs e) => BuildGrid();

        private void OnExportExcelClick(object sender, RoutedEventArgs e)
        {
            if (_projects.Count == 0)
            {
                MessageBox.Show("Nenhum dado para exportar. Importe os projetos primeiro.",
                    "Exportar", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title      = "Exportar Mapa de Alocação",
                Filter     = "Excel XML 2003 (*.xml)|*.xml",
                DefaultExt = ".xml",
                FileName   = "Mapa de Alocação para Projetos"
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                ExportAllocationToExcel(dlg.FileName);
                StatusText.Text = $"Exportado: {dlg.FileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao exportar:\n{ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportAllocationToExcel(string filePath)
        {
            var (periodStart, periodEnd) = GetPeriod();
            var months    = BuildMonths(periodStart, periodEnd);
            bool hideZero = OnlyWithHoursBox.IsChecked == true;

            XNamespace ns = "urn:schemas-microsoft-com:office:spreadsheet";
            XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";

            // ── Pré-computa dados (igual ao BuildGrid) ──
            var projResourceData = new List<(LoadedProject Proj,
                                             List<(string Res, double[] MonthHours)> Rows)>();
            var allResources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var proj in _projects)
            {
                var resources = proj.Data.Resources
                    .Where(r => r.Type == ResourceType.Work && !string.IsNullOrWhiteSpace(r.Name))
                    .Select(r => r.Name!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n)
                    .ToList();

                var rows = new List<(string Res, double[] MonthHours)>();
                foreach (var res in resources)
                {
                    var mh = new double[months.Count];
                    for (int mi = 0; mi < months.Count; mi++)
                    {
                        var ms = months[mi];
                        var me = new DateTime(ms.Year, ms.Month, DateTime.DaysInMonth(ms.Year, ms.Month));
                        mh[mi] = ComputeHours(proj.Data, res, ms, me);
                    }
                    if (hideZero && mh.Sum() < 0.01) continue;
                    rows.Add((res, mh));
                    allResources.Add(res);
                }
                projResourceData.Add((proj, rows));
            }

            var visibleMonths = Enumerable.Range(0, months.Count)
                .Where(mi => !hideZero || projResourceData.Any(p => p.Rows.Any(r => r.MonthHours[mi] > 0.01)))
                .ToList();

            // ── Aba 1: Horas por Projeto ──
            var sheet1Rows = new List<XElement>();

            // Cabeçalho
            var header = new List<string> { "Projeto", "Recurso", "Tipo", "Centro de Custo" };
            header.AddRange(visibleMonths.Select(mi => months[mi].ToString("MMM/yyyy")));
            header.Add("TOTAL");
            sheet1Rows.Add(ExXmlRow(ns, header.ToArray()));

            var grandByMonth1 = new double[months.Count];

            foreach (var (proj, resRows) in projResourceData)
            {
                var projByMonth = new double[months.Count];
                foreach (var (_, mh) in resRows)
                    for (int mi = 0; mi < months.Count; mi++)
                        projByMonth[mi] += mh[mi];

                // Linha do projeto (totais)
                var projCells = new List<object> { proj.Name, "", proj.IsOpex ? "OPEX" : "CAPEX", proj.CostCenter };
                foreach (var mi in visibleMonths) projCells.Add(Math.Round(projByMonth[mi], 1));
                projCells.Add(Math.Round(projByMonth.Sum(), 1));
                sheet1Rows.Add(ExXmlRowMixed(ns, projCells.ToArray()));

                for (int mi = 0; mi < months.Count; mi++)
                    grandByMonth1[mi] += projByMonth[mi];

                // Sub-linhas de recurso
                foreach (var (resName, mh) in resRows)
                {
                    var resCells = new List<object> { "", resName, "", "" };
                    foreach (var mi in visibleMonths) resCells.Add(Math.Round(mh[mi], 1));
                    resCells.Add(Math.Round(mh.Sum(), 1));
                    sheet1Rows.Add(ExXmlRowMixed(ns, resCells.ToArray()));
                }
            }

            // Linha TOTAL GERAL
            var totalCells = new List<object> { "TOTAL GERAL", "", "", "" };
            foreach (var mi in visibleMonths) totalCells.Add(Math.Round(grandByMonth1[mi], 1));
            totalCells.Add(Math.Round(visibleMonths.Select(mi => grandByMonth1[mi]).Sum(), 1));
            sheet1Rows.Add(ExXmlRowMixed(ns, totalCells.ToArray()));

            // ── Aba 2: Distribuição por Pessoa ──
            var resList = allResources.OrderBy(r => r).ToList();
            if (hideZero)
                resList = resList.Where(r => projResourceData.Any(p => p.Rows.Any(x => x.Res == r))).ToList();

            // dist[proj][res][month]
            var dist2 = projResourceData.Select(p =>
                resList.ToDictionary(r => r,
                    r => p.Rows.FirstOrDefault(x => string.Equals(x.Res, r, StringComparison.OrdinalIgnoreCase))
                               .MonthHours ?? new double[months.Count],
                    StringComparer.OrdinalIgnoreCase))
                .ToList();

            var visibleProj2 = Enumerable.Range(0, projResourceData.Count)
                .Where(pi => !hideZero || resList.Any(r => dist2[pi][r].Sum() > 0.01))
                .ToList();

            var sheet2Rows = new List<XElement>();

            // Cabeçalho linha 1: projeto spans
            var hdr2 = new List<string> { "Recurso" };
            foreach (var pi in visibleProj2)
            {
                var p = projResourceData[pi].Proj;
                string label = $"{p.Name} ({(p.IsOpex ? "OPEX" : "CAPEX")})";
                foreach (var mi in visibleMonths) hdr2.Add(label);
                hdr2.Add(label + " - Total");
            }
            hdr2.Add("TOTAL GERAL");
            sheet2Rows.Add(ExXmlRow(ns, hdr2.ToArray()));

            // Cabeçalho linha 2: meses
            var hdr2b = new List<string> { "" };
            foreach (var pi in visibleProj2)
            {
                foreach (var mi in visibleMonths) hdr2b.Add(months[mi].ToString("MMM/yyyy"));
                hdr2b.Add("Total");
            }
            hdr2b.Add("");
            sheet2Rows.Add(ExXmlRow(ns, hdr2b.ToArray()));

            var grandByProjMonth2 = visibleProj2.ToDictionary(pi => pi, _ => new double[months.Count]);

            foreach (var resName in resList)
            {
                var totalByMonth = new double[months.Count];
                foreach (var pi in visibleProj2)
                    for (int mi = 0; mi < months.Count; mi++)
                        totalByMonth[mi] += dist2[pi][resName][mi];

                double resTotal = totalByMonth.Sum();
                var cells = new List<object> { resName };

                foreach (var pi in visibleProj2)
                {
                    double projResTotal = 0;
                    foreach (var mi in visibleMonths)
                    {
                        double h   = dist2[pi][resName][mi];
                        double pct = totalByMonth[mi] > 0.01 ? h / totalByMonth[mi] * 100 : 0;
                        projResTotal += h;
                        grandByProjMonth2[pi][mi] += h;
                        cells.Add($"{h:0.#}h / {pct:0.#}%");
                    }
                    double projPct = resTotal > 0.01 ? projResTotal / resTotal * 100 : 0;
                    cells.Add($"{projResTotal:0.#}h / {projPct:0.#}%");
                }
                cells.Add(Math.Round(resTotal, 1));
                sheet2Rows.Add(ExXmlRowMixed(ns, cells.ToArray()));
            }

            // Total geral aba 2
            var totalCells2 = new List<object> { "TOTAL GERAL" };
            double grand2 = 0;
            foreach (var pi in visibleProj2)
            {
                foreach (var mi in visibleMonths)
                {
                    totalCells2.Add(Math.Round(grandByProjMonth2[pi][mi], 1));
                }
                double ps = visibleMonths.Sum(mi => grandByProjMonth2[pi][mi]);
                grand2 += ps;
                totalCells2.Add(Math.Round(ps, 1));
            }
            totalCells2.Add(Math.Round(grand2, 1));
            sheet2Rows.Add(ExXmlRowMixed(ns, totalCells2.ToArray()));

            // ── Gera o arquivo ──
            var workbook = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement(ns + "Workbook",
                    new XAttribute(XNamespace.Xmlns + "ss", ss),
                    new XElement(ns + "Worksheet",
                        new XAttribute(ss + "Name", "Horas por Projeto"),
                        new XElement(ns + "Table", sheet1Rows)),
                    new XElement(ns + "Worksheet",
                        new XAttribute(ss + "Name", "Distribuição por Pessoa"),
                        new XElement(ns + "Table", sheet2Rows))));

            workbook.Save(filePath);
        }

        private static XElement ExXmlRow(XNamespace ns, params string[] values)
        {
            return new XElement(ns + "Row",
                values.Select(v => new XElement(ns + "Cell",
                    new XElement(ns + "Data",
                        new XAttribute(ns + "Type", "String"), v))));
        }

        private static XElement ExXmlRowMixed(XNamespace ns, params object[] values)
        {
            return new XElement(ns + "Row",
                values.Select(v =>
                {
                    bool isNum = v is double or float or int;
                    return new XElement(ns + "Cell",
                        new XElement(ns + "Data",
                            new XAttribute(ns + "Type", isNum ? "Number" : "String"),
                            isNum ? ((double)Convert.ToDouble(v)).ToString(System.Globalization.CultureInfo.InvariantCulture) : v.ToString()));
                }));
        }

        private void OnSaveConfigClick(object sender, RoutedEventArgs e)
        {
            var opts = TfsConnectionStore.Load();
            foreach (var proj in _projects)
            {
                var cfg = opts.PortfolioProjectConfigs
                    .FirstOrDefault(c => string.Equals(c.ProjectName, proj.Name,
                                         StringComparison.OrdinalIgnoreCase));
                if (cfg == null)
                {
                    cfg = new PortfolioProjectConfig { ProjectName = proj.Name };
                    opts.PortfolioProjectConfigs.Add(cfg);
                }
                cfg.IsOpex     = proj.IsOpex;
                cfg.CostCenter = proj.CostCenter;
            }
            TfsConnectionStore.Save(opts, !string.IsNullOrEmpty(opts.PersonalAccessToken));
            StatusText.Text = "Configuração salva com sucesso.";
        }
    }
}
