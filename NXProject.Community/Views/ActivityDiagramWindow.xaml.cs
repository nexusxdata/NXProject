using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using NXProject.Models;

namespace NXProject.Views
{
    public partial class ActivityDiagramWindow : Window
    {
        // ── Layout constants ────────────────────────────────────────────────
        private const double NodeW      = 160;
        private const double NodeH      = 44;
        private const double HGap       = 40;   // horizontal gap between columns
        private const double VGap       = 16;   // vertical gap between nodes
        private const double ColPadding = 20;   // padding inside a column group

        // ── Model ───────────────────────────────────────────────────────────
        private readonly List<ProjectTask> _roots;

        // DiagramNode wraps a ProjectTask for diagram state
        private sealed class DiagramNode : INotifyPropertyChanged
        {
            public ProjectTask Task     { get; }
            public int         Level    { get; }     // hierarchy depth (0=root)
            public List<DiagramNode> Children { get; } = new();
            private bool _expanded = false;

            public DiagramNode(ProjectTask task, int level)
            {
                Task  = task;
                Level = level;
                foreach (var c in task.Children)
                    Children.Add(new DiagramNode(c, level + 1));
            }

            public bool IsExpanded
            {
                get => _expanded;
                set { _expanded = value; OnPropertyChanged(); }
            }

            public bool HasChildren => Children.Count > 0;

            // Rendered position (set during layout)
            public double X { get; set; }
            public double Y { get; set; }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? n = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        // Level checkbox binding
        public sealed class LevelItem : INotifyPropertyChanged
        {
            public string Label     { get; init; } = "";
            public int    Depth     { get; init; }
            private bool  _expanded = false;
            public bool IsExpanded
            {
                get => _expanded;
                set { _expanded = value; OnPropertyChanged(); }
            }
            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? n = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        private List<DiagramNode> _diagramRoots = new();
        private List<LevelItem>   _levelItems   = new();
        private double            _zoom         = 1.0;
        private Point             _panStart;
        private bool              _panning;

        // Colors per level
        private static readonly Color[] LevelColors =
        {
            Color.FromRgb(43,  87,  154),   // 0 – Épico/Projeto  (azul escuro)
            Color.FromRgb(0,  120,  215),   // 1 – Feature        (azul)
            Color.FromRgb(16, 137,  62),    // 2 – Story          (verde)
            Color.FromRgb(136,  0, 188),    // 3 – Task           (roxo)
            Color.FromRgb(209, 52,   56),   // 4+                 (vermelho)
        };

        private readonly ScaleTransform _scale = new(1.0, 1.0);

        public ActivityDiagramWindow(IEnumerable<ProjectTask> roots)
        {
            InitializeComponent();
            _roots = roots.ToList();
            DiagramViewbox.RenderTransform = _scale;
            DiagramViewbox.RenderTransformOrigin = new Point(0, 0);
            DiagramScroll.PreviewMouseWheel += OnScrollWheel;
            Loaded += (_, _) => Build();
        }

        // ── Build ────────────────────────────────────────────────────────────

        private void Build()
        {
            _diagramRoots = _roots.Select(r => new DiagramNode(r, 0)).ToList();

            // Detect max depth for level checkboxes
            int maxDepth = MaxDepth(_diagramRoots);
            _levelItems  = BuildLevelItems(maxDepth);
            LevelCheckList.ItemsSource = _levelItems;

            // Default: expand first two levels
            foreach (var li in _levelItems.Take(2))
                li.IsExpanded = true;

            ApplyLevelExpansion();
            Render();
        }

        private static int MaxDepth(IEnumerable<DiagramNode> nodes, int cur = 0)
        {
            int max = cur;
            foreach (var n in nodes)
                max = Math.Max(max, MaxDepth(n.Children, cur + 1));
            return max;
        }

        private static List<LevelItem> BuildLevelItems(int maxDepth)
        {
            var labels = new[] { "Épico", "Feature", "Story", "Task" };
            var items  = new List<LevelItem>();
            for (int d = 0; d <= maxDepth; d++)
            {
                items.Add(new LevelItem
                {
                    Depth = d,
                    Label = d < labels.Length ? labels[d] : $"Nível {d}"
                });
            }
            return items;
        }

        // ── Level checkbox handler ───────────────────────────────────────────

        private void OnLevelCheckChanged(object sender, RoutedEventArgs e)
        {
            ApplyLevelExpansion();
            Render();
        }

        private void ApplyLevelExpansion()
        {
            // Find the deepest checked level
            int deepest = _levelItems
                .Where(li => li.IsExpanded)
                .Select(li => li.Depth)
                .DefaultIfEmpty(-1)
                .Max();

            SetExpansion(_diagramRoots, 0, deepest);
        }

        private static void SetExpansion(List<DiagramNode> nodes, int depth, int deepest)
        {
            foreach (var n in nodes)
            {
                n.IsExpanded = depth < deepest && n.HasChildren;
                SetExpansion(n.Children, depth + 1, deepest);
            }
        }

        // ── Render ───────────────────────────────────────────────────────────

        private void Render()
        {
            DiagramCanvas.Children.Clear();

            // Layout: columns = hierarchy levels, rows = nodes within parent
            double canvasW = 0, canvasH = 0;
            var allNodes   = new List<DiagramNode>();
            LayoutColumn(_diagramRoots, 0, ColPadding, ColPadding, ref canvasW, ref canvasH, allNodes);

            DiagramCanvas.Width  = canvasW + ColPadding;
            DiagramCanvas.Height = canvasH + ColPadding;

            // Draw dependency arrows (same level only)
            DrawArrows(allNodes);

            // Draw nodes on top
            foreach (var n in allNodes)
                DrawNode(n);
        }

        // Returns the bottom Y reached by this column group
        private double LayoutColumn(
            List<DiagramNode> nodes, int depth,
            double x, double startY,
            ref double maxW, ref double maxH,
            List<DiagramNode> allNodes)
        {
            double y = startY;

            foreach (var node in nodes)
            {
                node.X = x;
                node.Y = y;
                allNodes.Add(node);

                if (node.IsExpanded && node.Children.Count > 0)
                {
                    // Children go to the right in a new column
                    double childX    = x + NodeW + HGap;
                    double childStartY = y;
                    double childEndY   = LayoutColumn(
                        node.Children, depth + 1,
                        childX, childStartY,
                        ref maxW, ref maxH, allNodes);

                    // Center the parent vertically relative to its children
                    double groupH = childEndY - childStartY;
                    node.Y = childStartY + (groupH - NodeH) / 2.0;

                    y = childEndY + VGap;
                }
                else
                {
                    y += NodeH + VGap;
                }

                maxW = Math.Max(maxW, node.X + NodeW);
                maxH = Math.Max(maxH, node.Y + NodeH);
            }

            return y;
        }

        // ── Draw node ────────────────────────────────────────────────────────

        private void DrawNode(DiagramNode node)
        {
            bool isTfs      = node.Task.HasTfsLink;
            var levelColor  = isTfs
                ? LevelColors[Math.Min(node.Level, LevelColors.Length - 1)]
                : Color.FromRgb(130, 100, 60);   // Interno: marrom/laranja escuro

            var bgTop = isTfs
                ? Color.FromRgb(
                    (byte)Math.Min(255, levelColor.R + 40),
                    (byte)Math.Min(255, levelColor.G + 40),
                    (byte)Math.Min(255, levelColor.B + 40))
                : Color.FromRgb(180, 140, 80);

            var bg = new LinearGradientBrush(bgTop, levelColor, 90);

            // Shadow
            var shadow = new Rectangle
            {
                Width = NodeW, Height = NodeH,
                RadiusX = 6, RadiusY = 6,
                Fill = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0))
            };
            Canvas.SetLeft(shadow, node.X + 3);
            Canvas.SetTop(shadow,  node.Y + 3);
            DiagramCanvas.Children.Add(shadow);

            // Tooltip rico
            var tt = BuildNodeTooltip(node);

            // Main box
            var rect = new Rectangle
            {
                Width = NodeW, Height = NodeH,
                RadiusX = 6, RadiusY = 6,
                Fill    = bg,
                Stroke  = new SolidColorBrush(levelColor),
                StrokeThickness = 1.5,
                Cursor  = node.HasChildren ? Cursors.Hand : Cursors.Arrow,
                ToolTip = tt
            };
            Canvas.SetLeft(rect, node.X);
            Canvas.SetTop(rect,  node.Y);
            rect.MouseLeftButtonDown += (_, _) => OnNodeClick(node);
            DiagramCanvas.Children.Add(rect);

            // Expand indicator
            if (node.HasChildren)
            {
                var indicator = new TextBlock
                {
                    Text       = node.IsExpanded ? "▼" : "▶",
                    FontSize   = 8,
                    Foreground = new SolidColorBrush(Colors.White),
                    Opacity    = 0.8
                };
                Canvas.SetLeft(indicator, node.X + NodeW - 14);
                Canvas.SetTop(indicator,  node.Y + 4);
                indicator.MouseLeftButtonDown += (_, _) => OnNodeClick(node);
                DiagramCanvas.Children.Add(indicator);
            }

            // Badge T: / I:
            var idKey   = node.Task.HasTfsLink ? $"T:{node.Task.TfsId}" : $"I:{node.Task.Id}";
            var idBadge = new TextBlock
            {
                Text       = idKey,
                FontSize   = 8,
                Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                FontWeight = FontWeights.Bold
            };
            Canvas.SetLeft(idBadge, node.X + NodeW - 8 - idKey.Length * 5);
            Canvas.SetTop(idBadge,  node.Y + NodeH - 14);
            idBadge.MouseLeftButtonDown += (_, _) => OnNodeClick(node);
            DiagramCanvas.Children.Add(idBadge);

            // Label
            var label = new TextBlock
            {
                Text          = TruncateText(node.Task.Name, 22),
                FontSize      = 11,
                FontWeight    = node.Level == 0 ? FontWeights.Bold : FontWeights.Normal,
                Foreground    = Brushes.White,
                TextWrapping  = TextWrapping.NoWrap,
                Width         = NodeW - 16,
                ToolTip       = node.Task.Name
            };
            Canvas.SetLeft(label, node.X + 8);
            Canvas.SetTop(label,  node.Y + 6);
            label.MouseLeftButtonDown += (_, _) => OnNodeClick(node);
            DiagramCanvas.Children.Add(label);

            // % complete sub-label
            if (node.Task.PercentComplete > 0)
            {
                var pct = new TextBlock
                {
                    Text       = $"{node.Task.PercentComplete:0}%",
                    FontSize   = 9,
                    Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255))
                };
                Canvas.SetLeft(pct, node.X + 8);
                Canvas.SetTop(pct,  node.Y + NodeH - 16);
                DiagramCanvas.Children.Add(pct);

                // Progress bar inside node
                var pBar = new Rectangle
                {
                    Width  = Math.Max(2, (NodeW - 16) * node.Task.PercentComplete / 100.0),
                    Height = 3,
                    Fill   = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                    RadiusX = 1, RadiusY = 1
                };
                Canvas.SetLeft(pBar, node.X + 8);
                Canvas.SetTop(pBar,  node.Y + NodeH - 7);
                DiagramCanvas.Children.Add(pBar);
            }
        }

        // ── Node click ───────────────────────────────────────────────────────

        private void OnNodeClick(DiagramNode node)
        {
            if (!node.HasChildren) return;
            node.IsExpanded = !node.IsExpanded;

            // Sync level checkboxes
            SyncLevelCheckboxes();
            Render();
        }

        private void SyncLevelCheckboxes()
        {
            // For each level, check if ALL nodes at that level are expanded
            foreach (var li in _levelItems)
            {
                var nodesAtLevel = GetNodesAtDepth(_diagramRoots, li.Depth);
                if (nodesAtLevel.Count == 0) continue;
                // Temporarily unsubscribe to avoid re-render loop
                li.IsExpanded = nodesAtLevel.All(n => n.IsExpanded);
            }
        }

        private static List<DiagramNode> GetNodesAtDepth(List<DiagramNode> nodes, int targetDepth, int cur = 0)
        {
            var result = new List<DiagramNode>();
            foreach (var n in nodes)
            {
                if (cur == targetDepth)
                    result.Add(n);
                else
                    result.AddRange(GetNodesAtDepth(n.Children, targetDepth, cur + 1));
            }
            return result;
        }

        // ── Draw arrows ──────────────────────────────────────────────────────

        private void DrawArrows(List<DiagramNode> allNodes)
        {
            // Build id→node map (ignora IDs duplicados, ex: Id=0 em tasks recém-criadas)
            var idMap = new Dictionary<int, DiagramNode>();
            foreach (var n in allNodes)
                idMap.TryAdd(n.Task.Id, n);
            var arrowBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100));

            foreach (var node in allNodes)
            {
                foreach (var predId in node.Task.PredecessorIds)
                {
                    if (!idMap.TryGetValue(predId, out var pred)) continue;
                    // Only same level
                    if (pred.Level != node.Level) continue;

                    DrawArrow(pred, node, arrowBrush);
                }
            }
        }

        private void DrawArrow(DiagramNode from, DiagramNode to, Brush brush)
        {
            // from right-center → to left-center (same column → vertical; different column → horizontal)
            double x1, y1, x2, y2;

            bool sameColumn = Math.Abs(from.X - to.X) < 1;
            if (sameColumn)
            {
                // Vertical arrow: bottom-center → top-center
                x1 = from.X + NodeW / 2; y1 = from.Y + NodeH;
                x2 = to.X   + NodeW / 2; y2 = to.Y;
            }
            else
            {
                // Horizontal arrow: right-center → left-center
                x1 = from.X + NodeW; y1 = from.Y + NodeH / 2;
                x2 = to.X;           y2 = to.Y   + NodeH / 2;
            }

            var line = new Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                Stroke          = brush,
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 4, 2 }
            };
            DiagramCanvas.Children.Add(line);

            // Arrowhead
            DrawArrowhead(x2, y2, sameColumn ? 90 : 0, brush);
        }

        private void DrawArrowhead(double tipX, double tipY, double angleDeg, Brush brush)
        {
            double rad = angleDeg * Math.PI / 180;
            double size = 7;
            double cos  = Math.Cos(rad);
            double sin  = Math.Sin(rad);

            var p1 = new Point(tipX - size * cos + size * 0.4 * sin,
                               tipY - size * sin - size * 0.4 * cos);
            var p2 = new Point(tipX - size * cos - size * 0.4 * sin,
                               tipY - size * sin + size * 0.4 * cos);

            var poly = new Polygon
            {
                Points = new PointCollection { new(tipX, tipY), p1, p2 },
                Fill   = brush
            };
            DiagramCanvas.Children.Add(poly);
        }

        // ── Node Tooltip ─────────────────────────────────────────────────────

        private static ToolTip BuildNodeTooltip(DiagramNode node)
        {
            var t = node.Task;
            var idKey  = t.HasTfsLink ? $"T:{t.TfsId}" : $"I:{t.Id}";
            var estado = t.TfsState ?? "—";
            var inicio = t.Start.ToString("dd/MM/yy");
            var fim    = t.Finish.ToString("dd/MM/yy");
            var hh     = t.EstimatedHours.HasValue ? $"{t.EstimatedHours:0} HH" : "—";
            var pc     = $"{t.PercentComplete:0}%";
            var recurso = t.Resources.Count > 0
                ? string.Join(", ", t.Resources.Select(r => r.Resource?.Name ?? r.ResourceId.ToString()))
                : "—";

            var panel = new StackPanel { Margin = new Thickness(6, 4, 6, 4), MaxWidth = 340 };

            void AddRow(string label, string value)
            {
                var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                row.Children.Add(new TextBlock
                {
                    Text = label + ": ", FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White, FontSize = 11, Width = 72
                });
                row.Children.Add(new TextBlock
                {
                    Text = value, Foreground = Brushes.LightCyan, FontSize = 11,
                    TextWrapping = TextWrapping.Wrap, MaxWidth = 250
                });
                panel.Children.Add(row);
            }

            panel.Children.Add(new TextBlock
            {
                Text = t.Name, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White, FontSize = 12,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4)
            });

            AddRow("ID",       idKey);
            AddRow("Tipo",     t.TfsType ?? "—");
            AddRow("Estado",   estado);
            AddRow("Início",   inicio);
            AddRow("Fim",      fim);
            AddRow("HH Est.",  hh);
            AddRow("Concluído", pc);
            AddRow("Recurso",  recurso);

            if (!string.IsNullOrWhiteSpace(t.TfsIterationPath))
                AddRow("Sprint", t.TfsIterationPath.Split('\\').Last());

            return new ToolTip
            {
                Content    = panel,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 50)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 120)),
                BorderThickness = new Thickness(1),
                HasDropShadow = true
            };
        }

        // ── Zoom / Reset ─────────────────────────────────────────────────────

        private void OnScrollWheel(object sender, MouseWheelEventArgs e)
        {
            if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl)) return;
            e.Handled = true;
            _zoom = Math.Clamp(_zoom + (e.Delta > 0 ? 0.1 : -0.1), 0.3, 3.0);
            _scale.ScaleX = _zoom;
            _scale.ScaleY = _zoom;
            ZoomLabel.Text = $"{_zoom:0%}";
        }

        private void OnResetZoom(object sender, RoutedEventArgs e)
        {
            _zoom = 1.0;
            _scale.ScaleX = 1.0;
            _scale.ScaleY = 1.0;
            ZoomLabel.Text = "100%";
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static string TruncateText(string text, int max) =>
            text.Length <= max ? text : text[..max] + "…";
    }
}
