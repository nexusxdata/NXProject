using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NXProject.Models;

namespace NXProject.Services
{
    /// <summary>
    /// Importa um projeto do Azure DevOps / TFS via API REST (PAT) montando a
    /// hierarquia Project -> Epic -> Feature -> Story. Tarefas (Task) sao ignoradas.
    ///
    /// Cada Story recebe o esforco (campo "HH Estimado") convertido de horas para
    /// dias uteis. Se a Story tem data de inicio ("Data_Inicio"), a barra comeca
    /// nela e conta os dias uteis a partir dai; se tem data de fim ("Data_Fim"),
    /// ela e usada diretamente. Sem data de inicio, as Stories sao encadeadas em
    /// sequencia a partir da data do projeto.
    /// </summary>
    public static class TfsImportService
    {
        private const string ApiVersion = "api-version=6.0";
        private static readonly HttpClient Http = new();

        // Nomes de exibicao dos campos customizados procurados, na ordem de
        // preferencia. O rotulo "HH Estimado" no formulario corresponde ao campo
        // "Esforço Estimado"; o inicio/fim sao "Data_Inicio"/"Data_Fim" (com
        // underscore — distintos de "Data Inicio"/"Data Fim"). Casamos pelo nome
        // EXATO (case-insensitive), sem remover espacos/underscores, para nao
        // confundir campos diferentes.
        private static readonly string[] HoursFieldNames =
            { "Esforço Estimado", "Esforco Estimado", "HH Estimado", "HH_Estimado" };
        private static readonly string[] StartFieldNames =
            { "Data_Inicio", "Data Inicio", "DataInicio" };
        private static readonly string[] FinishFieldNames =
            { "Data_Fim", "Data Fim", "DataFim" };

        public static async Task<Project> ImportAsync(
            TfsConnectionOptions options,
            CancellationToken cancellationToken = default)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (!options.IsValid)
                throw new InvalidOperationException("Conexão TFS incompleta: informe organização, projeto, PAT e o ID do work item raiz.");

            var orgBase = options.OrganizationUrl.TrimEnd('/');
            var authHeader = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + options.PersonalAccessToken)));

            // 1) Descobre os reference names dos campos customizados. Usa o nome
            //    configurado (config_nxproject.json) e, se nao achar, tenta os
            //    candidatos conhecidos como fallback.
            var fieldMap = await LoadFieldMapAsync(orgBase, authHeader, cancellationToken);
            var hoursRef = ResolveField(fieldMap, options.EffortFieldName, HoursFieldNames);
            var startRef = ResolveField(fieldMap, options.StartFieldName, StartFieldNames);
            var finishRef = ResolveField(fieldMap, options.FinishFieldName, FinishFieldNames);

            // Sprints (iterations) do projeto. Carrega TODAS para o mapa de datas
            // (ancora das Stories sem data); as numeradas/exibidas serao so as
            // efetivamente usadas pelos work items (definidas apos baixar os itens).
            var allSprints = await LoadIterationsAsync(
                orgBase, options.TeamProject, authHeader, cancellationToken);
            var sprintStarts = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in allSprints)
                if (!string.IsNullOrEmpty(s.Path))
                    sprintStarts[s.Path!] = s.Start;

            // 2) Query recursiva de links hierarquicos a partir da raiz.
            var edges = await LoadHierarchyEdgesAsync(orgBase, options.TeamProject, authHeader, options.RootWorkItemId, cancellationToken);

            // 3) Coleta todos os ids e baixa os campos em lote.
            var allIds = new HashSet<int> { options.RootWorkItemId };
            foreach (var (parent, child) in edges)
            {
                allIds.Add(parent);
                allIds.Add(child);
            }

            var requestedFields = new List<string>
            {
                "System.Id", "System.Title", "System.WorkItemType", "System.State",
                "System.AssignedTo", "System.IterationPath", "System.Description", "System.Tags",
                "Microsoft.VSTS.Common.StackRank"
            };
            if (hoursRef != null) requestedFields.Add(hoursRef);
            if (startRef != null) requestedFields.Add(startRef);
            if (finishRef != null) requestedFields.Add(finishRef);

            var items = await LoadWorkItemsAsync(orgBase, authHeader, allIds, requestedFields, cancellationToken);

            if (!items.TryGetValue(options.RootWorkItemId, out var rootItem))
                throw new InvalidOperationException(
                    $"Work item raiz {options.RootWorkItemId} não encontrado ou sem acesso no projeto '{options.TeamProject}'.");

            // 4) Indexa filhos por pai (e pai por filho, para gravar TfsParentId).
            var childrenByParent = new Dictionary<int, List<int>>();
            var parentByChild = new Dictionary<int, int>();
            foreach (var (parent, child) in edges)
            {
                if (!childrenByParent.TryGetValue(parent, out var list))
                    childrenByParent[parent] = list = new List<int>();
                list.Add(child);
                parentByChild[child] = parent;
            }

            // 5) Monta o projeto NXProject.
            var rootStart = ReadDate(rootItem, startRef);

            // Lê as sprints (iterations) do DevOps para ancorar a numeração: a 1a
            // sprint usada pelo projeto vira "Sprint 1" e as seguintes contam em
            // sequência (2, 3, ...). Para isso, alinhamos a grade do cronograma —
            // início do projeto na 1a sprint e duração = cadência real das sprints.
            var (sprintAnchor, sprintDuration) = ComputeSprintAnchor(items.Values, sprintStarts);

            var project = new Project
            {
                Name = rootItem.Title,
                Description = $"Importado do TFS — {options.TeamProject} (#{options.RootWorkItemId})",
                StartDate = sprintAnchor ?? rootStart ?? DateTime.Today,
                FirstSprintNumber = 1,
                SprintNumberingMode = "Sequencial",
                FilePath = null
            };
            if (sprintDuration.HasValue)
                project.SprintDurationDays = sprintDuration.Value;
            var resourcesByKey = new Dictionary<string, Resource>(StringComparer.OrdinalIgnoreCase);
            AddResourceIfAssigned(project, resourcesByKey, rootItem, options.HoursPerDay);

            // Sprints exibidas/numeradas: so as efetivamente usadas pelos work items,
            // em ordem cronologica, numeradas de 1 em diante (a 1a vira "Sprint 1").
            foreach (var s in SelectUsedSprints(items.Values, allSprints))
                project.Sprints.Add(s);

            var context = new BuildContext
            {
                Items = items,
                ChildrenByParent = childrenByParent,
                HoursRef = hoursRef,
                StartRef = startRef,
                FinishRef = finishRef,
                HoursPerDay = options.HoursPerDay <= 0 ? ProjectCalendarService.WorkingHoursPerDay : options.HoursPerDay,
                ProjectStart = project.StartDate,
                SprintStartByPath = sprintStarts,
                ParentByChild = parentByChild,
                Project = project,
                ResourcesByKey = resourcesByKey
            };

            // Os Epics (filhos diretos da raiz) viram tarefas de nivel 0.
            foreach (var epicId in OrderedChildren(childrenByParent, options.RootWorkItemId, items))
            {
                if (!items.TryGetValue(epicId, out var epic)) continue;
                if (!IsType(epic, "Epic")) continue;

                var epicTask = BuildBranch(context, epicId, level: 0);
                if (epicTask != null)
                    project.Tasks.Add(epicTask);
            }

            // Se nao houver Epics, cai para o nivel imediatamente abaixo da raiz.
            if (project.Tasks.Count == 0)
            {
                foreach (var childId in OrderedChildren(childrenByParent, options.RootWorkItemId, items))
                {
                    var task = BuildBranch(context, childId, level: 0);
                    if (task != null)
                        project.Tasks.Add(task);
                }
            }

            NormalizeIds(project.Tasks);
            foreach (var t in project.Tasks)
                t.RecalcSummary();

            if (project.Tasks.Count == 0)
                throw new InvalidOperationException(
                    "Nenhum Epic/Feature/Story encontrado abaixo do work item raiz informado.");

            return project;
        }

        // ── Sincronizacao (Export -> TFS/DevOps) ─────────────────────────────

        public sealed class SyncReport
        {
            public int Updated;
            public int Created;
            public int Reparented;
            public int Skipped;
            public int NotFound;
            public List<string> Messages = new();
            // Features/Stories que ficaram sem sprint (IterationPath vazio).
            public List<string> WithoutSprint = new();

            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Atualizados: {Updated}");
                if (Created > 0) sb.AppendLine($"Criados no DevOps: {Created}");
                if (Reparented > 0) sb.AppendLine($"Reparentados (parent atualizado): {Reparented}");
                sb.AppendLine($"Sem alteracao: {Skipped}");
                if (NotFound > 0) sb.AppendLine($"Nao encontrados no DevOps: {NotFound}");
                foreach (var m in Messages) sb.AppendLine(m);
                if (WithoutSprint.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"Atividades sem sprint ({WithoutSprint.Count}):");
                    foreach (var name in WithoutSprint)
                        sb.AppendLine($"  • {name}");
                }
                return sb.ToString().TrimEnd();
            }
        }

        /// <summary>
        /// Sincroniza tarefas vinculadas de volta para o DevOps:
        ///  - TfsId == 0 → CRIA o work item (Epic/Feature/Story) e grava o id retornado;
        ///  - TfsId > 0 → atualiza Título/Descrição e horas quando mudaram;
        ///    início só se o TFS já tiver início não-nulo; fim só se o estado encerrado;
        ///    e reparenta no DevOps se o pai hierárquico mudou (validando antes).
        /// </summary>
        public static async Task<SyncReport> SyncAsync(
            Project project, TfsConnectionOptions options, CancellationToken cancellationToken = default)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.OrganizationUrl) ||
                string.IsNullOrWhiteSpace(options.TeamProject) ||
                string.IsNullOrWhiteSpace(options.PersonalAccessToken))
                throw new InvalidOperationException("Conexão TFS incompleta: informe organização, projeto e PAT (use Importar → TFS para configurar).");

            var orgBase = options.OrganizationUrl.TrimEnd('/');
            var auth = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + options.PersonalAccessToken)));

            var fieldMap = await LoadFieldMapAsync(orgBase, auth, cancellationToken);
            var hoursRef = ResolveField(fieldMap, options.EffortFieldName, HoursFieldNames);
            var startRef = ResolveField(fieldMap, options.StartFieldName, StartFieldNames);
            var finishRef = ResolveField(fieldMap, options.FinishFieldName, FinishFieldNames);

            // Recalcula a ordem (StackRank) conforme a árvore do NXProject.
            ApplyDesiredStackRanks(project.Tasks);

            // Top-down: pais antes dos filhos (garante criar o pai antes de criar/reparentar o filho).
            var tasks = new List<ProjectTask>();
            CollectLinkedTasks(project.Tasks, tasks);

            var report = new SyncReport();
            if (tasks.Count == 0)
            {
                report.Messages.Add("Nenhuma tarefa vinculada ao DevOps (clique no ID para vincular ou digite 0 para criar).");
                return report;
            }

            // Lê os itens existentes (TfsId > 0) com relations (para detectar o pai atual).
            var existingIds = tasks.Where(t => t.TfsId!.Value > 0).Select(t => t.TfsId!.Value).ToList();
            var requested = new List<string> { "System.Id", "System.Title", "System.State", "System.Description" };
            if (hoursRef != null) requested.Add(hoursRef);
            if (startRef != null) requested.Add(startRef);
            if (finishRef != null) requested.Add(finishRef);

            var current = existingIds.Count > 0
                ? await LoadWorkItemsAsync(orgBase, auth, existingIds, requested, cancellationToken, expandRelations: true)
                : new Dictionary<int, WorkItem>();

            foreach (var task in tasks)
            {
                try
                {
                    // Pai desejado = pai na hierarquia do NXProject; raiz = work item do projeto.
                    int desiredParent = task.Parent?.TfsId ?? options.RootWorkItemId;

                    if (task.TfsId!.Value == 0)
                    {
                        // CRIAR no DevOps.
                        if (string.IsNullOrWhiteSpace(task.TfsType))
                        {
                            report.Messages.Add($"\"{task.Name}\": defina o Tipo DevOps para criar (clique no ID).");
                            continue;
                        }
                        if (desiredParent <= 0)
                        {
                            report.Messages.Add($"\"{task.Name}\": pai sem vínculo DevOps; vincule/crie o pai primeiro.");
                            continue;
                        }

                        var createOps = BuildCreateOps(task, desiredParent, orgBase, hoursRef, startRef, finishRef);
                        var newId = await CreateWorkItemAsync(orgBase, auth, options.TeamProject, task.TfsType!, createOps, cancellationToken);
                        task.TfsId = newId;
                        task.TfsParentId = desiredParent;
                        report.Created++;
                        continue;
                    }

                    // ATUALIZAR item existente.
                    if (!current.TryGetValue(task.TfsId.Value, out var wi))
                    {
                        report.NotFound++;
                        report.Messages.Add($"#{task.TfsId} ({task.Name}): não encontrado no DevOps.");
                        continue;
                    }

                    var ops = new List<object>();

                    if (!string.Equals((task.Name ?? string.Empty).Trim(), (wi.Title ?? string.Empty).Trim(), StringComparison.Ordinal))
                        ops.Add(PatchAdd("/fields/System.Title", task.Name ?? string.Empty));

                    if (task.Description != null &&
                        !string.Equals(task.Description.Trim(), (wi.Description ?? string.Empty).Trim(), StringComparison.Ordinal))
                        ops.Add(PatchAdd("/fields/System.Description", task.Description));

                    // Tags (ex.: "Block") — sincroniza se o conjunto mudou.
                    if (!TagsEqual(task.Tags, wi.Tags))
                        ops.Add(PatchAdd("/fields/System.Tags", NormalizeTagsForWrite(task.Tags)));

                    // Ordem (StackRank) — sincroniza se o rank desejado mudou.
                    if (task.TfsStackRank.HasValue)
                    {
                        var currentRank = ReadDouble(wi, "Microsoft.VSTS.Common.StackRank");
                        if (currentRank == null || Math.Abs(currentRank.Value - task.TfsStackRank.Value) > 0.0001)
                            ops.Add(PatchAdd("/fields/Microsoft.VSTS.Common.StackRank", task.TfsStackRank.Value));
                    }

                    if (hoursRef != null && task.EstimatedHours.HasValue)
                    {
                        var currentHours = ReadDouble(wi, hoursRef);
                        if (currentHours == null || Math.Abs(currentHours.Value - task.EstimatedHours.Value) > 0.0001)
                            ops.Add(PatchAdd($"/fields/{hoursRef}", task.EstimatedHours.Value));
                    }

                    if (startRef != null)
                    {
                        var currentStart = ReadDate(wi, startRef);
                        if (currentStart != null && currentStart.Value.Date != task.Start.Date)
                            ops.Add(PatchAdd($"/fields/{startRef}", FormatDateForTfs(task.Start)));
                    }

                    // Sprint (System.IterationPath): sincroniza se a sprint escolhida
                    // no NXProject difere da que esta no DevOps.
                    if (!string.IsNullOrWhiteSpace(task.TfsIterationPath) &&
                        !string.Equals(task.TfsIterationPath.Trim(), (wi.IterationPath ?? string.Empty).Trim(),
                            StringComparison.OrdinalIgnoreCase))
                        ops.Add(PatchAdd("/fields/System.IterationPath", task.TfsIterationPath.Trim()));

                    if (!string.IsNullOrWhiteSpace(task.TfsState) &&
                        !string.Equals(task.TfsState.Trim(), wi.State?.Trim() ?? string.Empty, StringComparison.Ordinal))
                    {
                        ops.Add(PatchAdd("/fields/System.State", task.TfsState.Trim()));
                    }

                    var effectiveState = string.IsNullOrWhiteSpace(task.TfsState) ? wi.State : task.TfsState;
                    if (finishRef != null && IsClosedState(effectiveState))
                    {
                        var currentFinish = ReadDate(wi, finishRef);
                        if (currentFinish == null || currentFinish.Value.Date != task.Finish.Date)
                            ops.Add(PatchAdd($"/fields/{finishRef}", FormatDateForTfs(task.Finish)));
                    }

                    // Parent: reparenta SÓ se o pai mudou em relação ao que está no DevOps.
                    var (currentParent, relIndex) = FindParentRelation(wi);
                    bool reparent = desiredParent > 0 && desiredParent != currentParent;
                    if (reparent)
                    {
                        if (relIndex >= 0)
                            ops.Add(new { op = "remove", path = $"/relations/{relIndex}" });
                        ops.Add(new
                        {
                            op = "add",
                            path = "/relations/-",
                            value = new
                            {
                                rel = "System.LinkTypes.Hierarchy-Reverse",
                                url = $"{orgBase}/_apis/wit/workItems/{desiredParent}"
                            }
                        });
                    }

                    if (ops.Count == 0)
                    {
                        report.Skipped++;
                        continue;
                    }

                    await PatchWorkItemAsync(orgBase, auth, task.TfsId.Value, ops, cancellationToken);
                    report.Updated++;
                    if (reparent)
                    {
                        report.Reparented++;
                        task.TfsParentId = desiredParent;
                    }
                }
                catch (Exception ex)
                {
                    report.Messages.Add($"#{task.TfsId} ({task.Name}): erro — {ex.Message}");
                }
            }

            // Aviso final: Features/Stories que ficaram sem sprint associada.
            foreach (var task in tasks)
                if (IsFeatureOrStory(task) && string.IsNullOrWhiteSpace(task.TfsIterationPath))
                    report.WithoutSprint.Add(task.Name);

            return report;
        }

        private static bool IsFeatureOrStory(ProjectTask task) =>
            string.Equals(task.TfsType, "Feature", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(task.TfsType, "Story", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(task.TfsType, "User Story", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Recalcula o StackRank desejado para refletir a ordem do NXProject, por
        /// grupo de irmãos: itens já em ordem crescente mantêm o rank (idempotente);
        /// itens movidos ou novos (id=0) recebem um rank encaixado entre o irmão
        /// anterior e o próximo (ou anterior + passo). Só itens com vínculo DevOps
        /// participam. Muta task.TfsStackRank (que vira o valor a sincronizar).
        /// </summary>
        private static void ApplyDesiredStackRanks(
            System.Collections.ObjectModel.ObservableCollection<ProjectTask> siblings)
        {
            double? prev = null;
            for (int i = 0; i < siblings.Count; i++)
            {
                var child = siblings[i];
                if (child.TfsId.HasValue)
                {
                    double? cur = child.TfsId.Value > 0 ? child.TfsStackRank : null;
                    double desired;
                    if (cur.HasValue && (!prev.HasValue || cur.Value > prev.Value))
                    {
                        desired = cur.Value; // já em ordem -> mantém
                    }
                    else
                    {
                        // Próximo irmão com rank maior que o anterior (limite superior).
                        double? next = null;
                        for (int j = i + 1; j < siblings.Count; j++)
                        {
                            var s = siblings[j];
                            if (s.TfsId is > 0 && s.TfsStackRank.HasValue &&
                                (!prev.HasValue || s.TfsStackRank.Value > prev.Value))
                            { next = s.TfsStackRank.Value; break; }
                        }

                        if (prev.HasValue && next.HasValue && next.Value - prev.Value > 0.0)
                            desired = prev.Value + (next.Value - prev.Value) / 2.0; // encaixa no meio
                        else if (prev.HasValue)
                            desired = prev.Value + 1000.0;                          // encadeia após o anterior
                        else if (next.HasValue)
                            desired = next.Value - 1000.0;                          // antes do primeiro com rank
                        else
                            desired = 1000000000.0;                                 // grupo sem nenhum rank
                    }

                    child.TfsStackRank = desired;
                    prev = desired;
                }

                ApplyDesiredStackRanks(child.Children);
            }
        }

        // Inclui tarefas com TfsId definido (incluindo 0 = criar), em ordem top-down.
        private static void CollectLinkedTasks(
            System.Collections.ObjectModel.ObservableCollection<ProjectTask> tasks, List<ProjectTask> acc)
        {
            foreach (var t in tasks)
            {
                if (t.TfsId.HasValue)
                    acc.Add(t);
                CollectLinkedTasks(t.Children, acc);
            }
        }

        private static List<object> BuildCreateOps(
            ProjectTask task, int parentId, string orgBase,
            string? hoursRef, string? startRef, string? finishRef)
        {
            var ops = new List<object>
            {
                PatchAdd("/fields/System.Title", task.Name ?? "Novo item")
            };

            if (!string.IsNullOrWhiteSpace(task.Description))
                ops.Add(PatchAdd("/fields/System.Description", task.Description!));

            if (!string.IsNullOrWhiteSpace(task.Tags))
                ops.Add(PatchAdd("/fields/System.Tags", NormalizeTagsForWrite(task.Tags)));

            if (!string.IsNullOrWhiteSpace(task.TfsState))
                ops.Add(PatchAdd("/fields/System.State", task.TfsState.Trim()));

            if (task.TfsStackRank.HasValue)
                ops.Add(PatchAdd("/fields/Microsoft.VSTS.Common.StackRank", task.TfsStackRank.Value));

            if (hoursRef != null && task.EstimatedHours.HasValue)
                ops.Add(PatchAdd($"/fields/{hoursRef}", task.EstimatedHours.Value));

            if (startRef != null)
                ops.Add(PatchAdd($"/fields/{startRef}", FormatDateForTfs(task.Start)));

            if (finishRef != null && IsClosedState(task.TfsState))
                ops.Add(PatchAdd($"/fields/{finishRef}", FormatDateForTfs(task.Finish)));

            ops.Add(new
            {
                op = "add",
                path = "/relations/-",
                value = new
                {
                    rel = "System.LinkTypes.Hierarchy-Reverse",
                    url = $"{orgBase}/_apis/wit/workItems/{parentId}"
                }
            });

            return ops;
        }

        // Mapeia o tipo do NXProject para o nome do work item type no DevOps.
        private static string MapWorkItemType(string type) =>
            string.Equals(type, "Story", StringComparison.OrdinalIgnoreCase) ? "User Story" : type;

        private static async Task<int> CreateWorkItemAsync(
            string orgBase, AuthenticationHeaderValue auth, string project, string type,
            List<object> ops, CancellationToken ct)
        {
            var typeName = MapWorkItemType(type);
            var url = $"{orgBase}/{Uri.EscapeDataString(project)}/_apis/wit/workitems/{Uri.EscapeDataString("$" + typeName)}?{ApiVersion}";
            var body = JsonSerializer.Serialize(ops);

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json-patch+json")
            };
            req.Headers.Authorization = auth;
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var doc = await SendAsync(req, ct);
            return doc.RootElement.GetProperty("id").GetInt32();
        }

        private static object PatchAdd(string path, object value) =>
            new { op = "add", path, value };

        private static string[] SplitTags(string? tags) =>
            string.IsNullOrWhiteSpace(tags)
                ? Array.Empty<string>()
                : tags.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        /// <summary>Compara dois conjuntos de tags ignorando ordem, espaços e caixa.</summary>
        private static bool TagsEqual(string? a, string? b)
        {
            var sa = new HashSet<string>(SplitTags(a), StringComparer.OrdinalIgnoreCase);
            var sb = new HashSet<string>(SplitTags(b), StringComparer.OrdinalIgnoreCase);
            return sa.SetEquals(sb);
        }

        /// <summary>Normaliza tags para o formato aceito pelo DevOps ("tag1; tag2").</summary>
        private static string NormalizeTagsForWrite(string? tags) =>
            string.Join("; ", SplitTags(tags));

        private static bool IsClosedState(string? state) =>
            state?.Trim().ToLowerInvariant() switch
            {
                "closed" or "resolved" or "done" or "completed" => true,
                _ => false
            };

        /// <summary>Formata a data como meia-noite local em UTC (ex.: 04/05 BRT -> 2026-05-04T03:00:00Z),
        /// para casar com o formato que o DevOps já usa nesses campos.</summary>
        private static string FormatDateForTfs(DateTime date)
        {
            var offset = TimeZoneInfo.Local.GetUtcOffset(date.Date);
            var local = new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, offset);
            return local.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        }

        private static async Task PatchWorkItemAsync(
            string orgBase, AuthenticationHeaderValue auth, int id, List<object> ops, CancellationToken ct)
        {
            var url = $"{orgBase}/_apis/wit/workitems/{id}?{ApiVersion}";
            var body = JsonSerializer.Serialize(ops);

            using var req = new HttpRequestMessage(new HttpMethod("PATCH"), url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json-patch+json")
            };
            req.Headers.Authorization = auth;
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var doc = await SendAsync(req, ct);
        }

        // ── Construcao da hierarquia ─────────────────────────────────────────

        private sealed class BuildContext
        {
            public Dictionary<int, WorkItem> Items = new();
            public Dictionary<int, List<int>> ChildrenByParent = new();
            public string? HoursRef;
            public string? StartRef;
            public string? FinishRef;
            public double HoursPerDay = 8.0;
            public DateTime ProjectStart;

            // Data de inicio de cada sprint (System.IterationPath -> startDate).
            public Dictionary<string, DateTime> SprintStartByPath = new();

            // Pai (DevOps id) de cada work item, para gravar TfsParentId.
            public Dictionary<int, int> ParentByChild = new();
            public Project Project = null!;
            public Dictionary<string, Resource> ResourcesByKey = new(StringComparer.OrdinalIgnoreCase);

            public int? GetParent(int devOpsId) =>
                ParentByChild.TryGetValue(devOpsId, out var p) ? p : null;

            // Cursor de encadeamento por FILA = (responsavel, sprint). Cada fila
            // comeca na data de inicio da sprint; pessoas diferentes (ou sprints
            // diferentes) correm em paralelo. Assim, mover a Story para outra
            // sprint a faz escorregar para a janela da nova sprint.
            public Dictionary<string, DateTime> CursorByLane = new();

            public DateTime? GetSprintStart(string? iterationPath)
            {
                if (string.IsNullOrWhiteSpace(iterationPath)) return null;
                return SprintStartByPath.TryGetValue(iterationPath.Trim(), out var d) ? d : null;
            }
        }

        /// <summary>
        /// Constroi recursivamente um ramo da arvore:
        /// nivel 0 = Epic, 1 = Feature, 2 = Story. Tasks (e abaixo) sao ignoradas.
        /// </summary>
        private static ProjectTask? BuildBranch(BuildContext ctx, int id, int level)
        {
            if (!ctx.Items.TryGetValue(id, out var item))
                return null;

            // Nivel 2 = Story: nao descemos para Task.
            if (level >= 2)
            {
                if (IsType(item, "Task"))
                    return null;
                return BuildStory(ctx, item, level);
            }

            // Bloqueio derivado: Task filha direta com tag Block (só visão).
            bool summaryBlocked = ctx.ChildrenByParent.TryGetValue(item.Id, out var directChildren) &&
                directChildren.Any(cid => ctx.Items.TryGetValue(cid, out var c) &&
                    IsType(c, "Task") && HasBlockTag(c.Tags));

            var task = new ProjectTask
            {
                Id = id,
                Name = item.Title,
                Level = level,
                IsSummary = true,
                PercentComplete = StateToPercent(item.State),
                TfsId = item.Id,
                TfsParentId = ctx.GetParent(item.Id),
                TfsType = item.WorkItemType,
                TfsState = item.State,
                Description = item.Description,
                Tags = item.Tags,
                BlockedByChild = summaryBlocked,
                TfsStackRank = item.StackRank,
                TfsIterationPath = item.IterationPath
            };
            AssignResource(ctx, task, item);

            foreach (var childId in OrderedChildren(ctx.ChildrenByParent, id, ctx.Items))
            {
                if (ctx.Items.TryGetValue(childId, out var child) && IsType(child, "Task"))
                    continue;

                var childTask = BuildBranch(ctx, childId, level + 1);
                if (childTask != null)
                {
                    childTask.Parent = task;
                    task.Children.Add(childTask);
                }
            }

            if (task.Children.Count > 0)
            {
                task.RecalcSummary();
            }
            else
            {
                // Epic/Feature sem Story: mantem como resumo vazio com datas neutras.
                task.Start = ctx.ProjectStart;
                task.Finish = ctx.ProjectStart;
            }

            return task;
        }

        private static ProjectTask BuildStory(BuildContext ctx, WorkItem item, int level)
        {
            var hours = ReadDouble(item, ctx.HoursRef);
            var explicitStart = ReadDate(item, ctx.StartRef);
            var explicitFinish = ReadDate(item, ctx.FinishRef);

            // HH ausente/vazia -> 1 dia util. HH == 0 -> milestone (duracao 0).
            bool isMilestone = hours.HasValue && hours.Value == 0;
            int workDays = hours.HasValue && hours.Value > 0
                ? Math.Max(1, (int)Math.Ceiling(hours.Value / ctx.HoursPerDay))
                : 1;

            var assignee = string.IsNullOrWhiteSpace(item.Assignee) ? "(sem responsável)" : item.Assignee;

            // Bloqueio derivado: se qualquer Task filha tem a tag Block, a Story
            // aparece bloqueada (só visão — nunca sincronizado de volta).
            bool blockedByChild = false;
            if (ctx.ChildrenByParent.TryGetValue(item.Id, out var taskChildren))
                blockedByChild = taskChildren.Any(cid =>
                    ctx.Items.TryGetValue(cid, out var c) && HasBlockTag(c.Tags));

            // Sem Data_Inicio, a Story ancora no inicio da SPRINT dela. A fila e
            // por (pessoa, sprint): a 1a Story da fila comeca na data de inicio da
            // sprint e as seguintes encadeiam; ao mover a Story para outra sprint,
            // ela cai em outra fila e escorrega para a janela da nova sprint.
            var sprintStart = ctx.GetSprintStart(item.IterationPath) ?? ctx.ProjectStart;
            var laneKey = assignee + " @@ " + (item.IterationPath ?? string.Empty);

            DateTime baseStart = ctx.CursorByLane.TryGetValue(laneKey, out var cursor)
                ? cursor
                : sprintStart;

            DateTime start = explicitStart ?? baseStart;
            var durationHours = hours.HasValue
                ? Math.Max(0.0, hours.Value)
                : ctx.HoursPerDay > 0
                    ? ctx.HoursPerDay
                    : ProjectCalendarService.WorkingHoursPerDay;

            DateTime finish = isMilestone
                ? start
                : (explicitFinish ?? ProjectCalendarService.AddWorkingHours(start, durationHours));
            if (finish < start) finish = isMilestone ? start : ProjectCalendarService.AddWorkingHours(start, durationHours);

            // Avanca a fila (pessoa, sprint) — SO para frente. Uma Story com data
            // explicita anterior nao pode puxar o cursor para tras (senao as
            // proximas se sobreporiam).
            ctx.CursorByLane[laneKey] = finish > baseStart ? finish : baseStart;

            var task = new ProjectTask
            {
                Id = item.Id,
                Name = item.Title,
                Level = level,
                IsSummary = false,
                IsMilestone = isMilestone,
                Start = start,
                Finish = finish,
                EstimatedHours = hours,
                PercentComplete = StateToPercent(item.State),
                TfsId = item.Id,
                TfsParentId = ctx.GetParent(item.Id),
                TfsType = item.WorkItemType,
                TfsState = item.State,
                Description = item.Description,
                Tags = item.Tags,
                BlockedByChild = blockedByChild,
                TfsStackRank = item.StackRank,
                TfsIterationPath = item.IterationPath,
                Notes = $"TFS #{item.Id} · {item.WorkItemType} · {item.State}"
                    + (string.IsNullOrWhiteSpace(item.Assignee) ? "" : $" · {item.Assignee}")
            };
            AssignResource(ctx, task, item, hours);
            return task;
        }

        private static void AssignResource(BuildContext ctx, ProjectTask task, WorkItem item, double? estimatedHours = null)
        {
            var resource = AddResourceIfAssigned(ctx.Project, ctx.ResourcesByKey, item, ctx.HoursPerDay);
            if (resource == null)
                return;

            task.Resources.Add(new TaskResource
            {
                ResourceId = resource.Id,
                Resource = resource,
                AllocationPercent = 100,
                EstimatedHours = estimatedHours
            });
        }

        private static Resource? AddResourceIfAssigned(
            Project project,
            Dictionary<string, Resource> resourcesByKey,
            WorkItem item,
            double hoursPerDay)
        {
            var key = !string.IsNullOrWhiteSpace(item.AssigneeEmail)
                ? item.AssigneeEmail.Trim()
                : item.Assignee.Trim();
            if (string.IsNullOrWhiteSpace(key))
                return null;

            if (resourcesByKey.TryGetValue(key, out var existing))
                return existing;

            var resource = new Resource
            {
                Id = project.Resources.Select(r => r.Id).DefaultIfEmpty(0).Max() + 1,
                Name = string.IsNullOrWhiteSpace(item.AssigneeName) ? key : item.AssigneeName.Trim(),
                Email = string.IsNullOrWhiteSpace(item.AssigneeEmail) ? null : item.AssigneeEmail.Trim(),
                MaxUnitsPerDay = hoursPerDay <= 0 ? ProjectCalendarService.WorkingHoursPerDay : hoursPerDay,
                IsImportedFromTfs = true
            };
            project.Resources.Add(resource);
            resourcesByKey[key] = resource;
            return resource;
        }

        private static IEnumerable<int> OrderedChildren(
            Dictionary<int, List<int>> childrenByParent, int parentId, Dictionary<int, WorkItem> items)
        {
            if (!childrenByParent.TryGetValue(parentId, out var list))
                yield break;

            // Ordena irmãos pelo StackRank (backlog do DevOps); sem rank vai por último.
            var ordered = list
                .OrderBy(id => items.TryGetValue(id, out var w) && w.StackRank.HasValue
                    ? w.StackRank!.Value
                    : double.MaxValue)
                .ThenBy(id => id);

            foreach (var id in ordered)
                yield return id;
        }

        private static void NormalizeIds(System.Collections.ObjectModel.ObservableCollection<ProjectTask> roots)
        {
            int next = 1;
            void Walk(ProjectTask t)
            {
                t.Id = next++;
                foreach (var c in t.Children)
                    Walk(c);
            }
            foreach (var r in roots)
                Walk(r);
        }

        // ── Chamadas REST ────────────────────────────────────────────────────

        private static async Task<Dictionary<string, string>> LoadFieldMapAsync(
            string orgBase, AuthenticationHeaderValue auth, CancellationToken ct)
        {
            var url = $"{orgBase}/_apis/wit/fields?{ApiVersion}";
            using var doc = await GetJsonAsync(url, auth, ct);
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.TryGetProperty("value", out var arr))
            {
                foreach (var f in arr.EnumerateArray())
                {
                    var name = f.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var refName = f.TryGetProperty("referenceName", out var r) ? r.GetString() : null;
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(refName))
                        map[Normalize(name)] = refName!;
                }
            }
            return map;
        }

        /// <summary>
        /// Carrega a arvore de iterations (sprints) do team project. Cada sprint com
        /// startDate vira um <see cref="Sprint"/> com nome (folha), caminho completo
        /// (System.IterationPath, ex.: "Projeto\\Pasta\\Sprint"), inicio e fim. A
        /// numeracao sequencial (1..N) e atribuida depois, na ordem cronologica.
        /// </summary>
        private static async Task<List<Sprint>> LoadIterationsAsync(
            string orgBase, string project, AuthenticationHeaderValue auth, CancellationToken ct)
        {
            var list = new List<Sprint>();
            var url = $"{orgBase}/{Uri.EscapeDataString(project)}/_apis/wit/classificationnodes/iterations?$depth=10&{ApiVersion}";

            JsonDocument doc;
            try { doc = await GetJsonAsync(url, auth, ct); }
            catch { return list; } // sem datas de sprint, cai no inicio do projeto

            using (doc)
                Walk(doc.RootElement, null, list);

            return list;

            static void Walk(JsonElement node, string? prefix, List<Sprint> acc)
            {
                var name = node.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrEmpty(name)) return;

                var path = string.IsNullOrEmpty(prefix) ? name : prefix + "\\" + name;

                if (node.TryGetProperty("attributes", out var attrs) &&
                    attrs.ValueKind == JsonValueKind.Object &&
                    attrs.TryGetProperty("startDate", out var sd) &&
                    sd.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(sd.GetString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var start))
                {
                    DateTime end = start.Date;
                    if (attrs.TryGetProperty("finishDate", out var fd) &&
                        fd.ValueKind == JsonValueKind.String &&
                        DateTime.TryParse(fd.GetString(), CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var finish))
                    {
                        end = finish.Date;
                    }

                    acc.Add(new Sprint
                    {
                        DisplayName = name,
                        Path = path,
                        Start = start.Date,
                        End = end
                    });
                }

                if (node.TryGetProperty("children", out var children) &&
                    children.ValueKind == JsonValueKind.Array)
                {
                    foreach (var child in children.EnumerateArray())
                        Walk(child, path, acc);
                }
            }
        }

        /// <summary>
        /// Filtra as iterations para as efetivamente referenciadas pelos work items
        /// e as numera de 1 em diante (ordem cronologica). Assim "quantas sprints o
        /// projeto tem" reflete so as usadas, comecando da 1.
        /// </summary>
        private static List<Sprint> SelectUsedSprints(
            IEnumerable<WorkItem> items, List<Sprint> allSprints)
        {
            // Só Feature/Story têm sprint — Project/Epic não contam para a lista.
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in items)
                if (IsFeatureOrStoryType(it.WorkItemType) && !string.IsNullOrWhiteSpace(it.IterationPath))
                    used.Add(it.IterationPath.Trim());

            return NumberSprints(allSprints.Where(s => s.Path != null && used.Contains(s.Path!)));
        }

        private static bool IsFeatureOrStoryType(string? type) =>
            string.Equals(type, "Feature", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "Story", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "User Story", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Ordena as sprints por inicio, atribui numeros sequenciais (1..N) e,
        /// quando uma sprint nao traz finishDate, fecha a janela no inicio da
        /// proxima (sprints contiguas). Devolve a lista numerada.
        /// </summary>
        private static List<Sprint> NumberSprints(IEnumerable<Sprint> sprints)
        {
            var ordered = sprints
                .GroupBy(s => s.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(s => s.Start)
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                ordered[i].Number = i + 1;
                // Sem fim explicito (ou fim antes do inicio): usa o inicio da proxima.
                if (ordered[i].End <= ordered[i].Start && i + 1 < ordered.Count)
                    ordered[i].End = ordered[i + 1].Start;
            }

            return ordered;
        }

        /// <summary>
        /// A partir dos work items importados e do mapa de datas das sprints,
        /// calcula como ancorar a numeração sequencial das sprints (começando da 1):
        ///  - <c>anchor</c>: data de início da PRIMEIRA sprint efetivamente usada
        ///    pelos work items (a que vira "Sprint 1"); null se nenhum item tem sprint.
        ///  - <c>durationDays</c>: cadência real das sprints (dias corridos) — a
        ///    diferença mais frequente entre inícios de sprints consecutivas, usando
        ///    TODAS as iterations (não só as usadas) para não inflar com sprints
        ///    puladas; null se não há dados suficientes (mantém o padrão do projeto).
        /// </summary>
        private static (DateTime? anchor, int? durationDays) ComputeSprintAnchor(
            IEnumerable<WorkItem> items, Dictionary<string, DateTime> sprintStarts)
        {
            // Início da 1a sprint usada por uma Feature/Story (Project/Epic não contam).
            DateTime? anchor = null;
            foreach (var it in items)
            {
                if (!IsFeatureOrStoryType(it.WorkItemType)) continue;
                if (string.IsNullOrWhiteSpace(it.IterationPath)) continue;
                if (!sprintStarts.TryGetValue(it.IterationPath.Trim(), out var s)) continue;
                if (anchor == null || s.Date < anchor.Value)
                    anchor = s.Date;
            }

            // Cadência: diferença mais comum (em dias corridos) entre inícios de
            // sprints consecutivas, considerando todas as iterations conhecidas.
            int? duration = null;
            var allStarts = sprintStarts.Values
                .Select(d => d.Date).Distinct().OrderBy(d => d).ToList();
            if (allStarts.Count >= 2)
            {
                var gapCounts = new Dictionary<int, int>();
                for (int i = 1; i < allStarts.Count; i++)
                {
                    int gap = (int)Math.Round((allStarts[i] - allStarts[i - 1]).TotalDays);
                    if (gap > 0)
                        gapCounts[gap] = gapCounts.TryGetValue(gap, out var c) ? c + 1 : 1;
                }
                if (gapCounts.Count > 0)
                    duration = gapCounts
                        .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key)
                        .First().Key;
            }

            return (anchor, duration);
        }

        private static async Task<List<(int parent, int child)>> LoadHierarchyEdgesAsync(
            string orgBase, string project, AuthenticationHeaderValue auth, int rootId, CancellationToken ct)
        {
            var wiql =
                "SELECT [System.Id] FROM WorkItemLinks " +
                $"WHERE [Source].[System.Id] = {rootId} " +
                "AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward' " +
                "MODE(Recursive)";

            var url = $"{orgBase}/{Uri.EscapeDataString(project)}/_apis/wit/wiql?{ApiVersion}";
            var body = JsonSerializer.Serialize(new { query = wiql });

            using var doc = await PostJsonAsync(url, body, auth, ct);
            var edges = new List<(int, int)>();
            if (doc.RootElement.TryGetProperty("workItemRelations", out var rels))
            {
                foreach (var rel in rels.EnumerateArray())
                {
                    if (!rel.TryGetProperty("target", out var target) ||
                        target.ValueKind != JsonValueKind.Object)
                        continue;

                    int childId = target.GetProperty("id").GetInt32();

                    if (rel.TryGetProperty("source", out var source) &&
                        source.ValueKind == JsonValueKind.Object &&
                        source.TryGetProperty("id", out var sid))
                    {
                        edges.Add((sid.GetInt32(), childId));
                    }
                }
            }
            return edges;
        }

        private static async Task<Dictionary<int, WorkItem>> LoadWorkItemsAsync(
            string orgBase, AuthenticationHeaderValue auth, IEnumerable<int> ids,
            List<string> fields, CancellationToken ct, bool expandRelations = false)
        {
            var result = new Dictionary<int, WorkItem>();
            var idList = ids.Distinct().ToList();
            const int batchSize = 200;

            for (int i = 0; i < idList.Count; i += batchSize)
            {
                var chunk = idList.Skip(i).Take(batchSize).ToArray();
                var url = $"{orgBase}/_apis/wit/workitemsbatch?{ApiVersion}";
                // workitemsbatch nao aceita "fields" junto com "$expand": com relations,
                // pedimos "all" (traz todos os campos + relations).
                var body = expandRelations
                    ? JsonSerializer.Serialize(new Dictionary<string, object> { ["ids"] = chunk, ["$expand"] = "all" })
                    : JsonSerializer.Serialize(new { ids = chunk, fields });

                using var doc = await PostJsonAsync(url, body, auth, ct);
                if (!doc.RootElement.TryGetProperty("value", out var arr))
                    continue;

                foreach (var wi in arr.EnumerateArray())
                {
                    int id = wi.GetProperty("id").GetInt32();
                    var f = wi.GetProperty("fields");
                    result[id] = new WorkItem
                    {
                        Id = id,
                        Title = GetString(f, "System.Title") ?? $"#{id}",
                        WorkItemType = GetString(f, "System.WorkItemType") ?? string.Empty,
                        State = GetString(f, "System.State") ?? string.Empty,
                        Assignee = GetIdentityName(f, "System.AssignedTo"),
                        AssigneeName = GetIdentityDisplayName(f, "System.AssignedTo"),
                        AssigneeEmail = GetIdentityUniqueName(f, "System.AssignedTo"),
                        IterationPath = GetString(f, "System.IterationPath") ?? string.Empty,
                        Description = GetString(f, "System.Description") ?? string.Empty,
                        Tags = GetString(f, "System.Tags") ?? string.Empty,
                        StackRank = GetDoubleField(f, "Microsoft.VSTS.Common.StackRank"),
                        Fields = f.Clone(),
                        Relations = wi.TryGetProperty("relations", out var relEl) &&
                                    relEl.ValueKind == JsonValueKind.Array
                            ? relEl.Clone()
                            : (JsonElement?)null
                    };
                }
            }
            return result;
        }

        /// <summary>
        /// Acha o link de pai (System.LinkTypes.Hierarchy-Reverse) nas relations de
        /// um work item: devolve o id do pai e o índice da relação (para remoção).
        /// </summary>
        private static (int? parentId, int relIndex) FindParentRelation(WorkItem wi)
        {
            if (wi.Relations is not { ValueKind: JsonValueKind.Array } rels)
                return (null, -1);

            int index = 0;
            foreach (var rel in rels.EnumerateArray())
            {
                var relType = rel.TryGetProperty("rel", out var rt) ? rt.GetString() : null;
                if (string.Equals(relType, "System.LinkTypes.Hierarchy-Reverse", StringComparison.OrdinalIgnoreCase))
                {
                    var url = rel.TryGetProperty("url", out var u) ? u.GetString() : null;
                    var pid = ParseIdFromUrl(url);
                    return (pid, index);
                }
                index++;
            }
            return (null, -1);
        }

        private static int? ParseIdFromUrl(string? url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var slash = url.LastIndexOf('/');
            return slash >= 0 && int.TryParse(url[(slash + 1)..], out var id) ? id : null;
        }

        private static async Task<JsonDocument> GetJsonAsync(
            string url, AuthenticationHeaderValue auth, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = auth;
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return await SendAsync(req, ct);
        }

        private static async Task<JsonDocument> PostJsonAsync(
            string url, string body, AuthenticationHeaderValue auth, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = auth;
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return await SendAsync(req, ct);
        }

        private static async Task<JsonDocument> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            using var resp = await Http.SendAsync(req, ct);
            var content = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                // Uma resposta HTML de login indica PAT invalido/expirado.
                if (content.TrimStart().StartsWith("<", StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Falha de autenticação ({(int)resp.StatusCode}). Verifique o PAT e a URL da organização.");

                throw new InvalidOperationException(
                    $"Erro do TFS ({(int)resp.StatusCode}): {Truncate(content, 500)}");
            }

            if (content.TrimStart().StartsWith("<", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "O TFS respondeu com uma página HTML (provável sessão/login). Verifique o PAT e a URL.");

            return JsonDocument.Parse(content);
        }

        // ── Helpers de campo ─────────────────────────────────────────────────

        private sealed class WorkItem
        {
            public int Id;
            public string Title = string.Empty;
            public string WorkItemType = string.Empty;
            public string State = string.Empty;
            public string Assignee = string.Empty;
            public string AssigneeName = string.Empty;
            public string AssigneeEmail = string.Empty;
            public string IterationPath = string.Empty;
            public string Description = string.Empty;
            public string Tags = string.Empty;
            public double? StackRank;
            public JsonElement Fields;
            public JsonElement? Relations;
        }

        private static bool HasBlockTag(string? tags) =>
            !string.IsNullOrWhiteSpace(tags) &&
            tags.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(t => string.Equals(t, "Block", StringComparison.OrdinalIgnoreCase));

        private static bool IsType(WorkItem item, string type) =>
            string.Equals(item.WorkItemType, type, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Resolve o reference name de um campo. Tenta, em ordem: o nome configurado
        /// (por display name exato; ou direto, se ja for um reference name como
        /// "Custom.xxx"); depois os candidatos de fallback por display name.
        /// </summary>
        private static string? ResolveField(
            Dictionary<string, string> fieldMap, string? configuredName, string[] fallbackCandidates)
        {
            if (!string.IsNullOrWhiteSpace(configuredName))
            {
                var name = configuredName.Trim();
                if (fieldMap.TryGetValue(Normalize(name), out var byDisplay))
                    return byDisplay;
                // Pode ja ser um reference name (ex.: Custom.Data_Inicio, System.X).
                if (name.Contains('.') && fieldMap.Values.Contains(name, StringComparer.OrdinalIgnoreCase))
                    return name;
            }

            foreach (var c in fallbackCandidates)
                if (fieldMap.TryGetValue(Normalize(c), out var refName))
                    return refName;

            return null;
        }

        private static double? ReadDouble(WorkItem item, string? refName)
        {
            if (refName == null) return null;
            if (item.Fields.ValueKind != JsonValueKind.Object) return null;
            if (!item.Fields.TryGetProperty(refName, out var el)) return null;

            switch (el.ValueKind)
            {
                case JsonValueKind.Number:
                    return el.GetDouble();
                case JsonValueKind.String:
                    return double.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
                        || double.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.GetCultureInfo("pt-BR"), out v)
                        ? v
                        : null;
                default:
                    return null;
            }
        }

        private static DateTime? ReadDate(WorkItem item, string? refName)
        {
            if (refName == null) return null;
            if (item.Fields.ValueKind != JsonValueKind.Object) return null;
            if (!item.Fields.TryGetProperty(refName, out var el)) return null;
            if (el.ValueKind != JsonValueKind.String) return null;

            var s = el.GetString();
            if (string.IsNullOrWhiteSpace(s)) return null;
            // O TFS devolve ISO 8601 em UTC (ex.: 2026-05-04T03:00:00Z = 04/05 00:00 BRT).
            // Convertemos para UTC e usamos a data, de forma independente do fuso da maquina.
            return DateTime.TryParse(s, CultureInfo.InvariantCulture,
                       DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
                ? dt.Date
                : (DateTime.TryParse(s, out var dt2) ? dt2.Date : null);
        }

        private static string? GetString(JsonElement fields, string refName) =>
            fields.TryGetProperty(refName, out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString()
                : null;

        private static double? GetDoubleField(JsonElement fields, string refName)
        {
            if (!fields.TryGetProperty(refName, out var el)) return null;
            return el.ValueKind switch
            {
                JsonValueKind.Number => el.GetDouble(),
                JsonValueKind.String => double.TryParse(el.GetString(),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null,
                _ => null
            };
        }

        private static string GetIdentityName(JsonElement fields, string refName)
        {
            var displayName = GetIdentityDisplayName(fields, refName);
            return string.IsNullOrWhiteSpace(displayName)
                ? GetIdentityUniqueName(fields, refName)
                : displayName;
        }

        private static string GetIdentityDisplayName(JsonElement fields, string refName)
        {
            if (!fields.TryGetProperty(refName, out var el) || el.ValueKind != JsonValueKind.Object)
                return string.Empty;
            if (el.TryGetProperty("displayName", out var d) && d.ValueKind == JsonValueKind.String)
                return d.GetString() ?? string.Empty;
            return string.Empty;
        }

        private static string GetIdentityUniqueName(JsonElement fields, string refName)
        {
            if (!fields.TryGetProperty(refName, out var el) || el.ValueKind != JsonValueKind.Object)
                return string.Empty;
            if (el.TryGetProperty("uniqueName", out var u) && u.ValueKind == JsonValueKind.String)
                return u.GetString() ?? string.Empty;
            return string.Empty;
        }

        private static double StateToPercent(string state) =>
            state?.Trim().ToLowerInvariant() switch
            {
                "closed" or "done" or "completed" or "resolved" or "removed" => 100,
                _ => 0
            };

        /// <summary>Avanca <paramref name="days"/> dias uteis (seg-sex) a partir de <paramref name="start"/>.</summary>
        private static DateTime AddWorkingDays(DateTime start, int days)
            => ProjectCalendarService.AddWorkingDays(start, days);

        // Normaliza apenas com trim + minusculas, preservando espacos e underscores
        // para nao colapsar campos distintos (ex.: "Data_Inicio" vs "Data Inicio").
        private static string Normalize(string value) =>
            value.Trim().ToLowerInvariant();

        private static string Truncate(string s, int max) =>
            s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
