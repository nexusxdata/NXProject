// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NXProject.Services;

namespace NXProject.Agent
{
    /// <summary>
    /// Porta de entrada do NXProject para agentes (o NXPilot carrega esta DLL da pasta instalada).
    /// Contrato pequeno e estável — JSON de entrada e de saída, sem expor tipos do NXProject — para
    /// o NXPilot não quebrar quando o NXProject mudar por dentro.
    ///
    /// Segurança:
    /// <list type="bullet">
    /// <item>Nenhum segredo é lido aqui: quem chama entrega a conexão, com o PAT/token CIFRADO
    /// (DPAPI do usuário + a entropia do par <see cref="SecretEntropy"/>). Carregar esta DLL não
    /// dá acesso ao PAT do NXProject.</item>
    /// <item>Nunca devolve PAT/token — só resultados.</item>
    /// <item>Antes de gravar confere o Adm_NX em modo RESTRITO: falha técnica nega (a tela do
    /// NXProject libera nesse caso; um agente, não).</item>
    /// <item>Só CRIA itens filhos, pelo gravador do próprio NXProject (campos HH, padrões) — não
    /// apaga nem altera itens existentes.</item>
    /// </list>
    /// </summary>
    public static class NxAgentApi
    {
        /// <summary>Versão do contrato. Mudança incompatível = número novo (o NXPilot confere).</summary>
        public const int ContractVersion = 1;

        /// <summary>Entropia do DPAPI combinada entre NXPilot e NXProject (o PAT chega cifrado com ela).</summary>
        public static readonly byte[] SecretEntropy = Encoding.UTF8.GetBytes("NXPilot<->NXProject/NxAgentApi/v1");

        private const int MaxItemsPerCall = 50;

        /// <summary>
        /// Status da conexão: quem é o dono do PAT/token e se ele pode gravar (Adm_NX restrito) a
        /// partir de <c>itemId</c> (opcional). Entrada: ver <see cref="ReadConnection"/>.
        /// Saída: {ok, contract, backend, user, canWrite?, rule, reason, item?}.
        /// </summary>
        public static async Task<string> StatusAsync(string requestJson, CancellationToken ct = default)
        {
            try
            {
                var req = JsonNode.Parse(requestJson) as JsonObject ?? throw new ArgumentException("JSON inválido.");
                var (kind, options, git) = ReadConnection(req);
                var itemId = Int(req, "itemId");
                var res = new JsonObject { ["ok"] = true, ["contract"] = ContractVersion, ["backend"] = Name(kind) };
                if (kind == NxBackendKind.GitProject)
                {
                    var client = new GitProjectClient(git!);
                    var (login, _) = await client.WhoAmIAsync();
                    res["user"] = login;
                    var snap = await client.LoadAsync();
                    var (allowed, rule, reason) = await GitProjectAdminService.CanSyncRestrictedAsync(git!, snap, client);
                    res["canWrite"] = allowed; res["rule"] = rule; res["reason"] = reason;
                }
                else
                {
                    var keys = await TfsImportService.GetCurrentUserKeysAsync(options, ct);
                    res["user"] = keys.FirstOrDefault() ?? "";
                    if (itemId > 0)
                    {
                        var adm = await TfsImportService.CheckAdmRestrictedForItemAsync(options, itemId, ct);
                        res["canWrite"] = adm.Allowed; res["rule"] = adm.Group; res["reason"] = adm.Reason;
                        res["item"] = new JsonObject { ["id"] = itemId, ["type"] = adm.ItemType, ["title"] = adm.ItemTitle, ["rootId"] = adm.RootId };
                    }
                }
                return res.ToJsonString();
            }
            catch (Exception ex) { return Error(ex.Message); }
        }

        /// <summary>
        /// Cria itens filhos de <c>parentId</c> pelo gravador do NXProject. Entrada: a conexão (ver
        /// <see cref="ReadConnection"/>) + {parentId, items: [{kind, title, description?,
        /// assignedTo?, hours?, start? (yyyy-MM-dd)}]} — kind no vocabulário do DevOps ("Task",
        /// "User Story", "Feature", "Epic"). Confere o Adm_NX (restrito) ANTES; negado, nada é
        /// criado. Saída: {ok, allowed, rule, reason, created: [{id, title, kind, message}], failed: [{title, message}]}.
        /// </summary>
        public static async Task<string> CreateItemsAsync(string requestJson, CancellationToken ct = default)
        {
            try
            {
                var req = JsonNode.Parse(requestJson) as JsonObject ?? throw new ArgumentException("JSON inválido.");
                var (kind, options, git) = ReadConnection(req);
                var parentId = Int(req, "parentId");
                if (parentId <= 0) throw new ArgumentException("parentId é obrigatório.");
                var items = req["items"] as JsonArray ?? throw new ArgumentException("items é obrigatório.");
                if (items.Count == 0) throw new ArgumentException("items está vazio.");
                if (items.Count > MaxItemsPerCall) throw new ArgumentException($"No máximo {MaxItemsPerCall} itens por chamada.");

                // Adm_NX restrito: negado (ou não deu para conferir) → nada é criado.
                bool allowed; string rule, reason; string? iteration = null;
                if (kind == NxBackendKind.GitProject)
                {
                    var client = new GitProjectClient(git!);
                    var snap = await client.LoadAsync();
                    (allowed, rule, reason) = await GitProjectAdminService.CanSyncRestrictedAsync(git!, snap, client);
                }
                else
                {
                    var adm = await TfsImportService.CheckAdmRestrictedForItemAsync(options, parentId, ct);
                    (allowed, rule, reason, iteration) = (adm.Allowed, adm.Group, adm.Reason, adm.IterationPath);
                }
                var res = new JsonObject { ["ok"] = true, ["allowed"] = allowed, ["rule"] = rule, ["reason"] = reason };
                if (!allowed) { res["created"] = new JsonArray(); res["failed"] = new JsonArray(); return res.ToJsonString(); }

                var writer = NxBoardWriters.For(kind, options, null, git);
                var created = new JsonArray();
                var failed = new JsonArray();
                foreach (var node in items)
                {
                    ct.ThrowIfCancellationRequested();
                    if (node is not JsonObject it) continue;
                    var title = Str(it, "title");
                    if (string.IsNullOrWhiteSpace(title)) { failed.Add(new JsonObject { ["title"] = "", ["message"] = "título vazio" }); continue; }
                    var itemKind = Str(it, "kind") is { Length: > 0 } k ? k : "Task";
                    double? hours = it["hours"] is JsonValue hv && double.TryParse(hv.ToJsonString().Trim('"'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h) ? h : null;
                    DateTime? start = Str(it, "start") is { Length: > 0 } sd && DateTime.TryParse(sd, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : null;
                    try
                    {
                        Task<(int Id, string Message)> Create() => writer.CreateChildAsync(itemKind, title, parentId,
                            Str(it, "iterationPath") is { Length: > 0 } ip ? ip : iteration,
                            Str(it, "description"), Str(it, "assignedTo"), hours, start);
                        var (id, message) = await Create();
                        // Pai ainda não visível no board do GitHub: espera mais um ciclo e tenta uma vez.
                        if (id <= 0 && kind == NxBackendKind.GitProject && (message ?? "").Contains($"#{parentId}"))
                        {
                            await GitProjectCreateService.EsperaAparecerAsync(git!, parentId);
                            (id, message) = await Create();
                        }
                        if (id > 0) created.Add(new JsonObject { ["id"] = id, ["title"] = title, ["kind"] = itemKind, ["message"] = message ?? "" });
                        else failed.Add(new JsonObject { ["title"] = title, ["message"] = message ?? "não criou" });
                    }
                    catch (Exception ex) { failed.Add(new JsonObject { ["title"] = title, ["message"] = ex.Message }); }
                }
                res["created"] = created;
                res["failed"] = failed;
                return res.ToJsonString();
            }
            catch (Exception ex) { return Error(ex.Message); }
        }

        /// <summary>
        /// Conexão vinda de quem chama. DevOps: {backend:"devops", org, project, pat (cifrado)}.
        /// GitProject: {backend:"gitproject", owner, ownerIsOrganization, number, token (cifrado)}.
        /// Os NOMES DE CAMPO e padrões vêm da configuração do NXProject (não são segredo); o PAT
        /// e o token, só daqui.
        /// </summary>
        private static (NxBackendKind Kind, TfsConnectionOptions Options, GitProjectOptions? Git) ReadConnection(JsonObject req)
        {
            var options = TfsConnectionStore.Load();
            // O que veio do arquivo de segredo não é usado: a conexão é só a que o chamador passou.
            options.PersonalAccessToken = "";
            if (options.GitProject != null) options.GitProject.Token = "";

            var backend = (Str(req, "backend") ?? "devops").Trim().ToLowerInvariant();
            if (backend is "gitproject" or "github")
            {
                var token = WindowsDataProtection.DecryptWithEntropy(Str(req, "token") ?? "", SecretEntropy);
                if (token.Length == 0) throw new ArgumentException("Token do GitHub ausente ou não decifrável (cifre com a entropia do NxAgentApi, no mesmo usuário do Windows).");
                var git = options.GitProject ?? new GitProjectOptions();
                if (Str(req, "owner") is { Length: > 0 } owner) git.Owner = owner;
                if (req["ownerIsOrganization"] is JsonValue oo && oo.TryGetValue<bool>(out var isOrg)) git.OwnerIsOrganization = isOrg;
                if (Int(req, "number") is > 0 and var n) git.ProjectNumber = n;
                git.Token = token;
                if (string.IsNullOrWhiteSpace(git.Owner) || git.ProjectNumber <= 0) throw new ArgumentException("GitProject: owner e number são obrigatórios.");
                return (NxBackendKind.GitProject, options, git);
            }

            var pat = WindowsDataProtection.DecryptWithEntropy(Str(req, "pat") ?? "", SecretEntropy);
            if (pat.Length == 0) throw new ArgumentException("PAT ausente ou não decifrável (cifre com a entropia do NxAgentApi, no mesmo usuário do Windows).");
            if (Str(req, "org") is { Length: > 0 } org) options.OrganizationUrl = org.TrimEnd('/');
            if (Str(req, "project") is { Length: > 0 } project) options.TeamProject = project;
            options.PersonalAccessToken = pat;
            if (string.IsNullOrWhiteSpace(options.OrganizationUrl) || string.IsNullOrWhiteSpace(options.TeamProject))
                throw new ArgumentException("DevOps: org e project são obrigatórios.");
            return (NxBackendKind.AzureDevOps, options, null);
        }

        private static string Name(NxBackendKind kind) => kind == NxBackendKind.GitProject ? "gitproject" : "devops";

        private static string Error(string message) =>
            new JsonObject { ["ok"] = false, ["contract"] = ContractVersion, ["error"] = message }.ToJsonString();

        private static string? Str(JsonObject o, string key) => o[key] switch
        {
            null => null,
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            var other => other.ToString(),
        };

        private static int Int(JsonObject o, string key) =>
            o[key] is JsonValue v && (v.TryGetValue<int>(out var i) || int.TryParse(v.ToString(), out i)) ? i : 0;
    }
}
