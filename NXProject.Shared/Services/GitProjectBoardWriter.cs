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

            // Campo que não existe no board: UM aviso por conceito, no fim, com a lista dos
            // cards afetados. Era um aviso por conceito sem os ids — e quem tinha vários cards
            // na fila não sabia qual deles tinha ficado para trás.
            var semCampo = new Dictionary<string, (string Sugestao, List<int> Itens)>(
                StringComparer.OrdinalIgnoreCase);

            async Task<bool> GravaCampo(GitProjectItem item, IEnumerable<string> nomes, string conceito, object valor)
            {
                var campo = Campo(nomes);
                if (campo == null)
                {
                    if (!semCampo.TryGetValue(conceito, out var falta))
                        semCampo[conceito] = falta =
                            (GitProjectOptions.Suggested(nomes.ToArray()), new List<int>());
                    if (!falta.Itens.Contains(item.Number)) falta.Itens.Add(item.Number);
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

            foreach (var (id, perc) in pend.Percents)
                await Para(id, async item =>
                {
                    if (perc is { } p)
                        await GravaCampo(item, options.PercentFields,
                            NxText.T("percentual de conclusão", "percent complete"), Math.Clamp(p, 0, 100));
                }, NxText.T("percentual de conclusão", "percent complete"));

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

            // ── Descrição = CORPO da issue ────────────────────────────────────────────────
            foreach (var (id, texto) in pend.Descriptions)
                await Para(id, item => cliente.SetIssueBodyAsync(item.IssueId, texto ?? ""),
                               NxText.T("descrição", "description"));

            // ── Critério de aceite: uma SEÇÃO do corpo ────────────────────────────────────
            //
            // O GitHub não tem esse campo. Em vez de inventar um campo no board (que ninguém mais
            // leria) ou perder o texto, ele vira uma seção marcada no corpo da issue — reescrita
            // no lugar quando muda, preservando o resto do que estiver escrito ali.
            foreach (var (id, texto) in pend.AcceptanceCriteria)
                await Para(id, async item =>
                {
                    var corpo = await cliente.IssueBodyAsync(item.IssueId);
                    await cliente.SetIssueBodyAsync(item.IssueId, ComSecaoDeAceite(corpo, texto ?? ""));
                }, NxText.T("critério de aceite", "acceptance criteria"));

            // ── Ordem do board ────────────────────────────────────────────────────────────
            //
            // No GitHub a ordem é a posição do item na lista do projeto, movida uma a uma — não
            // há campo de rank para gravar. Os itens pedidos são encadeados na ordem desejada:
            // cada um vai para logo depois do anterior.
            if (pend.Ranks.Count > 0)
            {
                var naOrdem = pend.Ranks.Where(kv => porNumero.ContainsKey(kv.Key))
                                        .OrderBy(kv => kv.Value)
                                        .Select(kv => porNumero[kv.Key])
                                        .ToList();
                string? anterior = null;
                foreach (var item in naOrdem)
                {
                    var alvo = anterior;   // captura: o laço segue adiante
                    await Para(item.Number, async i =>
                    {
                        await cliente.MoveItemAsync(snap.ProjectId, i.ItemId, alvo);
                    }, NxText.T("ordem", "order"));
                    anterior = item.ItemId;
                }
            }

            // ── Campos que o board não tem ────────────────────────────────────────────────
            // O aviso diz o que falta, onde faltou e O QUE FAZER: o NXProject cria o campo pela
            // tela de configuracao, e sem essa frase a pessoa so sabia que o valor se perdeu.
            // A frase comeca pela CAUSA: falta um campo NO PROJETO DO GITHUB. Dizer "o valor nao
            // foi gravado" primeiro fazia parecer que o problema era o valor digitado — e ele
            // estava certo; o que nao existe e o lugar onde guarda-lo.
            foreach (var (conceito, falta) in semCampo)
                problemas.Add(NxText.T(
                    "FALTA UM CAMPO NO PROJETO '{0}' DO GITHUB: '{1}' (o {2}) não existe lá, então o valor "
                    + "de {3} ficou sem onde ser gravado. Crie o campo em Configuração → Nomes dos campos "
                    + "→ GitProject → \"Criar campos no board\" e salve de novo.",
                    "A FIELD IS MISSING IN GITHUB PROJECT '{0}': '{1}' (the {2}) does not exist there, so the "
                    + "value of {3} had nowhere to be written. Create it in Settings → Field names "
                    + "→ GitProject → \"Create fields on the board\" and save again.",
                    snap.Title, falta.Sugestao, conceito, Cards(falta.Itens)));

            // Prioridade: no GitHub nao ha campo nativo, entao ela vive num campo de numero do
            // board como qualquer outro. Faltando o campo, o aviso e o mesmo dos demais — diz
            // qual campo criar e onde — em vez do antigo "no GitHub isso nao existe".
            foreach (var (id, prio) in pend.Priorities)
                await Para(id, async item =>
                    await GravaCampo(item, options.PriorityFields,
                        NxText.T("prioridade", "priority"), (double)prio),
                    NxText.T("prioridade", "priority"));
            // ── Exclusão ─────────────────────────────────────────────────────────────────
            //
            // Apagar issue é irreversível e o GitHub só deixa quem administra o repositório. A
            // tentativa é feita — a tela já confirmou com a pessoa —, e a recusa vem de lá, com
            // a sugestão de fechar o item, que é o caminho normal no GitHub.
            foreach (var id in pend.Deletes)
                await Para(id, async item =>
                {
                    try
                    {
                        // ANEXOS PRIMEIRO. No DevOps o anexo morre com o work item; aqui ele e um
                        // arquivo do repositorio e sobreviveria a issue, numa pasta que ninguem
                        // mais alcanca. Apagar antes garante que nao fica lixo se a exclusao da
                        // issue falhar depois — o item continua la, e os anexos podem ser
                        // reenviados; o contrario deixaria orfaos permanentes.
                        var repoAnexo = (item.Repository ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
                        if (repoAnexo.Length == 2)
                        {
                            var (apagados, erroAnexo) = await GitProjectAttachmentService
                                .DeleteAllOfItemAsync(options, repoAnexo[0], repoAnexo[1], id);
                            if (erroAnexo != null)
                                problemas.Add(NxText.T(
                                    "#{0}: os anexos não foram apagados ({1}) — os arquivos continuam em {2}.",
                                    "#{0}: attachments were not deleted ({1}) — the files remain in {2}.",
                                    id, erroAnexo, GitProjectAttachmentService.Root));
                        }

                        await cliente.DeleteIssueAsync(item.IssueId);
                    }
                    catch (Exception ex)
                    {
                        problemas.Add(NxText.T(
                            "#{0} não foi excluído: {1} Apagar issue exige permissão de administrador do "
                            + "repositório — fechar o item costuma ser o caminho.",
                            "#{0} was not deleted: {1} Deleting an issue requires repository admin permission "
                            + "— closing the item is usually the way.", id, ex.Message));
                    }
                }, NxText.T("exclusão", "deletion"));

            return new LocalBoardWriter.Result(aplicados, problemas);
        }

        /// <summary>
        /// Lista de cards para a mensagem: "#101, #103". Acima de dez, corta e diz quantos
        /// sobraram — aviso que vira parede de números não é lido.
        /// </summary>
        private static string Cards(IEnumerable<int> ids)
        {
            var lista = ids.Distinct().OrderBy(x => x).ToList();
            if (lista.Count == 0) return "-";
            var mostra = string.Join(", ", lista.Take(10).Select(x => "#" + x));
            return lista.Count <= 10 ? mostra
                : NxText.T($"{mostra} e mais {lista.Count - 10}", $"{mostra} and {lista.Count - 10} more");
        }

        /// <summary>Marcador da seção de critério de aceite dentro do corpo da issue.</summary>
        private const string TituloAceite = "## " + "Critério de aceite";

        /// <summary>
        /// Devolve o corpo com a seção de critério de aceite no lugar: substitui a que existir
        /// (até o próximo título de nível 2) e, se não existir, acrescenta no fim. Critério
        /// vazio remove a seção.
        /// </summary>
        public static string ComSecaoDeAceite(string? corpo, string criterio)
        {
            var texto = (corpo ?? "").Replace("\r\n", "\n");
            var inicio = texto.IndexOf(TituloAceite, StringComparison.OrdinalIgnoreCase);

            if (inicio >= 0)
            {
                var depois = texto.IndexOf("\n## ", inicio + TituloAceite.Length, StringComparison.Ordinal);
                var fim = depois < 0 ? texto.Length : depois + 1;
                texto = texto.Remove(inicio, fim - inicio).TrimEnd();
            }

            if (string.IsNullOrWhiteSpace(criterio)) return texto.Trim();
            var secao = TituloAceite + "\n\n" + criterio.Trim();
            return string.IsNullOrWhiteSpace(texto) ? secao : texto.TrimEnd() + "\n\n" + secao;
        }

        /// <summary>
        /// O inverso de <see cref="ComSecaoDeAceite"/>: devolve (descrição, critério de aceite)
        /// a partir do corpo da issue. Ficam no mesmo arquivo de propósito — são a mesma regra
        /// lida nos dois sentidos, e separá-las é a forma conhecida de uma divergir da outra.
        /// </summary>
        public static (string Descricao, string Aceite) SeparaAceiteDoCorpo(string? corpo)
        {
            var texto = (corpo ?? "").Replace("\r\n", "\n");
            var inicio = texto.IndexOf(TituloAceite, StringComparison.OrdinalIgnoreCase);
            if (inicio < 0) return (texto.Trim(), "");

            var depoisDoTitulo = inicio + TituloAceite.Length;
            var proximaSecao = texto.IndexOf("\n## ", depoisDoTitulo, StringComparison.Ordinal);
            var fim = proximaSecao < 0 ? texto.Length : proximaSecao;

            var aceite = texto[depoisDoTitulo..fim].Trim();
            var descricao = (texto[..inicio] + (proximaSecao < 0 ? "" : texto[fim..])).Trim();
            return (descricao, aceite);
        }

        /// <summary>A coluna escolhida representa item encerrado?</summary>
        private static bool Encerrado(string? estado) =>
            (estado ?? "").Trim().ToLowerInvariant() is "closed" or "done" or "completed" or "resolved"
                or "encerrado" or "concluido" or "concluído";
    }
}
