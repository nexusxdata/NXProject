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
    /// Quem GRAVA o board, seja ele qual for.
    ///
    /// A leitura já tinha a sua fachada (<see cref="NxBoardSources"/>); a escrita não, e a tela
    /// decidia por destino em cada ponto — <c>if (_backend == GitProject)</c> espalhado pelo
    /// TaskBoard. O custo apareceu: criar card pelo botão do card funcionava e pelo "Salvar" não,
    /// porque eram dois caminhos diferentes e só um tinha sido ensinado sobre o GitProject.
    ///
    /// Aqui a tela pede o que quer — "cria este filho", "aplica esta fila" — e o destino resolve
    /// como. Destino novo entra escrevendo esta interface, não revisando a tela.
    /// </summary>
    public interface INxBoardWriter
    {
        NxBackendKind Kind { get; }

        /// <summary>
        /// Cria um item filho e devolve o id dele (0 = não criou, com o motivo em Message).
        /// <paramref name="kind"/> usa os nomes do DevOps ("Epic", "Feature", "User Story",
        /// "Task") — é o vocabulário que o NXProject já fala em toda parte.
        /// </summary>
        Task<(int Id, string Message)> CreateChildAsync(
            string kind, string title, int parentId, string? iterationPath,
            string? description, string? assignedTo, double? effortHours, DateTime? startDate,
            IProgress<string>? progress = null);

        /// <summary>Aplica a fila de pendências do board (estado, horas, datas, ordem…).</summary>
        Task<LocalBoardWriter.Result> ApplyAsync(
            LocalBoardWriter.Pending pending, IProgress<string>? progress = null);

        /// <summary>
        /// Anexa um arquivo ao item e devolve o anexo criado (null com o motivo em Error).
        ///
        /// Cada destino guarda anexo de um jeito: o DevOps tem anexo de work item; o GitHub não
        /// tem — o arquivo vai versionado no repositório, com um comentário na issue apontando
        /// para ele. O botão 📎 da tela não precisa saber disso.
        /// </summary>
        Task<(TfsAttachmentService.TfsAttachmentInfo? Attachment, string Error)> AttachAsync(
            int id, string filePath);

        /// <summary>
        /// Baixa um anexo para <paramref name="destination"/>. Vai junto do anexar porque a URL
        /// só faz sentido para quem a criou: a do DevOps precisa do PAT da organização, a do
        /// GitHub é um arquivo do repositório.
        /// </summary>
        Task<string> DownloadAttachmentAsync(
            TfsAttachmentService.TfsAttachmentInfo attachment, string destination);

        /// <summary>
        /// Exclui o item. Devolve (ok, mensagem) — a mensagem explica a recusa quando o destino
        /// não permite apagar, que é o caso do GitHub sem permissão de administrador.
        /// </summary>
        Task<(bool Ok, string Message)> DeleteAsync(int id);
    }

    /// <summary>Gravador do Azure DevOps.</summary>
    public sealed class DevOpsBoardWriter : INxBoardWriter
    {
        private readonly TfsConnectionOptions _options;
        public DevOpsBoardWriter(TfsConnectionOptions options) => _options = options;
        public NxBackendKind Kind => NxBackendKind.AzureDevOps;

        public Task<(int Id, string Message)> CreateChildAsync(
            string kind, string title, int parentId, string? iterationPath,
            string? description, string? assignedTo, double? effortHours, DateTime? startDate,
            IProgress<string>? progress = null)
            => TfsImportService.CreateChildWorkItemAsync(_options, kind, title, parentId,
                string.IsNullOrWhiteSpace(iterationPath) ? null : iterationPath,
                string.IsNullOrWhiteSpace(description) ? null : TfsImportService.PlainTextToSimpleHtml(description),
                string.IsNullOrWhiteSpace(assignedTo) ? null : assignedTo,
                // Feature e EPIC não levam HH: o esforço deles é o rollup dos filhos.
                kind is "Epic" or "Feature" ? null : effortHours,
                kind == "User Story" ? startDate : null);

        public Task<LocalBoardWriter.Result> ApplyAsync(
            LocalBoardWriter.Pending pending, IProgress<string>? progress = null)
            => throw new NotSupportedException(
                "A gravação em lote do Azure DevOps é feita pelo TaskBoard, fase a fase.");

        public async Task<(TfsAttachmentService.TfsAttachmentInfo? Attachment, string Error)> AttachAsync(
            int id, string filePath)
        {
            try { return (await TfsAttachmentService.UploadAttachmentAsync(_options, id, filePath), ""); }
            catch (Exception ex) { return (null, ex.Message); }
        }

        public async Task<string> DownloadAttachmentAsync(
            TfsAttachmentService.TfsAttachmentInfo attachment, string destination)
        {
            try
            {
                await TfsAttachmentService.DownloadAttachmentAsync(
                    _options, attachment.Url, attachment.Name, destination);
                return "";
            }
            catch (Exception ex) { return ex.Message; }
        }

        public Task<(bool Ok, string Message)> DeleteAsync(int id)
            => TfsImportService.TryDeleteWorkItemAsync(_options, id);
    }

    /// <summary>Gravador do GitProject (GitHub Projects).</summary>
    public sealed class GitProjectBoardWriterFacade : INxBoardWriter
    {
        private readonly GitProjectOptions _options;
        public GitProjectBoardWriterFacade(GitProjectOptions options) => _options = options;
        public NxBackendKind Kind => NxBackendKind.GitProject;

        public async Task<(int Id, string Message)> CreateChildAsync(
            string kind, string title, int parentId, string? iterationPath,
            string? description, string? assignedTo, double? effortHours, DateTime? startDate,
            IProgress<string>? progress = null)
        {
            var avisos = new List<string>();
            // O GitProject chama a Story de "Story"; "User Story" é o nome do DevOps.
            var tipo = kind == "User Story" ? "Story" : kind;
            var (numero, msg) = await GitProjectCreateService.CreateChildAsync(
                _options, parentId, tipo, title, description, progress,
                UltimoSegmento(iterationPath), assignedTo, effortHours, startDate, avisos);

            if (numero <= 0) return (numero, msg);

            if (avisos.Count > 0) msg += " — " + string.Join(" ", avisos);
            // O GitHub leva alguns segundos para publicar o item novo na consulta do projeto;
            // recarregar antes disso faz o card sumir da tela, estando certo no servidor.
            await GitProjectCreateService.EsperaAparecerAsync(_options, numero, progress);
            return (numero, msg);
        }

        public Task<LocalBoardWriter.Result> ApplyAsync(
            LocalBoardWriter.Pending pending, IProgress<string>? progress = null)
            => GitProjectBoardWriter.ApplyAsync(_options, pending, progress);

        /// <summary>
        /// O arquivo vai versionado no repositório da issue, em
        /// <c>nxproject/anexos/tipo_numero/…</c>, e um comentário na issue aponta para ele —
        /// o GitHub não tem anexo de item. O serviço que faz isso já existia; faltava a tela
        /// poder chamá-lo sem saber de qual destino se trata.
        /// </summary>
        public async Task<(TfsAttachmentService.TfsAttachmentInfo? Attachment, string Error)> AttachAsync(
            int id, string filePath)
        {
            try
            {
                var conteudo = await System.IO.File.ReadAllBytesAsync(filePath);
                var nome = System.IO.Path.GetFileName(filePath);
                var snap = await new GitProjectClient(_options).LoadAsync();

                var (ok, url, erro) = await GitProjectAttachmentService.AttachAsync(
                    _options, snap, id, nome, conteudo);
                return ok
                    ? (new TfsAttachmentService.TfsAttachmentInfo(url, nome, conteudo.LongLength, url), "")
                    : (null, erro);
            }
            catch (Exception ex) { return (null, ex.Message); }
        }

        /// <summary>
        /// Apaga a ISSUE no GitHub. É irreversível e exige permissão de administrador do
        /// repositório — quando não há, a mensagem diz isso e sugere fechar o item, que é o
        /// caminho normal por lá.
        /// </summary>
        public async Task<(bool Ok, string Message)> DeleteAsync(int id)
        {
            try
            {
                var cliente = new GitProjectClient(_options);
                var snap = await cliente.LoadAsync();
                var item = snap.Items.FirstOrDefault(i => i.Number == id);
                var issueId = item?.IssueId ?? "";
                var nomeRepo = item?.Repository;
                if (item == null)
                {
                    // Item recém-criado: existe no repositório, mas a consulta do projeto pode levar
                    // minutos para mostrá-lo — procura pelo número (mesma regra de criar filho).
                    (issueId, nomeRepo) = await GitProjectCreateService.PaiPeloNumeroAsync(cliente, snap, _options, id);
                    if (string.IsNullOrEmpty(issueId))
                        return (false, NxText.T($"#{id} não está neste board.", $"#{id} is not on this board."));
                }

                // Os anexos vão junto: no GitHub eles são arquivos do repositório e não morrem
                // com a issue, como morrem no Azure DevOps.
                var repo = (nomeRepo ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
                var apagados = 0;
                if (repo.Length == 2)
                    (apagados, _) = await GitProjectAttachmentService
                        .DeleteAllOfItemAsync(_options, repo[0], repo[1], id);

                await cliente.DeleteIssueAsync(issueId);
                return (true, apagados == 0
                    ? NxText.T($"#{id} excluído.", $"#{id} deleted.")
                    : NxText.T($"#{id} excluído, com {apagados} anexo(s).",
                               $"#{id} deleted, with {apagados} attachment(s)."));
            }
            catch (Exception ex)
            {
                return (false, NxText.T(
                    $"Não deu para excluir #{id}: {ex.Message} No GitHub apagar uma issue exige permissão de "
                    + "administrador do repositório — fechar o item costuma ser o caminho.",
                    $"Could not delete #{id}: {ex.Message} On GitHub deleting an issue requires repository "
                    + "admin permission — closing the item is usually the way."));
            }
        }

        /// <summary>
        /// O anexo é um arquivo do repositório: baixa pelo conteúdo bruto (raw), sem passar pela
        /// API de anexo do DevOps — que pediria o PAT da organização e falharia com uma mensagem
        /// sobre um servidor que não é este.
        /// </summary>
        public async Task<string> DownloadAttachmentAsync(
            TfsAttachmentService.TfsAttachmentInfo attachment, string destination)
        {
            try
            {
                var conteudo = await GitProjectAttachmentService.DownloadAsync(_options, attachment.Url);
                await System.IO.File.WriteAllBytesAsync(destination, conteudo);
                return "";
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>A sprint do GitProject é o título da iteração, não um caminho.</summary>
        private static string UltimoSegmento(string? caminho)
        {
            var v = (caminho ?? "").TrimEnd('\\');
            var i = v.LastIndexOf('\\');
            return i >= 0 ? v[(i + 1)..] : v;
        }
    }

    /// <summary>Gravador do projeto local (o cronograma aberto).</summary>
    public sealed class LocalProjectBoardWriter : INxBoardWriter
    {
        private readonly Project _project;
        public LocalProjectBoardWriter(Project project) => _project = project;
        public NxBackendKind Kind => NxBackendKind.Local;

        public Task<(int Id, string Message)> CreateChildAsync(
            string kind, string title, int parentId, string? iterationPath,
            string? description, string? assignedTo, double? effortHours, DateTime? startDate,
            IProgress<string>? progress = null)
            => Task.FromResult((0, NxText.T(
                "O board do cronograma não cria itens: crie a atividade no próprio cronograma.",
                "The schedule board does not create items: create the activity in the schedule itself.")));

        public Task<LocalBoardWriter.Result> ApplyAsync(
            LocalBoardWriter.Pending pending, IProgress<string>? progress = null)
            => Task.FromResult(LocalBoardWriter.Apply(_project, pending));

        public Task<(TfsAttachmentService.TfsAttachmentInfo? Attachment, string Error)> AttachAsync(
            int id, string filePath)
            => Task.FromResult<(TfsAttachmentService.TfsAttachmentInfo?, string)>((null, NxText.T(
                "O board do cronograma não guarda anexos: o arquivo fica no servidor do item.",
                "The schedule board does not store attachments: files live on the item's server.")));

        public Task<string> DownloadAttachmentAsync(
            TfsAttachmentService.TfsAttachmentInfo attachment, string destination)
            => Task.FromResult(NxText.T(
                "O board do cronograma não guarda anexos.",
                "The schedule board does not store attachments."));

        public Task<(bool Ok, string Message)> DeleteAsync(int id)
            => Task.FromResult((false, NxText.T(
                "Exclua a atividade no próprio cronograma.",
                "Delete the activity in the schedule itself.")));
    }

    /// <summary>
    /// Escolhe o gravador do destino — o par de <see cref="NxBoardSources"/>, que escolhe a
    /// leitura. Quem chama não precisa saber qual é.
    /// </summary>
    public static class NxBoardWriters
    {
        public static INxBoardWriter For(NxBackendKind kind, TfsConnectionOptions options,
            Project? localProject, GitProjectOptions? git) => kind switch
        {
            NxBackendKind.GitProject => new GitProjectBoardWriterFacade(git ?? options.GitProject ?? new GitProjectOptions()),
            // Sem cronograma, Local caía no DevOps e gravava ids locais num work item alheio.
            NxBackendKind.Local => new LocalProjectBoardWriter(localProject ?? throw new InvalidOperationException(
                "Projeto Local exige um cronograma aberto: é nele que o board grava.")),
            _ => new DevOpsBoardWriter(options)
        };
    }
}
