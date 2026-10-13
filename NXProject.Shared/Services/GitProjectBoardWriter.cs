// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NXProject.Services
{
    /// <summary>
    /// Grava no board do GitHub a fila de pendências do TaskBoard — o "Salvar (N)".
    ///
    /// Recebe a MESMA <see cref="LocalBoardWriter.Pending"/> do modo local, de propósito: a tela
    /// monta uma fila só e não aprende um formato por destino. Antes deste arquivo, o Salvar com
    /// o board em GitProject caía no caminho do Azure DevOps e devolvia um erro que não fazia
    /// sentido nenhum para quem estava olhando ("Could not find workitemdataset").
    ///
    /// O que o GitHub não tem, vira AVISO em vez de erro: prioridade e exclusão de item não
    /// existem como no DevOps, e perder a gravação inteira por causa delas seria pior.
    /// </summary>
    public static class GitProjectBoardWriter
    {
        public static async Task<LocalBoardWriter.Result> ApplyAsync(
            GitProjectOptions options, LocalBoardWriter.Pending pend, IProgress<string>? progress = null)
        {
            var problemas = new List<string>();
            var aplicados = 0;
            if (options?.IsConfigured != true)
                return new LocalBoardWriter.Result(0, new[] {
                    NxText.T("Conexão do GitProject incompleta.", "Incomplete GitProject connection.") });

            var cliente = new GitProjectClient(options);
            progress?.Report(NxText.T("Lendo o board…", "Reading the board…"));
            var snap = await cliente.LoadAsync();
            var porNumero = snap.Items.ToDictionary(i => i.Number);

            GitProjectField? Campo(IEnumerable<string> nomes) =>
                snap.Fields.FirstOrDefault(f => GitProjectOptions.Matches(f.Name, nomes));

            var semCampo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Grava um valor de campo do board, avisando UMA vez quando o campo não existe: a
            // causa é a mesma para todos os itens, e repetir encheria a tela.
            async Task<bool> GravaCampo(GitProjectItem item, IEnumerable<string> nomes, string conceito, object valor)
            {
                var campo = Campo(nomes);
                if (campo == null)
                {
                    if (semCampo.Add(conceito))
                        problemas.Add(NxText.T(
                            "O board não tem o campo de {0} ({1}): esse valor não foi gravado.",
                            "The board has no {0} field ({1}): that value was not written.",
                            conceito, GitProjectOptions.Suggested(nomes.ToArray())));
                    return false;
                }
                await cliente.SetFieldAsync(snap.ProjectId, item.ItemId, campo.Id, valor);
                return true;
            }

            async Task Para(int id, Func<GitProjectItem, Task> acao, string oQue)
            {
                if (!porNumero.TryGetValue(id, out var item))
                {
                    problemas.Add(NxText.T("#{0} não está neste board — {1} não foi gravado.",
                                           "#{0} is not on this board — {1} was not written.", id, oQue));
                    return;
                }
                try
                {
                    progress?.Report($"#{id}: {oQue}…");   // o rótulo já vem traduzido
                    await acao(item);
                    aplicados++;
                }
                catch (Exception ex)
                {
                    problemas.Add($"#{id}, {oQue}: {ex.Message}");   // nomes e mensagem do servidor
                }
            }

            // ── Estado: a COLUNA do board, mais abrir/fechar a issue ───────────────────────
            //
            // São duas coisas independentes no GitHub, e o board mostra as duas: a coluna é o
            // fluxo do time (Todo / In Progress / Done) e o aberto/fechado é da issue. Mover para
            // uma coluna de encerramento sem fechar a issue deixaria o item "pronto" no board e
            // aberto no repositório.
            var status = Campo(new[] { "Status" });
            foreach (var (id, estado) in pend.States)
                await Para(id, async item =>
                {
                    if (status != null && status.Options.TryGetValue(estado ?? "", out var opcao))
                        await cliente.SetFieldAsync(snap.ProjectId, item.ItemId, status.Id, new GitSingleSelect(opcao));
                    else if (status != null)
                        problemas.Add(NxText.T("#{0}: o board não tem a coluna '{1}'.",
                                               "#{0}: the board has no '{1}' column.", id, estado));

                    var fechar = Encerrado(estado);
                    if (fechar != string.Equals(item.IssueState, "CLOSED", StringComparison.OrdinalIgnoreCase))
                        await cliente.SetIssueStateAsync(item.IssueId, fechar);
                }, NxText.T("estado", "state"));

            // ── Título ────────────────────────────────────────────────────────────────────
            foreach (var (id, titulo) in pend.Titles)
                await Para(id, item => cliente.SetIssueTitleAsync(item.IssueId, titulo ?? ""),
                               NxText.T("título", "title"));

            // ── Horas e datas: campos do board ────────────────────────────────────────────
            foreach (var (id, horas) in pend.EstimateHours)
                await Para(id, async item =>
                {
                    if (horas is { } h)
                        await GravaCampo(item, options.EffortFields, NxText.T("HH estimado", "estimated hours"), h);
                }, NxText.T("HH estimado", "estimated hours"));

            foreach (var (id, horas) in pend.CompletedHours)
                await Para(id, async item =>
                {
                    if (horas is { } h)
                        await GravaCampo(item, options.CurrentHoursFields, NxText.T("HH realizado", "completed hours"), h);
                }, NxText.T("HH realizado", "completed hours"));

            foreach (var (id, data) in pend.StartDates)
                await Para(id, async item =>
                {
                    if (data is { } d)
                        await GravaCampo(item, options.StartDateFields, NxText.T("data de início", "start date"), d);
                }, NxText.T("data de início", "start date"));

            foreach (var (id, data) in pend.FinishDates)
                await Para(id, async item =>
                {
                    if (data is { } d)
                        await GravaCampo(item, options.FinishDateFields, NxText.T("data de fim", "finish date"), d);
                }, NxText.T("data de fim", "finish date"));

            // ── Responsável ───────────────────────────────────────────────────────────────
            //
            // Precisa das contas do repositório: no GitHub só colaborador pode ser responsável.
            // Sem repositório configurado, nem há lista de candidatos — e atribuir por nome seria
            // palpite, que é o que a regra de pessoas evita.
            if (pend.Owners.Count > 0)
            {
                var partes = (options.Repository ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
                var contas = partes.Length == 2
                    ? await cliente.AssignableAccountsAsync(partes[0].Trim(), partes[1].Trim())
                    : new List<GitProjectPeopleService.Account>();

                if (contas.Count == 0)
                    problemas.Add(NxText.T(
                        "Responsável não foi gravado: informe o repositório (dono/repo) na configuração do "
                        + "GitProject — é dele que sai a lista de quem pode ser responsável.",
                        "Assignee was not written: set the repository (owner/repo) in the GitProject settings — "
                        + "that is where the list of possible assignees comes from."));
                else
                    foreach (var (id, dono) in pend.Owners)
                        await Para(id, async item =>
                        {
                            if (string.IsNullOrWhiteSpace(dono)) { await cliente.SetAssigneeAsync(item.IssueId, null); return; }
                            var r = GitProjectPeopleService.Resolve(dono, contas);
                            if (!r.Ok) { problemas.Add($"#{id}: {r.Problem}"); return; }
                            await cliente.SetAssigneeAsync(item.IssueId, r.Account!.Id);
                        }, NxText.T("responsável", "assignee"));
            }

            // ── Tags = labels da issue ────────────────────────────────────────────────────
            foreach (var (id, tags) in pend.Tags)
                await Para(id, async item =>
                {
                    var repo = await cliente.IssueRepositoryAsync(item.IssueId);
                    var p = (repo ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length != 2)
                    {
                        problemas.Add(NxText.T("#{0}: não descobri o repositório da issue.",
                                               "#{0}: could not find the issue's repository.", id));
                        return;
                    }
                    var lista = (tags ?? "").Split(';', ',')
                        .Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
                    await cliente.SetLabelsAsync(p[0], p[1], item.Number, lista);
                }, NxText.T("tags", "tags"));

            // ── Sprint ────────────────────────────────────────────────────────────────────
            var campoIteracao = snap.Fields.FirstOrDefault(f =>
                GitProjectOptions.Matches(f.Name, options.IterationFields));
            foreach (var (id, sprint) in pend.IterationPaths)
                await Para(id, async item =>
                {
                    var it = snap.Iterations.FirstOrDefault(x =>
                        string.Equals(x.Title?.Trim(), (sprint ?? "").Trim(), StringComparison.CurrentCultureIgnoreCase));
                    if (campoIteracao == null || it == null)
                    {
                        problemas.Add(NxText.T("#{0}: sprint '{1}' não existe neste board.",
                                               "#{0}: sprint '{1}' does not exist on this board.", id, sprint));
                        return;
                    }
                    await cliente.SetIterationAsync(snap.ProjectId, item.ItemId, campoIteracao.Id, it.Id);
                }, NxText.T("sprint", "sprint"));

            // ── Mover de pai (arrastar a Task para outra Story) ───────────────────────────
            //
            // No GitHub não existe "trocar o pai": é desligar do atual e ligar no novo. A ordem
            // importa — ligar antes de desligar deixaria a issue com dois pais por um instante, e
            // o GitHub recusa a segunda ligação.
            foreach (var (id, novoPai) in pend.NewParents)
                await Para(id, async item =>
                {
                    if (!porNumero.TryGetValue(novoPai, out var pai))
                    {
                        problemas.Add(NxText.T("#{0}: a Story #{1} não está neste board.",
                                               "#{0}: Story #{1} is not on this board.", id, novoPai));
                        return;
                    }

                    if (!GitProjectSyncService.HierarchyOk(
                            GitProjectBoardService.LevelKindOf(pai, porNumero, options),
                            GitProjectBoardService.LevelKindOf(item, porNumero, options)))
                    {
                        // A validação que o DevOps faz por nós: aqui ela é nossa, e acontece
                        // antes de desligar o item do pai que ele já tinha.
                        problemas.Add(NxText.T("#{0}: #{1} não pode ser pai deste item.",
                                               "#{0}: #{1} cannot be this item's parent.", id, novoPai));
                        return;
                    }

                    if (item.ParentNumber > 0 && porNumero.TryGetValue(item.ParentNumber, out var antigo))
                        await cliente.RemoveSubIssueAsync(antigo.IssueId, item.IssueId);
                    await cliente.AddSubIssueAsync(pai.IssueId, item.IssueId);
                }, NxText.T("mudança de pai", "parent change"));

            // ── O que o GitHub não faz como o DevOps ──────────────────────────────────────
            if (pend.Priorities.Count > 0)
                problemas.Add(NxText.T(
                    "Prioridade não foi gravada: no GitHub ela não é campo do item. Crie um campo de número "
                    + "no board se precisar dela.",
                    "Priority was not written: on GitHub it is not an item field. Create a number field on the "
                    + "board if you need it."));
            if (pend.Deletes.Count > 0)
                problemas.Add(NxText.T(
                    "{0} exclusão(ões) não foram feitas: apagar issue é irreversível e exige permissão de "
                    + "administrador. Feche o item em vez de excluí-lo.",
                    "{0} deletion(s) were not performed: deleting an issue is irreversible and requires admin "
                    + "permission. Close the item instead of deleting it.", pend.Deletes.Count));

            return new LocalBoardWriter.Result(aplicados, problemas);
        }

        /// <summary>A coluna escolhida representa item encerrado?</summary>
        private static bool Encerrado(string? estado) =>
            (estado ?? "").Trim().ToLowerInvariant() is "closed" or "done" or "completed" or "resolved"
                or "encerrado" or "concluido" or "concluído";
    }
}
