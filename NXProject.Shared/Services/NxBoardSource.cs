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

        public async Task<List<TfsImportService.SprintInfo>> ListSprintsAsync()
            => GitProjectBoardService.Sprints(await SnapshotAsync());

        public async Task<TfsImportService.SprintBoard> BuildBoardAsync(IReadOnlyCollection<string> iterationPaths)
            => GitProjectBoardService.Build(await SnapshotAsync(), _options, iterationPaths);

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
