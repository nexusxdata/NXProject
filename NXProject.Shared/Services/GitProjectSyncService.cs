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
    /// Devolve o planejamento do cronograma para o board do GitHub.
    ///
    /// O desenho segue o [Plano_GitProject_Import_Sync.md] e a ordem que ele recomenda: campos de
    /// item que já existe primeiro (risco baixo, valor alto), criação de item com validação de
    /// hierarquia depois, e a ORDEM DO BACKLOG fora — no GitHub ela é a posição na view, não um
    /// campo, e gravá-la é a parte mais frágil da API. O relatório diz isso em vez de fingir.
    ///
    /// Três diferenças em relação ao sync do DevOps que não são detalhe:
    ///
    /// 1. **O valor é do BOARD, não da issue.** Grava-se no item do projeto; a mesma issue em
    ///    outro board tem outro HH.
    /// 2. **Ninguém valida hierarquia.** O que o DevOps recusava por nós ("Task só grava sob
    ///    Story") passa a ser recusa daqui, antes de escrever.
    /// 3. **Não existe `Sync_version` nativo.** Ele vira campo do board. Sem ele, não há como
    ///    saber que outra pessoa mexeu depois — e o relatório avisa, em vez de sobrescrever em
    ///    silêncio dando a impressão de que havia controle.
    ///
    /// A separação entre <see cref="Build"/> (decide) e <see cref="SyncAsync"/> (grava) é o que
    /// torna a regra testável sem rede: os testes montam um board de mentira e conferem o que
    /// seria escrito.
    /// </summary>
    public static class GitProjectSyncService
    {
        /// <summary>Uma gravação pendente: em que atividade, que conceito, que valor.</summary>
        public sealed record Op(ProjectTask Task, int Number, string Key, object Value);

        /// <summary>O que o sync faria. Nada aqui tocou o GitHub ainda.</summary>
        public sealed class SyncPlan
        {
            public List<Op> Updates { get; } = new();
            public List<ProjectTask> Creates { get; } = new();
            /// <summary>Itens que outra pessoa mudou depois: não são gravados.</summary>
            public List<ProjectTask> Conflicts { get; } = new();
            public List<string> Warnings { get; } = new();
            public List<string> Errors { get; } = new();
            /// <summary>Atividades sem nada a fazer.</summary>
            public int Unchanged { get; set; }
        }

        /// <summary>
        /// A revisão do servidor como NÚMERO, para caber no mesmo `Sync_version` que o DevOps usa
        /// para guardar o `Rev`.
        ///
        /// Segundos desde 2000 — e não o tempo Unix — porque o campo do cronograma é `int`: o
        /// Unix estoura em 2038, esta contagem só em 2068, e segundo a segundo é granularidade
        /// mais que suficiente para dizer "alguém mexeu depois de você".
        ///
        /// A ideia é sua: em vez de o NXProject manter um contador próprio, ele ESPELHA a
        /// revisão do servidor. Assim a comparação passa a valer também para quem editou pela
        /// tela do GitHub, que é justamente quem um contador nosso não veria.
        /// </summary>
        public static int Revision(DateTime? quando) => quando is not { } dt ? 0
            : (int)Math.Max(0, (dt.ToUniversalTime() - new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds);

        /// <summary>
        /// A hierarquia do NXProject. No DevOps o servidor recusa o que foge disto; aqui a recusa
        /// é nossa, e acontece ANTES de escrever — criar a issue e só então descobrir que o pai
        /// é inválido deixaria lixo no repositório de alguém.
        /// </summary>
        public static bool HierarchyOk(string? pai, string? filho) =>
            NxLevelMap.Current.ParentChildOk(pai, filho);

        /// <summary>
        /// A mesma pergunta, enxergando o EPIC_TYPE: é o que permite o EPIC_GROUP — um EPIC que
        /// agrupa EPICs — sem abrir a hierarquia para Epic dentro de Epic em geral.
        /// </summary>
        public static bool HierarchyOk(string? pai, string? filho, string? epicTypePai, string? epicTypeFilho) =>
            NxLevelMap.Current.ParentChildOk(pai, filho, epicTypePai, epicTypeFilho);

        /// <summary>
        /// Decide o que gravar comparando o cronograma com o board. Não faz rede: recebe o board
        /// já lido.
        /// </summary>
        /// <param name="temControleDeVersao">
        /// O board tem os campos de concorrência? Quando não tem, o sync continua — mas o aviso
        /// é parte do resultado, porque a pessoa está gravando sem rede de proteção.
        /// </param>
        public static SyncPlan Build(Project project, IReadOnlyDictionary<int, GitProjectItem> board,
                                     GitProjectOptions options, bool temControleDeVersao)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (options == null) throw new ArgumentNullException(nameof(options));
            board ??= new Dictionary<int, GitProjectItem>();
            var plano = new SyncPlan();

            if (!temControleDeVersao)
                plano.Warnings.Add(NxText.T(
                    "O board não tem os campos de controle de concorrência ({0} e {1}): não é possível "
                    + "saber se outra pessoa alterou o item depois. Crie-os em Configurar Campos e Tags.",
                    "The board has no concurrency control fields ({0} and {1}): there is no way to tell "
                    + "whether someone else changed the item after you. Create them under Configure Fields and Tags.",
                    GitProjectOptions.Suggested(options.SyncVersionFields),
                    GitProjectOptions.Suggested(options.SyncNameFields)));

            foreach (var (tarefa, pai) in ComPai(project.Tasks, null))
            {
                // Hierarquia: vale para o que vai ser criado E para o que já existe — item que o
                // GitHub deixou torto não deve ser "consertado" por uma gravação de campo.
                if (pai != null && !HierarchyOk(pai.TfsType, tarefa.TfsType, pai.EpicType, tarefa.EpicType))
                {
                    plano.Errors.Add(NxText.T(
                        "'{0}' ({1}) está sob '{2}' ({3}): hierarquia que o NXProject não usa. Item ignorado.",
                        "'{0}' ({1}) sits under '{2}' ({3}): a hierarchy NXProject does not use. Item skipped.",
                        tarefa.Name, tarefa.TfsType, pai.Name, pai.TfsType));
                    continue;
                }

                if (tarefa.TfsId is not > 0)
                {
                    if (string.IsNullOrWhiteSpace(options.Repository))
                        plano.Errors.Add(NxText.T(
                            "'{0}' é nova e não foi criada: informe o repositório (dono/repo) na configuração do "
                            + "GitProject — no GitHub a issue nasce num repositório.",
                            "'{0}' is new and was not created: set the repository (owner/repo) in the GitProject "
                            + "settings — on GitHub an issue is born in a repository.",
                            tarefa.Name));
                    else
                        plano.Creates.Add(tarefa);
                    continue;
                }

                var numero = tarefa.TfsId!.Value;
                if (!board.TryGetValue(numero, out var item))
                {
                    plano.Errors.Add(NxText.T("#{0} ('{1}') não está neste board. Item ignorado.",
                                              "#{0} ('{1}') is not on this board. Item skipped.", numero, tarefa.Name));
                    continue;
                }

                // Alguém gravou depois de você: o item sai fora, inteiro. Gravar "só os campos
                // que mudaram aqui" é o jeito mais discreto de perder o trabalho do outro.
                //
                // A comparação principal é com a REVISÃO DO SERVIDOR (updatedAt), que o GitHub
                // atualiza sozinho a cada edição — inclusive pela tela dele. O campo
                // `Sync_version` do board entra como reforço: ele diz quem foi, e cobre o caso
                // de a revisão não ter sido lida.
                var revisaoServidor = Revision(item.UpdatedAt);
                var revisaoLocal = tarefa.SyncVersion ?? 0;
                var mudouNoServidor = revisaoServidor > 0 && revisaoLocal > 0 && revisaoServidor > revisaoLocal;

                if (mudouNoServidor
                    || (temControleDeVersao && item.SyncVersion is { } versaoBoard && versaoBoard > revisaoLocal))
                {
                    plano.Conflicts.Add(tarefa);
                    continue;
                }

                var antes = plano.Updates.Count;
                Compara(plano, tarefa, item);
                if (plano.Updates.Count == antes) plano.Unchanged++;
            }

            return plano;
        }

        /// <summary>
        /// O que mudou nesta atividade. HH só na folha: em resumo ele é soma dos filhos, e gravar
        /// a soma no board criaria um número que ninguém pode editar sem mentir.
        /// </summary>
        private static void Compara(SyncPlan plano, ProjectTask t, GitProjectItem item)
        {
            void Add(string chave, object valor) => plano.Updates.Add(new Op(t, item.Number, chave, valor));

            if (t.Start != default && item.StartDate?.Date != t.Start.Date) Add("start", t.Start.Date);

            var fim = FimInclusivo(t);
            if (fim != null && item.FinishDate?.Date != fim.Value.Date) Add("finish", fim.Value.Date);

            if (t.Children.Count == 0 && t.EstimatedHours is { } hh && Math.Abs((item.Effort ?? -1) - hh) > 0.001)
                Add("effort", hh);

            if (t.Children.Count == 0 && t.CurrentHours is { } atual
                && Math.Abs((item.CurrentHours ?? -1) - atual) > 0.001)
                Add("currentHours", atual);

            // HH restante e HH original: a mesma regra do Azure DevOps — o restante e o que ainda
            // esta planejado, e o original e o plano de antes, que o realizado nao pode apagar.
            if (t.Children.Count == 0 && t.EstimatedHours is { } rest
                && Math.Abs((item.RemainingHours ?? -1) - rest) > 0.001)
                Add("remainingHours", rest);

            if (t.Children.Count == 0 && t.OriginalEstimatedHours is { } orig && orig > 0
                && Math.Abs((item.OriginalHours ?? -1) - orig) > 0.001)
                Add("originalHours", orig);

            if (Math.Abs((item.Percent ?? -1) - t.PercentComplete) > 0.001)
                Add("percConclusao", t.PercentComplete);

            // Estado da ISSUE (aberta/fechada), que no GitHub é independente da coluna do board.
            // Fechar com 100% e reabrir quando voltou a andar: as duas direções, senão o board
            // guarda um "Closed" que o cronograma já desmentiu.
            var deveFechar = t.PercentComplete >= 99.999;
            var estaFechada = string.Equals(item.IssueState, "CLOSED", StringComparison.OrdinalIgnoreCase);
            if (deveFechar != estaFechada) Add("state", deveFechar);
        }

        /// <summary>
        /// Fim como o NXProject mostra: inclusivo. O motor guarda o fim exclusivo, e gravar esse
        /// no board faria toda atividade terminar um dia depois — é o mesmo cuidado do sync do
        /// DevOps e do fim de sprint.
        /// </summary>
        private static DateTime? FimInclusivo(ProjectTask t) =>
            t.Finish == default ? null
            : t.Finish.Date > t.Start.Date ? t.Finish.Date.AddDays(-1) : t.Finish.Date;

        private static IEnumerable<(ProjectTask Tarefa, ProjectTask? Pai)> ComPai(
            IEnumerable<ProjectTask> tarefas, ProjectTask? pai)
        {
            foreach (var t in tarefas)
            {
                yield return (t, pai);
                foreach (var f in ComPai(t.Children, t)) yield return f;
            }
        }

        // ── Gravação ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Lê o board, decide com <see cref="Build"/> e grava. O relatório é o MESMO do sync do
        /// DevOps (<see cref="TfsImportService.SyncReport"/>): a tela de resultado já existe, e
        /// um relatório diferente por destino seria uma segunda tela para manter.
        /// </summary>
        /// <param name="progress">
        /// O MESMO progresso do DevOps (fase, contagem e item): a tela de andamento e uma so, e
        /// um destino que so manda texto deixa a barra indeterminada girando sem dizer onde
        /// esta. O item vem identificado por nivel e numero — "Story #123" —, que e como se
        /// procura o que ficou para tras quando algo falha no meio.
        /// </param>
        /// <param name="predecessoras">
        /// Grava as predecessoras como "blocked by" — a mesma chave do DevOps
        /// (<c>SyncPredecessorLinks</c>): quem desliga lá não espera vê-las gravadas aqui.
        /// </param>
        public static async Task<TfsImportService.SyncReport> SyncAsync(
            Project project, GitProjectOptions options,
            IProgress<TfsImportService.SyncProgress>? progress = null,
            System.Threading.CancellationToken ct = default,
            bool predecessoras = true)
        {
            // Parar e decisao de quem olha a tela, e o GitHub nao tem transacao: o que ja foi
            // gravado fica. Por isso o corte e ENTRE itens e o relatorio diz onde parou.
            bool Parou()
            {
                if (!ct.IsCancellationRequested) return false;
                return true;
            }
            // Atalhos: fase sem contagem, e fase COM contagem e item.
            void Fase(string texto) => progress?.Report(new(texto, 0, 0, ""));
            void Passo(string fase, int atual, int total, ProjectTask t, int numero) =>
                progress?.Report(new(fase, atual, total, Rotulo(t, numero)));
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (options?.IsConfigured != true)
                throw new InvalidOperationException(NxText.T(
                    "Conexão do GitProject incompleta: informe dono, número do board e token.",
                    "Incomplete GitProject connection: provide owner, board number and token."));

            var relatorio = new TfsImportService.SyncReport();
            var cliente = new GitProjectClient(options);

            // Gravar exige ler antes: os ids internos do item no board, o valor atual de cada
            // campo (para nao regravar o que nao mudou) e a revisao de cada item (para recusar o
            // que alguem editou no GitHub depois da ultima sincronizacao) so existem la.
            Fase(NxText.T("Lendo o GitProject…", "Reading the GitProject…"));
            GitSyncLog.Inicia(options.Owner + "/" + options.ProjectNumber, ContaTarefas(project.Tasks));
            var snap = await GitSyncLog.MedeAsync("leitura do board", "", cliente.LoadAsync);
            var porNumero = snap.Items.ToDictionary(i => i.Number);

            GitProjectField? Campo(IEnumerable<string> nomes) =>
                snap.Fields.FirstOrDefault(f => GitProjectOptions.Matches(f.Name, nomes));

            // Quem pode gravar aqui? É a primeira pergunta, antes de qualquer escrita — e a
            // resposta sai do board, não da máquina de quem está sincronizando.
            var (podeGravar, regra) = await GitProjectAdminService.CanSyncAsync(options, snap, cliente);
            if (!podeGravar)
                throw new InvalidOperationException(NxText.T(
                    "Você não está autorizado a sincronizar este board. O campo de administração do "
                    + "NXProject está configurado como '{0}'.",
                    "You are not allowed to sync this board. The NXProject administration field is set to '{0}'.",
                    regra));

            var campoVersao = Campo(options.SyncVersionFields);
            var campoNome = Campo(options.SyncNameFields);
            var temControle = campoVersao != null && campoNome != null;

            var plano = Build(project, porNumero, options, temControle);
            foreach (var w in plano.Warnings) relatorio.LogWarning(w);
            foreach (var err in plano.Errors) relatorio.LogError(err);
            relatorio.Skipped = plano.Unchanged;

            foreach (var t in plano.Conflicts)
            {
                relatorio.Conflicts++;
                // Dizer QUEM e QUANDO vem do servidor, não do nosso carimbo: quem editou pela tela
                // do GitHub nunca passou pelo NXProject, e é justamente essa pessoa que o aviso
                // precisa nomear para alguém poder resolver.
                porNumero.TryGetValue(t.TfsId ?? 0, out var item);
                var quem = string.IsNullOrWhiteSpace(item?.LastChangedBy) ? ""
                    : NxText.T(" por {0}", " by {0}", item!.LastChangedBy);
                var quando = item?.UpdatedAt is { } dt
                    ? NxText.T(" em {0:dd/MM/yyyy HH:mm}", " on {0:yyyy-MM-dd HH:mm}", dt.ToLocalTime()) : "";
                relatorio.LogError(NxText.T(
                    "#{0} ('{1}'): alterado no GitHub{2}{3}, depois da sua última sincronização. Nada foi "
                    + "gravado neste item — reimporte para ver o que mudou.",
                    "#{0} ('{1}'): changed on GitHub{2}{3}, after your last sync. Nothing was written to this "
                    + "item — import again to see what changed.",
                    t.TfsId, t.Name, quem, quando));
            }

            // 1) Criação primeiro, e pai antes de filho: a sub-issue precisa do pai já existindo.
            if (plano.Creates.Count > 0 && !Parou())
                await CriaAsync(plano, project, options, cliente, snap, porNumero, relatorio, progress);

            // 2) Campos. Um conceito sem campo no board vira UM aviso, não um por atividade: a
            //    causa é a mesma e o relatório ficaria ilegível.
            var semCampo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tocados = new Dictionary<int, ProjectTask>();

            // Um passo por ITEM, e nao por campo: a pessoa acompanha "Story #123", nao
            // "percConclusao". Os campos do mesmo item sao gravados dentro do passo dele.
            var porItem = plano.Updates.GroupBy(u => u.Number).ToList();
            var feitos = 0;
            var faseCampos = NxText.T("Gravando os itens…", "Writing the items…");

            var interrompido = false;
            foreach (var grupo in porItem)
            {
            if (Parou()) { interrompido = true; break; }
            foreach (var op in grupo)
            {
                if (!porNumero.TryGetValue(op.Number, out var item)) continue;
                if (op == grupo.First()) Passo(faseCampos, ++feitos, porItem.Count, op.Task, op.Number);
                try
                {
                    if (op.Key == "state")
                    {
                        await GitSyncLog.MedeAsync("estado da issue", $"#{op.Number}",
                            () => cliente.SetIssueStateAsync(item.IssueId, (bool)op.Value));
                    }
                    else
                    {
                        var campo = Campo(NomesDoConceito(options, op.Key));
                        if (campo == null)
                        {
                            if (semCampo.Add(op.Key))
                                relatorio.LogWarning(NxText.T(
                                    "O board não tem o campo de '{0}' ({1}): esse valor não foi gravado. "
                                    + "Crie-o em Configurar Campos e Tags.",
                                    "The board has no '{0}' field ({1}): that value was not written. "
                                    + "Create it under Configure Fields and Tags.",
                                    op.Key, GitProjectOptions.Suggested(NomesDoConceito(options, op.Key))));
                            continue;
                        }
                        await GitSyncLog.MedeAsync("campo", $"#{op.Number} {op.Key}",
                            () => cliente.SetFieldAsync(snap.ProjectId, item.ItemId, campo.Id, op.Value));
                    }
                    tocados[op.Number] = op.Task;
                }
                catch (Exception ex)
                {
                    relatorio.LogError(NxText.T("#{0} ('{1}'), {2}: {3}", "#{0} ('{1}'), {2}: {3}",
                                                op.Number, op.Task.Name, op.Key, ex.Message));
                }
            }
            }

            if (interrompido)
                relatorio.LogWarning(NxText.T(
                    "Interrompido por você: {0} de {1} item(ns) foram gravados. O que já foi para o "
                    + "GitHub fica — lá não há como desfazer em bloco. Sincronize de novo para continuar.",
                    "Stopped by you: {0} of {1} item(s) were written. What already went to GitHub stays "
                    + "— there is no bulk undo there. Sync again to carry on.",
                    feitos, porItem.Count));

            // 3) Carimbo de concorrência, só no que foi realmente gravado: carimbar o que não
            //    mudou faria toda sincronização parecer conflito para a próxima pessoa.
            //
            //    A revisão que fica no CRONOGRAMA é lida DEPOIS de tudo (ver o passo 5): o
            //    próprio carimbo é uma gravação e também mexe no updatedAt do item. Ler antes
            //    dele — que era o que se fazia — deixava a revisão local sempre um pouco atrás
            //    da do servidor, e a sincronização seguinte acusava conflito contra a gravação
            //    anterior desta mesma pessoa.
            foreach (var (numero, tarefa) in tocados)
            {
                relatorio.Updated++;
                if (!temControle || !porNumero.TryGetValue(numero, out var item)) continue;
                try
                {
                    // O mesmo número vai para o campo do board: quem abrir o GitHub vê a revisão
                    // que o NXProject considerou, e o Sync_Name diz quem gravou.
                    // O TIPO e o do campo no board, nao o que o catalogo gostaria. O NX pede
                    // NUMBER para a versao, mas o GitHub NAO deixa trocar o tipo de um campo
                    // depois de criado — board com o campo em TEXT existe, e mandar numero para
                    // ele fazia a gravacao falhar em TODO item carimbado (medido: 11 chamadas,
                    // ~520 ms cada, todas perdidas, e o Sync_Name nem chegava a ser gravado).
                    await GitSyncLog.MedeAsync("carimbo Sync_version", $"#{numero}",
                        () => cliente.SetFieldAsync(snap.ProjectId, item.ItemId, campoVersao!.Id,
                                                    ValorNoTipo(campoVersao, tarefa.SyncVersion ?? 0)));
                    await GitSyncLog.MedeAsync("carimbo Sync_Name", $"#{numero}",
                        () => cliente.SetFieldAsync(snap.ProjectId, item.ItemId, campoNome!.Id,
                                                    Environment.UserName));
                }
                catch (Exception ex)
                {
                    relatorio.LogWarning(NxText.T(
                        "#{0}: gravado, mas o controle de concorrência falhou ({1}).",
                        "#{0}: written, but the concurrency stamp failed ({1}).", numero, ex.Message));
                }
            }

            // 3.5) Responsável. Separado dos campos porque não é campo do board: é da ISSUE, e
            //      depende de a pessoa ser colaboradora do repositório. O que não casar com
            //      segurança vira aviso — responsável errado muda a fila de trabalho de duas
            //      pessoas de uma vez.
            try
            {
                await SincronizaResponsavelAsync(project, options, cliente, porNumero, relatorio, progress);
            }
            catch (Exception ex)
            {
                relatorio.LogWarning(NxText.T("Responsável não pôde ser sincronizado: ",
                                              "Assignee could not be synced: ") + ex.Message);
            }

            // 3.6) Predecessoras, como "blocked by". Depois da criação (o item novo já tem id) e
            //      fora do passo de campos: dependência é da ISSUE, não do board.
            if (predecessoras && !Parou())
            {
                try
                {
                    await SincronizaPredecessorasAsync(project, cliente, porNumero, tocados, relatorio, progress);
                }
                catch (Exception ex)
                {
                    relatorio.LogWarning(NxText.T("Predecessoras não puderam ser sincronizadas: ",
                                                  "Predecessors could not be synced: ") + ex.Message);
                }
            }

            // 4) Ordem do backlog, por último: é a parte cara (medida em ~2s por movimento) e o
            //    item recém-criado só pode ser posicionado depois de existir. Quem decide COMO
            //    gravar é a fachada — aqui não se sabe que no GitHub isso é mover item na view.
            try
            {
                Fase(NxText.T("Aplicando a ordem do backlog…", "Applying backlog order…"));
                // O gravador da ordem fala em texto: adapta aqui, sem mudar a fachada por causa
                // da tela.
                var textoDaOrdem = new Progress<string>(m => Fase(m));
                // Reaproveita o board JA LIDO: reler custou 1,8 s medidos, e os ids dos itens
                // nao mudam — a unica coisa que precisa estar fresca e a FILA, que e lida a
                // parte, logo ali dentro.
                var ordem = await new GitProjectOrderWriter(options, snap)
                                                .ApplyAsync(project, textoDaOrdem, ct);
                foreach (var w in ordem.Warnings) relatorio.LogWarning(w);
                if (ordem.Moved > 0)
                    relatorio.LogSuccess(NxText.T("Ordem do backlog: {0} item(ns) reposicionado(s).",
                                                  "Backlog order: {0} item(s) repositioned.", ordem.Moved));
            }
            catch (Exception ex)
            {
                // A ordem é a única parte que o GitHub guarda como posição de view, e não como
                // campo. Falhar aqui não pode invalidar o planejamento que já foi gravado.
                relatorio.LogWarning(NxText.T("A ordem do backlog não pôde ser gravada: ",
                                              "Backlog order could not be written: ") + ex.Message);
            }

            // 5) A revisão que o cronograma guarda: lida AGORA, quando nada mais será gravado
            //    nestes itens. É o updatedAt do servidor — não o relógio desta máquina, que
            //    divergiria —, e inclui as nossas próprias gravações, inclusive o carimbo. Sem
            //    isto, sincronizar duas vezes seguidas acusava conflito na segunda.
            if (tocados.Count > 0)
            {
                Fase(NxText.T("Lendo a revisão dos itens gravados…", "Reading the revision of written items…"));
                var depoisDeTudo = (await GitSyncLog.MedeAsync("releitura (revisao final)", "",
                        () => new GitProjectClient(options).LoadAsync())).Items.ToDictionary(i => i.Number);
                foreach (var (numero, tarefa) in tocados)
                    if (depoisDeTudo.TryGetValue(numero, out var atualizado))
                        tarefa.SyncVersion = Revision(atualizado.UpdatedAt);
            }

            Fase(NxText.T("{0} item(ns) atualizado(s).", "{0} item(s) updated.", relatorio.Updated));
            GitSyncLog.Encerra();
            return relatorio;
        }

        /// <summary>Uma mudança de dependência: <c>Number</c> passa (ou deixa) de ser bloqueada por <c>Blocker</c>.</summary>
        public sealed record DependencyOp(int Number, ProjectTask Task, int Blocker, bool Add);

        /// <summary>
        /// O que muda no "blocked by" para o board bater com as predecessoras do cronograma.
        /// Função pura: a regra é testável sem servidor.
        ///
        /// A mesma regra do DevOps: só atividade cujos filhos são todos marcos (a folha, ou o
        /// resumo que só tem marcos), e predecessora com id no board. E uma diferença de
        /// propósito: só se REMOVE bloqueio por issue deste board. Um bloqueio vindo de fora o
        /// cronograma nunca enxergou — apagá-lo seria destruir o que outra pessoa registrou.
        /// </summary>
        public static (List<DependencyOp> Ops, List<string> Warnings) PlanoPredecessoras(
            Project project, IReadOnlyDictionary<int, GitProjectItem> porNumero)
        {
            var ops = new List<DependencyOp>();
            var avisos = new List<string>();
            var todas = new List<ProjectTask>();
            void Junta(IEnumerable<ProjectTask> ts) { foreach (var t in ts) { todas.Add(t); Junta(t.Children); } }
            Junta(project.Tasks);
            var porId = todas.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());

            bool MesmoRepo(string a, string b) =>
                string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)
                || string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

            foreach (var t in todas)
            {
                if (t.TfsId is not > 0 || !porNumero.TryGetValue(t.TfsId.Value, out var item)) continue;
                if (!t.Children.All(c => c.IsMilestone)) continue;

                var desejadas = new HashSet<int>();
                var invalidas = new List<int>();
                foreach (var guardado in t.PredecessorIds)
                {
                    if (guardado < 0) continue; // atividade só do cronograma: não existe no GitHub
                    if (porId.TryGetValue(guardado, out var pred) && pred.TfsId is > 0
                        && porNumero.ContainsKey(pred.TfsId.Value))
                        desejadas.Add(pred.TfsId.Value);
                    else if (!porId.ContainsKey(guardado) && porNumero.ContainsKey(guardado))
                        desejadas.Add(guardado); // guardado como número da issue
                    else invalidas.Add(guardado);
                }
                desejadas.Remove(t.TfsId.Value);
                if (invalidas.Count > 0)
                    avisos.Add(NxText.T(
                        "#{0} ({1}): predecessora(s) sem issue neste board ({2}) — ignoradas na sincronização.",
                        "#{0} ({1}): predecessor(s) with no issue on this board ({2}) — ignored in the sync.",
                        t.TfsId, t.Name, string.Join(", ", invalidas)));

                var atuais = item.BlockedBy
                    .Where(b => porNumero.TryGetValue(b.Number, out var nb) && MesmoRepo(b.Repository, nb.Repository))
                    .Select(b => b.Number).ToHashSet();

                foreach (var n in desejadas.Where(n => !atuais.Contains(n)).OrderBy(n => n))
                    ops.Add(new DependencyOp(t.TfsId.Value, t, n, Add: true));
                foreach (var n in atuais.Where(n => !desejadas.Contains(n)).OrderBy(n => n))
                    ops.Add(new DependencyOp(t.TfsId.Value, t, n, Add: false));
            }
            return (ops, avisos);
        }

        private static async Task SincronizaPredecessorasAsync(Project project, GitProjectClient cliente,
            Dictionary<int, GitProjectItem> porNumero, Dictionary<int, ProjectTask> tocados,
            TfsImportService.SyncReport relatorio, IProgress<TfsImportService.SyncProgress>? progress)
        {
            var (ops, avisos) = PlanoPredecessoras(project, porNumero);
            foreach (var a in avisos) relatorio.LogWarning(a);
            if (ops.Count == 0) return;

            var fase = NxText.T("Gravando predecessoras…", "Writing predecessors…");
            var feitas = 0;
            for (int i = 0; i < ops.Count; i++)
            {
                var op = ops[i];
                progress?.Report(new(fase, i + 1, ops.Count, Rotulo(op.Task, op.Number)));
                var issue = porNumero[op.Number].IssueId;
                var bloqueia = porNumero[op.Blocker].IssueId;
                try
                {
                    if (op.Add)
                        await GitSyncLog.MedeAsync("predecessora +", $"#{op.Number}<-#{op.Blocker}",
                            () => cliente.AddBlockedByAsync(issue, bloqueia));
                    else
                        await GitSyncLog.MedeAsync("predecessora -", $"#{op.Number}<-#{op.Blocker}",
                            () => cliente.RemoveBlockedByAsync(issue, bloqueia));
                    feitas++;
                    // A dependência mexe no updatedAt da issue: entra na releitura da revisão
                    // (passo 5), senão a próxima sincronização acusaria conflito contra esta.
                    if (!tocados.ContainsKey(op.Number)) { tocados[op.Number] = op.Task; relatorio.Updated++; }
                }
                catch (Exception ex)
                {
                    relatorio.LogWarning(NxText.T(
                        "#{0}: predecessora #{1} não foi {2} ({3}).",
                        "#{0}: predecessor #{1} was not {2} ({3}).",
                        op.Number, op.Blocker,
                        op.Add ? NxText.T("gravada", "added") : NxText.T("removida", "removed"), ex.Message));
                }
            }
            if (feitas > 0)
                relatorio.LogSuccess(NxText.T("Predecessoras: {0} vínculo(s) gravado(s) como \"blocked by\".",
                                              "Predecessors: {0} link(s) written as \"blocked by\".", feitas));
        }

        /// <summary>
        /// Grava o responsável de cada atividade na issue correspondente.
        ///
        /// A lista de candidatas vem do REPOSITÓRIO (`assignableUsers`), não da organização: no
        /// GitHub só colaborador pode ser responsável. E a resolução é conservadora — login ou
        /// nome completo, exato e único. O resto é aviso, nunca palpite.
        /// </summary>
        private static async Task SincronizaResponsavelAsync(Project project, GitProjectOptions options,
            GitProjectClient cliente, Dictionary<int, GitProjectItem> porNumero,
            TfsImportService.SyncReport relatorio, IProgress<TfsImportService.SyncProgress>? progress)
        {
            // Sem repositório não há lista de candidatas: o board sozinho não sabe quem pode ser
            // responsável por uma issue.
            var partes = (options.Repository ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length != 2) return;

            var candidatas = await cliente.AssignableAccountsAsync(partes[0].Trim(), partes[1].Trim());
            if (candidatas.Count == 0) return;

            var avisados = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

            foreach (var (t, _) in ComPai(project.Tasks, null))
            {
                if (t.TfsId is not > 0 || !porNumero.TryGetValue(t.TfsId.Value, out var item)) continue;
                var pessoa = t.Resources.FirstOrDefault()?.Resource;
                var recurso = pessoa?.Name;
                if (string.IsNullOrWhiteSpace(recurso)) continue;

                // O login gravado no recurso (lido no import) manda sobre o nome.
                var resolucao = GitProjectPeopleService.Resolve(recurso, candidatas, pessoa?.GitLogin);
                if (!resolucao.Ok)
                {
                    // Um aviso por PESSOA, não por atividade: a causa é a mesma e o relatório
                    // ficaria ilegível num cronograma de centenas de linhas.
                    if (resolucao.Problem.Length > 0 && avisados.Add(recurso!))
                        relatorio.LogWarning(resolucao.Problem);   // já vem no idioma certo
                    continue;
                }

                // Já é o responsável: nada a fazer. Comparar pelo que o board devolveu evita uma
                // gravação por sincronização em item que ninguém mexeu.
                if (GitProjectOptions.Matches(item.Assignee, new[] { resolucao.Account!.Login, resolucao.Account.Name }))
                    continue;

                try
                {
                    progress?.Report(new(NxText.T("Responsável…", "Assignee…"), 0, 0,
                                        NxText.T("#{0}", "#{0}", item.Number)));
                    await cliente.SetAssigneeAsync(item.IssueId, resolucao.Account.Id);
                    relatorio.LogSuccess(NxText.T("#{0}: responsável agora é {1}.",
                                                  "#{0}: assignee is now {1}.", item.Number, resolucao.Account.Login));
                }
                catch (Exception ex)
                {
                    relatorio.LogError(NxText.T("#{0}, responsável: {1}", "#{0}, assignee: {1}",
                                                item.Number, ex.Message));
                }
            }
        }

        /// <summary>
        /// Cria as issues novas: issue no repositório → item no board → sub-issue do pai → nível.
        /// São quatro chamadas onde o DevOps faz uma, e é por isso que a falha no meio importa:
        /// o item fica marcado no relatório, nunca como órfão silencioso.
        /// </summary>
        private static async Task CriaAsync(SyncPlan plano, Project project, GitProjectOptions options,
            GitProjectClient cliente, GitProjectClient.ProjectSnapshot snap,
            Dictionary<int, GitProjectItem> porNumero, TfsImportService.SyncReport relatorio,
            IProgress<TfsImportService.SyncProgress>? progress)
        {
            var partes = options.Repository.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length != 2)
            {
                relatorio.LogError(NxText.T("Repositório '{0}' inválido: use o formato dono/repo.",
                                            "Invalid repository '{0}': use the owner/repo format.", options.Repository));
                return;
            }

            string repoId;
            try { repoId = await cliente.RepositoryIdAsync(partes[0].Trim(), partes[1].Trim()); }
            catch (Exception ex)
            { relatorio.LogError(NxText.T("Criação cancelada: ", "Creation cancelled: ") + ex.Message); return; }

            var campoNivel = snap.Fields.FirstOrDefault(f => GitProjectOptions.Matches(f.Name, options.LevelFields));
            var paiDe = Pais(project.Tasks, null);

            // Pai antes de filho: a ordem da lista é a do cronograma, de cima para baixo, que já
            // é essa — mas depender disso por acaso seria frágil.
            var criados = 0;
            foreach (var t in plano.Creates.OrderBy(t => Profundidade(t, paiDe)))
            {
                progress?.Report(new(NxText.T("Criando os itens novos…", "Creating the new items…"),
                                    ++criados, plano.Creates.Count, Rotulo(t, 0)));
                try
                {
                    var (numero, issueId) = await cliente.CreateIssueAsync(repoId, t.Name ?? "(sem nome)",
                        $"Criado pelo NXProject a partir do cronograma '{project.Name}'.");
                    var itemId = await cliente.AddToProjectAsync(snap.ProjectId, issueId);

                    if (paiDe.TryGetValue(t, out var pai) && pai?.TfsId is > 0
                        && porNumero.TryGetValue(pai.TfsId!.Value, out var itemPai)
                        && !string.IsNullOrEmpty(itemPai.IssueId))
                        await cliente.AddSubIssueAsync(itemPai.IssueId, issueId);

                    if (campoNivel != null && NivelDoTipo(t.TfsType) is { Length: > 0 } nivel
                        && campoNivel.Options.TryGetValue(nivel, out var opcaoId))
                        await cliente.SetFieldAsync(snap.ProjectId, itemId, campoNivel.Id, new GitSingleSelect(opcaoId));

                    t.TfsId = numero;
                    porNumero[numero] = new GitProjectItem
                    { Number = numero, IssueId = issueId, ItemId = itemId, Title = t.Name ?? "", IssueState = "OPEN" };

                    // O recém-criado entra na fila de campos como qualquer outro item.
                    Compara(plano, t, porNumero[numero]);
                    relatorio.Created++;
                    relatorio.LogSuccess(NxText.T("#{0} criado para '{1}'.", "#{0} created for '{1}'.", numero, t.Name));
                }
                catch (Exception ex)
                {
                    relatorio.LogError(NxText.T("'{0}' não foi criado: {1}", "'{0}' was not created: {1}",
                                                t.Name, ex.Message));
                }
            }
        }

        /// <summary>
        /// Como o item aparece no andamento: nivel + numero + nome ("Story #123 — Carga"). O
        /// numero e o que permite achar o item no GitHub depois; o nivel diz em que altura da
        /// hierarquia a sincronizacao esta.
        /// </summary>
        private static string Rotulo(ProjectTask t, int numero)
        {
            var nivel = NivelDoTipo(t?.TfsType);
            if (string.IsNullOrWhiteSpace(nivel)) nivel = t?.TfsType ?? "";
            var id = numero > 0 ? numero : (t?.TfsId is > 0 ? t!.TfsId!.Value : 0);
            var cabeca = (nivel + (id > 0 ? $" #{id}" : "")).Trim();
            return string.IsNullOrWhiteSpace(cabeca) ? (t?.Name ?? "") : $"{cabeca} — {t?.Name}";
        }

        /// <summary>
        /// Nome do nível como o campo single-select do board o escreve. Sai do mapa: se a opção do
        /// board foi criada como "Iniciativa", é "Iniciativa" que tem de ser gravado.
        /// </summary>
        private static string NivelDoTipo(string? tipo) =>
            NxLevelMap.Current.LevelOf(tipo) is { } n ? NxLevelMap.Current.Suggested(n) : "";

        /// <summary>
        /// O valor no tipo que o CAMPO aceita. Campo de número recebe número; campo de texto
        /// recebe o número escrito. Sem isto, a gravação falha inteira — e falha item a item,
        /// gastando uma ida à rede por item para não gravar nada.
        /// </summary>
        private static object ValorNoTipo(GitProjectField campo, double valor) =>
            string.Equals(campo?.DataType, "NUMBER", StringComparison.OrdinalIgnoreCase)
                ? valor
                : valor.ToString("0", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Quantas atividades o cronograma tem — so para o cabecalho do log.</summary>
        private static int ContaTarefas(IEnumerable<ProjectTask>? tarefas) =>
            tarefas == null ? 0 : tarefas.Sum(t => 1 + ContaTarefas(t.Children));

        /// <summary>Nomes aceitos no board para o conceito que o plano quer gravar.</summary>
        private static string[] NomesDoConceito(GitProjectOptions o, string chave) => chave switch
        {
            "effort" => o.EffortFields,
            "currentHours" => o.CurrentHoursFields,
            "originalHours" => o.OriginalHoursFields,
            "remainingHours" => o.RemainingHoursFields,
            "start" => o.StartDateFields,
            "finish" => o.FinishDateFields,
            "percConclusao" => o.PercentFields,
            _ => Array.Empty<string>()
        };

        private static Dictionary<ProjectTask, ProjectTask?> Pais(IEnumerable<ProjectTask> tarefas, ProjectTask? pai)
        {
            var mapa = new Dictionary<ProjectTask, ProjectTask?>();
            foreach (var (t, p) in ComPai(tarefas, pai)) mapa[t] = p;
            return mapa;
        }

        private static int Profundidade(ProjectTask t, Dictionary<ProjectTask, ProjectTask?> pais)
        {
            var n = 0;
            var atual = t;
            while (pais.TryGetValue(atual, out var p) && p != null && n < 20) { atual = p; n++; }
            return n;
        }
    }
}
