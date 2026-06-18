using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using NXProject.Models;
using NXProject.Services;

namespace NXProject.ViewModels
{
    public partial class TaskViewModel : ObservableObject
    {
        private readonly ProjectTask _task;
        private readonly double _lowDaysPerSfp;
        private readonly double _mediumDaysPerSfp;
        private readonly double _highDaysPerSfp;

        // Callback fornecido pelo MainViewModel para recalcular o início quando
        // o usuário limpa o fix (digita "0"). Retorna a data de início calculada.
        public Func<DateTime>? GetSprintStart { get; set; }

        // Lookups injetados pelo MainViewModel para resolução de IDs de predecessoras.
        // FindByInternalId : Id interno → TaskViewModel (para exibir o DisplayId correto).
        // FindByDisplayId  : DisplayId digitado pelo usuário → Id interno da tarefa.
        public Func<int, TaskViewModel?>? FindByInternalId { get; set; }
        public Func<string, int?>? FindByDisplayId { get; set; }

        // Callback acionado quando o Finish desta tarefa muda, para cascatear o
        // reagendamento das tarefas que a têm como predecessora.
        public Action<TaskViewModel>? ScheduleSuccessors { get; set; }

        // Todas as sprints disponíveis para este ViewModel, sem filtro de data.
        public ObservableCollection<Sprint> AvailableSprintsForTask { get; } = new();

        public void RefreshSprintOptions(IEnumerable<Sprint> allOptions)
        {
            AvailableSprintsForTask.Clear();
            foreach (var s in allOptions)
                AvailableSprintsForTask.Add(s);
        }

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
        [ObservableProperty] private bool _isHighlightedPredecessor;
        [ObservableProperty] private bool _isHighlightSource;
        [ObservableProperty] private Brush? _hierarchyBackground;

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
                OnPropertyChanged(nameof(SupportsSprint));
                OnPropertyChanged(nameof(SprintDisplay));
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

        public bool HasSyncConflict => _task.HasSyncConflict;

        public DateTime? CalculatedFinish => _task.CalculatedFinish;

        public string Name
        {
            get => _task.Name;
            set { _task.Name = value; OnPropertyChanged(); _task.Parent?.RecalcSummary(); }
        }

        public bool StartFixed
        {
            get => _task.StartFixed;
            set
            {
                _task.StartFixed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StartDisplay));
            }
        }

        // Exibição na célula: "📌 dd/MM/yy" quando fixado, "dd/MM/yy" normal.
        public string StartDisplay =>
            _task.StartFixed
                ? "📌 " + _task.Start.ToString("dd/MM/yy")
                : _task.Start.ToString("dd/MM/yy");

        [CommunityToolkit.Mvvm.Input.RelayCommand]
        private void ClearStartFixed() => StartText = "0";

        // Setter de texto aceita data válida (marca fix) ou "0" (limpa fix e recalcula).
        public string StartText
        {
            set
            {
                var raw = value?.Trim() ?? string.Empty;
                if (raw == "0")
                {
                    _task.StartFixed = false;
                    var newStart = GetSprintStart?.Invoke() ?? _task.Start;
                    var durationHours = DurationHours;
                    _task.Start = newStart;
                    _task.Finish = ProjectCalendarService.AddWorkingHours(newStart, durationHours);
                    OnPropertyChanged(nameof(Start));
                    OnPropertyChanged(nameof(Finish));
                    OnPropertyChanged(nameof(StartFixed));
                    OnPropertyChanged(nameof(StartDisplay));
                    OnPropertyChanged(nameof(DurationDays));
                    OnPropertyChanged(nameof(DurationHours));
                    RecalcAncestorSummaries();
                    ScheduleSuccessors?.Invoke(this);
                }
                else if (DateTime.TryParse(raw, out var parsed))
                {
                    Start = parsed; // setter abaixo marca StartFixed = true
                }
            }
        }

        public DateTime Start
        {
            get => _task.Start;
            set
            {
                var durationHours = DurationHours;
                _task.Start = value;
                if (!_task.FinishFixed)
                    _task.Finish = ProjectCalendarService.AddWorkingHours(value, durationHours);
                _task.StartFixed = true;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Finish));
                OnPropertyChanged(nameof(FinishDisplay));
                OnPropertyChanged(nameof(DurationDays));
                OnPropertyChanged(nameof(DurationHours));
                OnPropertyChanged(nameof(DisplayAsMilestone));
                OnPropertyChanged(nameof(StartFixed));
                OnPropertyChanged(nameof(StartDisplay));
                RecalcAncestorSummaries();
                ScheduleSuccessors?.Invoke(this);
            }
        }

        public DateTime Finish
        {
            get => _task.Finish;
            set
            {
                _task.Finish = value;
                if (CanEditPercentComplete && value.Date < DateTime.Today && _task.PercentComplete < 100)
                {
                    _task.PercentComplete = 100;
                    OnPropertyChanged(nameof(PercentComplete));
                    NotifyParentPercentChanged();
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(FinishDisplay));
                OnPropertyChanged(nameof(FinishFixed));
                OnPropertyChanged(nameof(DurationDays));
                OnPropertyChanged(nameof(DurationHours));
                OnPropertyChanged(nameof(DisplayAsMilestone));
                RecalcAncestorSummaries();
                ScheduleSuccessors?.Invoke(this);
            }
        }

        public double DurationHours
        {
            // Duração total = HH Atual + HH Restante quando HH Atual > 0; caso contrário, baseada em datas.
            get => _task.CurrentHours is > 0
                ? _task.CurrentHours.Value + (_task.EstimatedHours ?? 0)
                : ProjectCalendarService.CountWorkingHours(_task.Start, _task.Finish);
            set
            {
                if (!CanEditDuration) return;

                if (value >= 0)
                {
                    // Ao editar, o usuário define o total; HH Atual não muda — extrai HH Restante.
                    var remaining = _task.CurrentHours is > 0
                        ? Math.Max(0, value - _task.CurrentHours.Value)
                        : value;
                    _task.EstimatedHours = remaining;
                    var totalH = _task.CurrentHours is > 0 ? _task.CurrentHours.Value + remaining : remaining;
                    if (!_task.FinishFixed)
                        _task.Finish = ProjectCalendarService.AddWorkingHours(_task.Start, totalH);
                    if (_task.PercentComplete < 100)
                    {
                        _task.OriginalEstimatedHours = remaining;
                        RefreshOriginalEstimatedHoursProperties();
                    }
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(EstimatedHoursDisplay));
                    OnPropertyChanged(nameof(Finish));
                    OnPropertyChanged(nameof(FinishDisplay));
                    OnPropertyChanged(nameof(DurationDays));
                    OnPropertyChanged(nameof(DisplayAsMilestone));
                    RecalcAncestorSummaries();
                    RefreshAncestorCalculatedProperties();
                    ScheduleSuccessors?.Invoke(this);
                }
            }
        }

        // Setter de texto aceita horas ("32") ou dias com sufixo "d" ("5d" → 5 × horas/dia).
        // O getter não é usado pelo binding; o display fica no CellTemplate via DurationHours.
        public string DurationText
        {
            set
            {
                if (!CanEditDuration) return;
                var raw = value?.Trim() ?? string.Empty;
                double hours;
                if (raw.EndsWith("d", StringComparison.OrdinalIgnoreCase) &&
                    double.TryParse(raw[..^1].Trim(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.CurrentCulture, out var days))
                {
                    hours = days * ProjectCalendarService.WorkingHoursPerDay;
                }
                else if (!double.TryParse(raw, System.Globalization.NumberStyles.Any,
                             System.Globalization.CultureInfo.CurrentCulture, out hours))
                {
                    return; // entrada inválida — ignora
                }
                DurationHours = Math.Max(0, hours);
            }
        }

        public int DurationDays
        {
            get => ProjectCalendarService.CountWorkingDays(_task.Start, _task.Finish);
            set
            {
                if (UsesSfpEstimate)
                    return;

                if (value >= 0)
                {
                    _task.Finish = ProjectCalendarService.AddWorkingHours(_task.Start, value * ProjectCalendarService.WorkingHoursPerDay);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Finish));
                    OnPropertyChanged(nameof(FinishDisplay));
                    OnPropertyChanged(nameof(DurationHours));
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
                OnPropertyChanged(nameof(CanEditDuration));
                OnPropertyChanged(nameof(IsDurationReadOnly));
                OnPropertyChanged(nameof(DurationDays));
                OnPropertyChanged(nameof(DurationHours));
                OnPropertyChanged(nameof(Finish));
                OnPropertyChanged(nameof(FinishDisplay));
                OnPropertyChanged(nameof(DisplayAsMilestone));
            }
        }

        public bool FinishFixed
        {
            get => _task.FinishFixed;
            set
            {
                _task.FinishFixed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FinishDisplay));
                OnPropertyChanged(nameof(IsDurationReadOnly));
                FixFinishCommand.NotifyCanExecuteChanged();
            }
        }

        public string FinishDisplay =>
            _task.FinishFixed
                ? "📌 " + ProjectCalendarService.GetInclusiveFinishDate(_task.Start, _task.Finish).ToString("dd/MM/yy")
                : ProjectCalendarService.GetInclusiveFinishDate(_task.Start, _task.Finish).ToString("dd/MM/yy");

        [CommunityToolkit.Mvvm.Input.RelayCommand(CanExecute = nameof(CanFixFinish))]
        private void FixFinish() => FinishFixed = true;
        private bool CanFixFinish() => !_task.FinishFixed;

        [CommunityToolkit.Mvvm.Input.RelayCommand]
        private void ClearFinishFixed()
        {
            _task.FinishFixed = false;
            OnPropertyChanged(nameof(FinishFixed));
            OnPropertyChanged(nameof(FinishDisplay));
            OnPropertyChanged(nameof(IsDurationReadOnly));
        }

        // Setter de texto aceita data válida (marca fix) ou "0" (limpa fix).
        public string FinishText
        {
            set
            {
                var raw = value?.Trim() ?? string.Empty;
                if (raw == "0")
                {
                    FinishFixed = false;
                }
                else if (DateTime.TryParse(raw, out var parsed))
                {
                    var inclusive = ProjectCalendarService.GetInclusiveFinishDate(_task.Start, _task.Finish);
                    if (parsed.Date != inclusive.Date)
                    {
                        _task.Finish = parsed;
                        _task.FinishFixed = true;
                        OnPropertyChanged(nameof(Finish));
                        OnPropertyChanged(nameof(FinishFixed));
                        OnPropertyChanged(nameof(FinishDisplay));
                        OnPropertyChanged(nameof(DurationDays));
                        OnPropertyChanged(nameof(DurationHours));
                        RecalcAncestorSummaries();
                    }
                }
            }
        }

        public bool UsesSfpEstimate => (SfpPoints ?? 0) > 0;

        public TaskViewModel? ParentViewModel { get; set; }

        public List<TaskViewModel> ChildrenViewModels { get; } = new();

        public double PercentComplete
        {
            get
            {
                if (!CanEditPercentComplete)
                    return CalculateSummaryPercent();

                return _task.PercentComplete;
            }
            set
            {
                if (!CanEditPercentComplete)
                    return;

                var normalized = Math.Clamp(value, 0, 100);
                if (Math.Abs(_task.PercentComplete - normalized) < 0.0001)
                    return;

                _task.PercentComplete = normalized;

                // Recalcula HH Atual e HH Restante com base no % e na duração total fixada.
                // Usa CountWorkingHours como fallback quando HH estão zerados (sem informação).
                var totalH = _task.CurrentHours is > 0
                    ? _task.CurrentHours.Value + (_task.EstimatedHours ?? 0)
                    : ((_task.EstimatedHours is > 0 ? _task.EstimatedHours.Value : (double?)null)
                       ?? ProjectCalendarService.CountWorkingHours(_task.Start, _task.Finish));
                if (totalH > 0)
                {
                    var newCurrentH   = Math.Round(normalized / 100.0 * totalH, 2);
                    var newRemainingH = Math.Round(Math.Max(0, totalH - newCurrentH), 2);
                    _task.CurrentHours   = newCurrentH > 0 ? newCurrentH : null;
                    _task.EstimatedHours = newRemainingH > 0 ? newRemainingH : 0;
                    OnPropertyChanged(nameof(CurrentHours));
                    OnPropertyChanged(nameof(CurrentHoursDisplay));
                    OnPropertyChanged(nameof(EstimatedHoursDisplay));
                }

                if (normalized >= 100)
                {
                    // Se a tarefa ainda não começou (start no futuro) e o start não é fixo,
                    // move o start para hoje preservando as horas estimadas — evita zerar a duração.
                    var estimatedH = _task.EstimatedHours ?? (_task.DurationHours > 0 ? _task.DurationHours : 0);
                    if (!_task.StartFixed && _task.Start.Date > DateTime.Today)
                    {
                        _task.Start = DateTime.Today;
                        if (estimatedH > 0)
                            _task.Finish = ProjectCalendarService.AddWorkingHours(DateTime.Today, estimatedH);
                        else
                            _task.Finish = DateTime.Today;
                        OnPropertyChanged(nameof(Start));
                        OnPropertyChanged(nameof(StartDisplay));
                    }
                    else
                    {
                        _task.Finish = DateTime.Today;
                    }
                    OnPropertyChanged(nameof(Finish));
                    OnPropertyChanged(nameof(FinishDisplay));
                    OnPropertyChanged(nameof(DurationDays));
                    OnPropertyChanged(nameof(DurationHours));
                    OnPropertyChanged(nameof(DisplayAsMilestone));
                    RecalcAncestorSummaries();
                }
                else if (!_task.FinishFixed)
                {
                    // Restaura Finish com base no total (HH Atual + HH Restante).
                    var restoreH = (_task.CurrentHours ?? 0) + (_task.EstimatedHours ?? 0);
                    if (restoreH > 0)
                    {
                        _task.Finish = ProjectCalendarService.AddWorkingHours(_task.Start, restoreH);
                        OnPropertyChanged(nameof(Finish));
                        OnPropertyChanged(nameof(FinishDisplay));
                        OnPropertyChanged(nameof(DurationDays));
                        OnPropertyChanged(nameof(DurationHours));
                        OnPropertyChanged(nameof(DisplayAsMilestone));
                        RecalcAncestorSummaries();
                    }
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(PercentCompleteTextBrush));
                OnPropertyChanged(nameof(OriginalEstimatedHoursDisplay));
                OnPropertyChanged(nameof(OriginalEstimatedHoursText));
                OnPropertyChanged(nameof(HasOriginalEstimate));
                OnPropertyChanged(nameof(IsDurationReadOnly));
                NotifyParentPercentChanged();
            }
        }

        public bool CanEditPercentComplete => _task.Children.Count == 0;

        public string? OriginalEstimatedHoursText
        {
            get
            {
                var orig = GetCalculatedOriginalEstimatedHours(_task);
                if (orig == null || orig <= 0) return null;
                var current = GetCalculatedEstimatedHours(_task) ?? DurationHours;
                var diff = current - orig.Value;
                var diffText = Math.Abs(diff) < 0.05 ? "" :
                    diff > 0 ? $"  (+{diff:0.#}h)" : $"  ({diff:0.#}h)";
                return $"Estimativa original: {orig.Value:0.#}h{diffText}";
            }
        }

        public bool HasOriginalEstimate => GetCalculatedOriginalEstimatedHours(_task) is > 0;

        // Valor exibido na coluna OrgH
        public string? OriginalEstimatedHoursDisplay =>
            GetCalculatedOriginalEstimatedHours(_task) is { } hours && hours > 0 ? $"{hours:0}" : null;

        public bool UseOriginalHoursView => _task.UseOriginalHoursView;

        // Valor de HH Original formatado para exibição compacta na célula (ex: "↑40").
        public string? OriginalHoursDisplay =>
            _task.UseOriginalHoursView && GetCalculatedOriginalEstimatedHours(_task) is { } hours && hours > 0
                ? $"↑{hours:0}"
                : null;

        public bool CanEditDuration => _task.Children.Count == 0 && !UsesSfpEstimate;

        public bool IsDurationReadOnly => !CanEditDuration || _task.PercentComplete >= 100;

        public double? CurrentHours => _task.CurrentHours;
        public double? EstimatedHoursValue => _task.EstimatedHours;

        public string CurrentHoursDisplay =>
            _task.CurrentHours is > 0 ? _task.CurrentHours.Value.ToString("0.#") : "";
        public string EstimatedHoursDisplay =>
            _task.EstimatedHours is > 0 ? _task.EstimatedHours.Value.ToString("0.#") : "";

        public void RefreshDerivedDisplayProperties()
        {
            OnPropertyChanged(nameof(DurationHours));
            OnPropertyChanged(nameof(EstimatedHoursDisplay));
            OnPropertyChanged(nameof(CurrentHoursDisplay));
            RefreshOriginalEstimatedHoursProperties();
        }

        private void RefreshOriginalEstimatedHoursProperties()
        {
            OnPropertyChanged(nameof(OriginalEstimatedHoursDisplay));
            OnPropertyChanged(nameof(OriginalEstimatedHoursText));
            OnPropertyChanged(nameof(HasOriginalEstimate));
            OnPropertyChanged(nameof(OriginalHoursDisplay));
        }

        private static double? GetCalculatedOriginalEstimatedHours(ProjectTask task)
        {
            if (task.Children.Count == 0)
                return task.OriginalEstimatedHours is > 0 ? task.OriginalEstimatedHours.Value : null;

            var total = task.Children
                .Select(GetCalculatedOriginalEstimatedHours)
                .Where(h => h is > 0)
                .Sum(h => h!.Value);

            return total > 0 ? total : null;
        }

        private static double? GetCalculatedEstimatedHours(ProjectTask task)
        {
            if (task.Children.Count == 0)
            {
                var hours = task.EstimatedHours ?? task.DurationHours;
                return hours > 0 ? hours : null;
            }

            var total = task.Children
                .Select(GetCalculatedEstimatedHours)
                .Where(h => h is > 0)
                .Sum(h => h!.Value);

            return total > 0 ? total : null;
        }

        private void RefreshAncestorCalculatedProperties()
        {
            var current = ParentViewModel;
            while (current != null)
            {
                current.OnPropertyChanged(nameof(DurationHours));
                current.OnPropertyChanged(nameof(DurationDays));
                current.OnPropertyChanged(nameof(Finish));
                current.OnPropertyChanged(nameof(FinishDisplay));
                current.OnPropertyChanged(nameof(DisplayAsMilestone));
                current.RefreshOriginalEstimatedHoursProperties();
                current = current.ParentViewModel;
            }
        }

        public void SetOriginalHoursView(bool useOriginal)
        {
            if (useOriginal == _task.UseOriginalHoursView) return;
            if (useOriginal && !(GetCalculatedOriginalEstimatedHours(_task) is > 0)) return;

            _task.UseOriginalHoursView = useOriginal;
            OnPropertyChanged(nameof(UseOriginalHoursView));
            OnPropertyChanged(nameof(IsDurationReadOnly));
            OnPropertyChanged(nameof(OriginalEstimatedHoursText));
            OnPropertyChanged(nameof(OriginalHoursDisplay));
        }

        public Brush PercentCompleteTextBrush =>
            PercentComplete <= 30
                ? Brushes.Black
                : Brushes.White;

        private void NotifyParentPercentChanged()
        {
            ParentViewModel?.OnSummaryPercentChanged();
        }

        private void OnSummaryPercentChanged()
        {
            OnPropertyChanged(nameof(PercentComplete));
            OnPropertyChanged(nameof(PercentCompleteTextBrush));
            ParentViewModel?.OnSummaryPercentChanged();
        }

        private double CalculateSummaryPercent()
        {
            if (ChildrenViewModels.Count == 0)
            {
                if (_task.Children.Count == 0)
                    return _task.PercentComplete;

                double modelTotalWeight = 0.0;
                double modelWeightedPercent = 0.0;
                foreach (var child in _task.Children)
                {
                    var weight = Math.Max(1.0, TaskScheduleService.GetEffectiveDurationHours(child));
                    modelWeightedPercent += child.PercentComplete * weight;
                    modelTotalWeight += weight;
                }

                return modelTotalWeight > 0 ? modelWeightedPercent / modelTotalWeight : 0.0;
            }

            double totalWeight = 0.0;
            double weightedPercent = 0.0;

            foreach (var childVm in ChildrenViewModels)
            {
                var weight = Math.Max(1.0, TaskScheduleService.GetEffectiveDurationHours(childVm.Model));
                weightedPercent += childVm.PercentComplete * weight;
                totalWeight += weight;
            }

            return totalWeight > 0 ? weightedPercent / totalWeight : 0.0;
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

        public bool DisplayAsMilestone => IsMilestone || DurationHours == 0;

        public bool IsSummary
        {
            get => _task.IsSummary;
            set
            {
                _task.IsSummary = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanEditPercentComplete));
                OnPropertyChanged(nameof(PercentComplete));
            }
        }

        public int SprintNumber
        {
            get => _task.SprintNumber;
            set
            {
                _task.SprintNumber = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SprintDisplay));
            }
        }

        // Caminho da sprint no DevOps (System.IterationPath). Editável pela grade:
        // ao escolher outra sprint, o caminho muda e é sincronizado de volta.
        public string? SprintPath
        {
            get => _task.TfsIterationPath;
            set
            {
                if (string.Equals(_task.TfsIterationPath, value, StringComparison.Ordinal)) return;
                _task.TfsIterationPath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SprintDisplay));
            }
        }

        // Só Feature e Story têm sprint. Projeto/Epic (e, no DevOps, qualquer outro
        // tipo) não. Tarefas sem vínculo DevOps (TfsType vazio) usam a numeração
        // sintética do cronograma e, por isso, suportam sprint.
        public bool SupportsSprint
        {
            get
            {
                var t = _task.TfsType?.Trim();
                if (string.IsNullOrEmpty(t)) return true;
                return string.Equals(t, "Feature", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(t, "Story", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(t, "User Story", StringComparison.OrdinalIgnoreCase);
            }
        }

        // Texto exibido na coluna Sprint: nome da sprint do DevOps (folha do
        // IterationPath) quando vinculada; senão o número sintético do cronograma.
        // Em branco para Projeto/Epic (não têm sprint).
        public string SprintDisplay
        {
            get
            {
                if (!SupportsSprint) return string.Empty;

                var path = _task.TfsIterationPath;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    var idx = path.LastIndexOf('\\');
                    return idx >= 0 ? path[(idx + 1)..] : path;
                }
                return _task.SprintNumber > 0 ? _task.SprintNumber.ToString() : string.Empty;
            }
        }

        public string PredecessorsText
        {
            get
            {
                // Exibe o DisplayId (TfsId se vinculado, senão Id interno) de cada predecessora.
                if (FindByInternalId == null)
                    return string.Join(",", _task.PredecessorIds);

                var parts = new System.Collections.Generic.List<string>();
                foreach (var internalId in _task.PredecessorIds)
                {
                    var pred = FindByInternalId(internalId);
                    parts.Add(pred != null ? pred.DisplayId : internalId.ToString());
                }
                return string.Join(",", parts);
            }
            set
            {
                if (!CanEditPredecessors)
                    return;

                _task.PredecessorIds.Clear();
                foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var raw = part.Trim();
                    if (string.IsNullOrEmpty(raw)) continue;

                    // Tenta resolver pelo DisplayId (TfsId ou Id interno) → armazena Id interno.
                    if (FindByDisplayId != null)
                    {
                        var resolved = FindByDisplayId(raw);
                        if (resolved.HasValue)
                        {
                            _task.PredecessorIds.Add(resolved.Value);
                            continue;
                        }
                    }
                    // Fallback: trata como Id interno direto.
                    if (int.TryParse(raw, out int id))
                        _task.PredecessorIds.Add(id);
                }
                if (_task.PredecessorIds.Count == 0)
                {
                    if (_task.StartFixed)
                    {
                        _task.StartFixed = false;
                        OnPropertyChanged(nameof(StartFixed));
                        OnPropertyChanged(nameof(StartDisplay));
                    }
                    // Recalcula início a partir do irmão anterior (ou início da sprint)
                    var newStart = GetSprintStart?.Invoke() ?? _task.Start;
                    if (newStart != _task.Start)
                    {
                        var durationHours = DurationHours;
                        _task.Start = newStart;
                        if (!_task.FinishFixed)
                            _task.Finish = ProjectCalendarService.AddWorkingHours(newStart, durationHours);
                        OnPropertyChanged(nameof(Start));
                        OnPropertyChanged(nameof(Finish));
                        OnPropertyChanged(nameof(StartDisplay));
                        OnPropertyChanged(nameof(FinishDisplay));
                        OnPropertyChanged(nameof(DurationDays));
                        RecalcAncestorSummaries();
                        ScheduleSuccessors?.Invoke(this);
                    }
                }

                MoveAfterLatestPredecessor();
                OnPropertyChanged();
            }
        }

        public bool CanEditPredecessors => _task.Children.Count == 0;

        public void MoveAfterLatestPredecessor()
        {
            if (_task.PredecessorIds.Count == 0 || FindByInternalId == null)
                return;

            var predecessors = _task.PredecessorIds
                .Select(id => FindByInternalId(id))
                .Where(pred => pred != null && !ReferenceEquals(pred, this))
                .Cast<TaskViewModel>()
                .ToList();

            if (predecessors.Count == 0)
                return;

            var latestFinish = predecessors
                .Select(pred => ProjectCalendarService.GetInclusiveFinishDate(pred.Model.Start, pred.Model.Finish))
                .Max();
            var nextStart = ProjectCalendarService.AddWorkingDays(latestFinish, 1);
            if (_task.Start.Date == nextStart.Date)
                return;

            var durationHours = DurationHours;
            _task.Start = nextStart;
            _task.Finish = ProjectCalendarService.AddWorkingHours(nextStart, durationHours);
            // Não marca StartFixed: o início é calculado pela predecessora, não fixo pelo usuário.

            OnPropertyChanged(nameof(Start));
            OnPropertyChanged(nameof(StartDisplay));
            OnPropertyChanged(nameof(Finish));
            OnPropertyChanged(nameof(FinishDisplay));
            OnPropertyChanged(nameof(DurationDays));
            OnPropertyChanged(nameof(DurationHours));
            OnPropertyChanged(nameof(DisplayAsMilestone));
            OnPropertyChanged(nameof(StartFixed));
            RecalcAncestorSummaries();
            ScheduleSuccessors?.Invoke(this);
        }

        // Dispara notificações de data/duração após alteração direta no Model (sem passar pelo setter).
        public void NotifyDatesChanged()
        {
            OnPropertyChanged(nameof(Start));
            OnPropertyChanged(nameof(StartDisplay));
            OnPropertyChanged(nameof(Finish));
            OnPropertyChanged(nameof(FinishDisplay));
            OnPropertyChanged(nameof(DurationDays));
            OnPropertyChanged(nameof(DurationHours));
            OnPropertyChanged(nameof(DisplayAsMilestone));
            OnPropertyChanged(nameof(StartFixed));
            RecalcAncestorSummaries();
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

        public string PercAlocText
        {
            get
            {
                if (_task.Resources.Count == 0) return string.Empty;
                if (_task.Resources.Count == 1)
                    return $"{_task.Resources[0].AllocationPercent:0}%";
                return string.Join(" / ", _task.Resources.Select(r => $"{r.AllocationPercent:0}%"));
            }
        }

        public void NotifyResourcesChanged()
        {
            OnPropertyChanged(nameof(ResourcesText));
            OnPropertyChanged(nameof(PercAlocText));
        }

        public void RecalcFinishFromPercAloc()
        {
            if (_task.FinishFixed || _task.IsSummary || _task.IsMilestone) return;

            // Garante que EstimatedHours está definido para que o fator de alocação
            // seja aplicado corretamente. Se a tarefa não tem HH explícito, usa o
            // span calendário atual como base de horas de trabalho (a 100% de alocação
            // esse valor é equivalente a horas de trabalho).
            if (!_task.EstimatedHours.HasValue || _task.EstimatedHours.Value <= 0)
                _task.EstimatedHours = _task.DurationHours > 0 ? _task.DurationHours : null;

            // Propaga para os assignments que também não têm EstimatedHours explícito.
            foreach (var r in _task.Resources)
                if (!r.EstimatedHours.HasValue || r.EstimatedHours.Value <= 0)
                    r.EstimatedHours = _task.EstimatedHours;

            // Não pula por PercentComplete — alteração de alocação deve sempre recalcular o fim.
            var effectiveHours = Services.TaskScheduleService.GetEffectiveDurationHours(_task);
            if (effectiveHours > 0)
                _task.Finish = Services.ProjectCalendarService.AddWorkingHours(_task.Start, effectiveHours);

            OnPropertyChanged(nameof(Finish));
            OnPropertyChanged(nameof(FinishDisplay));
            OnPropertyChanged(nameof(DurationDays));
            OnPropertyChanged(nameof(DurationHours));
            RecalcAncestorSummaries();
            ScheduleSuccessors?.Invoke(this);
        }

        // Conveniência: recurso principal (primeiro da lista). Usado pela grade para
        // permitir editar/atribuir rapidamente um recurso único.
        // Callback acionado quando o recurso primário muda, passando o ID do recurso anterior.
        public Action<TaskViewModel, int?>? PrimaryResourceChanged { get; set; }

        public NXProject.Models.Resource? PrimaryResource
        {
            get => _task.Resources.Count > 0 ? _task.Resources[0].Resource : null;
            set
            {
                var oldResourceId = _task.Resources.Count > 0 ? (int?)_task.Resources[0].ResourceId : null;

                if (value == null)
                {
                    _task.Resources.Clear();
                }
                else
                {
                    if (_task.Resources.Count > 0)
                    {
                        var first = _task.Resources[0];
                        first.ResourceId = value.Id;
                        first.Resource = value;
                    }
                    else
                    {
                        _task.Resources.Add(new NXProject.Models.TaskResource
                        {
                            ResourceId = value.Id,
                            Resource = value,
                            AllocationPercent = 100.0
                        });
                    }
                }

                OnPropertyChanged(); // PrimaryResource
                OnPropertyChanged(nameof(ResourcesText));

                var newResourceId = _task.Resources.Count > 0 ? (int?)_task.Resources[0].ResourceId : null;
                if (oldResourceId != newResourceId)
                    PrimaryResourceChanged?.Invoke(this, oldResourceId);
            }
        }

        public string? Notes
        {
            get => _task.Notes;
            set { _task.Notes = value; OnPropertyChanged(); }
        }

        public string? Justificativa
        {
            get => _task.Justificativa;
            set { _task.Justificativa = value; OnPropertyChanged(); }
        }

        public void ShiftSchedule(int dayDelta)
        {
            if (dayDelta == 0)
                return;

            _task.Start = _task.Start.AddDays(dayDelta);
            _task.Finish = _task.Finish.AddDays(dayDelta);
            OnPropertyChanged(nameof(Start));
            OnPropertyChanged(nameof(Finish));
            OnPropertyChanged(nameof(FinishDisplay));
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
            var calculatedWorkingDays = Math.Max(1, (int)Math.Ceiling(sfpPoints * daysPerSfp));
            var calculatedHours = calculatedWorkingDays * ProjectCalendarService.WorkingHoursPerDay;
            _task.Finish = ProjectCalendarService.AddWorkingHours(_task.Start, calculatedHours);
            OnPropertyChanged(nameof(FinishDisplay));
            RecalcAncestorSummaries();
        }
    }
}
