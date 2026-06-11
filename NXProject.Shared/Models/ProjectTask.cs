using System;
using System.Collections.ObjectModel;
using NXProject.Services;

namespace NXProject.Models
{
    public class ProjectTask
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Level { get; set; } = 0;
        public bool IsSummary { get; set; } = false;
        public bool IsMilestone { get; set; } = false;

        public DateTime Start { get; set; } = DateTime.Today;
        public DateTime Finish { get; set; } = DateTime.Today.AddDays(1);
        public TimeSpan Duration => Finish - Start;
        public double DurationHours => ProjectCalendarService.CountWorkingHours(Start, Finish);

        public double PercentComplete { get; set; } = 0;

        public string? Notes { get; set; }

        public double? EstimatedHours { get; set; }

        public double? SfpPoints { get; set; }

        // ── Vínculo com TFS / Azure DevOps ──────────────────────────────────
        // Id do work item no DevOps (distinto do Id interno, que é sequencial).
        // 0 = marcado para criar no DevOps na próxima sincronização.
        public int? TfsId { get; set; }
        // Id do work item PAI no DevOps (parent hierárquico) na época da importação,
        // usado para detectar reparenting e atualizar o link na sincronização.
        public int? TfsParentId { get; set; }
        // Tipo no DevOps: Project, Epic, Feature, Story.
        public string? TfsType { get; set; }
        // Estado no DevOps: New, Active, Block, Closed, Removed, Resolved.
        public string? TfsState { get; set; }
        // Descrição (System.Description) importada do DevOps, usada na sincronização.
        public string? Description { get; set; }
        // Tags do DevOps (System.Tags), separadas por "; " (ex.: "Block"). Lidas no
        // import e sincronizadas de volta se mudarem.
        public string? Tags { get; set; }
        // Bloqueio DERIVADO de Tasks filhas com tag Block. SÓ visão no NXProject —
        // nunca é sincronizado de volta (distinto de Tags).
        public bool BlockedByChild { get; set; }
        // Ordem no backlog do DevOps (Microsoft.VSTS.Common.StackRank): menor = mais
        // acima. Importado, usado para ordenar irmãos e sincronizado se a ordem mudar.
        public double? TfsStackRank { get; set; }
        // Caminho da sprint no DevOps (System.IterationPath), ex.: "Proj\\Sprint 5".
        // Lido no import; se alterado no NXProject, sincronizado de volta.
        public string? TfsIterationPath { get; set; }

        // Recursos alocados nesta tarefa
        public List<TaskResource> Resources { get; set; } = new();

        // Predecessoras: lista de IDs de tarefas
        public List<int> PredecessorIds { get; set; } = new();

        // Subtarefas
        public ObservableCollection<ProjectTask> Children { get; set; } = new();

        // Referência ao pai
        public ProjectTask? Parent { get; set; }

        public int SprintNumber { get; set; } = 0;

        // Recalcula datas de tarefas de resumo com base nos filhos
        public void RecalcSummary()
        {
            if (!IsSummary || Children.Count == 0) return;

            var minStart = DateTime.MaxValue;
            var maxFinish = DateTime.MinValue;

            foreach (var child in Children)
            {
                child.RecalcSummary();
                if (child.Start < minStart) minStart = child.Start;
                if (child.Finish > maxFinish) maxFinish = child.Finish;
            }

            Start = minStart;
            Finish = maxFinish;
            RecalcPercentComplete();
        }

        private void RecalcPercentComplete()
        {
            if (!IsSummary || Children.Count == 0)
                return;

            double totalWeight = 0.0;
            double weightedPercent = 0.0;

            foreach (var child in Children)
            {
                var weight = Math.Max(1.0, TaskScheduleService.GetEffectiveDurationHours(child));
                weightedPercent += child.PercentComplete * weight;
                totalWeight += weight;
            }

            PercentComplete = totalWeight > 0
                ? weightedPercent / totalWeight
                : Children.Average(c => c.PercentComplete);
        }

        public override string ToString() => $"{Id} - {Name}";
    }

    public enum DependencyType
    {
        FinishToStart,
        StartToStart,
        FinishToFinish,
        StartToFinish
    }

    public class TaskDependency
    {
        public int PredecessorId { get; set; }
        public int SuccessorId { get; set; }
        public DependencyType Type { get; set; } = DependencyType.FinishToStart;
        public int LagDays { get; set; } = 0;
    }
}
