// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NXProject.Models;

namespace NXProject.Services
{
    /// <summary>
    /// Traz um board do GitHub Projects para um cronograma do NXProject.
    ///
    /// A regra de cronograma NÃO mora aqui: HH virar dias úteis, encadear o que não tem data,
    /// recalcular pais — isso é do motor do NXProject, e continua sendo, exatamente como no
    /// import do DevOps. O que este serviço faz é **traduzir**: issue vira atividade, sub-issue
    /// vira hierarquia, campo do board vira HH e data.
    ///
    /// Três decisões que o GitHub obriga a tomar, e que ficam explícitas no relatório em vez de
    /// silenciosas:
    ///
    /// 1. **Hierarquia não é validada lá.** Task pode estar pendurada numa Feature. Aqui isso
    ///    vira AVISO e a atividade entra onde está — recusar a importação inteira por causa de
    ///    um item deixaria a pessoa sem nada, e o cronograma é justamente onde ela vai arrumar.
    /// 2. **Pai fora do board** é normal (um EPIC de ideias, outro portfólio). O item entra na
    ///    raiz, com aviso: inventar um pai seria pior.
    /// 3. **Estado é do board**, não da issue. Vem a coluna (Status); open/closed só decide
    ///    quando não há coluna.
    /// </summary>
    public static class GitProjectImportService
    {
        /// <summary>
        /// Monta o cronograma a partir do board. <paramref name="rootIssue"/> limita a um projeto
        /// dentro do board (o item marcado como Project); 0 traz o board inteiro.
        /// </summary>
        public static async Task<TfsImportService.ImportResult> ImportAsync(
            GitProjectOptions options, int rootIssue = 0, IProgress<string>? progress = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (!options.IsConfigured)
                throw new InvalidOperationException(NxText.T(
                    "Conexão do GitProject incompleta: informe dono, número do board e token.",
                    "Incomplete GitProject connection: provide owner, board number and token."));

            progress?.Report(NxText.T("Lendo o board no GitHub…", "Reading the board on GitHub…"));
            var snap = await new GitProjectClient(options).LoadAsync();
            var report = new TfsImportService.ImportReport();
            var projeto = new Project { Name = snap.Title };

            var porNumero = snap.Items.ToDictionary(i => i.Number);
            var itens = rootIssue > 0
                ? snap.Items.Where(i => i.Number == rootIssue || DescendeDe(i, rootIssue, porNumero)).ToList()
                : snap.Items.ToList();
            if (itens.Count == 0)
            {
                // Distinguir "board vazio" de "token sem permissão" importa: no segundo caso a
                // pessoa refaria o board sem necessidade, quando o conserto é o escopo do token.
                if (snap.InaccessibleItems > 0)
                    throw new InvalidOperationException(NxText.T(
                        "O board '{0}' tem {1} item(ns), mas o token não consegue abrir nenhum deles. As issues "
                        + "estão em repositório privado: gere o token clássico com o escopo 'repo' além de 'project'.",
                        "Board '{0}' has {1} item(s), but the token cannot open any of them. The issues live in a "
                        + "private repository: create a classic token with the 'repo' scope besides 'project'.",
                        snap.Title, snap.InaccessibleItems));
                report.LogWarning(rootIssue > 0
                    ? NxText.T("Nada a importar: o board '{0}' não tem itens sob #{1}.",
                               "Nothing to import: board '{0}' has no items under #{1}.", snap.Title, rootIssue)
                    : NxText.T("Nada a importar: o board '{0}' não tem itens.",
                               "Nothing to import: board '{0}' has no items.", snap.Title));
                return new TfsImportService.ImportResult(projeto, report);
            }
            if (snap.InaccessibleItems > 0)
                report.LogWarning(NxText.T(
                    "{0} item(ns) do board ficaram de fora: o token não enxerga o repositório deles (escopo 'repo').",
                    "{0} board item(s) were left out: the token cannot see their repository ('repo' scope).",
                    snap.InaccessibleItems));

            // Sprints do board viram as sprints do cronograma: é delas que sai o recorte por
            // sprint, e sem início/fim o "sprint atual" não existe.
            foreach (var it in snap.Iterations)
                projeto.Sprints.Add(new Sprint
                {
                    Number = projeto.Sprints.Count + 1,
                    DisplayName = it.Title,
                    Path = it.Title,     // no GitHub a sprint é o título; não há caminho
                    Start = it.Start,
                    End = it.End
                });

            // Ordem: pai antes do filho, para a árvore nascer montada. A ordem dentro do mesmo
            // pai é a que o board devolveu — é o mais próximo de "ordem do backlog" que o
            // GitHub oferece sem ler a view, que é outra consulta.
            var porPai = itens.ToLookup(i => PaiVisivel(i, porNumero, itens, rootIssue));
            var criados = new Dictionary<int, ProjectTask>();
            var semPai = new List<int>();

            void Monta(int paiNumero, ProjectTask? paiTarefa, int nivel)
            {
                foreach (var item in porPai[paiNumero])
                {
                    var tarefa = ToTask(item, porNumero, options, nivel, report);
                    AplicaResponsavel(projeto, tarefa, item);
                    criados[item.Number] = tarefa;
                    if (paiTarefa == null) projeto.Tasks.Add(tarefa);
                    else paiTarefa.Children.Add(tarefa);
                    Monta(item.Number, tarefa, nivel + 1);
                }
            }

            Monta(0, null, 0);   // 0 = raiz: item sem pai, ou com pai fora do recorte

            foreach (var item in itens)
            {
                if (criados.ContainsKey(item.Number)) continue;
                semPai.Add(item.Number);   // ciclo de sub-issues: raro, mas possível
            }
            if (semPai.Count > 0)
                report.LogWarning(NxText.T(
                    "{0} item(ns) não entraram por referência circular de sub-issue: {1}",
                    "{0} item(s) were skipped due to a circular sub-issue reference: {1}",
                    semPai.Count, string.Join(", ", semPai.Take(10).Select(n => "#" + n))));

            // Resumo: pai com filho é summary, e é o motor do NXProject que recalcula as datas
            // e o percentual a partir deles — nada disso é gravado pelo import.
            foreach (var t in Todas(projeto.Tasks))
                t.IsSummary = t.Children.Count > 0;

            report.LogInfo(NxText.T(
                "Board '{0}': {1} atividade(s), {2} sprint(s), {3} coluna(s) de estado.",
                "Board '{0}': {1} activity(ies), {2} sprint(s), {3} state column(s).",
                snap.Title, criados.Count, projeto.Sprints.Count, snap.StatusOptions.Count));
            progress?.Report(NxText.T("{0} atividade(s) importada(s).", "{0} activity(ies) imported.",
                                       criados.Count));
            return new TfsImportService.ImportResult(projeto, report);
        }

        /// <summary>
        /// Pai dentro do recorte. Fora dele — pai não carregado, ou acima da raiz escolhida — o
        /// item sobe para a raiz do cronograma, com aviso. É o caso do EPIC de ideias: a
        /// sub-issue aponta para algo que não faz parte deste projeto.
        /// </summary>
        private static int PaiVisivel(GitProjectItem item, IReadOnlyDictionary<int, GitProjectItem> porNumero,
                                      List<GitProjectItem> recorte, int rootIssue)
        {
            if (item.Number == rootIssue) return 0;
            if (item.ParentNumber <= 0) return 0;
            if (!porNumero.ContainsKey(item.ParentNumber)) return 0;
            return recorte.Any(x => x.Number == item.ParentNumber) ? item.ParentNumber : 0;
        }

        private static bool DescendeDe(GitProjectItem item, int raiz,
                                       IReadOnlyDictionary<int, GitProjectItem> porNumero)
        {
            var atual = item;
            for (var i = 0; i < 10; i++)
            {
                if (atual.ParentNumber <= 0) return false;
                if (atual.ParentNumber == raiz) return true;
                if (!porNumero.TryGetValue(atual.ParentNumber, out var pai)) return false;
                atual = pai;
            }
            return false;
        }

        /// <summary>
        /// Põe o responsável da issue como recurso da atividade — e guarda o LOGIN no recurso.
        ///
        /// É aqui que a identidade chega de graça: o GitHub devolve login e nome de quem está
        /// atribuído. Guardar o login agora é o que permite, no sync, gravar o responsável sem
        /// adivinhar por nome — que pode estar vazio, pode repetir e é digitado à mão.
        ///
        /// A pessoa é reconhecida PELO LOGIN quando ele já existe no cronograma; só quem nunca
        /// veio do GitHub é procurado por nome. Assim, trocar o nome de exibição no GitHub não
        /// cria uma segunda pessoa no cronograma.
        /// </summary>
        private static void AplicaResponsavel(Project projeto, ProjectTask tarefa, GitProjectItem item)
        {
            var login = (item.AssigneeLogin ?? "").Trim();
            var nome = string.IsNullOrWhiteSpace(item.Assignee) ? login : item.Assignee.Trim();
            if (nome.Length == 0) return;

            var recurso = projeto.Resources.FirstOrDefault(r =>
                    (login.Length > 0 && string.Equals(r.GitLogin, login, StringComparison.CurrentCultureIgnoreCase))
                    || string.Equals(r.Name, nome, StringComparison.CurrentCultureIgnoreCase));

            if (recurso == null)
            {
                recurso = new Resource
                {
                    Id = projeto.Resources.Select(r => r.Id).DefaultIfEmpty(0).Max() + 1,
                    Name = nome,
                    GitLogin = login.Length > 0 ? login : null,
                    MaxUnitsPerDay = ProjectCalendarService.WorkingHoursPerDay,
                    IsImportedFromTfs = true   // veio do servidor, não foi digitada no cronograma
                };
                projeto.Resources.Add(recurso);
            }
            // Pessoa que já existia sem login (veio do cronograma ou de um import antigo) passa a
            // ter o dela: é o que torna o sync de responsável possível sem perguntar nada.
            else if (login.Length > 0 && string.IsNullOrWhiteSpace(recurso.GitLogin))
                recurso.GitLogin = login;

            tarefa.Resources.Add(new TaskResource
            {
                ResourceId = recurso.Id,
                Resource = recurso,
                AllocationPercent = 100
            });
        }

        private static IEnumerable<ProjectTask> Todas(IEnumerable<ProjectTask> tarefas)
        {
            foreach (var t in tarefas)
            {
                yield return t;
                foreach (var f in Todas(t.Children)) yield return f;
            }
        }

        private static ProjectTask ToTask(GitProjectItem item,
            IReadOnlyDictionary<int, GitProjectItem> porNumero, GitProjectOptions options,
            int nivel, TfsImportService.ImportReport report)
        {
            var tipo = GitProjectBoardService.LevelKindOf(item, porNumero, options);
            var tipoNx = tipo switch
            {
                "Story" => "User Story",
                _ => tipo
            };
            var estado = GitProjectBoardService.StateOf(item);
            // Encerrada: a issue fechada no GitHub, ou a coluna do board sendo de encerramento.
            // Os dois existem e sao independentes la — e o import tem de respeitar ambos.
            var encerrada = string.Equals(item.IssueState, "CLOSED", StringComparison.OrdinalIgnoreCase)
                            || estado.Equals("Closed", StringComparison.OrdinalIgnoreCase)
                            || estado.Equals("Done", StringComparison.OrdinalIgnoreCase)
                            || estado.Equals("Completed", StringComparison.OrdinalIgnoreCase);

            // Hierarquia que o GitHub permitiu e o NXProject não valida: entra, mas avisa — o
            // cronograma e o lugar onde a pessoa arruma, e recusar tudo a deixaria sem nada.
            if (porNumero.TryGetValue(item.ParentNumber, out var pai))
            {
                var tipoPai = GitProjectBoardService.LevelKindOf(pai, porNumero, options);
                if (!HierarquiaValida(tipoPai, tipo, pai.EpicType, item.EpicType))
                    report.LogWarning(NxText.T(
                        "#{0} ({1}) está sob #{2} ({3}), hierarquia que o NXProject não usa. Importado como está "
                        + "— ajuste no cronograma.",
                        "#{0} ({1}) sits under #{2} ({3}), a hierarchy NXProject does not use. Imported as is — "
                        + "fix it in the schedule.",
                        item.Number, tipo, pai.Number, tipoPai));
            }
            else if (item.ParentNumber > 0)
                report.LogWarning(NxText.T(
                    "#{0}: o pai #{1} não está neste board. Importado na raiz.",
                    "#{0}: parent #{1} is not on this board. Imported at the root.",
                    item.Number, item.ParentNumber));

            return new ProjectTask
            {
                Id = item.Number,
                Name = item.Title,
                Level = nivel,
                IsSummary = false,
                IsMilestone = false,
                // Datas e HH vêm dos campos do board. Sem eles, o motor do NXProject calcula —
                // é a mesma regra do import do DevOps.
                Start = item.StartDate ?? default,
                Finish = item.FinishDate ?? default,
                EstimatedHours = encerrada ? 0 : item.Effort ?? 0,
                OriginalEstimatedHours = item.Effort,
                CurrentHours = item.CurrentHours,
                PercentComplete = item.Percent ?? (encerrada ? 100 : 0),
                // Revisão do servidor no mesmo campo em que o DevOps guarda o Rev: é contra ela
                // que o próximo sync vai dizer "alguém mexeu depois de você".
                SyncVersion = GitProjectSyncService.Revision(item.UpdatedAt),
                TfsId = item.Number,
                TfsParentId = item.ParentNumber > 0 ? item.ParentNumber : null,
                TfsType = tipoNx,
                // EPIC_TYPE vem junto: é ele que distingue o EPIC agrupador no cronograma, no
                // board e na validação de hierarquia.
                EpicType = string.IsNullOrWhiteSpace(item.EpicType) ? null : item.EpicType.Trim(),
                TfsState = estado,
                Tags = item.Labels,
                TfsIterationPath = item.IterationTitle,
                StartFixed = item.StartDate.HasValue,
                FinishFixed = false,
                Notes = $"GitHub #{item.Number} · {tipo} · {estado}"
                    + (string.IsNullOrWhiteSpace(item.Assignee) ? "" : $" · {item.Assignee}")
            };
        }

        /// <summary>
        /// A hierarquia que o NXProject usa. O DevOps recusa o que foge disso; o GitHub aceita
        /// tudo, então a verificação passa a ser nossa.
        /// </summary>
        private static bool HierarquiaValida(string pai, string filho,
                                             string? epicTypePai = null, string? epicTypeFilho = null) =>
            GitProjectSyncService.HierarchyOk(pai, filho, epicTypePai, epicTypeFilho);
    }
}
