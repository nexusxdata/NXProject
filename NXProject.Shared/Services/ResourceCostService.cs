using System;
using System.Collections.Generic;
using System.Linq;
using NXProject.Models;

namespace NXProject.Services
{
    public sealed record ResourceCostLine(
        string   EpicName,
        string   FeatureName,
        string   StoryName,
        string   ResourceName,
        ResourceCostType CostType,
        bool     IsCapex,
        int      Year,
        int      Month,
        double   Hours,
        decimal  Cost);

    public static class ResourceCostService
    {
        /// <summary>
        /// Calcula o custo por recurso, agrupado por Feature (nível 1 acima da task) e mês.
        /// </summary>
        public static List<ResourceCostLine> Compute(
            IEnumerable<ProjectTask> allTasks,
            IEnumerable<Resource>    resources)
        {
            var resById = resources.ToDictionary(r => r.Id);
            var lines   = new List<ResourceCostLine>();

            // Coleta todas as tasks folha com recursos
            var leaves = allTasks
                .Where(t => !t.IsSummary && t.Resources.Count > 0 && t.Start < t.Finish)
                .ToList();

            // Para recursos mensais: total de HH do recurso em TODO o projeto = 1 salário
            var totalHoursByResource = new Dictionary<int, double>();
            foreach (var task in leaves)
            {
                foreach (var tr in task.Resources)
                {
                    if (!resById.TryGetValue(tr.ResourceId, out var res)) continue;
                    if (res.CostType != ResourceCostType.Monthly) continue;
                    double h = (tr.EstimatedHours ?? task.EstimatedHours ?? 0) + (task.CurrentHours ?? 0);
                    totalHoursByResource.TryGetValue(tr.ResourceId, out var prev);
                    totalHoursByResource[tr.ResourceId] = prev + h;
                }
            }

            foreach (var task in leaves)
            {
                var epicName    = FindEpicName(task);
                var featureName = FindFeatureName(task);
                bool isCapex    = FindEpicAncestor(task)?.TipoCentroCusto
                                    ?.Equals("CAPEX", StringComparison.OrdinalIgnoreCase) == true;

                string storyName = task.Name ?? "";

                foreach (var tr in task.Resources)
                {
                    if (!resById.TryGetValue(tr.ResourceId, out var res)) continue;
                    if (res.Kind == ResourceKind.Internal) continue;
                    // HH total = HH Atual (realizadas) + HH Estimado (restante), igual ao DurationHours
                    double taskHours = (tr.EstimatedHours ?? task.EstimatedHours ?? 0)
                                     + (task.CurrentHours ?? 0);
                    double alloc     = tr.AllocationPercent / 100.0;

                    if (res.CostType == ResourceCostType.Hourly)
                    {
                        decimal costPerHour = res.CostPerHour > 0 ? res.CostPerHour : 0;
                        if (costPerHour == 0) continue;

                        foreach (var (year, month, h) in SplitByMonth(task.Start, task.Finish, taskHours * alloc))
                        {
                            lines.Add(new ResourceCostLine(
                                epicName, featureName, storyName, res.Name, ResourceCostType.Hourly, isCapex,
                                year, month, h, (decimal)h * costPerHour));
                        }
                    }
                    else // Monthly
                    {
                        if (res.MonthlyRate <= 0) continue;
                        if (!totalHoursByResource.TryGetValue(tr.ResourceId, out var totalH) || totalH <= 0) continue;

                        double effectiveHours = taskHours * alloc;
                        decimal taskCost = res.MonthlyRate * (decimal)(effectiveHours / totalH);

                        foreach (var (year, month, dayFrac) in SplitByDayFraction(task.Start, task.Finish))
                        {
                            lines.Add(new ResourceCostLine(
                                epicName, featureName, storyName, res.Name, ResourceCostType.Monthly, isCapex,
                                year, month, effectiveHours * dayFrac, taskCost * (decimal)dayFrac));
                        }
                    }
                }
            }

            return lines
                .OrderBy(l => l.Year).ThenBy(l => l.Month)
                .ThenBy(l => l.FeatureName).ThenBy(l => l.ResourceName)
                .ToList();
        }

        /// <summary>Divide as horas de uma task proporcionalmente pelos meses que ela abrange.</summary>
        // Fraciona por dias de calendário (não depende de horas)
        private static IEnumerable<(int year, int month, double fraction)> SplitByDayFraction(
            DateTime start, DateTime finish)
        {
            if (finish <= start) yield break;
            double totalDays = (finish - start).TotalDays;
            var cur = new DateTime(start.Year, start.Month, 1);
            while (cur <= finish)
            {
                var monthEnd     = cur.AddMonths(1);
                var overlapStart = cur > start ? cur : start;
                var overlapEnd   = monthEnd < finish ? monthEnd : finish;
                if (overlapEnd > overlapStart)
                    yield return (cur.Year, cur.Month, (overlapEnd - overlapStart).TotalDays / totalDays);
                cur = monthEnd;
            }
        }

        private static IEnumerable<(int year, int month, double hours)> SplitByMonth(
            DateTime start, DateTime finish, double totalHours)
        {
            if (totalHours <= 0 || finish <= start) yield break;

            double totalDays = (finish - start).TotalDays;
            var cur = new DateTime(start.Year, start.Month, 1);

            while (cur <= finish)
            {
                var monthEnd   = cur.AddMonths(1);
                var overlapStart = cur > start ? cur : start;
                var overlapEnd   = monthEnd < finish ? monthEnd : finish;
                if (overlapEnd > overlapStart)
                {
                    double fraction = (overlapEnd - overlapStart).TotalDays / totalDays;
                    yield return (cur.Year, cur.Month, totalHours * fraction);
                }
                cur = monthEnd;
            }
        }

        private static string FindFeatureName(ProjectTask task)
        {
            var p = task.Parent;
            while (p?.Parent?.Parent != null) p = p.Parent;
            return p?.Name ?? task.Name;
        }

        private static string FindEpicName(ProjectTask task)
            => FindEpicAncestor(task)?.Name ?? "";

        private static ProjectTask? FindEpicAncestor(ProjectTask task)
        {
            var cur = task.Parent;
            while (cur != null)
            {
                if (string.Equals(cur.TfsType, "Epic", StringComparison.OrdinalIgnoreCase))
                    return cur;
                cur = cur.Parent;
            }
            return null;
        }
    }
}
