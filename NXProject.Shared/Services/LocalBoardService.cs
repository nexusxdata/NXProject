// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NXProject.Models;

namespace NXProject.Services
{
    /// <summary>
    /// Monta o board do TaskBoard a partir do CRONOGRAMA ABERTO, sem servidor nenhum.
    ///
    /// É uma projeção: o `.nxproject` já guarda estado, tags, sprint, prioridade, rank,
    /// responsável, HH e descrição de cada atividade — o que falta é apresentá-los no formato que
    /// o board desenha (<see cref="TfsImportService.SprintBoard"/>). Nada é inventado aqui; o que
    /// não existe no arquivo (data de criação, encerramento) vem vazio, e o board já sabe lidar
    /// com isso.
    ///
    /// A hierarquia do board sai da hierarquia do cronograma:
    ///   nível 0/1 → Project/EPIC · Feature → coluna Feature · Story → linha · Task → card.
    /// Como o cronograma pode parar na Story (ela é folha por definição no NX), uma Story sem
    /// filho vira linha sem card — exatamente o que a visão Projeto &amp; Story já mostra.
    /// </summary>
    public static class LocalBoardService
    {
        /// <summary>Estados do board local. Sem processo de servidor, a lista é esta.</summary>
        public static readonly string[] LocalStates = { "New", "Active", "Closed" };

        /// <summary>
        /// Projeta o cronograma no board. <paramref name="iterationPaths"/> vazio = todas as
        /// sprints; com valores, filtra pela sprint gravada na atividade.
        /// </summary>
        public static TfsImportService.SprintBoard Build(Project project,
            IReadOnlyCollection<string>? iterationPaths = null)
        {
            var stories = new List<TfsImportService.SprintStoryRow>();
            var levelItems = new List<TfsImportService.SprintLevelItem>();
            var people = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
            if (project == null) return new TfsImportService.SprintBoard(LocalStates.ToList(), stories, people.ToList());

            var all = Flatten(project.Tasks).ToList();
            var byId = all.ToDictionary(t => t.Id);
            bool SprintOk(ProjectTask t) => iterationPaths == null || iterationPaths.Count == 0
                || iterationPaths.Contains(t.TfsIterationPath ?? "", StringComparer.OrdinalIgnoreCase);

            // Nível de cada atividade pela posição na árvore: o board precisa saber quem é
            // Feature, EPIC e Projeto para montar as colunas de cima.
            foreach (var t in all)
            {
                var kind = LevelKindOf(t, byId);
                if (kind is "Feature" or "Epic" or "Project")
                    levelItems.Add(new TfsImportService.SprintLevelItem(
                        t.Id, t.Name, kind, StateOf(t), OwnerOf(t))
                    { IterationPath = t.TfsIterationPath ?? "", Tags = t.Tags ?? "" });
            }

            foreach (var story in all.Where(t => LevelKindOf(t, byId) == "Story"))
            {
                if (!SprintOk(story)) continue;
                var tasks = new List<TfsImportService.SprintTaskCard>();
                foreach (var child in story.Children ?? Enumerable.Empty<ProjectTask>())
                    tasks.Add(ToCard(child));

                var feature = ParentOf(story, byId);
                var epic = feature != null ? ParentOf(feature, byId) : null;
                var proj = epic != null ? ParentOf(epic, byId) : null;

                var row = new TfsImportService.SprintStoryRow(story.Id, story.Name, StateOf(story),
                    OwnerOf(story), tasks)
                {
                    IterationPath = story.TfsIterationPath ?? "",
                    Tags = story.Tags ?? "",
                    AcceptanceCriteria = story.AcceptanceCriteria ?? "",
                    StackRank = story.TfsStackRank ?? double.NaN,
                    FeatureId = feature?.Id ?? 0,
                    FeatureTitle = feature?.Name ?? "",
                    FeatureAssignedTo = feature != null ? OwnerOf(feature) : "",
                    FeatureState = feature != null ? StateOf(feature) : "",
                    FeatureIterationPath = feature?.TfsIterationPath ?? "",
                    FeatureEpicId = epic?.Id ?? 0,
                    FeatureEpicTitle = epic?.Name ?? "",
                    FeatureEpicState = epic != null ? StateOf(epic) : "",
                    FeatureProjectId = proj?.Id ?? 0,
                    FeatureProjectTitle = proj?.Name ?? "",
                    FeatureProjectState = proj != null ? StateOf(proj) : ""
                };
                stories.Add(row);

                if (!string.IsNullOrWhiteSpace(row.AssignedTo)) people.Add(row.AssignedTo);
                foreach (var c in tasks)
                    if (!string.IsNullOrWhiteSpace(c.AssignedTo)) people.Add(c.AssignedTo);
            }

            var states = LocalStates
                .Concat(stories.Select(s => s.State))
                .Concat(stories.SelectMany(s => s.Tasks).Select(t => t.State));
            return new TfsImportService.SprintBoard(
                TfsImportService.OrderTaskboardStates(states), stories, people.ToList())
            { LevelItems = levelItems };
        }

        /// <summary>
        /// Sprints do cronograma, no formato que o board usa.
        ///
        /// A identidade da sprint é o <see cref="Sprint.Path"/> (o `System.IterationPath` que veio
        /// do DevOps), porque é por ele que a atividade guarda a sua sprint — filtrar pelo nome
        /// não acharia nada. Cronograma que nunca viu DevOps não tem caminho: aí o nome é a
        /// identidade possível, e o filtro de sprint só funciona se as atividades também o usarem.
        /// </summary>
        public static List<TfsImportService.SprintInfo> Sprints(Project project) =>
            (project?.Sprints?.ToList() ?? new List<Sprint>())
                .Where(s => !string.IsNullOrWhiteSpace(s.Name))
                .Select(s => new TfsImportService.SprintInfo(
                    s.Name, string.IsNullOrWhiteSpace(s.Path) ? s.Name : s.Path!, s.Start, s.End))
                .ToList();

        /// <summary>
        /// Que nível a atividade ocupa no board. O cronograma não guarda "tipo": a regra é a
        /// posição na árvore, de baixo para cima — folha sob Story é Task, e assim por diante.
        /// Quando a atividade tem TfsType (veio do DevOps), ele vence, porque é mais preciso.
        /// </summary>
        public static string LevelKindOf(ProjectTask t, IReadOnlyDictionary<int, ProjectTask> byId)
        {
            if (!string.IsNullOrWhiteSpace(t.TfsType))
            {
                if (TfsImportService.IsBoardProjectType(t.TfsType)) return "Project";
                if (TfsImportService.IsBoardEpicType(t.TfsType)) return "Epic";
                if (TfsImportService.IsBoardFeatureType(t.TfsType)) return "Feature";
                if (string.Equals(t.TfsType, "Task", StringComparison.OrdinalIgnoreCase)) return "Task";
                return "Story";
            }
            // Sem tipo: conta a profundidade até a raiz.
            var depth = 0;
            var cur = t;
            while (ParentOf(cur, byId) is { } p && depth < 10) { cur = p; depth++; }
            return depth switch { 0 => "Project", 1 => "Epic", 2 => "Feature", 3 => "Story", _ => "Task" };
        }

        private static ProjectTask? ParentOf(ProjectTask t, IReadOnlyDictionary<int, ProjectTask> byId) =>
            byId.Values.FirstOrDefault(p => p.Children != null && p.Children.Any(c => c.Id == t.Id));

        private static TfsImportService.SprintTaskCard ToCard(ProjectTask t) =>
            new(t.Id, t.Name, StateOf(t), OwnerOf(t),
                t.EstimatedHours?.ToString("0.##", CultureInfo.CurrentCulture) ?? "",
                null, t.Tags ?? "", null, t.Priority ?? 0, t.TfsStackRank ?? double.NaN)
            {
                IterationPath = t.TfsIterationPath ?? "",
                EstimateHours = t.EstimatedHours,
                CompletedHours = t.CurrentHours,
                FinishDate = t.Finish
            };

        /// <summary>
        /// Estado da atividade. Usa o TfsState quando existe (veio do servidor ou foi editado no
        /// board); senão deriva do percentual, que é o que o cronograma sempre teve.
        /// </summary>
        public static string StateOf(ProjectTask t)
        {
            if (!string.IsNullOrWhiteSpace(t.TfsState)) return t.TfsState!;
            if (t.PercentComplete >= 99.99) return "Closed";
            return t.PercentComplete > 0 ? "Active" : "New";
        }

        /// <summary>Responsável: o primeiro recurso da atividade — é o que o board mostra.</summary>
        public static string OwnerOf(ProjectTask t) =>
            t.Resources?.FirstOrDefault()?.Resource?.Name ?? "";

        private static IEnumerable<ProjectTask> Flatten(IEnumerable<ProjectTask>? tasks)
        {
            foreach (var t in tasks ?? Enumerable.Empty<ProjectTask>())
            {
                yield return t;
                foreach (var c in Flatten(t.Children)) yield return c;
            }
        }
    }
}
