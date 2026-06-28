using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NXProject.Models;

namespace NXProject.Services
{
    /// <summary>
    /// Salva e carrega a configuração de custo dos recursos em um arquivo .nxcost (JSON).
    /// O arquivo é mantido fora do .nxp para preservar o sigilo dos valores.
    /// A chave de correlação é o nome do recurso (case-insensitive).
    /// </summary>
    public static class ResourceCostConfigService
    {
        public const string FileExtension    = ".nxcost";
        public const string FileFilter       = "Configuração de Custo (*.nxcost)|*.nxcost|JSON (*.json)|*.json";
        public const string DefaultFileName  = "recursos-custo.nxcost";

        private sealed record CostEntry(
            string   Name,
            string   CostType,     // "Hourly" | "Monthly"
            decimal  HourlyRate,
            decimal  MonthlyRate);

        public static void Save(string filePath, IEnumerable<Resource> resources)
        {
            var entries = resources
                .Select(r => new CostEntry(r.Name, r.CostType.ToString(), r.CostPerHour, r.MonthlyRate))
                .ToList();
            var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Carrega o arquivo e aplica os valores nos recursos correspondentes por nome.
        /// Retorna o número de recursos atualizados.
        /// </summary>
        public static int Load(string filePath, IEnumerable<Resource> resources)
        {
            if (!File.Exists(filePath)) return 0;
            var json    = File.ReadAllText(filePath);
            var entries = JsonSerializer.Deserialize<List<CostEntry>>(json);
            if (entries == null) return 0;

            var map   = entries.ToDictionary(e => e.Name, e => e,
                            System.StringComparer.OrdinalIgnoreCase);
            int count = 0;
            foreach (var r in resources)
            {
                if (!map.TryGetValue(r.Name, out var e)) continue;
                r.CostType    = e.CostType == "Monthly" ? ResourceCostType.Monthly : ResourceCostType.Hourly;
                r.CostPerHour  = e.HourlyRate;
                r.MonthlyRate  = e.MonthlyRate;
                count++;
            }
            return count;
        }
    }
}
