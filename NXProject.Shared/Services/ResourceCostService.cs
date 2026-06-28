using System;
using System.Collections.Generic;
using System.Linq;
using NXProject.Models;

namespace NXProject.Services
{
    public sealed record ResourceCostLine(
        string   FeatureName,
        string   ResourceName,
        ResourceCostType CostType,
        int      Year,
        int      Month,
        double   Hours,          // HH da task/feature para o recurso nesse mês
        decimal  Cost);          // Custo calculado

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

            // Para o cálculo Mensal: total de HH do recurso por mês (denominador)
            // Computa antecipadamente para todos os recursos
            var totalHoursByResourceMonth = new Dictionary<(int resId, int year, int month), double>();
            foreach (var task in leaves)
            {
                foreach (var tr in task.Resources)
                {
                    if (!resById.TryGetValue(tr.ResourceId, out var res)) continue;
                    if (res.CostType != ResourceCostType.Monthly) continue;
                    var taskHours = tr.EstimatedHours ?? task.EstimatedHours ?? 0;
                    foreach (var (year, month, h) in SplitByMonth(task.Start, task.Finish, taskHours))
                    {
                        var key = (tr.ResourceId, year, month);
                        totalHoursByResourceMonth.TryGetValue(key, out var prev);
                        totalHoursByResourceMonth[key] = prev + h;
                    }
                }
            }

            foreach (var task in leaves)
            {
                var featureName = FindFeatureName(task);

                foreach (var tr in task.Resources)
                {
                    if (!resById.TryGetValue(tr.ResourceId, out var res)) continue;
                    double taskHours = tr.EstimatedHours ?? task.EstimatedHours ?? 0;
                    double alloc     = tr.AllocationPercent / 100.0;

                    if (res.CostType == ResourceCostType.Hourly)
                    {
                        // Custo total da task para o recurso, distribuído por mês
                        decimal costPerHour = res.CostPerHour > 0 ? res.CostPerHour : 0;
                        if (costPerHour == 0) continue;

                        foreach (var (year, month, h) in SplitByMonth(task.Start, task.Finish, taskHours * alloc))
                        {
                            lines.Add(new ResourceCostLine(
                                featureName, res.Name, ResourceCostType.Hourly,
                                year, month, h,
                                (decimal)h * costPerHour));
                        }
                    }
                    else // Monthly
                    {
                        if (res.MonthlyRate == 0) continue;

                        foreach (var (year, month, h) in SplitByMonth(task.Start, task.Finish, taskHours))
                        {
                            var key = (tr.ResourceId, year, month);
                            totalHoursByResourceMonth.TryGetValue(key, out var totalH);
                            if (totalH <= 0) continue;

                            decimal cost = res.MonthlyRate * (decimal)(h / totalH);
                            lines.Add(new ResourceCostLine(
                                featureName, res.Name, ResourceCostType.Monthly,
                                year, month, h, cost));
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
            while (p?.Parent?.Parent != null) p = p.Parent;  // sobe até nível 1
            return p?.Name ?? task.Name;
        }
    }
}
