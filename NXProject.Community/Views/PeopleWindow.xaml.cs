using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NXProject.Models;
using NXProject.Services;
using NXProject.ViewModels;

namespace NXProject.Views
{
    public partial class PeopleWindow : Window
    {
        private readonly MainViewModel _vm;
        private List<PersonRow> _rows = new();

        public PeopleWindow(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            Loaded += (_, _) =>
            {
                try { Refresh(); }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erro ao carregar pessoas:\n{ex.Message}", "Pessoas",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
        }

        // ── data binding ────────────────────────────────────────────────────────

        private void Refresh()
        {
            var assignments = ProjectTasks()
                .SelectMany(t => t.Resources ?? new List<TaskResource>())
                .ToList();

            _rows = (_vm.Project?.Resources ?? Enumerable.Empty<Resource>())
                .Where(r => r != null)
                .OrderBy(r => r.Name ?? string.Empty)
                .Select(r => new PersonRow(r, CountTasks(r, assignments)))
                .ToList();

            PeopleGrid.ItemsSource = null;
            PeopleGrid.ItemsSource = _rows;
            CountLabel.Text = $"{_rows.Count} pessoa(s)";
            StatusText.Text = string.Empty;
        }

        private static int CountTasks(Resource r, List<TaskResource> assignments) =>
            assignments.Count(a => a.ResourceId == r.Id);

        // ── toolbar ─────────────────────────────────────────────────────────────

        private void OnAddClick(object sender, RoutedEventArgs e)
        {
            var dlg = new AddPersonDialog { Owner = this };
            if (dlg.ShowDialog() != true)
                return;

            var name = dlg.PersonName.Trim();
            if (string.IsNullOrEmpty(name))
                return;

            var maxId = _vm.Project.Resources.Count > 0
                ? _vm.Project.Resources.Max(r => r.Id)
                : 0;

            var res = new Resource
            {
                Id = maxId + 1,
                Name = name,
                Email = dlg.PersonEmail.Trim(),
                AvailabilityPercent = 100.0,
                MaxUnitsPerDay = 8.0,
                IsImportedFromTfs = false
            };

            _vm.Project.Resources.Add(res);
            Refresh();
            MarkDirty("Pessoa incluída.");
        }

        private void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (PeopleGrid.SelectedItem is not PersonRow row)
            {
                MessageBox.Show("Selecione uma pessoa para excluir.", "Pessoas",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (row.TaskCount > 0)
            {
                var r = MessageBox.Show(
                    $"\"{row.Name}\" está alocada em {row.TaskCount} atividade(s).\nDeseja excluir mesmo assim?",
                    "Confirmar exclusão",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes)
                    return;

                // Remove assignments
                foreach (var t in ProjectTasks())
                    t.Resources.RemoveAll(a => a.ResourceId == row.Resource.Id);
            }
            else
            {
                var r = MessageBox.Show(
                    $"Excluir \"{row.Name}\"?", "Confirmar exclusão",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes)
                    return;
            }

            _vm.Project.Resources.Remove(row.Resource);
            Refresh();
            MarkDirty("Pessoa excluída.");
        }

        private void OnRecalcClick(object sender, RoutedEventArgs e)
        {
            CommitPendingEdits();
            var count = 0;
            foreach (var t in ProjectTasks())
            {
                if (t.IsSummary || t.IsMilestone)
                    continue;
                TaskScheduleService.RecalculateFinishFromAssignments(t);
                count++;
            }
            MarkDirty($"Cronograma recalculado ({count} atividades).");
        }

        private void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit)
                return;

            Dispatcher.InvokeAsync(() =>
            {
                if (e.Row.Item is PersonRow row)
                    row.PushToModel();

                MarkDirty("Alteração salva.");
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            CommitPendingEdits();
            Close();
        }

        // ── helpers ─────────────────────────────────────────────────────────────

        private void CommitPendingEdits()
        {
            PeopleGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
            foreach (var row in _rows)
                row.PushToModel();
        }

        private void MarkDirty(string msg)
        {
            if (_vm.Project != null)
                _vm.Project.IsDirty = true;
            _vm.StatusMessage = msg;
            StatusText.Text = msg;
        }

        private IEnumerable<ProjectTask> ProjectTasks()
        {
            if (_vm.FlatTasks.Count > 0)
                return _vm.FlatTasks.Select(t => t.Model);

            return Flatten(_vm.Project?.Tasks ?? Enumerable.Empty<ProjectTask>());
        }

        private static IEnumerable<ProjectTask> Flatten(IEnumerable<ProjectTask> tasks)
        {
            foreach (var task in tasks)
            {
                yield return task;

                foreach (var child in Flatten(task.Children ?? new System.Collections.ObjectModel.ObservableCollection<ProjectTask>()))
                    yield return child;
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        // Row wrapper
        // ════════════════════════════════════════════════════════════════════════

        public sealed class PersonRow : INotifyPropertyChanged
        {
            public Resource Resource { get; }

            private double _availPct;
            private string _name;
            private string _email;
            private double _maxUnitsPerDay;
            private string _typeLabel;
            private string _kindLabel;

            public static readonly string[] TypeOptions =
                { "Work", "Material", "Cost" };

            public static readonly string[] KindOptions =
                { "Project", "Internal" };

            private static readonly SolidColorBrush BrushGreen  = MakeFrozen(0x2E, 0x7D, 0x32);
            private static readonly SolidColorBrush BrushOrange = MakeFrozen(0xF5, 0x7C, 0x00);
            private static readonly SolidColorBrush BrushRed    = MakeFrozen(0xC6, 0x28, 0x28);

            private static SolidColorBrush MakeFrozen(byte r, byte g, byte b)
            {
                var b2 = new SolidColorBrush(Color.FromRgb(r, g, b));
                b2.Freeze();
                return b2;
            }

            public PersonRow(Resource r, int taskCount)
            {
                Resource = r;
                TaskCount = taskCount;
                _name = r.Name ?? string.Empty;
                _email = r.Email ?? string.Empty;
                _availPct = r.AvailabilityPercent;
                _maxUnitsPerDay = r.MaxUnitsPerDay;
                _typeLabel = r.Type.ToString();
                _kindLabel = r.Kind.ToString();
            }

            // ── editable fields ─────────────────────────────────────────────

            public string Name
            {
                get => _name;
                set { _name = value; OnPropertyChanged(); }
            }

            public string Email
            {
                get => _email;
                set { _email = value; OnPropertyChanged(); }
            }

            public double AvailabilityPercent
            {
                get => _availPct;
                set
                {
                    _availPct = Math.Clamp(value, 0, 100);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(AvailabilityText));
                    OnPropertyChanged(nameof(AvailabilityColor));
                    OnPropertyChanged(nameof(AvailabilityBarWidth));
                }
            }

            public double MaxUnitsPerDay
            {
                get => _maxUnitsPerDay;
                set { _maxUnitsPerDay = Math.Max(0.5, value); OnPropertyChanged(); }
            }

            public string TypeLabel
            {
                get => _typeLabel;
                set { _typeLabel = value; OnPropertyChanged(); }
            }

            public string KindLabel
            {
                get => _kindLabel;
                set { _kindLabel = value; OnPropertyChanged(); OnPropertyChanged(nameof(KindColor)); }
            }

            // ── read-only display ────────────────────────────────────────────

            public int TaskCount { get; }

            public bool IsImportedFromTfs => Resource.IsImportedFromTfs;

            public string AvailabilityText => $"{_availPct:0}%";

            public double AvailabilityBarWidth
            {
                get
                {
                    // max bar pixel width ≈ 64 (column width 140 minus text 52 minus padding)
                    return Math.Max(2, _availPct / 100.0 * 64);
                }
            }

            public Brush AvailabilityColor => _availPct switch
            {
                >= 80 => BrushGreen,
                >= 50 => BrushOrange,
                _     => BrushRed
            };

            public IEnumerable<string> TypeOptionsSource => TypeOptions;
            public IEnumerable<string> KindOptionsSource => KindOptions;

            public Brush KindColor => _kindLabel == "Internal"
                ? new SolidColorBrush(Color.FromRgb(91, 50, 112))
                : new SolidColorBrush(Color.FromRgb(43, 87, 154));

            // ── sync back to model ───────────────────────────────────────────

            public void PushToModel()
            {
                Resource.Name = _name.Trim();
                Resource.Email = string.IsNullOrWhiteSpace(_email) ? null : _email.Trim();
                Resource.AvailabilityPercent = _availPct;
                Resource.MaxUnitsPerDay = _maxUnitsPerDay;
                Resource.Type = Enum.TryParse<ResourceType>(_typeLabel, out var rt)
                    ? rt
                    : ResourceType.Work;
                Resource.Kind = Enum.TryParse<ResourceKind>(_kindLabel, out var rk)
                    ? rk
                    : ResourceKind.Project;
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    // Add person dialog
    // ════════════════════════════════════════════════════════════════════════════

    internal class AddPersonDialog : Window
    {
        private readonly TextBox _nameBox = new() { Margin = new Thickness(0, 0, 0, 8), Height = 28, Padding = new Thickness(6, 4, 6, 4) };
        private readonly TextBox _emailBox = new() { Height = 28, Padding = new Thickness(6, 4, 6, 4) };

        public string PersonName => _nameBox.Text;
        public string PersonEmail => _emailBox.Text;

        public AddPersonDialog()
        {
            Title = "Incluir Pessoa";
            Width = 340;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var ok = new Button { Content = "OK", IsDefault = true, Width = 80, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancelar", IsCancel = true, Width = 80, Height = 28 };
            ok.Click += (_, _) => { if (_nameBox.Text.Trim().Length > 0) DialogResult = true; };
            cancel.Click += (_, _) => DialogResult = false;

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = "Nome *", FontSize = 12, Margin = new Thickness(0, 0, 0, 3) });
            panel.Children.Add(_nameBox);
            panel.Children.Add(new TextBlock { Text = "E-mail", FontSize = 12, Margin = new Thickness(0, 0, 0, 3) });
            panel.Children.Add(_emailBox);
            panel.Children.Add(buttons);

            Content = panel;
            Loaded += (_, _) => _nameBox.Focus();
        }
    }
}
