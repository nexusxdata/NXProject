// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NXProject.Models;

namespace NXProject.Services
{
    /// <summary>
    /// De onde o TaskBoard LÊ: sprints e board. Um implementador por destino — Azure DevOps,
    /// GitHub Projects, Projeto Local.
    ///
    /// Por que interface, e não uma classe só recebendo o nome da conexão: uma classe só vira um
    /// `switch` por destino dentro de cada método, e cada destino novo obriga a revisar todos
    /// eles. Com um implementador por destino, o que é específico fica junto, e o que é regra do
    /// NXProject (hierarquia, datas, rateio de HH) continua fora daqui, onde sempre esteve.
    ///
    /// A tela não constrói implementador: ela pede à <see cref="NxBoardSources"/>, que é a
    /// fachada — um lugar só para chamar, qualquer que seja o destino.
    /// </summary>
    public interface INxBoardSource
    {
        /// <summary>Contra o que este provedor trabalha.</summary>
        NxBackendKind Kind { get; }

        /// <summary>O que este destino sabe fazer. A tela pergunta por aqui, não pelo Kind.</summary>
        NxBackendCapabilities Capabilities { get; }

        /// <summary>Sprints disponíveis, já com início e fim quando o destino os tiver.</summary>
        Task<List<TfsImportService.SprintInfo>> ListSprintsAsync();

        /// <summary>
        /// Board da(s) sprint(s) pedidas. Lista vazia = todas — é o mesmo contrato que o board
        /// já usava, para a tela não precisar aprender nada novo.
        /// </summary>
        Task<TfsImportService.SprintBoard> BuildBoardAsync(IReadOnlyCollection<string> iterationPaths);

        /// <summary>
        /// Auditoria de bloqueio do item: desde quando está em andamento e cada período em que
        /// ficou bloqueado. Consulta pura — não grava nada.
        ///
        /// Está na fachada porque a PERGUNTA é a mesma nos três destinos; o que muda é de onde a
        /// resposta sai: do histórico do work item no DevOps, da timeline da issue no GitHub, do
        /// log em disco no modo local. Devolve null quando o destino não sabe responder.
        /// </summary>
        Task<TfsImportService.BlockAudit?> LoadBlockAuditAsync(int id);

        /// <summary>
        /// Os campos do item que NÃO cabem no board e só são lidos ao abrir a edição: descrição,
        /// critério de aceite, horas, estado e datas.
        ///
        /// Está aqui porque a tela de edição perguntava isso direto ao Azure DevOps, com qualquer
        /// destino selecionado — e o DevOps respondia sobre um id que não era dele ("work item 47
        /// does not exist"). A pergunta é a mesma nos três; de onde sai a resposta é que muda.
        /// </summary>
        Task<NxItemDetails> LoadItemDetailsAsync(int id, string kind);

        /// <summary>
        /// Os filhos do item NO SERVIDOR: a quantidade e, quando o destino sabe, os ids.
        /// <c>Count</c> negativo = não deu para conferir.
        ///
        /// A pergunta existe por causa da exclusão: o board só enxerga as sprints carregadas, e
        /// um EPIC "vazio" na tela pode ter Features em outra sprint. Excluir assim deixaria
        /// trabalho órfão — e dizer QUAIS são os filhos é o que torna o aviso verificável: no
        /// GitProject eles saem do snapshot, que já tem a árvore inteira.
        /// </summary>
        Task<(int Count, IReadOnlyList<int> Ids)> ChildrenAsync(int id);

        /// <summary>
        /// O cronograma do projeto cuja raiz é <paramref name="rootId"/> (work item no DevOps,
        /// issue no GitHub).
        ///
        /// Está na fachada porque o Mapa de Alocação importava sempre pelo DevOps: um projeto do
        /// GitProject mandava o número da issue (#45) ao TFS, que respondia "Could not find
        /// workitemdataset with entity id: 45". Quem decide a origem é o destino do projeto.
        /// </summary>
        Task<TfsImportService.ImportResult> ImportScheduleAsync(int rootId, IProgress<string>? progress = null);

        /// <summary>
        /// Endereço do item no navegador; null quando o destino não tem página para ele. As
        /// telas montavam a URL do DevOps para qualquer id — num projeto do GitProject, o 🔗
        /// abria o work item #N de outro projeto.
        ///
        /// Assíncrono porque, no GitHub, a URL depende do repositório DA ISSUE, que só se sabe
        /// lendo o board — sem repositório na conexão, a versão síncrona devolvia nulo.
        /// </summary>
        Task<string?> ItemUrlAsync(int id);

        /// <summary>
        /// Tipo do item NO SERVIDOR (Story, Feature, Epic...), para as travas de "não excluir
        /// nem revincular o item errado". Null = o item não existe lá.
        /// </summary>
        Task<string?> ItemTypeAsync(int id);

        /// <summary>Histórico de comentários do item (Trâmite), mais novo primeiro.</summary>
        Task<List<TfsImportService.CommentEntry>> LoadCommentsAsync(int id);

        /// <summary>
        /// Backlog (só leitura) das raízes pedidas: raiz → Epic → Feature → Story → Task, já na
        /// ordem do destino e com a profundidade. Sem raiz, vazio — nunca o servidor inteiro.
        ///
        /// Está aqui porque o Backlog só sabia ler o DevOps: no portfólio, os projetos do
        /// GitProject eram procurados no TFS pelo título, e o cronograma do GitProject mandava a
        /// issue como raiz de work item.
        /// </summary>
        Task<List<TfsImportService.BacklogItem>> BuildBacklogAsync(IReadOnlyCollection<int> rootIds);

        /// <summary>Raízes dos projetos pelo título (o nome cadastrado no Portfólio).</summary>
        Task<List<int>> ResolveRootsAsync(IReadOnlyCollection<string> projectNames);

        /// <summary>Cor e ícone por tipo, quando o destino os define; vazio usa o padrão da tela.</summary>
        Task<Dictionary<string, (string Color, string Icon)>> TypeColorsAsync();

        /// <summary>A conexão deste destino está preenchida (para avisar antes de consultar).</summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Pessoas do servidor que podem receber trabalho. Termo vazio = as primeiras
        /// <paramref name="limit"/>. A tela de Pessoas buscava sempre na organização do DevOps,
        /// e num projeto do GitProject trazia gente que nem existe no GitHub.
        /// </summary>
        Task<List<TfsImportService.DevOpsUserInfo>> FindPeopleAsync(string term, int limit);

        /// <summary>Times do servidor (só onde <see cref="NxBackendCapabilities.ServerTeams"/>).</summary>
        Task<List<TfsImportService.DevOpsTeamInfo>> ListTeamsAsync();

        Task<List<TfsImportService.DevOpsUserInfo>> TeamMembersAsync(string teamId);
    }

    /// <summary>
    /// O que a tela de edição precisa saber de um item, já normalizado: a descrição é HTML (o
    /// editor do NXProject fala HTML), o resto é o valor do servidor — nulo quando o destino não
    /// tem aquele campo.
    /// </summary>
    public sealed record NxItemDetails(
        string DescriptionHtml, string AcceptanceCriteria, double? EstimateHours,
        double? CompletedHours, string State, DateTime? StartDate, DateTime? FinishDate)
    {
        public static readonly NxItemDetails Empty =
            new("", "", null, null, "", null, null);
    }

    /// <summary>
    /// Fachada: ponto único por onde as telas pedem o provedor do destino. Resolve pelo destino
    /// configurado (Portfólio) ou pelo escolhido na combo, e devolve algo pronto para usar.
    ///
    /// A fachada ENCAMINHA, não decide: toda a diferença de comportamento mora no provedor. Se um
    /// dia alguém precisar escrever `if` aqui dentro para um destino se comportar diferente, o
    /// lugar certo daquilo é o provedor, não este arquivo.
    /// </summary>
    public static class NxBoardSources
    {
        /// <summary>
        /// Provedor do destino pedido. <paramref name="localProject"/> só é usado pelo Projeto
        /// Local, e <paramref name="git"/> só pelo GitHub — passar o que não serve é inofensivo.
        /// </summary>
        public static INxBoardSource For(NxBackendKind kind, TfsConnectionOptions? devOps = null,
                                         Project? localProject = null, GitProjectOptions? git = null) => kind switch
        {
            NxBackendKind.Local => new LocalBoardSource(
                localProject ?? throw new InvalidOperationException(
                    "Projeto Local exige um cronograma aberto: é dele que o board sai.")),
            NxBackendKind.GitProject => new GitProjectBoardSource(
                git ?? throw new InvalidOperationException("GitProject exige a conexão configurada.")),
            _ => new DevOpsBoardSource(
                devOps ?? throw new InvalidOperationException("Azure DevOps exige a conexão configurada."))
        };
    }

    /// <summary>Azure DevOps: o caminho de sempre, agora atrás da interface.</summary>
    public sealed class DevOpsBoardSource : INxBoardSource
    {
        private readonly TfsConnectionOptions _options;
        public DevOpsBoardSource(TfsConnectionOptions options) => _options = options;

        public NxBackendKind Kind => NxBackendKind.AzureDevOps;
        public NxBackendCapabilities Capabilities => NxBackendCapabilities.For(NxBackendKind.AzureDevOps);

        public Task<List<TfsImportService.SprintInfo>> ListSprintsAsync()
            => TfsImportService.ListSprintsAsync(_options);

        public Task<TfsImportService.SprintBoard> BuildBoardAsync(IReadOnlyCollection<string> iterationPaths)
            => TfsImportService.BuildSprintBoardAsync(_options, iterationPaths?.ToListOrEmpty() ?? new List<string>());

        public Task<TfsImportService.BlockAudit?> LoadBlockAuditAsync(int id)
            => TfsImportService.LoadBlockAuditAsync(_options, id);

        /// <summary>
        /// As leituras partem JUNTAS e são aguardadas no fim: eram seis idas em sequência, e a
        /// edição demorava a abrir por causa delas.
        /// </summary>
        /// <summary>
        /// O DevOps responde a CONTAGEM (é o que a consulta de relações devolve barato); os ids
        /// ficam vazios, e o aviso cai no texto genérico.
        /// </summary>
        public async Task<(int Count, IReadOnlyList<int> Ids)> ChildrenAsync(int id)
            => (await TfsImportService.CountChildrenAsync(_options, id), Array.Empty<int>());

        public Task<TfsImportService.ImportResult> ImportScheduleAsync(int rootId, IProgress<string>? progress = null)
            => TfsImportService.ImportAsync(_options.WithRoot(rootId), progress);

        public Task<string?> ItemUrlAsync(int id)
            => Task.FromResult(id <= 0 || string.IsNullOrWhiteSpace(_options.OrganizationUrl) || string.IsNullOrWhiteSpace(_options.TeamProject)
                ? null
                : $"{_options.OrganizationUrl.TrimEnd('/')}/{Uri.EscapeDataString(_options.TeamProject.Trim())}/_workitems/edit/{id}");

        public Task<string?> ItemTypeAsync(int id) => TfsImportService.GetWorkItemTypeAsync(_options, id);

        public Task<List<TfsImportService.CommentEntry>> LoadCommentsAsync(int id)
            => TfsImportService.GetWorkItemCommentsAsync(_options, id);

        public Task<List<TfsImportService.BacklogItem>> BuildBacklogAsync(IReadOnlyCollection<int> rootIds)
            => TfsImportService.BuildBacklogAsync(_options, rootIds);

        public Task<List<int>> ResolveRootsAsync(IReadOnlyCollection<string> projectNames)
            => TfsImportService.ResolvePortfolioRootIdsAsync(_options, projectNames);

        public Task<Dictionary<string, (string Color, string Icon)>> TypeColorsAsync()
            => TfsImportService.ListWorkItemTypeColorsAsync(_options);

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.OrganizationUrl)
                                    && !string.IsNullOrWhiteSpace(_options.TeamProject)
                                    && !string.IsNullOrWhiteSpace(_options.PersonalAccessToken);

        public Task<List<TfsImportService.DevOpsUserInfo>> FindPeopleAsync(string term, int limit)
            => string.IsNullOrWhiteSpace(term)
                ? TfsImportService.FetchOrgUsersAsync(_options, limit)
                : TfsImportService.FetchUsersByFilterAsync(_options, term);

        public Task<List<TfsImportService.DevOpsTeamInfo>> ListTeamsAsync()
            => TfsImportService.FetchTeamsAsync(_options);

        public Task<List<TfsImportService.DevOpsUserInfo>> TeamMembersAsync(string teamId)
            => TfsImportService.FetchTeamMembersAsync(_options, teamId);

        public async Task<NxItemDetails> LoadItemDetailsAsync(int id, string kind)
        {
            if (id <= 0) return NxItemDetails.Empty;

            var desc = TfsImportService.LoadWorkItemDescriptionHtmlAsync(_options, id);
            var horas = kind != "Feature" ? TfsImportService.GetWorkItemHoursAsync(_options, id) : null;
            var inicio = kind is "Story" or "Task" ? TfsImportService.GetWorkItemStartDateAsync(_options, id) : null;
            var fim = kind == "Task" ? TfsImportService.GetWorkItemFinishDateAsync(_options, id) : null;
            var ac = kind == "Story" ? TfsImportService.GetWorkItemAcceptanceCriteriaAsync(_options, id) : null;

            var (est, feito, estado) = horas != null ? await horas : (null, null, "");
            return new NxItemDetails(
                await desc ?? "",
                ac != null ? await ac ?? "" : "",
                est, feito, estado,
                inicio != null ? await inicio : null,
                fim != null ? await fim : null);
        }
    }

    /// <summary>Projeto Local: o board sai do cronograma aberto, sem servidor nenhum.</summary>
    public sealed class LocalBoardSource : INxBoardSource
    {
        private readonly Project _project;
        public LocalBoardSource(Project project) => _project = project;

        public NxBackendKind Kind => NxBackendKind.Local;
        public NxBackendCapabilities Capabilities => NxBackendCapabilities.For(NxBackendKind.Local);

        public Task<List<TfsImportService.SprintInfo>> ListSprintsAsync()
            => Task.FromResult(LocalBoardService.Sprints(_project));

        public Task<TfsImportService.SprintBoard> BuildBoardAsync(IReadOnlyCollection<string> iterationPaths)
            => Task.FromResult(LocalBoardService.Build(_project, iterationPaths));

        // O modo local tem o log de BLOCK em disco, mas ele guarda o estado atual, nao a serie
        // de entradas e saidas. Reconstruir periodo exige historico — e inventar um daria numero
        // errado com cara de certo.
        public Task<TfsImportService.BlockAudit?> LoadBlockAuditAsync(int id)
            => Task.FromResult<TfsImportService.BlockAudit?>(null);

        /// <summary>Filhos no cronograma: a propria arvore da atividade.</summary>
        // Sem servidor, não há página do item nem trâmite gravado.
        public Task<string?> ItemUrlAsync(int id) => Task.FromResult<string?>(null);

        public Task<List<TfsImportService.CommentEntry>> LoadCommentsAsync(int id)
            => Task.FromResult(new List<TfsImportService.CommentEntry>());

        /// <summary>O backlog local é a árvore do próprio cronograma, na ordem dele.</summary>
        public Task<List<TfsImportService.BacklogItem>> BuildBacklogAsync(IReadOnlyCollection<int> rootIds)
        {
            var lista = new List<TfsImportService.BacklogItem>();
            var filhos = new HashSet<ProjectTask>(_project.Tasks.SelectMany(t => t.Children));
            void Desce(ProjectTask t, int nivel)
            {
                lista.Add(new TfsImportService.BacklogItem(t.TfsId ?? t.Id, t.Name ?? "", t.TfsType ?? "",
                    t.TfsState ?? "", t.Resources.FirstOrDefault()?.Resource?.Name ?? "",
                    t.EstimatedHours is > 0 ? t.EstimatedHours.Value.ToString("0.##") : "",
                    t.TfsIterationPath ?? "", nivel));
                foreach (var c in t.Children) Desce(c, nivel + 1);
            }
            foreach (var raiz in _project.Tasks.Where(t => !filhos.Contains(t))) Desce(raiz, 0);
            return Task.FromResult(lista);
        }

        public Task<List<int>> ResolveRootsAsync(IReadOnlyCollection<string> projectNames)
            => Task.FromResult(new List<int>());

        public Task<Dictionary<string, (string Color, string Icon)>> TypeColorsAsync()
            => Task.FromResult(new Dictionary<string, (string Color, string Icon)>());

        // Sem servidor: as pessoas são as do próprio cronograma.
        public bool IsConfigured => true;

        public Task<List<TfsImportService.DevOpsUserInfo>> FindPeopleAsync(string term, int limit)
            => Task.FromResult(new List<TfsImportService.DevOpsUserInfo>());

        public Task<List<TfsImportService.DevOpsTeamInfo>> ListTeamsAsync()
            => Task.FromResult(new List<TfsImportService.DevOpsTeamInfo>());

        public Task<List<TfsImportService.DevOpsUserInfo>> TeamMembersAsync(string teamId)
            => Task.FromResult(new List<TfsImportService.DevOpsUserInfo>());

        public Task<string?> ItemTypeAsync(int id)
            => Task.FromResult(_project.Tasks.FirstOrDefault(x => x.TfsId == id)?.TfsType);

        // O cronograma aberto JÁ é o projeto: não há servidor de onde trazê-lo.
        public Task<TfsImportService.ImportResult> ImportScheduleAsync(int rootId, IProgress<string>? progress = null)
            => Task.FromException<TfsImportService.ImportResult>(new InvalidOperationException(NxText.T(
                "Projeto Local não é importado de servidor: o cronograma é o próprio arquivo.",
                "A Local project is not imported from a server: the schedule is the file itself.")));

        public Task<(int Count, IReadOnlyList<int> Ids)> ChildrenAsync(int id)
        {
            var filhos = _project.Tasks.FirstOrDefault(x => x.TfsId == id)?.Children
                ?? new System.Collections.ObjectModel.ObservableCollection<ProjectTask>();
            IReadOnlyList<int> ids = filhos.Where(c => c.TfsId > 0).Select(c => c.TfsId!.Value).ToList();
            return Task.FromResult((filhos.Count, ids));
        }

        public Task<NxItemDetails> LoadItemDetailsAsync(int id, string kind)
        {
            var t = _project.Tasks.FirstOrDefault(x => x.TfsId == id);
            return Task.FromResult(t == null ? NxItemDetails.Empty : new NxItemDetails(
                t.Description ?? "", "", t.EstimatedHours, t.CurrentHours, "",
                // O cronograma guarda o fim EXCLUSIVO; a tela mostra o inclusivo.
                t.Start, t.Finish.AddDays(-1)));
        }
    }

    /// <summary>
    /// GitHub Projects: carrega o projeto inteiro e projeta no board. Aqui é só leitura — a
    /// escrita mora no <see cref="GitProjectSyncService"/>, separada de propósito: o board lê a
    /// cada abertura, e misturar gravação nesse caminho é como se perde o controle do que o
    /// conector toca.
    /// </summary>
    public sealed class GitProjectBoardSource : INxBoardSource
    {
        private readonly GitProjectOptions _options;
        private GitProjectClient.ProjectSnapshot? _cache;

        public GitProjectBoardSource(GitProjectOptions options) => _options = options;

        public NxBackendKind Kind => NxBackendKind.GitProject;
        public NxBackendCapabilities Capabilities => NxBackendCapabilities.For(NxBackendKind.GitProject);

        /// <summary>
        /// Uma carga serve às duas chamadas. A tela pede as sprints e, logo em seguida, o board:
        /// buscar o projeto inteiro duas vezes seria pagar a rede à toa.
        /// </summary>
        private async Task<GitProjectClient.ProjectSnapshot> SnapshotAsync()
            => _cache ??= await new GitProjectClient(_options).LoadAsync();

        /// <summary>Descarta o que está em memória — o 🔄 e o F5 do board passam por aqui.</summary>
        public void Invalidate() => _cache = null;

        public Task<TfsImportService.ImportResult> ImportScheduleAsync(int rootId, IProgress<string>? progress = null)
            => GitProjectImportService.ImportAsync(_options, rootId > 0 ? rootId : 0, progress);

        /// <summary>
        /// A issue mora no repositório DELA; sem o board carregado, vale o repositório da
        /// conexão. Fora de um repositório a issue não tem URL.
        /// </summary>
        public async Task<string?> ItemUrlAsync(int id)
        {
            if (id <= 0) return null;
            var repo = RepositoryOf(id);
            if (string.IsNullOrWhiteSpace(repo))
            {
                try { repo = (await SnapshotAsync()).Items.FirstOrDefault(i => i.Number == id)?.Repository; }
                catch { /* sem o board, ainda vale o repositório da conexão */ }
            }
            if (string.IsNullOrWhiteSpace(repo)) repo = _options.Repository;
            return string.IsNullOrWhiteSpace(repo) || !repo.Contains('/')
                ? null : $"https://github.com/{repo}/issues/{id}";
        }

        public async Task<List<TfsImportService.CommentEntry>> LoadCommentsAsync(int id)
        {
            var item = (await SnapshotAsync()).Items.FirstOrDefault(i => i.Number == id);
            if (item == null) return new List<TfsImportService.CommentEntry>();
            var lista = await new GitProjectClient(_options).IssueCommentsAsync(item.IssueId);
            lista.Sort((a, b) => Nullable.Compare(b.Date, a.Date)); // mais novo primeiro, como no DevOps
            return lista;
        }

        /// <summary>
        /// A árvore sai do snapshot (sub-issues), na ordem do board — que é a ordem do backlog
        /// no GitHub Projects, como o StackRank é no DevOps.
        /// </summary>
        public async Task<List<TfsImportService.BacklogItem>> BuildBacklogAsync(IReadOnlyCollection<int> rootIds)
        {
            var snap = await SnapshotAsync();
            var porNumero = snap.Items.ToDictionary(i => i.Number);
            var filhos = snap.Items.Where(i => i.ParentNumber > 0)
                .GroupBy(i => i.ParentNumber).ToDictionary(g => g.Key, g => g.ToList());
            var lista = new List<TfsImportService.BacklogItem>();
            var visto = new HashSet<int>();
            void Desce(GitProjectItem item, int nivel)
            {
                if (!visto.Add(item.Number)) return; // ciclo de sub-issue não trava a tela
                var tipo = GitProjectBoardService.LevelKindOf(item, porNumero, _options);
                lista.Add(new TfsImportService.BacklogItem(item.Number, item.Title,
                    tipo == "Story" ? "User Story" : tipo, GitProjectBoardService.StateOf(item),
                    item.Assignee, item.Effort is > 0 ? item.Effort.Value.ToString("0.##") : "",
                    item.IterationTitle, nivel));
                if (filhos.TryGetValue(item.Number, out var fs))
                    foreach (var f in fs) Desce(f, nivel + 1);
            }
            foreach (var r in (rootIds ?? Array.Empty<int>()).Where(r => r > 0).Distinct())
                if (porNumero.TryGetValue(r, out var raiz)) Desce(raiz, 0);
            return lista;
        }

        public async Task<List<int>> ResolveRootsAsync(IReadOnlyCollection<string> projectNames)
        {
            var nomes = new HashSet<string>(projectNames.Where(n => !string.IsNullOrWhiteSpace(n)),
                StringComparer.CurrentCultureIgnoreCase);
            return (await SnapshotAsync()).Items.Where(i => nomes.Contains(i.Title.Trim()))
                .Select(i => i.Number).ToList();
        }

        // O GitHub não tem cor por tipo de item: a tela usa a dela.
        public Task<Dictionary<string, (string Color, string Icon)>> TypeColorsAsync()
            => Task.FromResult(new Dictionary<string, (string Color, string Icon)>());

        public bool IsConfigured => _options.IsConfigured;

        /// <summary>
        /// As contas atribuíveis dos repositórios do board (<see cref="GitProjectClient.AssignableAccountsAsync"/>)
        /// — a mesma lista em que o sync procura o responsável. Vai com o LOGIN: é por ele que
        /// <see cref="GitProjectPeopleService.Resolve"/> acha a conta sem depender do nome.
        /// </summary>
        public async Task<List<TfsImportService.DevOpsUserInfo>> FindPeopleAsync(string term, int limit)
        {
            var t = (term ?? "").Trim().TrimStart('@');
            return (await ContasDoBoardAsync(await SnapshotAsync()))
                .Where(c => t.Length == 0
                            || c.Login.Contains(t, StringComparison.CurrentCultureIgnoreCase)
                            || c.Name.Contains(t, StringComparison.CurrentCultureIgnoreCase))
                .Take(limit > 0 ? limit : int.MaxValue)
                .Select(c => new TfsImportService.DevOpsUserInfo(
                    string.IsNullOrWhiteSpace(c.Name) ? c.Login : c.Name, "", c.Login))
                .ToList();
        }

        // Sem times no GitHub Projects: ServerTeams = false, a tela nem oferece.
        public Task<List<TfsImportService.DevOpsTeamInfo>> ListTeamsAsync()
            => Task.FromResult(new List<TfsImportService.DevOpsTeamInfo>());

        public Task<List<TfsImportService.DevOpsUserInfo>> TeamMembersAsync(string teamId)
            => Task.FromResult(new List<TfsImportService.DevOpsUserInfo>());

        /// <summary>O tipo é o nível no board — a mesma regra do import (Story vira "User Story").</summary>
        public async Task<string?> ItemTypeAsync(int id)
        {
            var snap = await SnapshotAsync();
            var porNumero = snap.Items.ToDictionary(i => i.Number);
            if (!porNumero.TryGetValue(id, out var item)) return null;
            var tipo = GitProjectBoardService.LevelKindOf(item, porNumero, _options);
            return tipo == "Story" ? "User Story" : tipo;
        }

        /// <summary>
        /// Filhos do item no projeto do GitHub: sub-issues, contadas no snapshot que o board já
        /// carregou — a árvore inteira veio junto, não há o que perguntar de novo.
        /// </summary>
        public async Task<(int Count, IReadOnlyList<int> Ids)> ChildrenAsync(int id)
        {
            try
            {
                var snap = await SnapshotAsync();
                IReadOnlyList<int> ids = snap.Items.Where(i => i.ParentNumber == id)
                                                   .Select(i => i.Number).OrderBy(n => n).ToList();
                return (ids.Count, ids);
            }
            catch { return (-1, Array.Empty<int>()); }
        }

        /// <summary>
        /// Descrição, critério de aceite, horas e datas do item.
        ///
        /// Quase tudo já veio no snapshot do board — ir ao servidor de novo seria pagar a rede à
        /// toa. Só o CORPO da issue é buscado, porque é texto grande e não entra na consulta do
        /// board; dele sai a descrição e, separada, a seção de critério de aceite que o
        /// NXProject escreve lá (o GitHub não tem esse campo).
        /// </summary>
        public async Task<NxItemDetails> LoadItemDetailsAsync(int id, string kind)
        {
            var snap = await SnapshotAsync();
            var item = snap.Items.FirstOrDefault(i => i.Number == id);
            if (item == null) return NxItemDetails.Empty;

            var corpo = "";
            try { corpo = await new GitProjectClient(_options).IssueBodyAsync(item.IssueId); }
            catch { /* sem o corpo a edicao ainda abre, com os demais campos */ }

            var (descricao, aceite) = GitProjectBoardWriter.SeparaAceiteDoCorpo(corpo);
            return new NxItemDetails(
                TfsImportService.PlainTextToSimpleHtml(descricao),
                aceite,
                item.Effort, item.CurrentHours,
                GitProjectBoardService.StateOf(item),
                item.StartDate, item.FinishDate);
        }

        /// <summary>
        /// Repositório (dono/repo) da issue, lido do que já está em memória — é o que dá a URL do
        /// item no GitHub. Vazio quando o board ainda não foi carregado ou o item não é dele.
        /// </summary>
        public string? RepositoryOf(int number) =>
            _cache?.Items.FirstOrDefault(i => i.Number == number)?.Repository;

        public async Task<List<TfsImportService.SprintInfo>> ListSprintsAsync()
            => GitProjectBoardService.Sprints(await SnapshotAsync());

        public async Task<TfsImportService.SprintBoard> BuildBoardAsync(IReadOnlyCollection<string> iterationPaths)
        {
            var snap = await SnapshotAsync();
            var board = GitProjectBoardService.Build(snap, _options, iterationPaths,
                await AnexosDoRepositorioAsync(snap));

            // QUEM PODE receber trabalho, e não só quem já tem: as contas atribuíveis dos
            // repositórios presentes no board. Sem isto, escolher um responsável numa sprint
            // vazia era impossível — o combo listava apenas as pessoas dos itens carregados, e
            // numa sprint recém-aberta não há nenhuma.
            foreach (var pessoa in await PessoasAtribuiveisAsync(snap))
                if (!board.People.Contains(pessoa, StringComparer.CurrentCultureIgnoreCase))
                    board.People.Add(pessoa);
            board.People.Sort(StringComparer.CurrentCultureIgnoreCase);
            return board;
        }

        /// <summary>
        /// Anexos de todos os itens, lidos UMA vez por carga da árvore do repositório. Falha de
        /// rede devolve vazio: anexo é informação do card, não motivo para o board não abrir.
        /// </summary>
        private async Task<Dictionary<int, List<TfsAttachmentService.TfsAttachmentInfo>>> AnexosDoRepositorioAsync(
            GitProjectClient.ProjectSnapshot snap)
        {
            var todos = new Dictionary<int, List<TfsAttachmentService.TfsAttachmentInfo>>();
            try
            {
                var repos = snap.Items.Select(i => i.Repository)
                    .Where(r => !string.IsNullOrWhiteSpace(r) && r.Contains('/'))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(5)
                    .ToList();
                if (repos.Count == 0 && !string.IsNullOrWhiteSpace(_options.Repository))
                    repos.Add(_options.Repository);

                foreach (var repo in repos)
                {
                    var partes = repo.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (partes.Length != 2) continue;
                    foreach (var kv in await GitProjectAttachmentService.ByItemAsync(
                                 _options, partes[0].Trim(), partes[1].Trim()))
                        if (todos.TryGetValue(kv.Key, out var lista)) lista.AddRange(kv.Value);
                        else todos[kv.Key] = kv.Value;
                }
            }
            catch { /* sem a arvore, o card so nao mostra anexo */ }
            return todos;
        }

        /// <summary>
        /// Contas atribuíveis dos repositórios das issues do board (no máximo cinco repositórios:
        /// é uma conveniência de tela, não vale travar a carga por ela). Falha de rede devolve
        /// lista vazia — o board continua abrindo.
        /// </summary>
        private async Task<List<string>> PessoasAtribuiveisAsync(GitProjectClient.ProjectSnapshot snap)
        {
            try
            {
                return (await ContasDoBoardAsync(snap))
                    .Select(c => string.IsNullOrWhiteSpace(c.Name) ? c.Login : c.Name).ToList();
            }
            catch { return new List<string>(); } // conveniencia: sem as contas, fica a lista de quem ja tem item
        }

        /// <summary>
        /// Contas atribuíveis dos repositórios das issues do board, sem repetir login. Erro de
        /// rede SOBE: quem pede pessoas para cadastrar precisa saber que não veio nada.
        /// </summary>
        private async Task<List<GitProjectPeopleService.Account>> ContasDoBoardAsync(GitProjectClient.ProjectSnapshot snap)
        {
            var contas = new List<GitProjectPeopleService.Account>();
            var repos = snap.Items
                .Select(i => i.Repository)
                .Where(r => !string.IsNullOrWhiteSpace(r) && r.Contains('/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();

            // Sem repositório nas issues (board vazio), o configurado ainda serve.
            if (repos.Count == 0 && !string.IsNullOrWhiteSpace(_options.Repository))
                repos.Add(_options.Repository);

            var cliente = new GitProjectClient(_options);
            foreach (var repo in repos)
            {
                var partes = repo.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (partes.Length != 2) continue;
                foreach (var conta in await cliente.AssignableAccountsAsync(partes[0].Trim(), partes[1].Trim()))
                    if (!contas.Any(c => string.Equals(c.Login, conta.Login, StringComparison.OrdinalIgnoreCase)))
                        contas.Add(conta);
            }
            return contas;
        }

        /// <summary>
        /// A auditoria sai da TIMELINE da issue — label que entra e sai, com hora e autor. Usa o
        /// snapshot ja carregado para achar a issue e so entao faz a consulta do historico, que e
        /// de um item so.
        /// </summary>
        public async Task<TfsImportService.BlockAudit?> LoadBlockAuditAsync(int id)
            => await GitProjectBlockAuditService.LoadAsync(_options, await SnapshotAsync(), id);
    }

    internal static class ListExtensions
    {
        internal static List<string> ToListOrEmpty(this IReadOnlyCollection<string> source)
            => source is List<string> lista ? lista : new List<string>(source);
    }
}
