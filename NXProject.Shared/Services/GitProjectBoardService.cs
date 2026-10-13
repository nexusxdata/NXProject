// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Linq;

namespace NXProject.Services
{
    /// <summary>
    /// Traduz um projeto do GitHub para o board do NXProject. É projeção pura: recebe dados já
    /// carregados e devolve o <see cref="TfsImportService.SprintBoard"/> — nada de rede aqui, e
    /// por isso dá para testar sem nenhuma.
    ///
    /// Três diferenças do GitHub que esta tradução precisa resolver, e que o
    /// [Plano_GitProject.md] já antecipava:
    ///
    /// 1. **Não existe hierarquia validada.** Sub-issue aceita qualquer coisa embaixo de qualquer
    ///    coisa. O nível vem do Issue Type (só em organização) ou de um campo do board; sem
    ///    nenhum dos dois, vale a POSIÇÃO na árvore, como no modo local.
    /// 2. **Estado é do board, não da issue.** A issue só tem OPEN/CLOSED; a coluna ("Status")
    ///    é um campo do projeto. Usamos a coluna quando existe, e caímos no OPEN/CLOSED quando
    ///    não — assim um projeto sem Status ainda abre.
    /// 3. **Sprint é o título da iteração**, não um caminho. Vira o IterationPath do board, que
    ///    é o que a tela usa para recortar.
    /// </summary>
    public static class GitProjectBoardService
    {
        /// <summary>Estados quando o projeto não tem coluna "Status" configurada.</summary>
        public static readonly string[] FallbackStates = { "New", "Active", "Closed" };

        /// <summary>Sprints do projeto, no formato que o board usa (o título é a identidade).</summary>
        public static List<TfsImportService.SprintInfo> Sprints(GitProjectClient.ProjectSnapshot snap) =>
            (snap?.Iterations ?? new List<GitIteration>())
                .Where(i => !string.IsNullOrWhiteSpace(i.Title))
                .Select(i => new TfsImportService.SprintInfo(i.Title, i.Title, i.Start, i.End))
                .ToList();

        /// <summary>
        /// Projeta o projeto no board. <paramref name="iterationPaths"/> vazio = todas as
        /// sprints; com valores, filtra pelo título da iteração do item.
        /// </summary>
        public static TfsImportService.SprintBoard Build(GitProjectClient.ProjectSnapshot snap,
            GitProjectOptions options, IReadOnlyCollection<string>? iterationPaths = null)
        {
            var stories = new List<TfsImportService.SprintStoryRow>();
            var levelItems = new List<TfsImportService.SprintLevelItem>();
            var people = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
            if (snap == null || snap.Items.Count == 0)
                return new TfsImportService.SprintBoard(
                    (snap?.StatusOptions?.Count ?? 0) > 0 ? snap!.StatusOptions.ToList() : FallbackStates.ToList(),
                    stories, people.ToList());

            var porNumero = snap.Items.ToDictionary(i => i.Number);
            bool SprintOk(GitProjectItem i) => iterationPaths == null || iterationPaths.Count == 0
                || iterationPaths.Contains(i.IterationTitle ?? "", StringComparer.CurrentCultureIgnoreCase);

            // Níveis (Project/EPIC/Feature) entram como itens de nível, para as colunas de cima.
            foreach (var item in snap.Items)
            {
                var nivel = LevelKindOf(item, porNumero, options);
                if (nivel is "Feature" or "Epic" or "Project")
                    levelItems.Add(new TfsImportService.SprintLevelItem(
                        item.Number, item.Title, nivel, StateOf(item), item.Assignee)
                    { IterationPath = item.IterationTitle ?? "", Tags = item.Labels ?? "" });
            }

            foreach (var story in snap.Items.Where(i => LevelKindOf(i, porNumero, options) == "Story"))
            {
                if (!SprintOk(story)) continue;
                var tasks = snap.Items
                    .Where(t => t.ParentNumber == story.Number
                                && LevelKindOf(t, porNumero, options) == "Task")
                    .Select(ToCard)
                    .ToList();

                var feature = Parent(story, porNumero);
                var epic = feature != null ? Parent(feature, porNumero) : null;
                var proj = epic != null ? Parent(epic, porNumero) : null;

                var row = new TfsImportService.SprintStoryRow(story.Number, story.Title, StateOf(story),
                    story.Assignee, tasks)
                {
                    IterationPath = story.IterationTitle ?? "",
                    Tags = story.Labels ?? "",
                    StackRank = double.NaN,
                    FeatureId = feature?.Number ?? 0,
                    FeatureTitle = feature?.Title ?? "",
                    FeatureAssignedTo = feature?.Assignee ?? "",
                    FeatureState = feature != null ? StateOf(feature) : "",
                    FeatureIterationPath = feature?.IterationTitle ?? "",
                    FeatureEpicId = epic?.Number ?? 0,
                    FeatureEpicTitle = epic?.Title ?? "",
                    FeatureEpicState = epic != null ? StateOf(epic) : "",
                    FeatureProjectId = proj?.Number ?? 0,
                    FeatureProjectTitle = proj?.Title ?? "",
                    FeatureProjectState = proj != null ? StateOf(proj) : ""
                };
                stories.Add(row);

                if (!string.IsNullOrWhiteSpace(row.AssignedTo)) people.Add(row.AssignedTo);
                foreach (var c in tasks)
                    if (!string.IsNullOrWhiteSpace(c.AssignedTo)) people.Add(c.AssignedTo);
            }

            // Colunas: as opções do campo Status DO PROJETO, na ordem em que ele as define —
            // é esse o fluxo do time ali ("Todo / In Progress / Done" ou o que tiverem criado).
            // O conjunto fixo do NXProject só vale para projeto sem Status; misturar os dois
            // enchia o board de colunas que não existem no GitHub.
            var doProjeto = (snap.StatusOptions ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            var estados = (doProjeto.Count > 0 ? doProjeto : FallbackStates.ToList())
                // Estado que aparece num item mas não está na lista entra no fim: card sem
                // coluna é card que some do board.
                .Concat(stories.Select(s => s.State))
                .Concat(stories.SelectMany(s => s.Tasks).Select(t => t.State))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            return new TfsImportService.SprintBoard(estados, stories, people.ToList())
            { LevelItems = levelItems };
        }

        /// <summary>
        /// Que nível o item ocupa. Ordem de decisão, da mais confiável para a mais frouxa:
        /// Issue Type (organização), campo de nível do board, e por fim a POSIÇÃO na árvore de
        /// sub-issues — igual ao modo local, onde o cronograma também não guarda "tipo".
        ///
        /// A posição é o último recurso de propósito: o GitHub não valida nada, então deduzir
        /// pela árvore é a única leitura que sempre existe.
        /// </summary>
        public static string LevelKindOf(GitProjectItem item, IReadOnlyDictionary<int, GitProjectItem> porNumero,
                                         GitProjectOptions options)
        {
            if (Normalize(item.IssueType) is { Length: > 0 } tipo) return tipo;
            if (Normalize(item.LevelValue) is { Length: > 0 } nivel) return nivel;

            // Pai FORA do projeto é normal: a sub-issue pode pendurar num EPIC de ideias, num
            // outro Work Item Project, em qualquer coisa — e nada obriga esse pai a estar aqui.
            // Mas aí a profundidade na árvore não quer dizer nada: contar daria ZERO e o item
            // viraria "Project", uma coluna de topo inventada a partir de um buraco.
            //
            // Sem como deduzir, vale Story: é a folha do cronograma no NXProject, o nível em que
            // o item aparece como linha do board. Errar para Story mostra o trabalho no lugar
            // mais provável; errar para Project muda a cara do board inteiro.
            if (item.ParentNumber > 0 && Parent(item, porNumero) == null) return "Story";

            var profundidade = 0;
            var atual = item;
            while (Parent(atual, porNumero) is { } pai && profundidade < 10) { atual = pai; profundidade++; }
            return profundidade switch { 0 => "Project", 1 => "Epic", 2 => "Feature", 3 => "Story", _ => "Task" };
        }

        /// <summary>
        /// Nome do nível vindo do GitHub para o vocabulário do NXProject. Aceita o que o time
        /// escreveu — "História", "User Story", "Tarefa" — porque o campo é texto livre do board
        /// e ninguém vai batizar igual ao DevOps.
        /// </summary>
        private static string Normalize(string? valor)
        {
            // Os nomes saem do mapa de niveis, que e configuravel e vale para os dois destinos:
            // o time que batizou o Issue Type de "Entrega" ou "Iniciativa" ajusta na tela em vez
            // de ficar fora. Antes esta lista era fixa aqui, e divergia da do DevOps.
            return NxLevelMap.Current.LevelOf(valor) is { } nivel ? nivel.ToString() : "";
        }

        private static GitProjectItem? Parent(GitProjectItem item, IReadOnlyDictionary<int, GitProjectItem> porNumero) =>
            item.ParentNumber > 0 && porNumero.TryGetValue(item.ParentNumber, out var pai) ? pai : null;

        /// <summary>
        /// Estado do card: a coluna do board (campo Status) quando o projeto tem uma, senão o
        /// OPEN/CLOSED da própria issue. Projeto sem Status continua abrindo, com três colunas.
        /// </summary>
        public static string StateOf(GitProjectItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.Status)) return item.Status.Trim();
            return string.Equals(item.IssueState, "CLOSED", StringComparison.OrdinalIgnoreCase) ? "Closed" : "New";
        }

        private static TfsImportService.SprintTaskCard ToCard(GitProjectItem item) =>
            new(item.Number, item.Title, StateOf(item), item.Assignee,
                item.Effort is { } e ? e.ToString("0.##") : "",
                item.ParentNumber, item.Labels ?? "", item.ClosedAt, 0, double.NaN)
            {
                IterationPath = item.IterationTitle ?? "",
                EstimateHours = item.Effort,
                CompletedHours = item.CurrentHours,
                // O card do board so carrega a data ALVO; a de inicio e lida quando a atividade
                // e aberta, como ja acontece no DevOps.
                FinishDate = item.FinishDate
            };
    }
}
