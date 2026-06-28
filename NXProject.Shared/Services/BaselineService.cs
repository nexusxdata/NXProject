using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NXProject.Models;

namespace NXProject.Services
{
    /// <summary>
    /// Salva e carrega o Baseline do projeto em um arquivo .nxb (JSON) ao lado do .nxp.
    /// Não modifica o arquivo do projeto — impacto zero em projetos que não usam baseline.
    /// </summary>
    public static class BaselineService
    {
        private sealed record BaselineEntry(
            int    Id,
            string Start,
            string Finish,
            double? Hours);

        private static string GetBaselinePath(string projectFilePath) =>
            Path.ChangeExtension(projectFilePath, ".nxb");

        public static bool HasBaseline(string projectFilePath) =>
            !string.IsNullOrWhiteSpace(projectFilePath) &&
            File.Exists(GetBaselinePath(projectFilePath));

        /// <summary>Salva snapshot de datas e horas de todas as tarefas leaf+summary.</summary>
        public static void Save(string projectFilePath, IEnumerable<ProjectTask> allTasks)
        {
            var entries = new List<BaselineEntry>();
            foreach (var t in allTasks)
            {
                entries.Add(new BaselineEntry(
                    t.Id,
                    t.Start.ToString("yyyy-MM-dd"),
                    t.Finish.ToString("yyyy-MM-dd"),
                    t.EstimatedHours));
            }

            var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(GetBaselinePath(projectFilePath), json);
        }

        /// <summary>Carrega o baseline e aplica nos campos Baseline* das tarefas em memória.</summary>
        public static bool Load(string projectFilePath, IEnumerable<ProjectTask> allTasks)
        {
            var path = GetBaselinePath(projectFilePath);
            if (!File.Exists(path)) return false;

            try
            {
                var json    = File.ReadAllText(path);
                var entries = JsonSerializer.Deserialize<List<BaselineEntry>>(json);
                if (entries == null) return false;

                var map = entries.ToDictionary(e => e.Id);
                foreach (var t in allTasks)
                {
                    if (!map.TryGetValue(t.Id, out var e)) continue;
                    t.BaselineStart  = DateTime.TryParse(e.Start,  out var s) ? s : null;
                    t.BaselineFinish = DateTime.TryParse(e.Finish, out var f) ? f : null;
                    t.BaselineHours  = e.Hours;
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>Remove o arquivo .nxb e limpa os campos Baseline* em memória.</summary>
        public static void Clear(string projectFilePath, IEnumerable<ProjectTask> allTasks)
        {
            var path = GetBaselinePath(projectFilePath);
            if (File.Exists(path)) File.Delete(path);

            foreach (var t in allTasks)
            {
                t.BaselineStart  = null;
                t.BaselineFinish = null;
                t.BaselineHours  = null;
            }
        }
    }
}
