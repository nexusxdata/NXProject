// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NXProject.Services
{
    /// <summary>
    /// Anexos no GitProject: arquivo versionado no repositório da issue, com o link no item.
    ///
    /// O GitHub **não tem API de anexo de item** — foi medido (`NXTestUnit gitpoc`). Na tela dele
    /// o arquivo vai para um CDN pelo corpo do comentário, e esse caminho não é automatizável. O
    /// substituto é a API de conteúdo do repositório, que versiona o arquivo e devolve link.
    ///
    /// O caminho espelha a HIERARQUIA do backlog, não um monte de ids soltos:
    ///
    ///     nxproject/anexos/project_45/epic_46/story_60/proposta.pdf
    ///
    /// Assim o anexo é achado navegando, como no cronograma, em vez de procurando por número.
    ///
    /// A pasta é `tipo_número` e **não leva o título**. É deliberado: título muda o tempo todo, e
    /// se ele fizesse parte do caminho, renomear um item faria os anexos antigos sumirem do lugar
    /// onde o NXProject vai procurá-los. Tipo e número não mudam.
    ///
    /// Duas consequências que o DevOps não tem, e que precisam ser ditas a quem anexa:
    ///
    ///   • **é versionado para sempre** — apagar o arquivo deixa o histórico do Git. Não é lugar
    ///     para dado pessoal;
    ///   • **quem vê o repositório vê o anexo**, mesmo sem acesso ao board.
    /// </summary>
    public static class GitProjectAttachmentService
    {
        /// <summary>Pasta raiz dos anexos no repositório. Reservada ao NXProject.</summary>
        public const string Root = "nxproject/anexos";

        /// <summary>Um degrau da hierarquia: o nível e o número do item.</summary>
        public sealed record Step(string Level, int Number, string Title);

        /// <summary>
        /// Caminho do anexo no repositório, dos ancestrais até o arquivo: uma pasta por nível,
        /// nomeada `tipo_número`. Caminho estável — nada nele depende de texto editável.
        /// </summary>
        public static string PathFor(IEnumerable<Step> hierarquia, string arquivo)
        {
            var partes = new List<string> { Root };
            foreach (var passo in hierarquia ?? Enumerable.Empty<Step>())
                partes.Add($"{Limpa(passo.Level)}_{passo.Number}");
            partes.Add(NomeDeArquivo(arquivo));
            return string.Join("/", partes);
        }

        /// <summary>
        /// A hierarquia de um item, da raiz até ele. Usa o pai de sub-issue, que é como o GitHub
        /// guarda a árvore; para no primeiro pai fora do board — ele não é deste projeto.
        /// </summary>
        public static List<Step> HierarchyOf(GitProjectItem item,
            IReadOnlyDictionary<int, GitProjectItem> porNumero, GitProjectOptions options)
        {
            var caminho = new List<Step>();
            var atual = item;
            var guarda = 0;
            while (atual != null && guarda++ < 10)
            {
                caminho.Insert(0, new Step(
                    GitProjectBoardService.LevelKindOf(atual, porNumero, options), atual.Number, atual.Title ?? ""));
                atual = atual.ParentNumber > 0 && porNumero.TryGetValue(atual.ParentNumber, out var pai) ? pai : null;
            }
            return caminho;
        }

        /// <summary>
        /// Versiona o arquivo e comenta o link na issue.
        ///
        /// O link vai em COMENTÁRIO, não no corpo da issue: o corpo é de quem a abriu, e o
        /// NXProject reescrevê-lo apagaria o que a pessoa escreveu.
        /// </summary>
        public static async Task<(bool Ok, string Url, string Error)> AttachAsync(
            GitProjectOptions options, GitProjectClient.ProjectSnapshot snap, int numero,
            string nomeArquivo, byte[] conteudo)
        {
            var item = snap?.Items?.FirstOrDefault(i => i.Number == numero);
            if (item == null || snap == null)
                return (false, "", NxText.T("#{0} não está neste board.", "#{0} is not on this board.", numero));

            var cliente = new GitProjectClient(options);
            var repo = await cliente.IssueRepositoryAsync(item.IssueId);
            var partes = (repo ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length != 2)
                return (false, "", NxText.T("Não consegui descobrir o repositório da issue.",
                                            "Could not find the issue's repository."));

            var porNumero = snap.Items.ToDictionary(i => i.Number);
            var caminho = PathFor(HierarchyOf(item, porNumero, options), nomeArquivo);

            var (ok, url, _, erro) = await cliente.PutFileAsync(partes[0], partes[1], caminho,
                Convert.ToBase64String(conteudo), $"NXProject: anexo em #{numero}", jaEmBase64: true);
            if (!ok) return (false, "", erro);

            // Comentário com o link: é o que faz o anexo aparecer PARA QUEM OLHA A ISSUE. Sem
            // isso o arquivo existiria no repositório e ninguém saberia.
            try
            {
                await cliente.AddCommentAsync(item.IssueId,
                    NxText.T("📎 Anexo do NXProject: {0}", "📎 NXProject attachment: {0}", url));
            }
            catch { /* o arquivo já está lá; o comentário é conveniência */ }
            return (true, url, "");
        }

        /// <summary>
        /// Apaga os anexos de um item e devolve quantos saíram.
        ///
        /// Existe porque excluir um item precisa levar os anexos junto: no Azure DevOps o anexo
        /// morre com o work item, e aqui ele é um arquivo versionado do repositório — a pasta
        /// ficaria lá, cheia, apontada por uma issue que não existe mais.
        ///
        /// O conteúdo continua no HISTÓRICO do Git, e isso não é escolha do NXProject: apagar um
        /// arquivo em Git remove a versão atual, não o passado.
        /// </summary>
        public static async Task<(int Apagados, string? Erro)> DeleteAllOfItemAsync(
            GitProjectOptions options, string dono, string repo, int numero)
        {
            try
            {
                var cliente = new GitProjectClient(options);
                var arquivos = await cliente.ListFilesAsync(dono, repo, Root + "/");
                var apagados = 0;

                foreach (var (caminho, _, sha) in arquivos)
                {
                    var partes = caminho.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (partes.Length < 2) continue;
                    // A pasta do item é a penúltima: "tipo_numero".
                    var pasta = partes[^2];
                    var sublinhado = pasta.LastIndexOf('_');
                    if (sublinhado < 0 || !int.TryParse(pasta[(sublinhado + 1)..], out var n) || n != numero)
                        continue;

                    if (await cliente.DeleteFileAsync(dono, repo, caminho, sha,
                            $"NXProject: anexo removido com #{numero}"))
                        apagados++;
                }
                return (apagados, null);
            }
            catch (Exception ex) { return (0, ex.Message); }
        }

        /// <summary>
        /// Baixa um anexo a partir da URL que o <see cref="AttachAsync"/> devolveu
        /// (https://github.com/dono/repo/blob/ref/caminho). A URL é de navegação; o que importa
        /// dela são as quatro partes, e o arquivo vem pela API, com o token — senão repositório
        /// privado não baixaria e público devolveria a página HTML.
        /// </summary>
        public static async Task<byte[]> DownloadAsync(GitProjectOptions options, string url)
        {
            var (dono, repo, referencia, caminho) = PartesDaUrl(url);
            if (dono.Length == 0 || caminho.Length == 0)
                throw new InvalidOperationException(NxText.T(
                    "Não reconheci a URL do anexo: {0}", "Unrecognized attachment URL: {0}", url));
            return await new GitProjectClient(options).GetFileBytesAsync(dono, repo, caminho, referencia);
        }

        /// <summary>Quebra a URL de um arquivo do GitHub em (dono, repo, ref, caminho).</summary>
        public static (string Dono, string Repo, string Ref, string Caminho) PartesDaUrl(string? url)
        {
            var limpa = (url ?? "").Replace("https://github.com/", "")
                                   .Replace("https://raw.githubusercontent.com/", "");
            var partes = limpa.Split('/', StringSplitOptions.RemoveEmptyEntries);
            // dono / repo / blob / ref / caminho...
            if (partes.Length < 5 || !string.Equals(partes[2], "blob", StringComparison.OrdinalIgnoreCase))
                return ("", "", "", "");
            return (partes[0], partes[1], partes[3], string.Join("/", partes.Skip(4)));
        }

        /// <summary>
        /// Anexos de TODOS os itens do repositório, por número do item.
        ///
        /// O caminho é <c>nxproject/anexos/…/tipo_numero/arquivo</c>: o número sai da última
        /// pasta, que é justamente a parte estável do caminho. Uma leitura por carga do board,
        /// não uma por card.
        /// </summary>
        public static async Task<Dictionary<int, List<TfsAttachmentService.TfsAttachmentInfo>>> ByItemAsync(
            GitProjectOptions options, string dono, string repo, string referencia = "main")
        {
            var porItem = new Dictionary<int, List<TfsAttachmentService.TfsAttachmentInfo>>();
            var arquivos = await new GitProjectClient(options).ListFilesAsync(dono, repo, Root + "/");

            foreach (var (caminho, tamanho, _) in arquivos)
            {
                var partes = caminho.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (partes.Length < 2) continue;

                var pastaDoItem = partes[^2];                       // tipo_numero
                var sublinhado = pastaDoItem.LastIndexOf('_');
                if (sublinhado < 0 || !int.TryParse(pastaDoItem[(sublinhado + 1)..], out var numero)) continue;

                var nome = partes[^1];
                var url = $"https://github.com/{dono}/{repo}/blob/{referencia}/{caminho}";
                if (!porItem.TryGetValue(numero, out var lista))
                    porItem[numero] = lista = new List<TfsAttachmentService.TfsAttachmentInfo>();
                lista.Add(new TfsAttachmentService.TfsAttachmentInfo(url, nome, tamanho, url));
            }
            return porItem;
        }

        /// <summary>Nome de pasta legível: sem acento, sem espaço, curto.</summary>
        public static string Slug(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return "";
            var form = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var ch in form)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch)
                    == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
            }
            var s = sb.ToString().Trim('-');
            return s.Length <= 40 ? s : s[..40].Trim('-');
        }

        /// <summary>Nível sem espaço: "User Story" vira "User-Story" e não quebra o caminho.</summary>
        private static string Limpa(string? nivel) =>
            Slug(nivel) is { Length: > 0 } s ? s : "item";

        /// <summary>
        /// Nome do arquivo preservado — é por ele que a pessoa reconhece o anexo —, mas sem o que
        /// quebraria o caminho: barra vira traço, senão o arquivo iria parar noutra pasta.
        /// </summary>
        private static string NomeDeArquivo(string? arquivo)
        {
            var nome = (arquivo ?? "").Trim();
            if (nome.Length == 0) return "anexo";
            foreach (var proibido in new[] { '/', '\\', ':', '?', '*', '"', '<', '>', '|' })
                nome = nome.Replace(proibido, '-');
            // ".." nao pode sobreviver nem no meio do nome: e com ele que se sobe de pasta.
            while (nome.Contains("..")) nome = nome.Replace("..", "-");
            return nome.Trim('.', '-');
        }
    }
}
