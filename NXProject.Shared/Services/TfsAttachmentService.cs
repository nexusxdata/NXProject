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
                    var i = 0;
                    foreach (var rel in rels.EnumerateArray())
                    {
                        if (string.Equals(GetString(rel, "rel"), AttachedFileRel, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(GetString(rel, "url"), attachmentUrl, StringComparison.OrdinalIgnoreCase))
                        { index = i; break; }
                        i++;
                    }
                }
            }
            if (index < 0) return;   // ja nao esta ligado: nada a remover

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
