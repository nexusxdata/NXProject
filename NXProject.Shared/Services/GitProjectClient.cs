// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NXProject.Services
{
    /// <summary>
    /// Conversa com o GitHub Projects (Projects v2). Leitura do projeto, dos campos, das
    /// iterações e dos itens — mais a CRIAÇÃO de campo do board, que é a única escrita até aqui.
    /// Gravar VALOR em item continua sendo a fase seguinte do [Plano_GitProject.md]: criar campo
    /// não toca em nenhuma issue e se desfaz pela tela do GitHub; gravar errado em item, não.
    ///
    /// A API do GitHub é GraphQL, não REST com JSON-Patch como a do DevOps: uma consulta pede
    /// exatamente os campos que vão ser usados, e a resposta vem no mesmo formato. Por isso as
    /// consultas ficam aqui, inteiras e legíveis, em vez de montadas por concatenação.
    ///
    /// A separação importa: este arquivo faz HTTP e devolve dados; quem traduz isso para o board
    /// é o <see cref="GitProjectBoardService"/>, que não sabe o que é rede — e por isso pode ser
    /// testado sem nenhuma.
    /// </summary>
    public sealed class GitProjectClient
    {
        private readonly GitProjectOptions _options;
        private readonly HttpClient _http;

        public GitProjectClient(GitProjectOptions options, HttpClient? http = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _http = http ?? new HttpClient();
            if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
                _http.DefaultRequestHeaders.UserAgent.ParseAdd("NXProject");
        }

        /// <summary>Resultado de uma carga: o projeto, seus campos e os itens.</summary>
        public sealed record ProjectSnapshot(
            string ProjectId, string Title,
            IReadOnlyList<GitIteration> Iterations,
            IReadOnlyList<GitProjectItem> Items,
            IReadOnlyList<string> FieldNames)
        {
            /// <summary>
            /// Opções do campo Status, NA ORDEM do board. São as colunas do projeto — "Todo",
            /// "In Progress", "Done" ou o que o time tiver criado. Vazio quando o projeto não
            /// tem Status, e aí o board cai no conjunto fixo do NXProject.
            /// </summary>
            public IReadOnlyList<string> StatusOptions { get; init; } = new List<string>();

            /// <summary>
            /// Itens que o board tem mas o token nao consegue abrir. O GitHub nao recusa a
            /// consulta: devolve o item com o conteudo NULO. Sem contar isso, um token sem o
            /// escopo `repo` produz um board vazio e a culpa cai no mapeamento — foi exatamente
            /// o que aconteceu no primeiro teste contra um repositorio privado.
            /// </summary>
            public int InaccessibleItems { get; init; }

            /// <summary>
            /// Os campos do board com ID e tipo. A leitura se contenta com o nome; a ESCRITA
            /// não: toda mutation do GitHub é por id, e opção de single-select também tem id
            /// próprio.
            /// </summary>
            public IReadOnlyList<GitProjectField> Fields { get; init; } = new List<GitProjectField>();
        }

        // ── Consultas ────────────────────────────────────────────────────────────────────

        private const string ProjectQuery = @"
query($login: String!, $number: Int!) {
  %OWNER%(login: $login) {
    projectV2(number: $number) {
      id
      title
      fields(first: 50) {
        nodes {
          ... on ProjectV2FieldCommon { id name dataType }
          ... on ProjectV2SingleSelectField { id name dataType options { id name } }
          ... on ProjectV2IterationField {
            id
            name
            dataType
            configuration {
              duration
              iterations { id title startDate duration }
              completedIterations { id title startDate duration }
            }
          }
        }
      }
    }
  }
}";

        private const string ItemsQuery = @"
query($projectId: ID!, $after: String) {
  node(id: $projectId) {
    ... on ProjectV2 {
      items(first: 100, after: $after) {
        pageInfo { hasNextPage endCursor }
        nodes {
          id
          updatedAt
          fieldValues(first: 30) {
            nodes {
              ... on ProjectV2ItemFieldNumberValue { number updatedAt creator { login } field { ... on ProjectV2FieldCommon { name } } }
              ... on ProjectV2ItemFieldDateValue { date updatedAt creator { login } field { ... on ProjectV2FieldCommon { name } } }
              ... on ProjectV2ItemFieldTextValue { text updatedAt creator { login } field { ... on ProjectV2FieldCommon { name } } }
              ... on ProjectV2ItemFieldSingleSelectValue { name updatedAt creator { login } field { ... on ProjectV2FieldCommon { name } } }
              ... on ProjectV2ItemFieldIterationValue { title updatedAt creator { login } field { ... on ProjectV2FieldCommon { name } } }
            }
          }
          content {
            ... on Issue {
              id
              updatedAt
              editor { login }
              number
              title
              state
              closedAt
              repository { nameWithOwner }
              issueType { name }
              parent { number }
              assignees(first: 1) { nodes { login name } }
              labels(first: 20) { nodes { name } }
            }
          }
        }
      }
    }
  }
}";

        // ── Chamadas ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Carrega projeto, campos, iterações e TODOS os itens (paginando de 100 em 100).
        ///
        /// Um projeto pode ter até 50.000 itens; carregar tudo é a leitura mais simples e
        /// honesta para a primeira versão, e o recorte por sprint acontece depois, em memória —
        /// igual ao modo local. Se isso doer num projeto grande, o lugar de resolver é a
        /// consulta, filtrando por iteração, e não um limite escondido aqui.
        /// </summary>
        public async Task<ProjectSnapshot> LoadAsync()
        {
            if (!_options.IsConfigured)
                throw new InvalidOperationException(NxText.T(
                    "GitProject: informe dono, número do projeto e token.",
                    "GitProject: provide owner, project number and token."));

            var ownerField = _options.OwnerIsOrganization ? "organization" : "user";
            var raiz = await PostAsync(ProjectQuery.Replace("%OWNER%", ownerField), new Dictionary<string, object?>
            {
                ["login"] = _options.Owner,
                ["number"] = _options.ProjectNumber
            });

            var proj = raiz.GetProperty("data").GetProperty(ownerField);
            if (proj.ValueKind == JsonValueKind.Null)
                throw new InvalidOperationException(NxText.T(
                    "GitProject: '{0}' não foi encontrado como {1}.",
                    "GitProject: '{0}' was not found as {1}.", _options.Owner, ownerField));
            var projeto = proj.GetProperty("projectV2");
            if (projeto.ValueKind == JsonValueKind.Null)
                throw new InvalidOperationException(NxText.T(
                    "GitProject: o projeto #{0} não existe em '{1}' ou o token não o enxerga.",
                    "GitProject: project #{0} does not exist in '{1}' or the token cannot see it.",
                    _options.ProjectNumber, _options.Owner));

            var projectId = projeto.GetProperty("id").GetString() ?? "";
            var titulo = projeto.GetProperty("title").GetString() ?? "";
            var (iteracoes, nomes, status, campos) = ParseFields(projeto);

            var itens = new List<GitProjectItem>();
            var ocultos = 0;
            string? cursor = null;
            do
            {
                var pagina = await PostAsync(ItemsQuery, new Dictionary<string, object?>
                {
                    ["projectId"] = projectId,
                    ["after"] = cursor
                });
                var items = pagina.GetProperty("data").GetProperty("node").GetProperty("items");
                foreach (var no in items.GetProperty("nodes").EnumerateArray())
                    if (ParseItem(no) is { } item) itens.Add(item);
                    else if (no.TryGetProperty("content", out var c)
                             && (c.ValueKind == JsonValueKind.Null || c.ValueKind == JsonValueKind.Object
                                 && !c.TryGetProperty("number", out _)))
                        ocultos++;
                var page = items.GetProperty("pageInfo");
                cursor = page.GetProperty("hasNextPage").GetBoolean()
                    ? page.GetProperty("endCursor").GetString() : null;
            } while (cursor != null);

            return new ProjectSnapshot(projectId, titulo, iteracoes, itens, nomes)
            { StatusOptions = status, InaccessibleItems = ocultos, Fields = campos };
        }

        /// <summary>
        /// Lista os projetos do dono — serve à tela de configuração, para a pessoa escolher em
        /// vez de decorar o número que aparece na URL.
        /// </summary>
        public async Task<List<(int Number, string Title)>> ListProjectsAsync()
        {
            var ownerField = _options.OwnerIsOrganization ? "organization" : "user";
            var consulta = $@"
query($login: String!) {{
  {ownerField}(login: $login) {{
    projectsV2(first: 50, orderBy: {{field: NUMBER, direction: DESC}}) {{
      nodes {{ number title closed }}
    }}
  }}
}}";
            var raiz = await PostAsync(consulta, new Dictionary<string, object?> { ["login"] = _options.Owner });
            var dono = raiz.GetProperty("data").GetProperty(ownerField);
            var lista = new List<(int, string)>();
            if (dono.ValueKind == JsonValueKind.Null) return lista;
            foreach (var p in dono.GetProperty("projectsV2").GetProperty("nodes").EnumerateArray())
                lista.Add((p.GetProperty("number").GetInt32(), p.GetProperty("title").GetString() ?? ""));
            return lista;
        }

        /// <summary>
        /// Cria um campo no board. É a única escrita desta fase, e é deliberado: criar campo é
        /// reversível pela tela do GitHub e não toca em nenhuma issue — bem diferente de gravar
        /// valor em item, que é a fase seguinte.
        ///
        /// O tipo é definitivo: o GitHub não deixa trocar o tipo de um campo depois de criado.
        /// Em single-select, as opções vão na criação — cor e descrição são obrigatórias na API,
        /// mesmo quando não interessam, por isso vão fixas aqui.
        /// </summary>
        public async Task CreateFieldAsync(string projectId, string nome, string dataType,
                                           IEnumerable<string>? opcoes = null)
        {
            if (string.IsNullOrWhiteSpace(nome)) throw new ArgumentException("nome", nameof(nome));

            var vars = new Dictionary<string, object?>
            {
                ["projectId"] = projectId,
                ["name"] = nome.Trim(),
                ["dataType"] = dataType
            };

            var assinatura = "$projectId: ID!, $name: String!, $dataType: ProjectV2CustomFieldType!";
            var argumentos = "projectId: $projectId, name: $name, dataType: $dataType";
            if (dataType == "SINGLE_SELECT")
            {
                var lista = (opcoes ?? Enumerable.Empty<string>())
                    .Select(o => (o ?? "").Trim()).Where(o => o.Length > 0).Distinct().ToList();
                if (lista.Count == 0)
                    throw new InvalidOperationException(NxText.T(
                        "GitProject: o campo '{0}' é single-select e precisa de pelo menos um valor.",
                        "GitProject: field '{0}' is single-select and needs at least one value.", nome));
                vars["options"] = lista.Select(o => new { name = o, color = "GRAY", description = "" }).ToList();
                assinatura += ", $options: [ProjectV2SingleSelectFieldOptionInput!]";
                argumentos += ", singleSelectOptions: $options";
            }

            await PostAsync($@"
mutation({assinatura}) {{
  createProjectV2Field(input: {{{argumentos}}}) {{
    projectV2Field {{ ... on ProjectV2Field {{ id name }} ... on ProjectV2SingleSelectField {{ id name }} }}
  }}
}}", vars);
        }

        // ── Escrita de valores (sync) ────────────────────────────────────────────────────

        /// <summary>
        /// Grava um valor de campo NO ITEM DO BOARD. No GitHub o valor é do item do projeto, não
        /// da issue — a mesma issue em dois boards tem dois HH, e é por isso que o sync recebe o
        /// id do item, não o número da issue.
        /// </summary>
        public Task SetFieldAsync(string projectId, string itemId, string fieldId, object valor)
        {
            var campo = valor switch
            {
                double d => (object)new { number = d },
                DateTime dt => new { date = dt.ToString("yyyy-MM-dd") },
                GitSingleSelect s => new { singleSelectOptionId = s.OptionId },
                _ => new { text = valor?.ToString() ?? "" }
            };
            return PostAsync(@"
mutation($projectId: ID!, $itemId: ID!, $fieldId: ID!, $value: ProjectV2FieldValue!) {
  updateProjectV2ItemFieldValue(input: {projectId: $projectId, itemId: $itemId, fieldId: $fieldId, value: $value}) {
    projectV2Item { id }
  }
}", new Dictionary<string, object?>
            {
                ["projectId"] = projectId, ["itemId"] = itemId, ["fieldId"] = fieldId, ["value"] = campo
            });
        }

        /// <summary>Fecha ou reabre a issue. É o estado DELA, independente da coluna do board.</summary>
        public Task SetIssueStateAsync(string issueId, bool fechada) =>
            PostAsync(@"
mutation($id: ID!, $state: IssueState!) {
  updateIssue(input: {id: $id, state: $state}) { issue { number state } }
}", new Dictionary<string, object?> { ["id"] = issueId, ["state"] = fechada ? "CLOSED" : "OPEN" });

        /// <summary>
        /// Apaga a issue. Irreversível e restrito a quem administra o repositório — é o GitHub
        /// que decide, e a recusa vem como erro da mutation.
        /// </summary>
        public Task DeleteIssueAsync(string issueId) =>
            PostAsync(@"
mutation($id: ID!) { deleteIssue(input: {issueId: $id}) { clientMutationId } }",
                new Dictionary<string, object?> { ["id"] = issueId });

        /// <summary>Renomeia a issue. O título é dela, não do board — por isso não é campo.</summary>
        public Task SetIssueTitleAsync(string issueId, string titulo) =>
            PostAsync(@"
mutation($id: ID!, $title: String!) {
  updateIssue(input: {id: $id, title: $title}) { issue { number } }
}", new Dictionary<string, object?> { ["id"] = issueId, ["title"] = titulo });

        /// <summary>
        /// Reescreve o CORPO da issue — é onde mora a descrição no GitHub (no DevOps é um campo
        /// do work item). O corpo é texto Markdown da issue, não um campo do board.
        /// </summary>
        public Task SetIssueBodyAsync(string issueId, string corpo) =>
            PostAsync(@"
mutation($id: ID!, $body: String!) {
  updateIssue(input: {id: $id, body: $body}) { issue { number } }
}", new Dictionary<string, object?> { ["id"] = issueId, ["body"] = corpo });

        /// <summary>Corpo atual da issue, para alterar uma parte sem perder o resto.</summary>
        public async Task<string> IssueBodyAsync(string issueId)
        {
            var raiz = await PostAsync(@"
query($id: ID!) { node(id: $id) { ... on Issue { body } } }",
                new Dictionary<string, object?> { ["id"] = issueId });
            var no = raiz.GetProperty("data").GetProperty("node");
            return no.ValueKind == JsonValueKind.Object ? Texto(no, "body") : "";
        }

        /// <summary>
        /// Substitui as labels da issue pelo conjunto informado (REST: a API de labels não existe
        /// em GraphQL por nome).
        ///
        /// Label é do REPOSITÓRIO: a que não existir é criada antes, senão a aplicação falha e a
        /// tag simplesmente não apareceria — o mesmo cuidado do botão de criar campos.
        /// </summary>
        public async Task SetLabelsAsync(string dono, string repo, int numero, IReadOnlyList<string> labels)
        {
            foreach (var l in labels)
                await RestAsync(HttpMethod.Post, $"repos/{dono}/{repo}/labels",
                    JsonSerializer.Serialize(new { name = l, color = "BFD4F2", description = "NXProject" }));

            var (ok, erro) = await RestAsync(HttpMethod.Put, $"repos/{dono}/{repo}/issues/{numero}/labels",
                JsonSerializer.Serialize(new { labels }));
            if (!ok) throw new InvalidOperationException(erro);
        }

        /// <summary>
        /// Põe o item numa iteração (sprint) do board. Iteração tem mutation própria: o valor é o
        /// id da iteração, não uma opção de single-select.
        /// </summary>
        public Task SetIterationAsync(string projectId, string itemId, string fieldId, string iterationId) =>
            PostAsync(@"
mutation($projectId: ID!, $itemId: ID!, $fieldId: ID!, $iter: String!) {
  updateProjectV2ItemFieldValue(input: {projectId: $projectId, itemId: $itemId, fieldId: $fieldId,
                                        value: {iterationId: $iter}}) {
    projectV2Item { id }
  }
}", new Dictionary<string, object?>
            {
                ["projectId"] = projectId, ["itemId"] = itemId, ["fieldId"] = fieldId, ["iter"] = iterationId
            });

        /// <summary>Id do repositório onde as issues novas nascem (`dono/repo`).</summary>
        public async Task<string> RepositoryIdAsync(string dono, string repo)
        {
            var raiz = await PostAsync(@"
query($owner: String!, $name: String!) { repository(owner: $owner, name: $name) { id } }",
                new Dictionary<string, object?> { ["owner"] = dono, ["name"] = repo });
            var r = raiz.GetProperty("data").GetProperty("repository");
            if (r.ValueKind == JsonValueKind.Null)
                throw new InvalidOperationException(NxText.T(
                    "GitProject: repositório '{0}/{1}' não encontrado pelo token.",
                    "GitProject: repository '{0}/{1}' not found with this token.", dono, repo));
            return r.GetProperty("id").GetString() ?? "";
        }

        /// <summary>Cria a issue. Ela ainda NÃO está no board — isso é a chamada seguinte.</summary>
        public async Task<(int Number, string IssueId)> CreateIssueAsync(string repositoryId, string titulo, string corpo)
        {
            var raiz = await PostAsync(@"
mutation($repo: ID!, $title: String!, $body: String) {
  createIssue(input: {repositoryId: $repo, title: $title, body: $body}) { issue { id number } }
}", new Dictionary<string, object?> { ["repo"] = repositoryId, ["title"] = titulo, ["body"] = corpo });
            var issue = raiz.GetProperty("data").GetProperty("createIssue").GetProperty("issue");
            return (issue.GetProperty("number").GetInt32(), issue.GetProperty("id").GetString() ?? "");
        }

        /// <summary>Põe a issue no board e devolve o id do ITEM — é nele que os campos são gravados.</summary>
        public async Task<string> AddToProjectAsync(string projectId, string issueId)
        {
            var raiz = await PostAsync(@"
mutation($projectId: ID!, $contentId: ID!) {
  addProjectV2ItemById(input: {projectId: $projectId, contentId: $contentId}) { item { id } }
}", new Dictionary<string, object?> { ["projectId"] = projectId, ["contentId"] = issueId });
            return raiz.GetProperty("data").GetProperty("addProjectV2ItemById")
                       .GetProperty("item").GetProperty("id").GetString() ?? "";
        }

        /// <summary>
        /// Desliga a sub-issue do pai atual. Mover de pai no GitHub é remover e ligar de novo —
        /// não há "mudar o pai" numa chamada, como o reparent do DevOps.
        /// </summary>
        public Task RemoveSubIssueAsync(string paiIssueId, string filhoIssueId) =>
            PostAsync(@"
mutation($parent: ID!, $child: ID!) {
  removeSubIssue(input: {issueId: $parent, subIssueId: $child}) { issue { number } }
}", new Dictionary<string, object?> { ["parent"] = paiIssueId, ["child"] = filhoIssueId });

        /// <summary>Liga a issue como sub-issue do pai: é assim que a hierarquia existe no GitHub.</summary>
        public Task AddSubIssueAsync(string paiIssueId, string filhoIssueId) =>
            PostAsync(@"
mutation($parent: ID!, $child: ID!) {
  addSubIssue(input: {issueId: $parent, subIssueId: $child}) { issue { number } }
}", new Dictionary<string, object?> { ["parent"] = paiIssueId, ["child"] = filhoIssueId });

        /// <summary>
        /// Só os IDs dos itens, NA ORDEM em que a API os devolve.
        ///
        /// Existe separado da carga completa porque a ordem é do ITEM DO BOARD e não depende do
        /// conteúdo: um token sem acesso ao repositório lê a fila, mas não as issues. É também a
        /// leitura mais barata para conferir reordenação.
        /// </summary>
        public async Task<List<string>> ListItemIdsAsync(string projectId)
        {
            var ids = new List<string>();
            string? cursor = null;
            do
            {
                var pagina = await PostAsync(@"
query($projectId: ID!, $after: String) {
  node(id: $projectId) {
    ... on ProjectV2 {
      items(first: 100, after: $after) {
        pageInfo { hasNextPage endCursor }
        nodes { id content { ... on Issue { number } } }
      }
    }
  }
}", new Dictionary<string, object?> { ["projectId"] = projectId, ["after"] = cursor });
                var items = pagina.GetProperty("data").GetProperty("node").GetProperty("items");
                foreach (var no in items.GetProperty("nodes").EnumerateArray())
                    if (no.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } v)
                        ids.Add(v);
                var page = items.GetProperty("pageInfo");
                cursor = page.GetProperty("hasNextPage").GetBoolean()
                    ? page.GetProperty("endCursor").GetString() : null;
            } while (cursor != null);
            return ids;
        }

        /// <summary>
        /// Move o item na ordem do projeto: depois de <paramref name="depoisDeItemId"/>, ou para o
        /// TOPO quando ele vem nulo.
        ///
        /// É a tentativa de gravar a ordem do backlog. No GitHub a ordem não é um campo — é a
        /// posição do item na view —, e esta é a única mutation que a toca. O quanto ela é
        /// confiável está medido na sonda `NXTestUnit gitorder`, e é de lá que sai a decisão de
        /// usá-la ou não no sync.
        /// </summary>
        public Task MoveItemAsync(string projectId, string itemId, string? depoisDeItemId)
        {
            var vars = new Dictionary<string, object?>
            {
                ["projectId"] = projectId, ["itemId"] = itemId, ["afterId"] = depoisDeItemId
            };
            return PostAsync(@"
mutation($projectId: ID!, $itemId: ID!, $afterId: ID) {
  updateProjectV2ItemPosition(input: {projectId: $projectId, itemId: $itemId, afterId: $afterId}) {
    items(first: 1) { nodes { id } }
  }
}", vars);
        }

        // ── Histórico, anexos e pessoas ──────────────────────────────────────────────────

        /// <summary>
        /// Uma issue PÚBLICA com histórico de label, para medir a forma da API quando o token não
        /// abre o repositório privado do board. Dado público é legível com qualquer token — e a
        /// pergunta "a timeline devolve entrada e saída de label" não depende de QUAL issue é.
        /// </summary>
        public async Task<(string IssueId, string Onde)> AnyPublicLabeledIssueAsync(string consulta)
        {
            var raiz = await PostAsync(@"
query($q: String!) {
  search(query: $q, type: ISSUE, first: 5) {
    nodes { ... on Issue { id number repository { nameWithOwner } labels(first: 1) { totalCount } } }
  }
}", new Dictionary<string, object?> { ["q"] = consulta });
            foreach (var no in raiz.GetProperty("data").GetProperty("search").GetProperty("nodes").EnumerateArray())
            {
                if (no.ValueKind != JsonValueKind.Object || !no.TryGetProperty("id", out var id)) continue;
                var onde = no.TryGetProperty("repository", out var r) ? Texto(r, "nameWithOwner") : "";
                return (id.GetString() ?? "", $"{onde}#{(no.TryGetProperty("number", out var n) ? n.GetInt32() : 0)}");
            }
            return ("", "");
        }

        /// <summary>
        /// Quem é o token e que escopos ele tem, como o próprio GitHub os reporta
        /// (`X-OAuth-Scopes`).
        ///
        /// É a pergunta que separa "o board está vazio" de "o token não enxerga": sem ela, a
        /// suspeita cai no mapeamento de campos, que é o lugar errado. Devolve só login e
        /// escopos — o token nunca é impresso.
        /// </summary>
        public async Task<(string Login, string Scopes)> WhoAmIAsync()
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.Token);
            using var resp = await _http.SendAsync(req);
            var escopos = resp.Headers.TryGetValues("X-OAuth-Scopes", out var v)
                ? string.Join(", ", v) : "(nao informado: token fine-grained ou invalido)";
            if (!resp.IsSuccessStatusCode) return ($"HTTP {(int)resp.StatusCode}", escopos);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return (Texto(doc.RootElement, "login"), escopos);
        }

        /// <summary>
        /// Em que repositório a issue vive (`dono/repo`).
        ///
        /// O board é uma visão sobre issues de vários repositórios — e nem toda operação é do
        /// board: label, anexo e responsável são do repositório. Perguntar à issue evita obrigar
        /// a pessoa a configurar à mão algo que o próprio dado já diz.
        /// </summary>
        public async Task<string> IssueRepositoryAsync(string issueId)
        {
            var raiz = await PostAsync(@"
query($id: ID!) { node(id: $id) { ... on Issue { repository { nameWithOwner } } } }",
                new Dictionary<string, object?> { ["id"] = issueId });
            var no = raiz.GetProperty("data").GetProperty("node");
            return no.ValueKind == JsonValueKind.Null ? ""
                : Texto(no.GetProperty("repository"), "nameWithOwner");
        }

        /// <summary>
        /// Aplica uma label à issue, criando-a no repositório se ainda não existir.
        ///
        /// No GitHub a label é do REPOSITÓRIO, não do board: um projeto que junta issues de
        /// vários repositórios precisa da mesma label criada em cada um — e aplicar uma que não
        /// existe não a cria sozinha. É a diferença prática para a tag do DevOps, que é texto
        /// livre no próprio work item.
        /// </summary>
        public async Task<(bool Ok, string Error)> AddLabelAsync(string dono, string repo, int numero, string label)
        {
            // 422 aqui significa "já existe", que é sucesso para o nosso propósito.
            await RestAsync(HttpMethod.Post, $"repos/{dono}/{repo}/labels",
                JsonSerializer.Serialize(new { name = label, color = "B60205",
                    description = "Bloqueio (NXProject)" }));

            var (ok, texto) = await RestAsync(HttpMethod.Post, $"repos/{dono}/{repo}/issues/{numero}/labels",
                JsonSerializer.Serialize(new { labels = new[] { label } }));
            return (ok, ok ? "" : texto);
        }

        /// <summary>Tira a label da issue — sem apagá-la do repositório.</summary>
        public async Task<bool> RemoveLabelAsync(string dono, string repo, int numero, string label)
        {
            var (ok, _) = await RestAsync(HttpMethod.Delete,
                $"repos/{dono}/{repo}/issues/{numero}/labels/{Uri.EscapeDataString(label)}", null);
            return ok;
        }

        /// <summary>Apaga a label do repositório (limpeza da POC).</summary>
        public async Task<bool> DeleteLabelAsync(string dono, string repo, string label)
        {
            var (ok, _) = await RestAsync(HttpMethod.Delete,
                $"repos/{dono}/{repo}/labels/{Uri.EscapeDataString(label)}", null);
            return ok;
        }

        /// <summary>
        /// Chamada REST. Label, anexo e conteúdo de repositório não existem em GraphQL — a API do
        /// GitHub é dividida assim, e fingir o contrário só esconderia a divisão.
        /// </summary>
        private async Task<(bool Ok, string Body)> RestAsync(HttpMethod metodo, string caminho, string? corpo)
        {
            using var req = new HttpRequestMessage(metodo, "https://api.github.com/" + caminho);
            if (corpo != null) req.Content = new StringContent(corpo, Encoding.UTF8, "application/json");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.Token);
            using var resp = await _http.SendAsync(req);
            var texto = await resp.Content.ReadAsStringAsync();
            return (resp.IsSuccessStatusCode, resp.IsSuccessStatusCode ? texto : $"HTTP {(int)resp.StatusCode}. {Resumo(texto)}");
        }

        /// <summary>Quando uma label entrou ou saiu da issue, por quem.</summary>
        public sealed record LabelEvent(DateTime CreatedAt, string Label, bool Added, string Actor);

        /// <summary>
        /// Histórico de labels da issue — o equivalente do `/updates` do DevOps para a auditoria
        /// de BLOCK.
        ///
        /// No DevOps o bloqueio sai das revisões do work item; aqui sai da TIMELINE, que guarda
        /// cada vez que a label entrou e saiu, com hora e autor. É outro formato para o mesmo
        /// dado — e é isso que torna a auditoria possível deste lado.
        /// </summary>
        public async Task<List<LabelEvent>> LabelHistoryAsync(string issueId)
        {
            var eventos = new List<LabelEvent>();
            var raiz = await PostAsync(@"
query($id: ID!) {
  node(id: $id) {
    ... on Issue {
      timelineItems(first: 100, itemTypes: [LABELED_EVENT, UNLABELED_EVENT]) {
        nodes {
          __typename
          ... on LabeledEvent { createdAt label { name } actor { login } }
          ... on UnlabeledEvent { createdAt label { name } actor { login } }
        }
      }
    }
  }
}", new Dictionary<string, object?> { ["id"] = issueId });

            var no = raiz.GetProperty("data").GetProperty("node");
            if (no.ValueKind == JsonValueKind.Null) return eventos;
            foreach (var e in no.GetProperty("timelineItems").GetProperty("nodes").EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var tipo = Texto(e, "__typename");
                if (!DateTime.TryParse(Texto(e, "createdAt"), out var quando)) continue;
                var label = e.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.Object
                    ? Texto(l, "name") : "";
                var autor = e.TryGetProperty("actor", out var a) && a.ValueKind == JsonValueKind.Object
                    ? Texto(a, "login") : "";
                eventos.Add(new LabelEvent(quando, label,
                    string.Equals(tipo, "LabeledEvent", StringComparison.Ordinal), autor));
            }
            return eventos.OrderBy(x => x.CreatedAt).ToList();
        }

        /// <summary>
        /// Contas que podem ser responsáveis por uma issue do repositório.
        ///
        /// É a parte que falta para sincronizar responsável: a mutation é trivial, o difícil é
        /// saber QUE conta é a pessoa do cronograma. Sem esta lista, casar por nome seria
        /// adivinhação — e responsável errado é pior que responsável vazio.
        /// </summary>
        public async Task<List<(string Login, string Name)>> AssignableUsersAsync(string dono, string repo) =>
            (await AssignableAccountsAsync(dono, repo)).Select(c => (c.Login, c.Name)).ToList();

        /// <summary>
        /// As contas atribuíveis COM o id. É o id que a mutation exige — nome e login servem só
        /// para a pessoa reconhecer quem é.
        /// </summary>
        public async Task<List<GitProjectPeopleService.Account>> AssignableAccountsAsync(string dono, string repo)
        {
            var raiz = await PostAsync(@"
query($owner: String!, $name: String!) {
  repository(owner: $owner, name: $name) {
    assignableUsers(first: 100) { nodes { id login name } }
  }
}", new Dictionary<string, object?> { ["owner"] = dono, ["name"] = repo });
            var r = raiz.GetProperty("data").GetProperty("repository");
            var lista = new List<GitProjectPeopleService.Account>();
            if (r.ValueKind == JsonValueKind.Null) return lista;
            foreach (var u in r.GetProperty("assignableUsers").GetProperty("nodes").EnumerateArray())
                lista.Add(new GitProjectPeopleService.Account(Texto(u, "id"), Texto(u, "login"), Texto(u, "name")));
            return lista;
        }

        /// <summary>
        /// Define o responsável da issue. Substitui a lista inteira: o NXProject tem UM
        /// responsável por atividade, e acrescentar sem tirar deixaria o antigo lá, respondendo
        /// por trabalho que não é mais dele.
        /// </summary>
        public Task SetAssigneeAsync(string issueId, string? userId) =>
            PostAsync(@"
mutation($id: ID!, $actors: [ID!]!) {
  replaceActorsForAssignable(input: {assignableId: $id, actorIds: $actors}) {
    assignable { ... on Issue { number } }
  }
}", new Dictionary<string, object?>
            {
                ["id"] = issueId,
                ["actors"] = string.IsNullOrEmpty(userId) ? new List<string>() : new List<string> { userId }
            });

        /// <summary>
        /// Logins dos membros de um time da ORGANIZAÇÃO. Time não existe em conta pessoal — ali o
        /// equivalente é a lista de logins escrita no próprio campo.
        ///
        /// Exige `read:org` no token. Sem ele a consulta falha, e quem chama trata isso como
        /// falha técnica (que libera), não como negativa.
        /// </summary>
        public async Task<List<string>> TeamMembersAsync(string org, string time)
        {
            var raiz = await PostAsync(@"
query($org: String!, $team: String!) {
  organization(login: $org) {
    team(slug: $team) { members(first: 100) { nodes { login } } }
  }
}", new Dictionary<string, object?> { ["org"] = org, ["team"] = time });

            var o = raiz.GetProperty("data").GetProperty("organization");
            var lista = new List<string>();
            if (o.ValueKind == JsonValueKind.Null) return lista;
            var t = o.GetProperty("team");
            if (t.ValueKind == JsonValueKind.Null) return lista;
            foreach (var m in t.GetProperty("members").GetProperty("nodes").EnumerateArray())
                lista.Add(Texto(m, "login"));
            return lista;
        }

        /// <summary>
        /// Versiona um arquivo no repositório (REST, não GraphQL — a API de conteúdo não existe
        /// em GraphQL).
        ///
        /// É o substituto de anexo: o GitHub não tem API de anexo de item (na tela, o arquivo vai
        /// para um CDN pelo corpo do comentário, e isso não é automatizável). Arquivo no
        /// repositório é versionado e tem histórico — mais do que o anexo do DevOps oferece.
        /// </summary>
        /// <summary>
        /// Arquivos do repositório sob um prefixo, em UMA chamada (git/trees recursivo).
        ///
        /// É assim que os anexos entram no board sem uma ida por card: a pasta de anexos inteira
        /// vem de uma vez e é distribuída em memória. Repositório sem a pasta devolve lista
        /// vazia — e não é erro, é o caso normal de quem nunca anexou nada.
        /// </summary>
        public async Task<List<(string Path, long Size, string Sha)>> ListFilesAsync(
            string dono, string repo, string prefixo, string referencia = "HEAD")
        {
            var achados = new List<(string, long, string)>();
            var (ok, corpo) = await RestAsync(HttpMethod.Get,
                $"repos/{dono}/{repo}/git/trees/{referencia}?recursive=1", null);
            if (!ok) return achados;

            using var doc = JsonDocument.Parse(corpo);
            if (!doc.RootElement.TryGetProperty("tree", out var arvore)) return achados;
            foreach (var no in arvore.EnumerateArray())
            {
                if (Texto(no, "type") != "blob") continue;
                var caminho = Texto(no, "path");
                if (!caminho.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase)) continue;
                // O sha vem junto porque apagar arquivo pela API exige o sha do blob — buscar
                // de novo, um por arquivo, seria uma ida à rede por anexo.
                achados.Add((caminho,
                    no.TryGetProperty("size", out var tam) && tam.TryGetInt64(out var v) ? v : 0,
                    Texto(no, "sha")));
            }
            return achados;
        }

        /// <summary>
        /// Baixa o conteúdo BRUTO de um arquivo do repositório (o anexo do GitProject é um
        /// arquivo versionado). Vai pela API com o token: o repositório pode ser privado, e a
        /// URL de navegação do GitHub devolveria a página HTML, não o arquivo.
        /// </summary>
        public async Task<byte[]> GetFileBytesAsync(string dono, string repo, string caminho, string? referencia = null)
        {
            var url = $"https://api.github.com/repos/{dono}/{repo}/contents/{caminho}"
                    + (string.IsNullOrWhiteSpace(referencia) ? "" : $"?ref={referencia}");
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.Token);
            req.Headers.UserAgent.ParseAdd("NXProject");
            req.Headers.Accept.ParseAdd("application/vnd.github.raw");
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"HTTP {(int)resp.StatusCode} ao baixar {caminho}. {Resumo(await resp.Content.ReadAsStringAsync())}");
            return await resp.Content.ReadAsByteArrayAsync();
        }

        public async Task<(bool Ok, string Url, string Sha, string Error)> PutFileAsync(
            string dono, string repo, string caminho, string conteudo, string mensagem,
            bool jaEmBase64 = false)
        {
            var corpo = JsonSerializer.Serialize(new
            {
                message = mensagem,
                // Anexo e binario: quem chama ja manda em base64. Texto (o caso da POC) e
                // convertido aqui, para a chamada simples continuar simples.
                content = jaEmBase64 ? conteudo : Convert.ToBase64String(Encoding.UTF8.GetBytes(conteudo))
            });
            using var req = new HttpRequestMessage(HttpMethod.Put,
                $"https://api.github.com/repos/{dono}/{repo}/contents/{caminho}")
            { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.Token);
            using var resp = await _http.SendAsync(req);
            var texto = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                return (false, "", "", $"HTTP {(int)resp.StatusCode}. {Resumo(texto)}");

            using var doc = JsonDocument.Parse(texto);
            var content = doc.RootElement.GetProperty("content");
            return (true, Texto(content, "html_url"), Texto(content, "sha"), "");
        }

        /// <summary>
        /// Comenta na issue. É assim que o anexo aparece para quem olha o item: o arquivo sozinho
        /// no repositório existiria sem ninguém saber.
        /// </summary>
        public Task AddCommentAsync(string issueId, string texto) =>
            PostAsync(@"
mutation($id: ID!, $body: String!) {
  addComment(input: {subjectId: $id, body: $body}) { clientMutationId }
}", new Dictionary<string, object?> { ["id"] = issueId, ["body"] = texto });

        /// <summary>Remove um arquivo versionado — a POC não pode deixar sujeira no repositório.</summary>
        public async Task<bool> DeleteFileAsync(string dono, string repo, string caminho, string sha, string mensagem)
        {
            var corpo = JsonSerializer.Serialize(new { message = mensagem, sha });
            using var req = new HttpRequestMessage(HttpMethod.Delete,
                $"https://api.github.com/repos/{dono}/{repo}/contents/{caminho}")
            { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.Token);
            using var resp = await _http.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }

        // ── Transporte ───────────────────────────────────────────────────────────────────

        private async Task<JsonElement> PostAsync(string query, Dictionary<string, object?> variables)
        {
            var corpo = JsonSerializer.Serialize(new { query, variables });
            using var req = new HttpRequestMessage(HttpMethod.Post, _options.GraphQlUrl)
            {
                Content = new StringContent(corpo, Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.Token);
            using var resp = await _http.SendAsync(req);
            var texto = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"GitProject: HTTP {(int)resp.StatusCode}. {Resumo(texto)}");

            var doc = JsonDocument.Parse(texto);
            // No GraphQL o erro vem com HTTP 200: ignorar "errors" era receber dados vazios e
            // culpar o mapeamento. O caso mais comum e o escopo do token (read:project).
            if (doc.RootElement.TryGetProperty("errors", out var erros) && erros.GetArrayLength() > 0)
                throw new InvalidOperationException("GitProject: " + string.Join(" | ",
                    erros.EnumerateArray().Select(e =>
                        e.TryGetProperty("message", out var m) ? m.GetString() : e.ToString())));
            return doc.RootElement.Clone();
        }

        private static string Resumo(string texto) =>
            string.IsNullOrWhiteSpace(texto) ? "(sem corpo)"
            : texto.Length <= 300 ? texto : texto.Substring(0, 300) + "…";

        // ── Leitura do JSON ──────────────────────────────────────────────────────────────

        private static (List<GitIteration>, List<string>, List<string>, List<GitProjectField>) ParseFields(JsonElement projeto)
        {
            var iteracoes = new List<GitIteration>();
            var nomes = new List<string>();
            var status = new List<string>();
            var campos = new List<GitProjectField>();
            foreach (var f in projeto.GetProperty("fields").GetProperty("nodes").EnumerateArray())
            {
                if (f.ValueKind != JsonValueKind.Object) continue;
                if (f.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } nome)
                {
                    nomes.Add(nome);
                    var campo = new GitProjectField(
                        f.TryGetProperty("id", out var fid) ? fid.GetString() ?? "" : "",
                        nome,
                        f.TryGetProperty("dataType", out var dt) ? dt.GetString() ?? "" : "");
                    if (f.TryGetProperty("options", out var todas) && todas.ValueKind == JsonValueKind.Array)
                        foreach (var op in todas.EnumerateArray())
                            if (op.TryGetProperty("name", out var opn) && opn.GetString() is { Length: > 0 } opNome)
                                campo.Options[opNome] = op.TryGetProperty("id", out var opi) ? opi.GetString() ?? "" : "";
                    campos.Add(campo);
                    // As colunas do board sao as opcoes do Status, na ordem em que o projeto as
                    // define — nao o conjunto fixo do NXProject.
                    if (string.Equals(nome, "Status", StringComparison.CurrentCultureIgnoreCase)
                        && f.TryGetProperty("options", out var ops) && ops.ValueKind == JsonValueKind.Array)
                        foreach (var op in ops.EnumerateArray())
                            if (op.TryGetProperty("name", out var on) && on.GetString() is { Length: > 0 } opt)
                                status.Add(opt);
                }
                if (!f.TryGetProperty("configuration", out var cfg) || cfg.ValueKind != JsonValueKind.Object) continue;
                // As encerradas vêm à parte, e fazem falta: sprint passada é o histórico do board.
                foreach (var chave in new[] { "iterations", "completedIterations" })
                    if (cfg.TryGetProperty(chave, out var arr) && arr.ValueKind == JsonValueKind.Array)
                        foreach (var it in arr.EnumerateArray())
                            if (DateTime.TryParse(it.GetProperty("startDate").GetString(), out var ini))
                                iteracoes.Add(new GitIteration(
                                    it.GetProperty("id").GetString() ?? "",
                                    it.GetProperty("title").GetString() ?? "",
                                    ini.Date,
                                    it.TryGetProperty("duration", out var d) ? d.GetInt32() : 14));
            }
            return (iteracoes.OrderBy(i => i.Start).ToList(), nomes, status, campos);
        }

        /// <summary>
        /// Converte um item do projeto. Devolve null para o que não é issue — o board do GitHub
        /// aceita pull request e rascunho, e nenhum dos dois é atividade de cronograma.
        /// </summary>
        private GitProjectItem? ParseItem(JsonElement no)
        {
            if (!no.TryGetProperty("content", out var c) || c.ValueKind != JsonValueKind.Object
                || !c.TryGetProperty("number", out var num)) return null;

            var item = new GitProjectItem
            {
                Number = num.GetInt32(),
                Title = Texto(c, "title"),
                IssueState = Texto(c, "state"),
                IssueType = c.TryGetProperty("issueType", out var tp) && tp.ValueKind == JsonValueKind.Object
                    ? Texto(tp, "name") : "",
                ParentNumber = c.TryGetProperty("parent", out var pai) && pai.ValueKind == JsonValueKind.Object
                    && pai.TryGetProperty("number", out var pn) ? pn.GetInt32() : 0,
                IssueId = Texto(c, "id"),
                Repository = c.TryGetProperty("repository", out var rp) && rp.ValueKind == JsonValueKind.Object
                    ? Texto(rp, "nameWithOwner") : "",
                ItemId = Texto(no, "id")
            };

            // A revisão é o MAIOR dos dois relógios: mudar o HH mexe no item do board, mudar o
            // título mexe na issue, e o sync precisa perceber as duas coisas.
            if (c.TryGetProperty("editor", out var ed) && ed.ValueKind == JsonValueKind.Object)
                item.LastChangedBy = Texto(ed, "login");

            foreach (var prop in new[] { Texto(c, "updatedAt"), Texto(no, "updatedAt") })
                if (DateTime.TryParse(prop, out var quando)
                    && (item.UpdatedAt == null || quando.ToUniversalTime() > item.UpdatedAt))
                    item.UpdatedAt = quando.ToUniversalTime();
            if (c.TryGetProperty("closedAt", out var fech) && fech.ValueKind == JsonValueKind.String
                && DateTime.TryParse(fech.GetString(), out var dtFech)) item.ClosedAt = dtFech;
            if (c.TryGetProperty("assignees", out var resp) && resp.ValueKind == JsonValueKind.Object)
                foreach (var a in resp.GetProperty("nodes").EnumerateArray())
                {
                    // Prefere o nome de exibição: é o que casa com o recurso do cronograma.
                    // O login vai junto, separado: é ele a identidade de verdade — nome pode
                    // estar vazio, pode repetir, e não serve para gravar de volta.
                    var nome = Texto(a, "name");
                    item.AssigneeLogin = Texto(a, "login");
                    item.Assignee = string.IsNullOrWhiteSpace(nome) ? item.AssigneeLogin : nome;
                    break;
                }
            if (c.TryGetProperty("labels", out var labs) && labs.ValueKind == JsonValueKind.Object)
                item.Labels = string.Join("; ", labs.GetProperty("nodes").EnumerateArray()
                    .Select(l => Texto(l, "name")).Where(s => s.Length > 0));

            DateTime? ultimoCampo = null;
            foreach (var v in no.GetProperty("fieldValues").GetProperty("nodes").EnumerateArray())
            {
                if (v.ValueKind != JsonValueKind.Object || !v.TryGetProperty("field", out var campo)
                    || campo.ValueKind != JsonValueKind.Object) continue;
                var nomeCampo = Texto(campo, "name");
                if (nomeCampo.Length == 0) continue;

                // Quem mexeu por ultimo: o autor do valor de campo mais recente. Cada valor traz
                // o seu proprio relogio, entao o mais novo manda — e o editor da issue so vale
                // quando nenhum campo e mais recente que ele.
                if (DateTime.TryParse(Texto(v, "updatedAt"), out var quandoCampo)
                    && v.TryGetProperty("creator", out var autor) && autor.ValueKind == JsonValueKind.Object
                    && Texto(autor, "login") is { Length: > 0 } login
                    && (ultimoCampo == null || quandoCampo.ToUniversalTime() > ultimoCampo))
                {
                    ultimoCampo = quandoCampo.ToUniversalTime();
                    item.LastChangedBy = login;
                }

                if (v.TryGetProperty("number", out var numero) && numero.ValueKind == JsonValueKind.Number)
                    AplicaNumero(item, nomeCampo, numero.GetDouble());
                else if (v.TryGetProperty("date", out var data) && data.ValueKind == JsonValueKind.String
                         && DateTime.TryParse(data.GetString(), out var dt))
                    AplicaData(item, nomeCampo, dt.Date);
                else if (v.TryGetProperty("title", out var tit) && tit.ValueKind == JsonValueKind.String)
                {
                    // Iteração: o título é o nome da sprint ("Sprint 5").
                    if (Igual(nomeCampo, _options.IterationFields)) item.IterationTitle = tit.GetString() ?? "";
                }
                else if (v.TryGetProperty("text", out var texto) && texto.ValueKind == JsonValueKind.String)
                {
                    if (Igual(nomeCampo, _options.SyncNameFields)) item.SyncName = texto.GetString() ?? "";
                    else if (Igual(nomeCampo, _options.AdmGroupFields)) item.AdmGroup = texto.GetString() ?? "";
                    else if (Igual(nomeCampo, _options.EpicTypeFields)) item.EpicType = texto.GetString() ?? "";
                }
                else if (v.TryGetProperty("name", out var opcao) && opcao.ValueKind == JsonValueKind.String)
                {
                    var valor = opcao.GetString() ?? "";
                    if (Igual(nomeCampo, _options.LevelFields)) item.LevelValue = valor;
                    // O EPIC_TYPE costuma ser single-select no board (DELIVERY / BACKLOG /
                    // EPIC_GROUP), mas texto também vale — por isso ele é lido nos dois ramos.
                    else if (Igual(nomeCampo, _options.EpicTypeFields)) item.EpicType = valor;
                    else if (Igual(nomeCampo, "Status")) item.Status = valor;
                }
            }
            return item;
        }

        private void AplicaNumero(GitProjectItem item, string campo, double valor)
        {
            if (Igual(campo, _options.EffortFields)) item.Effort = valor;
            else if (Igual(campo, _options.CurrentHoursFields)) item.CurrentHours = valor;
            else if (Igual(campo, _options.OriginalHoursFields)) item.OriginalHours = valor;
            else if (Igual(campo, _options.RemainingHoursFields)) item.RemainingHours = valor;
            else if (Igual(campo, _options.PercentFields)) item.Percent = valor;
            else if (Igual(campo, _options.PriorityFields)) item.Priority = valor;
            else if (Igual(campo, _options.SyncVersionFields)) item.SyncVersion = valor;
        }

        private void AplicaData(GitProjectItem item, string campo, DateTime valor)
        {
            if (Igual(campo, _options.StartDateFields)) item.StartDate = valor;
            else if (Igual(campo, _options.FinishDateFields)) item.FinishDate = valor;
        }

        // A regra de casar nome mora nas opcoes, junto dos nomes — e e testada la.
        private static bool Igual(string campo, string nome) =>
            GitProjectOptions.Matches(campo, new[] { nome });

        private static bool Igual(string campo, IEnumerable<string>? nomes) =>
            GitProjectOptions.Matches(campo, nomes);

        private static string Texto(JsonElement e, string prop) =>
            e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    }
}
