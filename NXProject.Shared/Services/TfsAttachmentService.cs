// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NXProject.Services
{
    /// <summary>
    /// Serviços de anexos para Azure DevOps / TFS.
    /// Mantém o arquivo real no TFS, enquanto a UI do TaskBoard só precisa conhecer o
    /// nome do anexo (e opcionalmente o id do attachment para abrir/baixar).
    ///
    /// O Azure DevOps NAO tem rota "workitems/{id}/attachments". Anexar e em dois passos:
    ///   1) POST {org}/{projeto}/_apis/wit/attachments?fileName=... (corpo = bytes do arquivo)
    ///      → devolve { id, url } do arquivo guardado, ainda solto;
    ///   2) PATCH {org}/_apis/wit/workitems/{id} com a relacao "AttachedFile" apontando para
    ///      essa url → o anexo aparece na aba de anexos da Task.
    /// Listar = ler as relacoes do work item e filtrar "AttachedFile".
    /// </summary>
    public static class TfsAttachmentService
    {
        private const string ApiVersion = "api-version=7.1";
        private const string AttachedFileRel = "AttachedFile";
        private static readonly HttpClient Http = new();

        public sealed record TfsAttachmentInfo(
            string Id,
            string Name,
            long SizeBytes,
            string Url)
        {
            public string DisplayName => Name;
        }

        /// <summary>
        /// Faz o parse de uma lista de anexos. Aceita dois formatos:
        /// - { "value": [ { id, name, size, url } ] } (lista simples);
        /// - o work item com "relations" (rel = "AttachedFile", nome/tamanho em "attributes").
        /// </summary>
        public static List<TfsAttachmentInfo> ParseAttachmentsList(string json)
        {
            var items = new List<TfsAttachmentInfo>();
            if (string.IsNullOrWhiteSpace(json))
                return items;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("value", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in array.EnumerateArray())
                {
                    var id = GetString(el, "id");
                    var name = GetString(el, "name");
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                        continue;
                    items.Add(new TfsAttachmentInfo(id, name, GetLong(el, "size"), GetString(el, "url")));
                }
                return items;
            }

            if (root.TryGetProperty("relations", out var rels) && rels.ValueKind == JsonValueKind.Array)
            {
                foreach (var rel in rels.EnumerateArray())
                {
                    if (!string.Equals(GetString(rel, "rel"), AttachedFileRel, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var url = GetString(rel, "url");
                    var attrs = rel.TryGetProperty("attributes", out var a) ? a : default;
                    var name = attrs.ValueKind == JsonValueKind.Object ? GetString(attrs, "name") : "";
                    var size = attrs.ValueKind == JsonValueKind.Object ? GetLong(attrs, "resourceSize") : 0;
                    // o id do anexo e o ultimo segmento da url (.../_apis/wit/attachments/{guid})
                    var id = url.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    items.Add(new TfsAttachmentInfo(id, string.IsNullOrWhiteSpace(name) ? id : name, size, url));
                }
            }

            return items;
        }

        /// <summary>
        /// Anexos a partir do elemento JSON do work item ja carregado (relacoes "AttachedFile").
        /// Usado na carga do board: le direto do elemento, sem serializar e reparsear o item.
        /// </summary>
        public static List<TfsAttachmentInfo> ParseAttachmentRelations(JsonElement workItem)
        {
            var items = new List<TfsAttachmentInfo>();
            if (workItem.ValueKind != JsonValueKind.Object
                || !workItem.TryGetProperty("relations", out var rels) || rels.ValueKind != JsonValueKind.Array)
                return items;
            foreach (var rel in rels.EnumerateArray())
            {
                if (!string.Equals(GetString(rel, "rel"), AttachedFileRel, StringComparison.OrdinalIgnoreCase))
                    continue;
                var url = GetString(rel, "url");
                var attrs = rel.TryGetProperty("attributes", out var a) ? a : default;
                var name = attrs.ValueKind == JsonValueKind.Object ? GetString(attrs, "name") : "";
                var size = attrs.ValueKind == JsonValueKind.Object ? GetLong(attrs, "resourceSize") : 0;
                var id = url.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
                if (string.IsNullOrWhiteSpace(id)) continue;
                items.Add(new TfsAttachmentInfo(id, string.IsNullOrWhiteSpace(name) ? id : name, size, url));
            }
            return items;
        }

        /// <summary>Lista anexos do Work Item informado (relacoes "AttachedFile").</summary>
        public static async Task<List<TfsAttachmentInfo>> ListAttachmentsAsync(
            TfsConnectionOptions options,
            int workItemId,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(options);

            var url = BuildWorkItemUrl(options, workItemId) + $"?$expand=relations&{ApiVersion}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = CreateBasicAuth(options);

            using var response = await Http.SendAsync(request, ct);
            await EnsureSuccessAsync(response, "listar anexos", ct);

            var json = await response.Content.ReadAsStringAsync(ct);
            return ParseAttachmentsList(json);
        }

        /// <summary>
        /// Faz upload de um arquivo e o liga ao work item. Retorna o anexo criado.
        /// O arquivo permanece no Azure DevOps; a UI deve guardar apenas o nome/id.
        /// </summary>
        public static async Task<TfsAttachmentInfo> UploadAttachmentAsync(
            TfsConnectionOptions options,
            int workItemId,
            string filePath,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Caminho do arquivo é obrigatório.", nameof(filePath));
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Arquivo para upload não encontrado.", filePath);

            var auth = CreateBasicAuth(options);
            var fileName = Path.GetFileName(filePath);
            var size = new FileInfo(filePath).Length;

            // 1) envia o arquivo (fica guardado no DevOps, ainda sem ligacao)
            string attachmentId, attachmentUrl;
            await using (var stream = File.OpenRead(filePath))
            using (var content = new StreamContent(stream))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                using var upload = new HttpRequestMessage(HttpMethod.Post, BuildUploadUrl(options, workItemId, fileName))
                {
                    Content = content
                };
                upload.Headers.Authorization = auth;
                using var upResp = await Http.SendAsync(upload, ct);
                await EnsureSuccessAsync(upResp, "enviar o arquivo", ct);

                using var upDoc = JsonDocument.Parse(await upResp.Content.ReadAsStringAsync(ct));
                attachmentId = GetString(upDoc.RootElement, "id");
                attachmentUrl = GetString(upDoc.RootElement, "url");
            }
            if (string.IsNullOrWhiteSpace(attachmentUrl))
                throw new InvalidOperationException("Upload do anexo não retornou a URL do arquivo no Azure DevOps.");

            // 2) liga o arquivo ao work item (relacao AttachedFile)
            var ops = new object[]
            {
                new
                {
                    op = "add",
                    path = "/relations/-",
                    value = new { rel = AttachedFileRel, url = attachmentUrl, attributes = new { comment = "" } }
                }
            };
            using var link = new HttpRequestMessage(new HttpMethod("PATCH"), BuildWorkItemUrl(options, workItemId) + "?" + ApiVersion)
            {
                Content = new StringContent(JsonSerializer.Serialize(ops), Encoding.UTF8, "application/json-patch+json")
            };
            link.Headers.Authorization = auth;
            using var linkResp = await Http.SendAsync(link, ct);
            await EnsureSuccessAsync(linkResp, "ligar o anexo à Task", ct);

            return new TfsAttachmentInfo(attachmentId, fileName, size, attachmentUrl);
        }

        /// <summary>
        /// Mesmo upload em 2 passos, mas a partir de bytes na memoria: a imagem colada na
        /// descricao ou no tramite nao existe como arquivo em disco.
        /// </summary>
        public static async Task<TfsAttachmentInfo> UploadAttachmentBytesAsync(
            TfsConnectionOptions options,
            int workItemId,
            string fileName,
            byte[] bytes,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (bytes == null || bytes.Length == 0)
                throw new ArgumentException("Conteudo da imagem e obrigatorio.", nameof(bytes));

            var auth = CreateBasicAuth(options);
            string attachmentId, attachmentUrl;
            using (var content = new ByteArrayContent(bytes))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                using var upload = new HttpRequestMessage(HttpMethod.Post, BuildUploadUrl(options, workItemId, fileName))
                {
                    Content = content
                };
                upload.Headers.Authorization = auth;
                using var upResp = await Http.SendAsync(upload, ct);
                await EnsureSuccessAsync(upResp, "enviar a imagem", ct);

                using var upDoc = JsonDocument.Parse(await upResp.Content.ReadAsStringAsync(ct));
                attachmentId = GetString(upDoc.RootElement, "id");
                attachmentUrl = GetString(upDoc.RootElement, "url");
            }
            if (string.IsNullOrWhiteSpace(attachmentUrl))
                throw new InvalidOperationException("Upload da imagem nao retornou a URL no Azure DevOps.");

            var ops = new object[]
            {
                new
                {
                    op = "add",
                    path = "/relations/-",
                    value = new { rel = AttachedFileRel, url = attachmentUrl, attributes = new { comment = "" } }
                }
            };
            using var link = new HttpRequestMessage(new HttpMethod("PATCH"), BuildWorkItemUrl(options, workItemId) + "?" + ApiVersion)
            {
                Content = new StringContent(JsonSerializer.Serialize(ops), Encoding.UTF8, "application/json-patch+json")
            };
            link.Headers.Authorization = auth;
            using var linkResp = await Http.SendAsync(link, ct);
            await EnsureSuccessAsync(linkResp, "ligar a imagem ao work item", ct);

            return new TfsAttachmentInfo(attachmentId, fileName, bytes.LongLength, attachmentUrl);
        }

        private static readonly System.Text.RegularExpressions.Regex InlineImageRegex = new(
            "src\\s*=\\s*(?<q>[\"'])data:image/(?<ext>[a-zA-Z0-9.+-]+);base64,(?<b64>[^\"']+)\\k<q>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>
        /// Troca imagem EMBUTIDA (src="data:image/...;base64,...") por anexo do work item, deixando
        /// no HTML a mesma tag &lt;img&gt; que a tela do DevOps escreve, so que com src de URL.
        /// Motivo: o DevOps higieniza o System.Description e joga fora o src "data:" — a imagem
        /// virava &lt;img alt=""&gt; vazio, sem erro nenhum na API. Imagem cujo upload falhar fica
        /// como estava, para nao perder o conteudo.
        /// </summary>
        public static async Task<string> InlineImagesToAttachmentsAsync(
            TfsConnectionOptions options,
            int workItemId,
            string? html,
            string namePrefix = InlineImageDescriptionPrefix,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(html)) return html ?? string.Empty;
            var matches = InlineImageRegex.Matches(html);
            if (matches.Count == 0) return html;

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var result = html;
            var n = 0;
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                n++;
                byte[] bytes;
                try { bytes = Convert.FromBase64String(m.Groups["b64"].Value.Trim()); }
                catch { continue; }   // base64 quebrado: deixa a tag como esta
                var ext = m.Groups["ext"].Value.ToLowerInvariant();
                if (ext == "jpeg") ext = "jpg";
                if (ext == "svg+xml") ext = "svg";
                var info = await UploadAttachmentBytesAsync(
                    options, workItemId, $"{namePrefix}{stamp}-{n}.{ext}", bytes, ct);
                var sep = info.Url.Contains('?') ? "&" : "?";
                result = result.Replace(
                    m.Value, $"src=\"{info.Url}{sep}fileName={Uri.EscapeDataString(info.Name)}\"");
            }
            return result;
        }

        /// <summary>Prefixos dos anexos que sao imagem COLADA no texto, nao arquivo que o usuario
        /// anexou. Servem para o card nao listar essas imagens junto dos documentos.</summary>
        public const string InlineImageDescriptionPrefix = "nx_description_";
        public const string InlineImageTramitePrefix = "nx_tramite_";

        /// <summary>True quando o anexo e imagem embutida na descricao/tramite pelo NX.</summary>
        public static bool IsInlineImageAttachment(string? fileName)
            => !string.IsNullOrEmpty(fileName)
               && (fileName!.StartsWith(InlineImageDescriptionPrefix, StringComparison.OrdinalIgnoreCase)
                   || fileName.StartsWith(InlineImageTramitePrefix, StringComparison.OrdinalIgnoreCase));

        private static readonly System.Text.RegularExpressions.Regex AttachmentRefRegex = new(
            "_apis/wit/attachments/(?<id>[0-9a-fA-F-]{36})",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg"
        };

        /// <summary>IDs de anexo citados dentro de um HTML (descricao ou tramite).</summary>
        public static HashSet<string> ReferencedAttachmentIds(string? html)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(html)) return ids;
            foreach (System.Text.RegularExpressions.Match m in AttachmentRefRegex.Matches(html))
                ids.Add(m.Groups["id"].Value);
            return ids;
        }

        /// <summary>
        /// Apaga o anexo da imagem que SAIU da descricao. Sem isso, tirar a imagem do texto deixava
        /// o arquivo orfao na lista de anexos da Task. A regra e estreita de proposito: so entra o
        /// anexo que estava na descricao ANTERIOR, nao esta na nova, nao aparece em nenhum tramite
        /// e tem extensao de imagem — documento anexado de proposito nunca e tocado.
        /// Devolve o que foi removido; falha em um anexo nao derruba a gravacao.
        /// </summary>
        public static async Task<List<TfsAttachmentInfo>> CleanupUnusedDescriptionImagesAsync(
            TfsConnectionOptions options,
            int workItemId,
            string? oldHtml,
            string? newHtml,
            IEnumerable<string>? stillUsedHtml = null,
            CancellationToken ct = default)
        {
            var removed = new List<TfsAttachmentInfo>();
            var orphans = ReferencedAttachmentIds(oldHtml);
            if (orphans.Count == 0) return removed;
            orphans.ExceptWith(ReferencedAttachmentIds(newHtml));
            if (stillUsedHtml != null)
                foreach (var h in stillUsedHtml)
                    orphans.ExceptWith(ReferencedAttachmentIds(h));
            if (orphans.Count == 0) return removed;

            var current = await ListAttachmentsAsync(options, workItemId, ct);
            foreach (var att in current)
            {
                if (!orphans.Contains(att.Id)) continue;
                if (!ImageExtensions.Contains(Path.GetExtension(att.Name))) continue;
                try
                {
                    await RemoveAttachmentAsync(options, workItemId, att.Url, ct);
                    removed.Add(att);
                }
                catch { /* anexo orfao que nao saiu nao pode derrubar a gravacao */ }
            }
            return removed;
        }

        /// <summary>
        /// Baixa o conteudo de um anexo (a url devolvida no upload / nas relacoes) para
        /// <paramref name="destinationPath"/>. Sobrescreve o arquivo se ja existir.
        /// </summary>
        public static async Task DownloadAttachmentAsync(
            TfsConnectionOptions options,
            string attachmentUrl,
            string fileName,
            string destinationPath,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (string.IsNullOrWhiteSpace(attachmentUrl))
                throw new ArgumentException("URL do anexo é obrigatória.", nameof(attachmentUrl));

            var sep = attachmentUrl.Contains('?') ? "&" : "?";
            var url = $"{attachmentUrl}{sep}fileName={Uri.EscapeDataString(fileName)}&download=true&{ApiVersion}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = CreateBasicAuth(options);

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            await EnsureSuccessAsync(response, "baixar o anexo", ct);

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = File.Create(destinationPath);
            await source.CopyToAsync(target, ct);
        }

        /// <summary>
        /// Exclui o anexo do work item: remove a relacao "AttachedFile" que aponta para
        /// <paramref name="attachmentUrl"/>. A remocao e por posicao na lista de relacoes, entao
        /// o PATCH leva um "test" da revisao lida — se alguem mudou o item nesse meio tempo, o
        /// DevOps recusa em vez de remover a relacao errada.
        /// </summary>
        public static async Task RemoveAttachmentAsync(
            TfsConnectionOptions options,
            int workItemId,
            string attachmentUrl,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (string.IsNullOrWhiteSpace(attachmentUrl))
                throw new ArgumentException("URL do anexo é obrigatória.", nameof(attachmentUrl));

            var auth = CreateBasicAuth(options);
            var itemUrl = BuildWorkItemUrl(options, workItemId);

            int rev, index = -1;
            using (var get = new HttpRequestMessage(HttpMethod.Get, itemUrl + $"?$expand=relations&{ApiVersion}"))
            {
                get.Headers.Authorization = auth;
                using var resp = await Http.SendAsync(get, ct);
                await EnsureSuccessAsync(resp, "ler os anexos da Task", ct);
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                rev = (int)GetLong(doc.RootElement, "rev");
                if (doc.RootElement.TryGetProperty("relations", out var rels) && rels.ValueKind == JsonValueKind.Array)
                {
                    // Casa pelo ID do anexo (ultimo segmento da URL), nao pela URL inteira: a mesma
                    // relacao pode voltar com host/rota diferentes e a comparacao literal falhava.
                    var wanted = AttachmentIdOf(attachmentUrl);
                    var i = 0;
                    foreach (var rel in rels.EnumerateArray())
                    {
                        if (string.Equals(GetString(rel, "rel"), AttachedFileRel, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(AttachmentIdOf(GetString(rel, "url")), wanted, StringComparison.OrdinalIgnoreCase))
                        { index = i; break; }
                        i++;
                    }
                }
            }
            // Antes isso devolvia sucesso CALADO: o anexo continuava no TFS e o usuario via como
            // excluido. Agora falha com mensagem, e quem chamou decide o que mostrar.
            if (index < 0)
                throw new InvalidOperationException(
                    $"O anexo nao esta mais ligado a Task #{workItemId} no DevOps (pode ja ter sido removido).");

            var ops = new object[]
            {
                new { op = "test", path = "/rev", value = rev },
                new { op = "remove", path = $"/relations/{index}" }
            };
            using var patch = new HttpRequestMessage(new HttpMethod("PATCH"), itemUrl + "?" + ApiVersion)
            {
                Content = new StringContent(JsonSerializer.Serialize(ops), Encoding.UTF8, "application/json-patch+json")
            };
            patch.Headers.Authorization = auth;
            using var patchResp = await Http.SendAsync(patch, ct);
            await EnsureSuccessAsync(patchResp, "excluir o anexo", ct);
        }

        /// <summary>ID do anexo: ultimo segmento da URL, sem query. Usado para casar as relacoes.</summary>
        private static string AttachmentIdOf(string url)
            => (url ?? "").Split('?')[0].TrimEnd('/').Split('/').LastOrDefault() ?? "";

        /// <summary>URL do POST de upload do arquivo (passo 1). O work item nao entra na rota.</summary>
        public static string BuildUploadUrl(TfsConnectionOptions options, int workItemId, string fileName)
        {
            var baseUrl = NormalizeOrgBase(options);
            var project = NormalizeProject(options);
            var encoded = Uri.EscapeDataString(fileName);
            return $"{baseUrl}/{Uri.EscapeDataString(project)}/_apis/wit/attachments?fileName={encoded}&{ApiVersion}";
        }

        /// <summary>URL do work item (sem query), usada para ler relacoes e ligar o anexo.</summary>
        public static string BuildWorkItemUrl(TfsConnectionOptions options, int workItemId)
        {
            var baseUrl = NormalizeOrgBase(options);
            return $"{baseUrl}/_apis/wit/workitems/{workItemId}";
        }

        /// <summary>Falha com a mensagem do DevOps (o EnsureSuccessStatusCode so dizia "404").</summary>
        private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken ct)
        {
            if (response.IsSuccessStatusCode) return;
            var body = await response.Content.ReadAsStringAsync(ct);
            var msg = $"HTTP {(int)response.StatusCode}";
            try
            {
                using var d = JsonDocument.Parse(body);
                if (d.RootElement.TryGetProperty("message", out var m) && m.GetString() is { Length: > 0 } text)
                    msg += " — " + text;
            }
            catch { /* corpo nao-JSON: fica so o status */ }
            throw new InvalidOperationException($"Não foi possível {action}: {msg}");
        }

        private static AuthenticationHeaderValue CreateBasicAuth(TfsConnectionOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.PersonalAccessToken))
                throw new InvalidOperationException("PAT do Azure DevOps não informado para anexos.");

            var token = options.PersonalAccessToken.Trim();
            var bytes = Encoding.ASCII.GetBytes($":{token}");
            return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
        }

        private static string NormalizeOrgBase(TfsConnectionOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.OrganizationUrl))
                throw new InvalidOperationException("URL da organização do Azure DevOps não informada.");

            var baseUrl = options.OrganizationUrl.Trim().TrimEnd('/');
            var project = (options.TeamProject ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(project))
                return baseUrl;

            // Algumas configurações guardam a URL da organização já com o nome do projeto na URL
            // (ex.: https://dev.azure.com/org/projeto). Isso duplicaria o caminho e causaria 404.
            var lastSegment = baseUrl.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (!string.IsNullOrWhiteSpace(lastSegment) &&
                string.Equals(lastSegment, project, StringComparison.OrdinalIgnoreCase))
            {
                var idx = baseUrl.LastIndexOf('/');
                if (idx > 0)
                    return baseUrl.Substring(0, idx);
            }

            return baseUrl;
        }

        private static string NormalizeProject(TfsConnectionOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.TeamProject))
                throw new InvalidOperationException("Nome do Team Project do Azure DevOps não informado.");

            return options.TeamProject.Trim();
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value))
                return string.Empty;

            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
        }

        private static long GetLong(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value))
                return 0;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var parsed))
                return parsed;

            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsedString))
                return parsedString;

            return 0;
        }
    }
}
