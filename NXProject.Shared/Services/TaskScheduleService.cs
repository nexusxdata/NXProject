using System;
using System.Collections.Generic;
using System.Linq;
using NXProject.Models;

namespace NXProject.Services
{
    public static class TaskScheduleService
    {
        public static double NormalizeAllocationPercent(double allocationPercent) =>
            double.IsNaN(allocationPercent) || allocationPercent <= 0
                ? 100.0
                : allocationPercent;

        public static double GetAssignmentHours(ProjectTask task, TaskResource assignment)
        {
            if (assignment.EstimatedHours.HasValue && assignment.EstimatedHours.Value > 0)
                return assignment.EstimatedHours.Value;

            var allocationFactor = NormalizeAllocationPercent(assignment.AllocationPercent) / 100.0;
            return Math.Max(0.0, task.DurationHours) * allocationFactor;
        }

        public static double? GetTaskEstimatedHours(ProjectTask task)
        {
            var assignmentHours = task.Resources
                .Where(r => r.EstimatedHours.HasValue && r.EstimatedHours.Value > 0)
                .Sum(r => r.EstimatedHours!.Value);

            if (assignmentHours > 0)
                return assignmentHours;

            return task.EstimatedHours.HasValue && task.EstimatedHours.Value > 0
                ? task.EstimatedHours.Value
                : null;
        }

        /// <summary>
        /// Fator de disponibilidade geral da pessoa no projeto (0–1).
        /// Padrão 1,0 quando não definido ou inválido.
        /// </summary>
        private static double NormalizeAvailabilityFactor(Resource? resource)
        {
            var pct = resource?.AvailabilityPercent ?? 100.0;
            return (double.IsNaN(pct) || pct <= 0) ? 1.0 : Math.Min(1.0, pct / 100.0);
        }

        public static double GetEffectiveDurationHours(ProjectTask task)
        {
            if (task.IsMilestone)
                return 0.0;

            var durations = new List<double>();
            foreach (var assignment in task.Resources)
            {
                var hours = assignment.EstimatedHours;
                if (!hours.HasValue || hours.Value <= 0)
                    continue;

                // Fator combinado: % alocação na tarefa × % disponibilidade geral
                var allocationFactor    = NormalizeAllocationPercent(assignment.AllocationPercent) / 100.0;
                var availabilityFactor  = NormalizeAvailabilityFactor(assignment.Resource);
                var combined            = Math.Max(0.01, allocationFactor * availabilityFactor);
                durations.Add(hours.Value / combined);
            }

            if (durations.Count > 0)
                return durations.Max();

            var estimatedHours = task.EstimatedHours;
            if (estimatedHours.HasValue && estimatedHours.Value > 0)
            {
                // Sem horas por assignment: distribui pelo somatório de alocação × disponibilidade
                var combinedFactor = task.Resources.Count == 0
                    ? 1.0
                    : task.Resources
                        .Select(r => NormalizeAllocationPercent(r.AllocationPercent) / 100.0
                                     * NormalizeAvailabilityFactor(r.Resource))
                        .DefaultIfEmpty(1.0)
                        .Sum();

                return estimatedHours.Value / Math.Max(0.01, combinedFactor);
            }

            return Math.Max(0.0, task.DurationHours);
        }

        public static void SyncTaskEstimatedHoursFromAssignments(ProjectTask task)
        {
            var total = task.Resources
                .Where(r => r.EstimatedHours.HasValue && r.EstimatedHours.Value > 0)
                .Sum(r => r.EstimatedHours!.Value);

            task.EstimatedHours = total > 0 ? total : task.EstimatedHours;
        }

        public static void RecalculateFinishFromAssignments(ProjectTask task)
        {
            if (task.IsSummary)
                return;

            if (task.PercentComplete >= 100)
                return;

            SyncTaskEstimatedHoursFromAssignments(task);

            var durationHours = GetEffectiveDurationHours(task);
            task.Finish = durationHours <= 0
                ? task.Start
                : ProjectCalendarService.AddWorkingHours(task.Start, durationHours);
        }
    }
}
