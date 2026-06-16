using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
                        resRow.Children.Add(MakeHoursCell(h, ColWidth, RowHeight, resBg));
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

            // Coleta todos os recursos únicos com horas no período
            var allResources = _projects
                .SelectMany(p => p.Data.Resources
                    .Where(r => r.Type == ResourceType.Work && !string.IsNullOrWhiteSpace(r.Name))
                    .Select(r => r.Name!))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();

            // Para cada projeto, total de horas por recurso no período inteiro
            // dist[projIdx][resName] = totalHours
            var dist = new List<Dictionary<string, double>>();
            foreach (var proj in _projects)
            {
                var d = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var res in allResources)
                {
                    var mStart = new DateTime(periodStart.Year, periodStart.Month, 1);
                    var mEnd   = new DateTime(periodEnd.Year, periodEnd.Month,
                                     DateTime.DaysInMonth(periodEnd.Year, periodEnd.Month));
                    d[res] = ComputeHours(proj.Data, res, mStart, mEnd);
                }
                dist.Add(d);
            }

            // Filtra recursos sem horas em nenhum projeto
            if (hideZero)
                allResources = allResources
                    .Where(r => dist.Any(d => d[r] > 0.01))
                    .ToList();

            // Filtra projetos sem horas (colunas zeradas)
            var visibleProjIdx = Enumerable.Range(0, _projects.Count)
                .Where(pi => !hideZero || allResources.Any(r => dist[pi][r] > 0.01))
                .ToList();

            const double ProjColW  = 140;
            const double TotalColW2 = 80;

            // ── Cabeçalho linha 1: projetos ──
            foreach (var pi in visibleProjIdx)
            {
                var proj = _projects[pi];
                DistProjHeaderPanel.Items.Add(new Border
                {
                    Width           = ProjColW,
                    Height          = 22,
                    Background      = new SolidColorBrush(Color.FromRgb(43, 87, 154)),
                    BorderBrush     = new SolidColorBrush(Color.FromRgb(29, 63, 115)),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    ToolTip         = proj.Name,
                    Child           = new TextBlock
                    {
                        Text                = proj.Name,
                        Foreground          = Brushes.White,
                        FontWeight          = FontWeights.SemiBold,
                        FontSize            = 11,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment   = VerticalAlignment.Center,
                        TextTrimming        = TextTrimming.CharacterEllipsis,
                        Margin              = new Thickness(2, 0, 2, 0)
                    }
                });
            }
            DistProjHeaderPanel.Items.Add(new Border
            {
                Width           = TotalColW2,
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

            // ── Cabeçalho linha 2: tipo OPEX/CAPEX de cada projeto ──
            foreach (var pi in visibleProjIdx)
            {
                var proj = _projects[pi];
                var typeColor = proj.IsOpex
                    ? Color.FromRgb(200, 235, 200)
                    : Color.FromRgb(255, 230, 200);
                var typeFg = proj.IsOpex
                    ? Color.FromRgb(0, 100, 0)
                    : Color.FromRgb(140, 60, 0);
                DistTypeHeaderPanel.Items.Add(new Border
                {
                    Width           = ProjColW,
                    Height          = 20,
                    Background      = new SolidColorBrush(typeColor),
                    BorderBrush     = new SolidColorBrush(Color.FromRgb(197, 208, 224)),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Child           = new TextBlock
                    {
                        Text                = proj.IsOpex ? "OPEX" : "CAPEX",
                        FontSize            = 10,
                        FontWeight          = FontWeights.SemiBold,
                        Foreground          = new SolidColorBrush(typeFg),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment   = VerticalAlignment.Center
                    }
                });
            }
            DistTypeHeaderPanel.Items.Add(new Border
            {
                Width           = TotalColW2,
                Height          = 20,
                Background      = new SolidColorBrush(Color.FromRgb(220, 232, 252)),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(197, 208, 224)),
                BorderThickness = new Thickness(0, 0, 1, 1)
            });

            // ── Linhas por recurso ──
            for (int ri = 0; ri < allResources.Count; ri++)
            {
                var resName = allResources[ri];
                double resTotal = visibleProjIdx.Sum(pi => dist[pi][resName]);

                var resBg = new SolidColorBrush(ri % 2 == 0
                    ? Color.FromRgb(255, 255, 255)
                    : Color.FromRgb(248, 250, 255));

                // Coluna fixa: nome do recurso
                DistResPanel.Items.Add(new Border
                {
                    Width           = 200,
                    Height          = RowHeight,
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

                // Células de projeto
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var pi in visibleProjIdx)
                {
                    double h   = dist[pi][resName];
                    double pct = resTotal > 0.01 ? h / resTotal * 100 : 0;
                    string txt = h < 0.05 ? "–" : $"{h:0.#}h\n{pct:0.#}%";

                    var cellBg = h < 0.05 ? resBg
                        : new SolidColorBrush(Color.FromArgb(
                            (byte)Math.Min(255, 80 + (int)(pct * 1.6)),
                            210, 225, 250));

                    row.Children.Add(new Border
                    {
                        Width           = ProjColW,
                        Height          = RowHeight,
                        Background      = cellBg,
                        BorderBrush     = new SolidColorBrush(Color.FromRgb(220, 228, 240)),
                        BorderThickness = new Thickness(0, 0, 1, 1),
                        Child           = new TextBlock
                        {
                            Text                = txt,
                            FontSize            = 11,
                            Foreground          = h < 0.05
                                ? new SolidColorBrush(Color.FromRgb(200, 200, 200))
                                : new SolidColorBrush(Color.FromRgb(20, 50, 120)),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment   = VerticalAlignment.Center,
                            TextAlignment       = TextAlignment.Center
                        }
                    });
                }

                // Célula TOTAL
                row.Children.Add(MakeTotalCell(resTotal, TotalColW2, RowHeight));
                DistDataPanel.Items.Add(row);
            }

            // ── Linha TOTAL GERAL ──
            var totalBg2 = new SolidColorBrush(Color.FromRgb(43, 87, 154));
            DistResPanel.Items.Add(MakeCell("TOTAL GERAL", 200, RowHeight + 2, totalBg2,
                bold: true, fg: Colors.White, leftPad: 8));

            double grandTotal2 = 0;
            var grandRow2 = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var pi in visibleProjIdx)
            {
                double projSum = allResources.Sum(r => dist[pi][r]);
                grandTotal2 += projSum;
                grandRow2.Children.Add(MakeTotalCell(projSum, ProjColW, RowHeight + 2,
                    bold: true, fg: Colors.White, bg: Color.FromRgb(43, 87, 154)));
            }
            grandRow2.Children.Add(MakeTotalCell(grandTotal2, TotalColW2, RowHeight + 2,
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
        }

        // ── Handlers ──────────────────────────────────────────────────────────
        private void OnSelectProjectsClick(object sender, RoutedEventArgs e)
        {
            var opts       = TfsConnectionStore.Load();
            var devOpsList = DevOpsProjectListService.Load(opts.DevOpsProjectListPath);

            if (devOpsList.Count == 0)
            {
                MessageBox.Show(
                    "Nenhum projeto DevOps cadastrado.\n\n" +
                    "Acesse Visualizar → Projetos DevOps (lista) para configurar a lista de projetos.",
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
                    "Edite a lista de projetos DevOps (Visualizar → Projetos DevOps) e informe o ID raiz de cada projeto.",
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

        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            var opts = TfsConnectionStore.Load();
            LoadProjects(opts);
            BuildGrid();
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
