using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using NXProject.Models;

namespace NXProject.ViewModels
{
    public partial class TaskViewModel : ObservableObject
    {
        private readonly ProjectTask _task;
        private readonly double _lowDaysPerSfp;
        private readonly double _mediumDaysPerSfp;
        private readonly double _highDaysPerSfp;

        public TaskViewModel(
            ProjectTask task,
            int depth = 0,
            double lowDaysPerSfp = 1.0,
            double mediumDaysPerSfp = 1.0,
            double highDaysPerSfp = 1.0)
        {
            _task = task;
            Depth = depth;
            _lowDaysPerSfp = Math.Max(0, lowDaysPerSfp);
            _mediumDaysPerSfp = Math.Max(0, mediumDaysPerSfp);
            _highDaysPerSfp = Math.Max(0, highDaysPerSfp);

            if (UsesSfpEstimate)
                ApplySfpDuration();
        }

        public ProjectTask Model => _task;

        public int Depth { get; }

        // Indentacao visual na grade
        public double Indent => Depth * 16.0;

        [ObservableProperty] private bool _isExpanded = true;
        [ObservableProperty] private bool _isSelected;

        public int Id
        {
            get => _task.Id;
            set { _task.Id = value; OnPropertyChanged(); }
        }

        // ID exibido na grade: o do DevOps quando vinculado, senão o interno.
        public string DisplayId => _task.TfsId?.ToString() ?? _task.Id.ToString();

        public int? TfsId
        {
            get => _task.TfsId;
            set
            {
                _task.TfsId = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayId));
            }
        }

        public string? TfsType
        {
            get => _task.TfsType;
            set
            {
                _task.TfsType = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DevOpsTag));
                OnPropertyChanged(nameof(DevOpsTooltip));
            }
        }

        public string? TfsState
        {
            get => _task.TfsState;
            set
            {
                _task.TfsState = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PercentComplete));
                OnPropertyChanged(nameof(DevOpsTag));
                OnPropertyChanged(nameof(DevOpsTooltip));
                OnPropertyChanged(nameof(DevOpsBrush));
            }
        }

        // ── Selo compacto DevOps (cronograma) ────────────────────────────────
        // Ex.: "FEA·A" (Feature, Active). Vazio quando não vinculado.
        public string DevOpsTag
        {
            get
            {
                var t = TypeShort(_task.TfsType);
                var s = StateShort(_task.TfsState);
                if (string.IsNullOrEmpty(t) && string.IsNullOrEmpty(s)) return string.Empty;
                if (string.IsNullOrEmpty(s)) return t;
                if (string.IsNullOrEmpty(t)) return s;
                return $"{t}·{s}";
            }
        }

        public string DevOpsTooltip
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrWhiteSpace(_task.TfsType)) parts.Add(_task.TfsType!);
                if (!string.IsNullOrWhiteSpace(_task.TfsState)) parts.Add(_task.TfsState!);
                return string.Join(" · ", parts);
            }
        }

        // Cor pelo estado DevOps.
        public Brush DevOpsBrush => (_task.TfsState?.Trim().ToLowerInvariant()) switch
        {
            "active" => new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A)),   // azul
            "resolved" => new SolidColorBrush(Color.FromRgb(0x87, 0x64, 0xB8)), // roxo
            "closed" => new SolidColorBrush(Color.FromRgb(0x21, 0x73, 0x46)),   // verde
            "removed" => new SolidColorBrush(Color.FromRgb(0xA4, 0x26, 0x2C)),  // vermelho
            _ => new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70))           // cinza (New/none)
        };

        private static string TypeShort(string? type) => (type?.Trim().ToLowerInvariant()) switch
        {
            "project" => "PRJ",
            "epic" => "EPC",
            "feature" => "FEA",
            "user story" or "story" => "STO",
            null or "" => string.Empty,
            _ => type!.Trim().ToUpperInvariant()[..Math.Min(3, type!.Trim().Length)]
        };

        private static string StateShort(string? state) => (state?.Trim().ToLowerInvariant()) switch
        {
            "new" => "N",
            "active" => "A",
            "resolved" => "R",
            "closed" => "C",
            "removed" => "X",
            null or "" => string.Empty,
            _ => state!.Trim().ToUpperInvariant()[..1]
        };

        public string? Description
        {
            get => _task.Description;
            set { _task.Description = value; OnPropertyChanged(); }
        }

        public string? Tags
        {
            get => _task.Tags;
            set
            {
                _task.Tags = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsBlocked));
            }
        }

        // Conveniência: item bloqueado — tag "Block" própria OU rollup de Task filha
        // bloqueada (este último é só visão, não sincroniza).
        public bool IsBlocked =>
            _task.BlockedByChild ||
            (_task.Tags ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(t => string.Equals(t, "Block", StringComparison.OrdinalIgnoreCase));

        public string Name
        {
            get => _task.Name;
            set { _task.Name = value; OnPropertyChanged(); _task.Parent?.RecalcSummary(); }
        }

        public DateTime Start
        {
            get => _task.Start;
            set
            {
                var durationDays = DurationDays;
                _task.Start = value;
                _task.Finish = value.AddDays(durationDays);
                OnPropertyChanged();
                OnPropertyChanged(nameof(Finish));
                OnPropertyChanged(nameof(DurationDays));
                OnPropertyChanged(nameof(DisplayAsMilestone));
                RecalcAncestorSummaries();
            }
        }

        public DateTime Finish
        {
            get => _task.Finish;
            set
            {
                var minimumFinish = _task.Start.AddDays(DurationDays);
                if (value < minimumFinish)
                {
                    MessageBox.Show(
                        "Para reduzir a data de termino abaixo da duracao atual, altere primeiro a duracao da atividade.",
                        "Alterar duracao",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                _task.Finish = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DurationDays));
                OnPropertyChanged(nameof(DisplayAsMilestone));
                RecalcAncestorSummaries();
            }
        }

        public int DurationDays
        {
            get => (int)(_task.Finish - _task.Start).TotalDays;
            set
            {
                if (UsesSfpEstimate)
                    return;

                if (value >= 0)
                {
                    _task.Finish = _task.Start.AddDays(value);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Finish));
                    OnPropertyChanged(nameof(DisplayAsMilestone));
                    RecalcAncestorSummaries();
                }
            }
        }

        public double? SfpPoints
        {
            get => _task.SfpPoints;
            set
            {
                _task.SfpPoints = value.HasValue && value.Value > 0 ? value.Value : null;
                if (UsesSfpEstimate)
                    ApplySfpDuration();

                OnPropertyChanged();
                OnPropertyChanged(nameof(UsesSfpEstimate));
                OnPropertyChanged(nameof(DurationDays));
                OnPropertyChanged(nameof(Finish));
                OnPropertyChanged(nameof(DisplayAsMilestone));
            }
        }

        public bool UsesSfpEstimate => (SfpPoints ?? 0) > 0;

        public double PercentComplete
        {
            get => _task.PercentComplete;
            set { _task.PercentComplete = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
        }

        public bool IsMilestone
        {
            get => _task.IsMilestone;
            set
            {
                _task.IsMilestone = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayAsMilestone));
            }
        }

        public bool DisplayAsMilestone => IsMilestone || DurationDays == 0;

        public bool IsSummary
        {
            get => _task.IsSummary;
            set { _task.IsSummary = value; OnPropertyChanged(); }
        }

        public int SprintNumber
        {
            get => _task.SprintNumber;
            set { _task.SprintNumber = value; OnPropertyChanged(); }
        }

        public string PredecessorsText
        {
            get => string.Join(",", _task.PredecessorIds);
            set
            {
                _task.PredecessorIds.Clear();
                foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    if (int.TryParse(part.Trim(), out int id))
                        _task.PredecessorIds.Add(id);
                OnPropertyChanged();
            }
        }

        public string ResourcesText
        {
            get
            {
                var names = new System.Collections.Generic.List<string>();
                foreach (var r in _task.Resources)
                    names.Add(r.ToString());
                return string.Join(", ", names);
            }
        }

        public string? Notes
        {
            get => _task.Notes;
            set { _task.Notes = value; OnPropertyChanged(); }
        }

        public void ShiftSchedule(int dayDelta)
        {
            if (dayDelta == 0)
                return;

            _task.Start = _task.Start.AddDays(dayDelta);
            _task.Finish = _task.Finish.AddDays(dayDelta);
            OnPropertyChanged(nameof(Start));
            OnPropertyChanged(nameof(Finish));
            OnPropertyChanged(nameof(DurationDays));
            OnPropertyChanged(nameof(DisplayAsMilestone));
            RecalcAncestorSummaries();
        }

        private void RecalcAncestorSummaries()
        {
            var current = _task.Parent;
            while (current != null)
            {
                current.RecalcSummary();
                current = current.Parent;
            }
        }

        private void ApplySfpDuration()
        {
            if (!UsesSfpEstimate)
                return;

            var sfpPoints = SfpPoints ?? 0;
            var daysPerSfp = sfpPoints <= 3
                ? _lowDaysPerSfp
                : sfpPoints < 6
                    ? _mediumDaysPerSfp
                    : _highDaysPerSfp;
            var calculatedDuration = Math.Max(1, (int)Math.Ceiling(sfpPoints * daysPerSfp));
            _task.Finish = _task.Start.AddDays(calculatedDuration);
            RecalcAncestorSummaries();
        }
    }
}
