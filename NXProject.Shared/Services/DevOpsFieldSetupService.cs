// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
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
    /// Criação dos campos que o NXProject precisa no Azure DevOps, pela Process API — para não
    /// depender de alguém cadastrar oito campos à mão, sem errar nome nem tipo, em cada
    /// organização nova.
    ///
    /// A regra é conservadora de propósito: este serviço só CRIA o que não existe. Campo que já
    /// existe na organização nunca é alterado — nem o tipo, nem a descrição —, porque ele pode
    /// ser de outra equipe e ter dado em produção. Quando o nome já está ocupado por um campo de
    /// tipo diferente do que o NX precisa, a resposta é avisar para escolher outro nome, não
    /// tentar contornar.
    /// </summary>
    public static class DevOpsFieldSetupService
    {
        private const string ApiVersion = "api-version=7.1";

        /// <summary>
        /// Os endpoints de ESCRITA da Process API (criar campo, ligar campo ao work item) só
        /// existem em preview — com a api-version estável o servidor responde 400 reclamando de
        /// valor faltando, que parece erro de payload e não é.
        /// </summary>
        private const string ProcessApiVersion = "api-version=7.1-preview.2";

        /// <summary>
        /// Prefixo sugerido na tela de instalação. Serve para separar, na lista de campos da
        /// organização, o que o NXProject usa do que é do processo da empresa. É só sugestão: os
        /// nomes padrão continuam sendo os de sempre, para não quebrar quem já instalou.
        /// </summary>
        public const string SuggestedPrefix = NxDevOpsFieldCatalog.SuggestedPrefix;

        /// <summary>
        /// O catálogo dos campos do NXProject. Fica em <see cref="NxDevOpsFieldCatalog"/> porque
        /// a mesma regra serve ao import, à tela de configuração e a este serviço; aqui ele só é
        /// reexposto para quem já chamava por este nome.
        /// </summary>
        public static IReadOnlyList<NxDevOpsFieldCatalog.NxFieldSpec> Catalog => NxDevOpsFieldCatalog.All;

        /// <summary>Como está um campo na organização, do ponto de vista do NXProject.</summary>
        public enum FieldStatus
        {
            /// <summary>Não existe: dá para criar.</summary>
            Missing,
            /// <summary>Existe, com o tipo certo, e já está em todos os work items necessários.</summary>
            Ready,
            /// <summary>Existe com o tipo certo, mas falta em algum work item.</summary>
            NeedsWorkItemTypes,
            /// <summary>Existe com OUTRO tipo: o nome está ocupado, tem que escolher outro.</summary>
            TypeConflict,
            /// <summary>
            /// Existe como número, mas mais estreito do que o NX grava (inteiro onde o NX manda
            /// decimal). O campo FUNCIONA — só não guarda a fração: 7,5h vira 8h ou é recusada.
            /// É aviso, não impedimento: trocar o tipo de um campo em uso é decisão da equipe.
            /// </summary>
            TypeNarrower,
            /// <summary>Não deu para consultar.</summary>
            Unknown
        }

        /// <summary>Resultado da checagem de um campo.</summary>
        public sealed class FieldCheck
        {
            public string Key { get; init; } = "";
            public string Name { get; init; } = "";
            public FieldStatus Status { get; set; } = FieldStatus.Unknown;
            /// <summary>Nome de referência no DevOps (ex.: Custom.HHEstimado), quando existe.</summary>
            public string ReferenceName { get; set; } = "";
            /// <summary>Tipo encontrado, quando existe (para explicar o conflito).</summary>
            public string FoundType { get; set; } = "";
            /// <summary>Work items em que o campo ainda não está e faz falta.</summary>
            public List<string> MissingIn { get; } = new();

            /// <summary>
            /// Work items que NÃO precisam do campo personalizado porque o NXProject usa lá um
            /// campo padrão do DevOps. Texto pronto, no formato "Task: Original Estimate".
            /// </summary>
            public List<string> CoveredByStandard { get; } = new();
            /// <summary>
            /// Nome com que o campo foi achado, quando veio por APELIDO e nao pelo nome digitado.
            /// Vazio quando bateu o nome exato.
            /// </summary>
            public string MatchedByAlias { get; set; } = "";
            /// <summary>Mensagem pronta, quando algo impediu a checagem.</summary>
            public string Note { get; set; } = "";
        }

        /// <summary>Estado do processo da organização — a criação só funciona em processo herdado.</summary>
        public sealed class ProcessInfo
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            /// <summary>"system" (de fábrica) ou "inherited" (herdado).</summary>
            public string CustomizationType { get; set; } = "";
            public bool CanCreateFields => string.Equals(CustomizationType, "inherited",
                StringComparison.OrdinalIgnoreCase);
            /// <summary>Nome do work item -> referenceName no processo (para adicionar o campo).</summary>
            public Dictionary<string, string> WorkItemTypeRefs { get; } =
                new(StringComparer.CurrentCultureIgnoreCase);
            public string Error { get; set; } = "";
        }

        private static HttpClient NewClient(string pat)
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + pat));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return http;
        }

        private static string OrgBase(string org)
        {
            var o = (org ?? "").Trim().TrimEnd('/');
            if (o.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return o;
            return "https://dev.azure.com/" + o;
        }

        /// <summary>
        /// Descobre o processo do projeto e se ele aceita campos novos. Processo de fábrica
        /// (Agile/Scrum/CMMI) é somente leitura: a organização precisa herdá-lo antes, e isso é
        /// decisão do administrador — o NXProject não faz isso por conta própria.
        /// </summary>
        public static async Task<ProcessInfo> LoadProcessAsync(string org, string project, string pat,
            CancellationToken ct = default)
        {
            var info = new ProcessInfo();
            try
            {
                using var http = NewClient(pat);
                var baseUrl = OrgBase(org);

                // 1) O ID do projeto. As propriedades só aceitam o GUID: passar o NOME do
                // projeto devolve HTTP 400, mesmo o nome sendo válido em todo o resto da API.
                var idUrl = $"{baseUrl}/_apis/projects/{Uri.EscapeDataString(project)}?{ApiVersion}";
                string projectId;
                using (var idResp = await http.GetAsync(idUrl, ct))
                {
                    if (!idResp.IsSuccessStatusCode)
                    {
                        info.Error = $"HTTP {(int)idResp.StatusCode} ao ler o projeto \"{project}\"";
                        return info;
                    }
                    using var idDoc = JsonDocument.Parse(await idResp.Content.ReadAsStringAsync(ct));
                    projectId = idDoc.RootElement.TryGetProperty("id", out var pid) ? pid.GetString() ?? "" : "";
                }
                if (string.IsNullOrEmpty(projectId))
                {
                    info.Error = "o DevOps não devolveu o id do projeto";
                    return info;
                }

                // 2) Qual processo o projeto usa (vem nas propriedades do projeto).
                var propUrl = $"{baseUrl}/_apis/projects/{projectId}/properties?api-version=7.1-preview.1";
                using var propResp = await http.GetAsync(propUrl, ct);
                if (!propResp.IsSuccessStatusCode)
                {
                    info.Error = $"HTTP {(int)propResp.StatusCode} ao ler as propriedades do projeto";
                    return info;
                }
                using var propDoc = JsonDocument.Parse(await propResp.Content.ReadAsStringAsync(ct));
                foreach (var p in propDoc.RootElement.GetProperty("value").EnumerateArray())
                {
                    if (!p.TryGetProperty("name", out var n)) continue;
                    if (!string.Equals(n.GetString(), "System.ProcessTemplateType", StringComparison.OrdinalIgnoreCase))
                        continue;
                    info.Id = p.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "";
                }
                if (string.IsNullOrEmpty(info.Id))
                {
                    info.Error = "não foi possível descobrir o processo do projeto";
                    return info;
                }

                // 3) O processo é herdado? Só nele a Process API aceita criar campo.
                var procUrl = $"{baseUrl}/_apis/work/processes/{info.Id}?{ApiVersion}";
                using var procResp = await http.GetAsync(procUrl, ct);
                if (!procResp.IsSuccessStatusCode)
                {
                    info.Error = $"HTTP {(int)procResp.StatusCode} ao ler o processo (precisa de permissão de editar processo)";
                    return info;
                }
                using var procDoc = JsonDocument.Parse(await procResp.Content.ReadAsStringAsync(ct));
                var root = procDoc.RootElement;
                info.Name = root.TryGetProperty("name", out var pn) ? pn.GetString() ?? "" : "";
                info.CustomizationType = root.TryGetProperty("customizationType", out var cz)
                    ? cz.GetString() ?? "" : "";

                // 4) Os tipos de work item DESTE processo, para casar "User Story" -> referenceName.
                var witUrl = $"{baseUrl}/_apis/work/processes/{info.Id}/workitemtypes?{ApiVersion}";
                using var witResp = await http.GetAsync(witUrl, ct);
                if (witResp.IsSuccessStatusCode)
                {
                    using var witDoc = JsonDocument.Parse(await witResp.Content.ReadAsStringAsync(ct));
                    foreach (var w in witDoc.RootElement.GetProperty("value").EnumerateArray())
                    {
                        var name = w.TryGetProperty("name", out var wn) ? wn.GetString() ?? "" : "";
                        var rf = w.TryGetProperty("referenceName", out var wr) ? wr.GetString() ?? "" : "";
                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(rf))
                            info.WorkItemTypeRefs[name] = rf;
                    }
                }
            }
            catch (Exception ex) { info.Error = ex.Message; }
            return info;
        }

        /// <summary>
        /// Checa, campo a campo, o que já existe na organização. É o passo obrigatório antes de
        /// criar: nome ocupado por campo de outro tipo vira <see cref="FieldStatus.TypeConflict"/>,
        /// e a tela pede outro nome em vez de tentar criar e levar erro do servidor.
        /// </summary>
        /// <summary>
        /// Pares "chave do campo + work item" em que a pessoa escolheu usar o campo PADRÃO do
        /// DevOps no lugar do personalizado (ex.: start na Task). Vindo daqui, a detecção para de
        /// cobrar o campo naquele tipo e a criação não o associa.
        /// </summary>
        public sealed record StandardChoice(string Key, string WorkItemType);

        public static async Task<List<FieldCheck>> CheckAsync(string org, string project, string pat,
            IEnumerable<(string Key, string Name)> fields, CancellationToken ct = default,
            IEnumerable<StandardChoice>? useStandard = null)
        {
            var standardChosen = new HashSet<string>(
                (useStandard ?? Enumerable.Empty<StandardChoice>()).Select(c => c.Key + "|" + c.WorkItemType),
                StringComparer.CurrentCultureIgnoreCase);
            var wanted = fields.ToList();
            var result = wanted.Select(f => new FieldCheck { Key = f.Key, Name = f.Name }).ToList();
            var specByKey = Catalog.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);

            try
            {
                using var http = NewClient(pat);
                var baseUrl = OrgBase(org);

                // Todos os campos da organização, de uma vez: nome -> (referenceName, tipo).
                var byName = new Dictionary<string, (string Ref, string Type)>(StringComparer.CurrentCultureIgnoreCase);
                var fieldsUrl = $"{baseUrl}/_apis/wit/fields?{ApiVersion}";
                using (var resp = await http.GetAsync(fieldsUrl, ct))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        foreach (var r in result) r.Note = $"HTTP {(int)resp.StatusCode} ao listar os campos";
                        return result;
                    }
                    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                    foreach (var f in doc.RootElement.GetProperty("value").EnumerateArray())
                    {
                        var name = f.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        var rf = f.TryGetProperty("referenceName", out var r2) ? r2.GetString() ?? "" : "";
                        var tp = f.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                        if (!string.IsNullOrEmpty(name)) byName[name] = (rf, tp);
                    }
                }

                // Campos por tipo de work item, só dos tipos que o catálogo usa.
                var neededTypes = Catalog.SelectMany(c => c.Scope)
                    .Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
                var refsByType = new Dictionary<string, HashSet<string>>(StringComparer.CurrentCultureIgnoreCase);
                foreach (var wit in neededTypes)
                {
                    var url = $"{baseUrl}/{Uri.EscapeDataString(project)}/_apis/wit/workitemtypes/"
                            + $"{Uri.EscapeDataString(wit)}/fields?{ApiVersion}";
                    try
                    {
                        using var resp = await http.GetAsync(url, ct);
                        if (!resp.IsSuccessStatusCode) continue;   // tipo não existe neste projeto
                        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var f in doc.RootElement.GetProperty("value").EnumerateArray())
                            if (f.TryGetProperty("referenceName", out var rn) && rn.GetString() is { } s)
                                set.Add(s);
                        refsByType[wit] = set;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { /* um tipo que falha não invalida os outros */ }
                }

                foreach (var check in result)
                {
                    if (!specByKey.TryGetValue(check.Key, out var spec)) continue;
                    if (string.IsNullOrWhiteSpace(check.Name)) { check.Status = FieldStatus.Missing; continue; }
                    if (!byName.TryGetValue(check.Name, out var found))
                    {
                        // O nome digitado nao existe — mas o campo pode estar la com OUTRO nome
                        // aceito pelo import. Sem olhar os apelidos, a resposta seria "nao existe"
                        // e o passo seguinte criaria um campo duplicado na organizacao.
                        var alias = spec.Aliases.FirstOrDefault(a => byName.ContainsKey(a));
                        if (alias == null) { check.Status = FieldStatus.Missing; continue; }
                        check.MatchedByAlias = alias;
                        found = byName[alias];
                    }
                    check.ReferenceName = found.Ref;
                    check.FoundType = found.Type;
                    if (!TypeMatches(spec.Type, found.Type))
                    {
                        // Inteiro onde o NX grava decimal é um caso à parte: não é nome ocupado
                        // por outra coisa, é o mesmo campo com menos casas. Avisa e segue.
                        check.Status = IsNumericNarrowing(spec.Type, found.Type)
                            ? FieldStatus.TypeNarrower : FieldStatus.TypeConflict;
                        if (check.Status == FieldStatus.TypeConflict) continue;
                    }
                    foreach (var wit in spec.Scope)
                    {
                        // Tipo coberto por campo de fábrica não entra como falta: na Task as horas
                        // são Original Estimate / Completed Work, e criar um campo personalizado ali
                        // só duplicaria o que o DevOps já guarda.
                        var standard = spec.StandardFor(wit);
                        if (standard.Length == 0 && standardChosen.Contains(spec.Key + "|" + wit)
                            && spec.OptionalStandardFor(wit) is { Length: > 0 } chosen)
                            standard = new[] { chosen };
                        if (standard.Length > 0)
                        {
                            check.CoveredByStandard.Add(wit + ": " + string.Join(", ", standard.Select(ShortName)));
                            continue;
                        }
                        if (refsByType.TryGetValue(wit, out var set) && !set.Contains(found.Ref))
                            check.MissingIn.Add(wit);
                    }
                    check.Status = check.MissingIn.Count == 0
                        ? FieldStatus.Ready : FieldStatus.NeedsWorkItemTypes;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                foreach (var r in result) if (r.Status == FieldStatus.Unknown) r.Note = ex.Message;
            }
            return result;
        }

        /// <summary>
        /// O tipo que o DevOps devolve casa com o que o NX precisa? O REST usa nomes um pouco
        /// diferentes conforme o endpoint (double/decimal, dateTime/datetime), então a comparação
        /// é por família, não por string exata.
        /// </summary>
        /// <summary>
        /// O campo é numérico dos dois lados, e o que existe é apenas mais estreito (inteiro) do
        /// que o NX grava (decimal)? Dá para usar; só as frações se perdem.
        /// </summary>
        private static bool IsNumericNarrowing(string needed, string found) =>
            NormType(needed) == "double" && NormType(found) == "integer";

        private static string NormType(string t) => (t ?? "").Trim().ToLowerInvariant() switch
        {
            "double" or "decimal" or "number" => "double",
            "integer" or "int" => "integer",
            "datetime" or "date" => "dateTime",
            "boolean" or "bool" => "boolean",
            _ => "string"
        };

        private static bool TypeMatches(string needed, string found)
        {
            return NormType(needed) == NormType(found);
        }

        /// <summary>Resultado de uma criação, linha a linha, para o log da tela.</summary>
        public sealed class CreateResult
        {
            public string Key { get; init; } = "";
            public string Name { get; init; } = "";
            public bool Ok { get; set; }
            /// <summary>Texto curto do que aconteceu (criado / já existia / erro X).</summary>
            public string Message { get; set; } = "";
        }

        /// <summary>
        /// Cria os campos marcados e os adiciona aos work items do catálogo.
        ///
        /// Nada é criado sem antes passar pelo <see cref="CheckAsync"/>: campo que já existe é
        /// reaproveitado (só entra nos work items que faltam) e nome ocupado por tipo diferente é
        /// recusado com a orientação de trocar o nome. Criar campo não apaga nem altera dado —
        /// mas vale lembrar que o campo passa a existir para TODOS os projetos do processo.
        /// </summary>
        public static async Task<List<CreateResult>> CreateAsync(string org, string project, string pat,
            ProcessInfo process, IEnumerable<(string Key, string Name)> fields,
            IProgress<string>? progress = null, CancellationToken ct = default,
            IEnumerable<StandardChoice>? useStandard = null)
        {
            var standardChosen = new HashSet<string>(
                (useStandard ?? Enumerable.Empty<StandardChoice>()).Select(c => c.Key + "|" + c.WorkItemType),
                StringComparer.CurrentCultureIgnoreCase);
            var wanted = fields.ToList();
            var results = new List<CreateResult>();
            var specByKey = Catalog.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);

            if (!process.CanCreateFields)
            {
                foreach (var f in wanted)
                    results.Add(new CreateResult { Key = f.Key, Name = f.Name, Ok = false,
                        Message = "processo não é herdado — herde o processo no DevOps antes" });
                return results;
            }

            var checks = (await CheckAsync(org, project, pat, wanted, ct, useStandard))
                .ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);

            using var http = NewClient(pat);
            var baseUrl = OrgBase(org);

            foreach (var (key, name) in wanted)
            {
                var res = new CreateResult { Key = key, Name = name };
                results.Add(res);
                if (!specByKey.TryGetValue(key, out var spec)) { res.Message = "campo desconhecido"; continue; }
                if (string.IsNullOrWhiteSpace(name)) { res.Message = "nome vazio"; continue; }
                var check = checks.TryGetValue(key, out var c) ? c : null;

                if (check?.Status == FieldStatus.TypeConflict)
                {
                    res.Message = $"já existe com o tipo {check.FoundType} (o NX precisa de {spec.Type}) — escolha outro nome";
                    continue;
                }

                progress?.Report(name);
                var refName = check?.ReferenceName ?? "";
                try
                {
                    if (string.IsNullOrEmpty(refName))
                    {
                        var body = JsonSerializer.Serialize(new
                        {
                            name,
                            type = spec.Type,
                            description = spec.Purpose
                        });
                        var url = $"{baseUrl}/_apis/work/processes/fields?{ProcessApiVersion}";
                        using var req = new HttpRequestMessage(HttpMethod.Post, url)
                        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                        using var resp = await http.SendAsync(req, ct);
                        var text = await resp.Content.ReadAsStringAsync(ct);
                        if (!resp.IsSuccessStatusCode)
                        {
                            res.Message = Explain((int)resp.StatusCode, text);
                            continue;
                        }
                        using var doc = JsonDocument.Parse(text);
                        refName = doc.RootElement.TryGetProperty("referenceName", out var rn)
                            ? rn.GetString() ?? "" : "";
                        res.Message = "campo criado";
                    }
                    else res.Message = "campo já existia";

                    if (string.IsNullOrEmpty(refName))
                    {
                        res.Message += " — sem referenceName de volta, não deu para ligar aos work items";
                        continue;
                    }

                    var added = new List<string>();
                    var failed = new List<string>();
                    foreach (var wit in spec.Scope)
                    {
                        // Work item coberto por campo de fábrica (sempre, ou por escolha desta
                        // instalação) não recebe o personalizado.
                        if (spec.StandardFor(wit).Length > 0) continue;
                        if (standardChosen.Contains(spec.Key + "|" + wit)) continue;
                        // Só mexe no que falta: o campo já ligado ao work item fica como está.
                        if (check != null && check.Status != FieldStatus.Missing
                            && !check.MissingIn.Contains(wit, StringComparer.CurrentCultureIgnoreCase))
                            continue;
                        if (!process.WorkItemTypeRefs.TryGetValue(wit, out var witRef))
                        { failed.Add($"{wit} (tipo não existe no processo)"); continue; }

                        var body = JsonSerializer.Serialize(new
                        {
                            referenceName = refName,
                            required = false,
                            defaultValue = (string?)null
                        });
                        var url = $"{baseUrl}/_apis/work/processes/{process.Id}/workItemTypes/"
                                + $"{Uri.EscapeDataString(witRef)}/fields?{ProcessApiVersion}";
                        using var req = new HttpRequestMessage(HttpMethod.Post, url)
                        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                        using var resp = await http.SendAsync(req, ct);
                        if (resp.IsSuccessStatusCode) added.Add(wit);
                        else failed.Add($"{wit} ({Explain((int)resp.StatusCode, await resp.Content.ReadAsStringAsync(ct))})");
                    }
                    if (added.Count > 0) res.Message += " · adicionado em: " + string.Join(", ", added);
                    if (failed.Count > 0) res.Message += " · falhou em: " + string.Join(", ", failed);
                    res.Ok = failed.Count == 0;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { res.Message = ex.Message; }
            }
            return results;
        }

        /// <summary>"Microsoft.VSTS.Scheduling.OriginalEstimate" -> "Original Estimate".</summary>
        private static string ShortName(string referenceName)
        {
            var last = (referenceName ?? "").Split('.').LastOrDefault() ?? "";
            return System.Text.RegularExpressions.Regex.Replace(last, "(?<=[a-z])(?=[A-Z])", " ");
        }

        /// <summary>
        /// Traduz a recusa do servidor para uma frase que diz o que fazer. 401/403 é quase sempre
        /// o mesmo caso: o token serve para work item, mas criar campo mexe no PROCESSO e exige
        /// Project Collection Administrator. Nos demais, mostra a mensagem do próprio DevOps, que
        /// costuma nomear o campo ou a regra que barrou.
        /// </summary>
        private static string Explain(int status, string body)
        {
            if (status is 401 or 403)
                return $"HTTP {status}: sem permissão para alterar o processo "
                     + "(precisa de PAT com Work Items read/write/manage e conta Project Collection Administrator)";
            var msg = ServerMessage(body);
            return string.IsNullOrEmpty(msg) ? $"HTTP {status}" : $"HTTP {status}: {msg}";
        }

        /// <summary>O campo "message" da resposta de erro do DevOps, que é o texto útil.</summary>
        private static string ServerMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "";
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("message", out var m) && m.GetString() is { } s)
                    return Trim(s);
            }
            catch { /* resposta que nao e JSON: cai no corpo cru */ }
            return Trim(body);
        }

        private static string Trim(string s) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= 180 ? s : s.Substring(0, 180) + "…");
    }
}
