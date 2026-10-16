// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Linq;

namespace NXProject.Services
{
    /// <summary>
    /// Conexão com um projeto do GitHub Projects (Projects v2).
    ///
    /// Diferente do Azure DevOps, onde HH e datas são campos DO WORK ITEM, aqui eles são
    /// **campos do projeto** (do board). Por isso os nomes ficam configuráveis: cada time batiza
    /// os seus, e a issue que sair daquele projeto leva os valores embora. Os defaults seguem o
    /// vocabulário que o NXProject já usa no DevOps, para quem está migrando não ter de traduzir
    /// nada.
    /// </summary>
    public sealed class GitProjectOptions
    {
        /// <summary>Dono do projeto: o login do usuário ou da organização.</summary>
        public string Owner { get; set; } = string.Empty;

        /// <summary>
        /// O dono é uma organização? Muda a consulta (`organization` em vez de `user`) e, mais
        /// adiante, a disponibilidade de Issue Types, que só existem em organização.
        /// </summary>
        public bool OwnerIsOrganization { get; set; }

        /// <summary>Número do projeto, o que aparece na URL (…/projects/7).</summary>
        public int ProjectNumber { get; set; }

        /// <summary>
        /// Token de acesso. Leitura exige o escopo `read:project` (além de `repo` para issues de
        /// repositório privado); escrita, mais adiante, exige `project`. Guardado cifrado pelo
        /// Windows (DPAPI), nunca em texto plano — a mesma regra do PAT do DevOps.
        /// </summary>
        public string Token { get; set; } = string.Empty;

        // ── Nomes dos campos do projeto (board) ──────────────────────────────────────────
        //
        // LISTA de nomes aceitos, nao um nome so: o mesmo conceito aparece batizado de formas
        // diferentes, e quem monta o board no GitHub costuma copiar o nome que ve no DevOps. O
        // PRIMEIRO da lista e o nome sugerido na hora de criar o campo.
        //
        // Os nomes saem do <see cref="NxDevOpsFieldCatalog"/>, que e a fonte unica do vocabulario
        // de campos do NXProject — duplicar aqui era criar a terceira copia da mesma regra, que
        // foi exatamente o que deu errado antes entre o NX e o Setup.
        //
        // O default e em INGLES, e de proposito. No DevOps os nomes em portugues existem para
        // nao colidir com os campos nativos do processo (Start Date, Finish Date e Original
        // Estimate ja sao do DevOps, e a Task do NX grava e le deles). No GitHub nao ha campo
        // nativo nenhum para colidir: o board nasce vazio, e o nome em ingles e o que combina
        // com o resto da tela do GitHub.
        //
        // Os nomes em portugues seguem ACEITOS como apelidos — quem ja montou o board copiando o
        // vocabulario do DevOps nao precisa renomear nada.
        public string[] EffortFields { get; set; } = Aceitos("effort", "Original Estimate", "Estimate", "Esforco Original", "nx_estimated_hours");
        public string[] CurrentHoursFields { get; set; } = Aceitos("currentHours", "Completed Work", "Completed Hours", "HH Realizado", "Trabalho Concluido");

        /// <summary>Horas de bloqueio — conceito do NX, logo com prefixo.</summary>
        /// <summary>
        /// HH original e HH restante no board. No Azure DevOps eles sao opcionais porque a Task do
        /// Agile/CMMI ja traz campos nativos; aqui nao ha nada nativo, entao sao campos do board
        /// como os outros — e o botao de criar campos os cria.
        /// </summary>
        public string[] OriginalHoursFields { get; set; } = Aceitos("originalHours", "nx_original_hours");

        /// <inheritdoc cref="OriginalHoursFields"/>
        public string[] RemainingHoursFields { get; set; } = Aceitos("remainingHours", "nx_remaining_hours");

        public string[] BlockHoursFields { get; set; } = Aceitos("blockDuration", "nx_block_hours");

        /// <summary>Percentual de alocação do recurso — idem.</summary>
        public string[] AllocationFields { get; set; } = Aceitos("percAloc", "nx_allocation_percent");
        public string[] StartDateFields { get; set; } = Aceitos("start", "Start Date", "Data de Inicio", "nx_start_date");
        public string[] FinishDateFields { get; set; } = Aceitos("finish", "Finish Date", "Target Date", "Data Alvo", "nx_finish_date");
        // O que NAO espelha um campo padrao do DevOps nasce com o prefixo nx_: no board do
        // GitHub o campo e do projeto e fica a vista de todo mundo, e o prefixo diz de quem ele
        // e. Os nomes sem prefixo seguem aceitos, para nao quebrar board ja montado.
        public string[] PercentFields { get; set; } =
            Aceitos("percConclusao", "nx_percent_complete", "Percent Complete", "Progress");

        /// <summary>
        /// Prioridade. No DevOps ela e nativa; aqui precisa ser um campo de numero do board —
        /// sem ele o board le e grava tudo menos a prioridade, e o card dizia so que ela "nao e
        /// campo do item".
        /// </summary>
        public string[] PriorityFields { get; set; } = Aceitos("priority", "nx_priority", "Priority");

        /// <summary>
        /// Nomes aceitos para um campo: os padroes do DevOps na frente, depois os apelidos que o
        /// catalogo do NXProject ja conhece. Sem repetir, e sem diferenciar maiusculas.
        /// </summary>
        private static string[] Aceitos(string chave, params string[] padroes) =>
            padroes.Concat(NxDevOpsFieldCatalog.Names(chave))
                   .Where(n => !string.IsNullOrWhiteSpace(n))
                   .Distinct(StringComparer.CurrentCultureIgnoreCase)
                   .ToArray();

        /// <summary>
        /// Campo que diz o NÍVEL do item (Project, Epic, Feature, Story, Task).
        ///
        /// O GitHub não valida hierarquia: sub-issue aceita qualquer coisa embaixo de qualquer
        /// coisa. Issue Types resolveriam, mas só existem em organização — então o padrão é um
        /// campo single-select do próprio board, que funciona em conta pessoal também. Vazio
        /// significa "usar o Issue Type", quando houver.
        /// </summary>
        public string[] LevelFields { get; set; } = Aceitos("cardType");

        // ── Concorrência ────────────────────────────────────────────────────────────────
        //
        // O GitHub não tem nada como o `Sync_version` do DevOps: a issue muda e ninguém avisa.
        // Sem esses dois campos no board, o sync grava por cima de quem mexeu depois e não tem
        // como saber. Por isso eles são campos do board como os outros — e a falta deles vira
        // aviso no relatório, não silêncio.
        public string[] SyncVersionFields { get; set; } = Aceitos("syncVersion", "nx_sync_version");
        public string[] SyncNameFields { get; set; } = Aceitos("syncName", "nx_sync_name");

        /// <summary>
        /// Campo que diz QUEM pode sincronizar: um time (`@org/time`) ou uma lista de logins
        /// separada por vírgula. Vazio libera, como o `Adm_NX` do DevOps.
        /// </summary>
        public string[] AdmGroupFields { get; set; } = Aceitos("admGroup", "nx_adm_group");

        /// <summary>
        /// Campo que diz o TIPO do EPIC: entrega, backlog ou agrupador (`EPIC_GROUP`). É o mesmo
        /// conceito do EPIC_TYPE do DevOps — e é dele que sai a única exceção da hierarquia.
        /// </summary>
        public string[] EpicTypeFields { get; set; } = Aceitos("epicType", "EPIC_TYPE", "nx_epic_type");

        /// <summary>
        /// Repositório onde as issues NOVAS são criadas, no formato `dono/repo`.
        ///
        /// É preciso dizer: no GitHub a issue nasce num REPOSITÓRIO, e o board é só uma visão
        /// sobre issues de vários. Sem isto o sync atualiza o que existe, mas não cria.
        /// </summary>
        public string Repository { get; set; } = string.Empty;

        /// <summary>Campo de iteração (sprint) do board.</summary>
        // Sprint fica SEM prefixo: iteracao e conceito nativo do GitHub Projects (um tipo de
        // campo proprio), nao invencao do NXProject.
        public string[] IterationFields { get; set; } = { "Sprint", "Iteration", "Iteração", "Iteracao" };

        /// <summary>
        /// Nome sugerido ao CRIAR o campo no board: o primeiro da lista, sempre em ingles. O
        /// resto da lista so serve para reconhecer o que ja existe.
        /// </summary>
        public static string Suggested(string[] nomes) => nomes.Length > 0 ? nomes[0] : "";

        /// <summary>
        /// Nome de fábrica deste conceito no board, no padrão PT ou EN.
        ///
        /// É o mesmo par de padrões do DevOps, com uma diferença que vem do GitHub: lá o padrão
        /// EN significa "usar o campo nativo do processo"; aqui não há nativo, então ele significa
        /// apenas "o campo se chama em inglês". Por isso o default do GitProject é o EN — é o que
        /// combina com o resto da tela do GitHub — e o PT fica para quem montou o board copiando
        /// o vocabulário do DevOps.
        /// </summary>
        public static string DefaultFieldName(string chave, bool ingles)
        {
            var spec = NxDevOpsFieldCatalog.Find(chave);
            if (spec == null) return "";
            return ingles ? NxDevOpsFieldCatalog.EnglishFieldName(chave) : spec.DefaultName;
        }

        /// <summary>
        /// O campo do board casa com algum dos nomes aceitos? Ignora maiusculas e espacos nas
        /// pontas, porque o nome do campo e digitado a mao na tela do GitHub.
        ///
        /// E publico — e testado — porque e a falha mais silenciosa deste conector: nome que nao
        /// casa nao da erro nenhum, so devolve o board com HH e datas em branco, e a suspeita cai
        /// no lugar errado.
        /// </summary>
        public static bool Matches(string? campo, IEnumerable<string>? nomes) =>
            !string.IsNullOrWhiteSpace(campo) && nomes != null
            && nomes.Any(n => !string.IsNullOrWhiteSpace(n)
                              && string.Equals(campo!.Trim(), n.Trim(), StringComparison.CurrentCultureIgnoreCase));

        // ── Tags ────────────────────────────────────────────────────────────────────────
        //
        // No GitHub a tag é a LABEL da issue — o conector já as lê, e é por elas que o board
        // sabe que uma Task está bloqueada ou acima do WIP. Dois limites que vêm de lá e mudam o
        // que se pode esperar:
        //
        //   • label é do REPOSITÓRIO, não do board: um projeto que junta issues de vários
        //     repositórios precisa da mesma label criada em cada um;
        //   • aplicar uma label que não existe não a cria — o NXProject vai precisar criá-la, e
        //     isso exige permissão de escrita no repositório.
        public string FixedStartTag { get; set; } = "DT-INI-NEG";
        public string UnplannedTag { get; set; } = "NP";
        public string WipTag { get; set; } = "WIP";
        public string BlockedTag { get; set; } = "BLOCK";

        /// <summary>URL da API GraphQL. Configurável por causa do GitHub Enterprise Server.</summary>
        public string GraphQlUrl { get; set; } = "https://api.github.com/graphql";

        /// <summary>
        /// A conexão com o GitHub Projects está em uso?
        ///
        /// Desligada, o NXProject se comporta como se ela não existisse — o par da mesma opção
        /// no Azure DevOps. Os dados ficam guardados; só deixam de valer.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Tem os dados mínimos para tentar conectar? É só o preenchimento — não olha se a
        /// conexão está ligada. Quem decide "dá para usar" é <see cref="IsConfigured"/>; esta
        /// aqui serve à tela de configuração, que precisa validar o que foi digitado mesmo com
        /// a conexão desligada.
        /// </summary>
        public bool HasData =>
            !string.IsNullOrWhiteSpace(Owner) && ProjectNumber > 0 && !string.IsNullOrWhiteSpace(Token);

        /// <summary>
        /// Dá para usar esta conexão? Preenchida E ligada. Como todo o aplicativo já pergunta
        /// isto antes de chamar a API, desligar aqui basta para o GitProject sumir de tudo —
        /// board, importação e sincronização — sem caçar um `if` em cada tela.
        /// </summary>
        public bool IsConfigured => Enabled && HasData;

        /// <summary>
        /// Classificação por nível: campo single-select do board preenchido ao CRIAR o item.
        ///
        /// É o equivalente dos "campos de classificação" do DevOps, com uma diferença que vem do
        /// GitHub: lá o campo é do work item e pode ser obrigatório; aqui é do board e nunca é
        /// obrigatório — a issue só exige título. Então isto é conveniência (nascer classificado),
        /// não exigência do servidor.
        /// </summary>
        public List<GitProjectClassification> Classifications { get; set; } = new();

        /// <summary>
        /// Tipo do campo no board para este conceito, no vocabulario da API do GitHub
        /// (ProjectV2CustomFieldType).
        ///
        /// Importa mais do que parece: campo de HH criado como TEXT aceita "8h" e nunca soma, e
        /// data criada como TEXT nao entra em nenhuma visao de roadmap. Criar com o tipo certo e
        /// a unica chance — o GitHub nao deixa trocar o tipo de um campo depois.
        /// </summary>
        public static string FieldDataType(string chave) => chave switch
        {
            "effort" or "currentHours" or "originalHours" or "remainingHours"
                or "percConclusao" or "percAloc" or "blockDuration" or "priority" => "NUMBER",
            "start" or "finish" => "DATE",
            "cardType" => "SINGLE_SELECT",
            _ => "TEXT"
        };

        /// <summary>
        /// Opcoes de fabrica de um campo single-select do NX. So o card_type tem: e ele que diz o
        /// NIVEL do item, porque o GitHub nao valida hierarquia e Issue Types so existem em
        /// organizacao.
        /// </summary>
        public static string[] DefaultOptions(string chave) => chave == "cardType"
            ? new[] { "Project", "Epic", "Feature", "Story", "Task" }
            : Array.Empty<string>();

        /// <summary>Tipo escolhido na tela de classificacao, no vocabulario da API.</summary>
        public static string DataTypeOf(string tipoNaTela) => (tipoNaTela ?? "").Trim().ToLowerInvariant() switch
        {
            "number" or "numero" or "número" => "NUMBER",
            "date" or "data" => "DATE",
            "text" or "texto" => "TEXT",
            _ => "SINGLE_SELECT"
        };

        public GitProjectOptions Clone() => (GitProjectOptions)MemberwiseClone();
    }

    /// <summary>
    /// Um campo de classificação do board: em que NÍVEL ele vale, qual campo é, e os valores
    /// aceitos. Nível "Todos" vale para qualquer item.
    /// </summary>
    public sealed class GitProjectClassification
    {
        public static readonly string[] AllLevels = { "Project", "Epic", "Feature", "Story", "Task", "Todos" };

        /// <summary>
        /// Tipos que o GitHub deixa CRIAR por API. Iteração existe como tipo, mas as iterações
        /// em si só podem ser configuradas pela tela do GitHub — por isso ela não entra aqui.
        /// </summary>
        public static readonly string[] AllFieldTypes = { "Single select", "Text", "Number", "Date" };

        public string[] AvailableLevels => AllLevels;
        public string[] AvailableFieldTypes => AllFieldTypes;

        public string Level { get; set; } = "Todos";
        /// <summary>Nome do campo no board.</summary>
        public string Field { get; set; } = string.Empty;
        /// <summary>Tipo do campo no board. Single select é o caso da classificação.</summary>
        public string FieldType { get; set; } = "Single select";
        /// <summary>Valores aceitos, separados por vírgula — viram as opções do campo.</summary>
        public string Values { get; set; } = string.Empty;

        /// <summary>
        /// Valor que o NXProject grava ao CRIAR o item. Em single-select é uma das opções; em
        /// texto, número ou data é o valor literal.
        ///
        /// O GitHub também tem valor padrão próprio (em campo de texto e número, configurado no
        /// board): ali ele vale para QUALQUER item, de onde quer que venha. Este aqui é do NX e
        /// vale só para o que o NXProject cria — por isso os dois convivem, e o do board manda
        /// quando este fica vazio.
        /// </summary>
        public string DefaultValue { get; set; } = string.Empty;
    }

    /// <summary>Valor de single-select: no GitHub se grava o ID da opção, não o texto dela.</summary>
    public sealed record GitSingleSelect(string OptionId);

    /// <summary>
    /// Um campo do board como o GitHub o descreve: id, nome, tipo e, em single-select, o id de
    /// cada opção. A leitura se vira com o nome; a escrita é toda por id.
    /// </summary>
    public sealed class GitProjectField
    {
        public GitProjectField(string id, string name, string dataType)
        { Id = id; Name = name; DataType = dataType; }

        public string Id { get; }
        public string Name { get; }
        /// <summary>NUMBER, DATE, TEXT, SINGLE_SELECT, ITERATION…</summary>
        public string DataType { get; }
        /// <summary>Opções do single-select: nome → id.</summary>
        public Dictionary<string, string> Options { get; } =
            new(StringComparer.CurrentCultureIgnoreCase);
    }

    /// <summary>Iteração (sprint) do board, já com o fim calculado a partir da duração.</summary>
    public sealed record GitIteration(string Id, string Title, DateTime Start, int DurationDays)
    {
        /// <summary>
        /// Fim INCLUSIVO, como o NXProject mostra sprint em toda a interface. O GitHub devolve
        /// início e duração em dias; uma sprint de 14 dias que começa dia 5 termina dia 18.
        /// </summary>
        public DateTime End => Start.AddDays(Math.Max(1, DurationDays) - 1);
    }

    /// <summary>
    /// Um item do board: a issue mais os valores dos campos do projeto. É o que o
    /// <see cref="GitProjectBoardService"/> transforma em linha e card.
    /// </summary>
    public sealed class GitProjectItem
    {
        public int Number { get; set; }
        public string Title { get; set; } = string.Empty;
        /// <summary>Estado da issue no GitHub: OPEN ou CLOSED.</summary>
        public string IssueState { get; set; } = string.Empty;
        public string Assignee { get; set; } = string.Empty;

        /// <summary>Login da conta atribuída — a identidade, que o nome de exibição não é.</summary>
        public string AssigneeLogin { get; set; } = string.Empty;
        public string IssueType { get; set; } = string.Empty;
        /// <summary>Número da issue pai (sub-issues). 0 = item de topo.</summary>
        public int ParentNumber { get; set; }
        public DateTime? ClosedAt { get; set; }
        public string IterationTitle { get; set; } = string.Empty;
        public string LevelValue { get; set; } = string.Empty;
        /// <summary>Estado do board: a coluna (single-select "Status"), quando houver.</summary>
        public string Status { get; set; } = string.Empty;
        public double? Effort { get; set; }
        public double? CurrentHours { get; set; }

        /// <summary>HH planejado originalmente e HH que ainda falta — os mesmos do Azure DevOps.</summary>
        public double? OriginalHours { get; set; }

        /// <inheritdoc cref="OriginalHours"/>
        public double? RemainingHours { get; set; }
        public double? Percent { get; set; }
        /// <summary>Prioridade lida do campo de numero do board (null = o board nao tem o campo).</summary>
        public double? Priority { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? FinishDate { get; set; }
        public string Labels { get; set; } = string.Empty;

        /// <summary>
        /// Id do item NO BOARD (PVTI_…). É por ele que se grava campo: no GitHub o valor é do
        /// item do projeto, não da issue.
        /// </summary>
        public string ItemId { get; set; } = string.Empty;

        /// <summary>Id da issue (I_…). É por ele que se fecha, reabre e liga sub-issue.</summary>
        public string IssueId { get; set; } = string.Empty;

        /// <summary>
        /// Repositório da issue, no formato dono/repo. No GitHub o item do board É uma issue, e
        /// issue mora num repositório — é dele que saem as pessoas que podem ser atribuídas e é
        /// nele que um filho criado pelo board nasce.
        /// </summary>
        public string Repository { get; set; } = string.Empty;

        /// <summary>
        /// Revisão do servidor: o instante da última alteração, tomado como o MAIOR entre o da
        /// issue e o do item do board.
        ///
        /// É o equivalente do `Rev` do work item do DevOps, e tem a mesma virtude: quem o
        /// atualiza é o SERVIDOR, a cada edição — inclusive quando alguém mexe pela tela do
        /// GitHub. Um contador mantido pelo NXProject só pegaria alterações feitas por outro
        /// NXProject, que é a metade errada do problema.
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// Quem alterou por último — o login de quem gravou o valor de campo mais recente do
        /// board, ou quem editou a issue.
        ///
        /// É o que o `Sync_Name` do DevOps guarda, só que aqui vem do próprio servidor em vez de
        /// ser carimbado por nós: um nome que o NXProject escreve só sabe das gravações do
        /// NXProject, e quem mexeu pela tela do GitHub ficaria invisível — justamente a pessoa
        /// que o aviso de conflito precisa nomear.
        /// </summary>
        public string LastChangedBy { get; set; } = string.Empty;

        /// <summary>Quem pode sincronizar, como está escrito no campo do board.</summary>
        public string AdmGroup { get; set; } = string.Empty;

        /// <summary>Tipo do EPIC: DELIVERY, BACKLOG ou EPIC_GROUP (agrupador de EPICs).</summary>
        public string EpicType { get; set; } = string.Empty;

        /// <summary>Controle de concorrência, quando o board tem os campos.</summary>
        public double? SyncVersion { get; set; }
        public string SyncName { get; set; } = string.Empty;
    }
}
