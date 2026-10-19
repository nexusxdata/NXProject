// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NXProject.Services
{
    /// <summary>
    /// Mapeamento de um estado TFS para um label de exibição no gráfico de status.
    /// </summary>
    public sealed class StoryStatusMapping
    {
        /// <summary>Estado exato no DevOps (ex.: "Corrigindo Causa Raiz").</summary>
        public string TfsState { get; set; } = string.Empty;

        /// <summary>Label agrupador exibido no gráfico (ex.: "Corrigindo").</summary>
        public string ChartLabel { get; set; } = string.Empty;

        /// <summary>Cor da barra em hex RRGGBB (ex.: "FFA726"). Vazio = cor automática.</summary>
        public string ColorHex { get; set; } = string.Empty;

        /// <summary>Ordem de exibição da barra (menor = mais à esquerda).</summary>
        public int Order { get; set; }
    }

    /// <summary>
    /// Configuração de um projeto no portfólio (tipo OPEX/CAPEX e centro de custo).
    /// </summary>
    public sealed class PortfolioProjectConfig
    {
        /// <summary>Nome do projeto DevOps (chave de associação).</summary>
        public string ProjectName { get; set; } = string.Empty;
        /// <summary>Caminho do arquivo .nxproject local correspondente.</summary>
        public string FilePath    { get; set; } = string.Empty;
        public bool   IsOpex           { get; set; } = true;
        public string CostCenter       { get; set; } = string.Empty;
        // "CAPEX", "OPEX" ou "EPIC". Vazio = derivado de IsOpex.
        public string CostCenterSource { get; set; } = string.Empty;

        /// <summary>
        /// Contra o que este projeto trabalha: Default (o padrão do NXProject), AzureDevOps,
        /// GitProject ou Local. É aqui que a escolha mora — não na tela —, porque é do PROJETO, e
        /// é daqui que o TaskBoard, o Importar e o Sincronizar nascem com o destino certo.
        /// </summary>
        public NxBackendKind BackendKind { get; set; } = NxBackendKind.Default;

        /// <summary>
        /// Pasta do projeto no modo Local: artefatos por atividade e o log de BLOCK. Vazio = a
        /// pasta de mesmo nome ao lado do arquivo .nxproject.
        /// </summary>
        public string LocalFolderPath { get; set; } = string.Empty;

        /// <summary>
        /// Projeto que veio do DevOps mas é trabalhado localmente: permite, depois, levar para o
        /// servidor o que foi feito no board local. Desligado por padrão — local é local até
        /// alguém pedir o contrário.
        /// </summary>
        public bool SyncLocalBoardToDevOps { get; set; }
    }

    /// <summary>Campo fixo extra enviado na criação de work items.</summary>
    public sealed class ExtraWorkItemField
    {
        /// <summary>Referência do campo no DevOps, ex.: "Custom.Type".</summary>
        public string Ref { get; set; } = string.Empty;
        /// <summary>Valor fixo a ser enviado, ex.: "Atividade".</summary>
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>
    /// Dados de conexao com o Azure DevOps / TFS usados pelo import.
    /// </summary>
    public sealed class TfsConnectionOptions
    {
        /// <summary>URL da organizacao, ex.: https://dev.azure.com/sua-organizacao </summary>
        public string OrganizationUrl { get; set; } = "";

        /// <summary>Nome do team project, ex.: Seu Projeto </summary>
        public string TeamProject { get; set; } = "";

        /// <summary>Personal Access Token (Work Items - Read).</summary>
        public string PersonalAccessToken { get; set; } = string.Empty;

        /// <summary>ID do work item raiz (tipo Project) a ser importado.</summary>
        public int RootWorkItemId { get; set; }

        /// <summary>
        /// Última issue raiz importada do GitHub Projects (0 = o board inteiro). Fica SEPARADA do
        /// <see cref="RootWorkItemId"/>: importar do GitHub gravava o número da issue (#45) como raiz
        /// do Azure DevOps, e o DevOps (e o NXPilot) passavam a apontar para um work item que não existe.
        /// </summary>
        public int GitLastRootIssue { get; set; }

        /// <summary>Horas que equivalem a 1 dia util ao converter esforco.</summary>
        public double HoursPerDay { get; set; } = 8.0;

        /// <summary>Nome do campo de esforco em horas (rotulo "HH Estimado").</summary>
        public string EffortFieldName { get; set; } = "HH Estimado";

        /// <summary>Nome do campo de data de inicio da Story.</summary>
        /// <summary>
        /// Os tres campos de horas da Story. Opcionais: o import tem fallback para todos, mas sem
        /// eles o NX perde fidelidade (restante vira o estimado inteiro, realizado some, plano
        /// original nao e preservado). Configuraveis porque o nome depende do processo.
        /// </summary>
        public string OriginalHoursFieldName { get; set; } = "HH_Original_float";

        /// <inheritdoc cref="OriginalHoursFieldName"/>
        public string RemainingHoursFieldName { get; set; } = "HH_Restante_float";

        /// <inheritdoc cref="OriginalHoursFieldName"/>
        public string CurrentHoursFieldName { get; set; } = "HH_Atual_float";

        public string StartFieldName { get; set; } = "Data_Inicio";

        /// <summary>Nome do campo de data de fim da Story.</summary>
        public string FinishFieldName { get; set; } = "Data_Fim";

        /// <summary>Nome do campo de percentual de alocação do recurso na Story (ex.: "Perc_Alocacao").</summary>
        public string PercAlocFieldName { get; set; } = "Perc_Alocacao";

        /// <summary>Nome do campo de percentual de conclusão da Story (ex.: "Perc_Conclusao").</summary>
        public string PercConclusaoFieldName { get; set; } = "Perc_Conclusao";

        /// <summary>
        /// Nome do campo de prioridade. No DevOps o padrão é o nativo "Priority"; guardar o nome
        /// aqui é o que permite apontar para outro campo no futuro, como no HH.
        /// </summary>
        public string PriorityFieldName { get; set; } = "Priority";

        /// <summary>Campo inteiro que armazena a versão de sincronização (controle de concorrência).</summary>
        public string SyncVersionFieldName { get; set; } = "Sync_version";

        /// <summary>Campo texto que armazena o nome de quem realizou a última sincronização.</summary>
        public string SyncNameFieldName { get; set; } = "Sync_Name";

        /// <summary>
        /// Habilita a leitura do campo de aprovação da Task no DevOps para preencher a coluna
        /// "Aprovada" do Task Plan. Desligado, a coluna segue com o padrão do NXProject.
        /// </summary>
        public bool ApprovedFieldEnabled { get; set; } = true;

        /// <summary>
        /// Nome (ou reference name) do campo de aprovação da Task no DevOps — ex.: "Approved"
        /// ou "Custom.Approved". Só é usado quando <see cref="ApprovedFieldEnabled"/> está ligado.
        /// </summary>
        public string ApprovedFieldName { get; set; } = "Approved";

        /// <summary>
        /// Habilita a leitura do tipo do EPIC no DevOps (ENTREGA/BACKLOG). Desligado, todo EPIC
        /// conta as horas normalmente. Padrão: habilitado.
        /// </summary>
        public bool EpicTypeFieldEnabled { get; set; } = true;

        /// <summary>
        /// Nome (ou reference name) do campo de tipo do EPIC — ex.: "EPIC_TYPE" ou
        /// "Custom.EPIC_TYPE". Só é usado com <see cref="EpicTypeFieldEnabled"/> ligado.
        /// </summary>
        public string EpicTypeFieldName { get; set; } = "EPIC_TYPE";

        /// <summary>
        /// Habilita a leitura do grupo administrador do NX no work item Project (campo
        /// <see cref="AdmGroupFieldName"/>). Os membros desse grupo do DevOps são os únicos que
        /// podem sincronizar (Export → Sincronizar). Desligado, ou campo vazio, libera para todos.
        /// </summary>
        public bool AdmGroupFieldEnabled { get; set; } = true;

        /// <summary>
        /// Nome (ou reference name) do campo do work item Project que aponta o grupo administrador
        /// do NX — ex.: "Adm_NX" ou "Custom.Adm_NX". O valor é o nome de um grupo do DevOps cujos
        /// membros podem sincronizar. Só é usado com <see cref="AdmGroupFieldEnabled"/> ligado.
        /// </summary>
        public string AdmGroupFieldName { get; set; } = "Adm_NX";

        /// <summary>
        /// Habilita faixa personalizada de prioridade da Task. Desligado (padrão), a gravação
        /// usa a faixa padrão do DevOps (1–4). Ligue apenas se o processo do DevOps aceitar
        /// valores maiores — valor fora da faixa do processo dá erro na gravação do TFS.
        /// </summary>
        public bool TaskPriorityRangeEnabled { get; set; } = false;

        /// <summary>Prioridade mínima da Task quando a faixa personalizada está habilitada.</summary>
        public int TaskPriorityMin { get; set; } = 1;

        /// <summary>Prioridade máxima da Task quando a faixa personalizada está habilitada.</summary>
        public int TaskPriorityMax { get; set; } = 4;

        /// <summary>Tag DevOps que marca data de início fixada/negociada (ex.: "DT-INI-NEG").</summary>
        public string FixedStartTagName { get; set; } = "DT-INI-NEG";

        /// <summary>Tag que marca a Task como NÃO PLANEJADA no DevOps. Aparece em destaque
        /// no card do TaskBoard quando a Task tem essa tag.</summary>
        public string UnplannedTagName { get; set; } = "NP";

        /// <summary>Tag que marca a Task como acima do limite de WIP configurado no TaskBoard.</summary>
        public string WipTagName { get; set; } = "WIP";

        /// <summary>Tag que marca a Story/Task como BLOQUEADA no DevOps.</summary>
        public string BlockedTagName { get; set; } = "BLOCK";

        /// <summary>
        /// Habilita gravar no DevOps quanto tempo o item ficou impedido (campo
        /// <see cref="BlockDurationFieldName"/>). DESLIGADO por padrão: o campo é opcional e
        /// precisa existir no processo do DevOps. Desligado, o bloqueio funciona como sempre —
        /// tag + trâmite — e nada é gravado nesse campo.
        /// </summary>
        public bool BlockDurationFieldEnabled { get; set; }

        /// <summary>
        /// Nome (ou reference name) do campo numérico que recebe o tempo TOTAL de impedimento do
        /// item, em horas inteiras — ex.: "block_duration_hours" ou "Custom.block_duration_hours".
        /// Só é usado com <see cref="BlockDurationFieldEnabled"/> ligado.
        /// </summary>
        public string BlockDurationFieldName { get; set; } = "block_duration_hours";

        /// <summary>Sincroniza links de predecessora no DevOps durante Export → Sincronizar.</summary>
        public bool SyncPredecessorLinks { get; set; } = true;

        /// <summary>Exige TKs > 0 para permitir digitar 100% manualmente em Story DevOps.</summary>
        public bool EnforceStoryCompletionWithTasks { get; set; } = true;

        /// <summary>
        /// Habilita a descoberta ampla de pessoas na organização (Discovery TFS).
        /// Requer o escopo Graph (Read) no PAT. Quando desabilitado, o botão
        /// "Discovery TFS" nem aparece na tela de Pessoas. Padrão: desabilitado.
        /// </summary>
        public bool EnableOrgPeopleDiscovery { get; set; } = false;

        /// <summary>
        /// Janela de dias futuros para incluir sprints no dropdown mesmo sem work items.
        /// Padrão 90 dias. Use 0 para incluir apenas sprints com itens importados.
        /// </summary>
        public int FutureSprintDays { get; set; } = 90;

        /// <summary>Caminho do arquivo JSON da lista de projetos DevOps (compartilhável entre usuários).</summary>
        public string DevOpsProjectListPath { get; set; } = string.Empty;

        /// <summary>
        /// A conexão com o Azure DevOps está em uso?
        ///
        /// Desligada, o NXProject se comporta como se não houvesse DevOps: a aba some e o
        /// destino deixa de ser oferecido. Serve a quem usa só o GitProject (ou só o
        /// cronograma) e a quem precisa conferir como o app fica sem ele — sem apagar a URL,
        /// o projeto e o PAT já configurados.
        ///
        /// Padrão LIGADA; nasce desligada quando não há PAT, que é o caso de quem nunca
        /// configurou — ligar algo que não funciona só produz erro na primeira tentativa.
        /// </summary>
        public bool DevOpsEnabled { get; set; } = true;

        /// <summary>
        /// Última origem escolhida na tela de importação (AzureDevOps ou GitProject). A tela
        /// reabre onde parou: trocar a origem a cada importação, num projeto que vive num só
        /// destino, era um passo repetido à toa.
        /// </summary>
        public NxBackendKind LastImportSource { get; set; } = NxBackendKind.AzureDevOps;

        /// <summary>Código do idioma selecionado pelo usuário (ex: "pt-BR", "en-US"). Vazio = detectar do Windows.</summary>
        public string Language { get; set; } = string.Empty;

        /// <summary>Nome da empresa exibido no cabeçalho do PDF exportado.</summary>
        public string CompanyName { get; set; } = string.Empty;

        /// <summary>Logo da empresa em Base64 PNG (normalizado 300×80px) para cabeçalho do PDF. Vazio = sem logo.</summary>
        public string CompanyLogoBase64 { get; set; } = string.Empty;

        /// <summary>Habilita log de diagnóstico em %LocalAppData%\NXProject.Community\sprint_alert_debug.log.</summary>
        public bool DebugLogEnabled { get; set; } = false;

        /// <summary>Carrega automaticamente o .nxb ao abrir o projeto. false = apenas via menu Abrir Baseline.</summary>
        public bool AutoLoadBaseline { get; set; } = true;

        /// <summary>Mapeamentos customizados de estado TFS → label do gráfico de status.</summary>
        public List<StoryStatusMapping> StoryStatusMappings { get; set; } = [];

        /// <summary>Caminhos de arquivos conhecidos no portfólio (Mapa de Alocação).</summary>
        public List<string> PortfolioProjectPaths { get; set; } = [];

        /// <summary>Configuração por projeto do portfólio (OPEX/CAPEX, centro de custo).</summary>
        public List<PortfolioProjectConfig> PortfolioProjectConfigs { get; set; } = [];

        /// <summary>
        /// Conexão com o GitHub Projects. Fica junto da do DevOps porque responde a mesma
        /// pergunta — "contra o que este NXProject trabalha" —, e o destino de cada projeto é
        /// escolhido no Portfólio. O token é cifrado como o PAT, nunca em texto plano.
        /// </summary>
        public GitProjectOptions GitProject { get; set; } = new();

        /// <summary>
        /// Que nome de tipo é cada nível (Project → Epic → Feature → Story → Task), nos dois
        /// destinos. Antes isso era nome fixo no código e deixava de fora quem batizou o tipo de
        /// "Iniciativa" ou "Produto".
        /// </summary>
        public NxLevelMap Levels { get; set; } = new();

        /// <summary>
        /// Campos fixos enviados em toda criação de work item (útil para campos obrigatórios
        /// do processo do cliente que o NXProject não gerencia).
        /// Exemplo: [{ "ref": "Custom.Type", "value": "Atividade" }]
        /// </summary>
        public List<ExtraWorkItemField> ExtraCreateFields { get; set; } = [];

        /// <summary>
        /// Mapeamentos de campos por tipo de work item (Epic, Feature, Story, Task).
        /// Sobrescreve os campos globais (EffortFieldName, StartFieldName, FinishFieldName etc.)
        /// para o tipo especificado.
        /// Exemplo: { "Epic": { "EffortField": "Effort", "StartField": "Start Date", "FinishField": "Target Date" } }
        /// </summary>
        public Dictionary<string, TypeFieldConfig> TypeFieldMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Valores do picklist de classificação (campo customizado obrigatório na criação).
        /// Exibidos no dropdown "Alterar Classificação" no NXProject.
        /// Vazio = usa os valores padrão embutidos.
        /// </summary>
        public List<string> ClassificationPicklistValues { get; set; } = [];

        /// <summary>Lista de categorias de atividade (campo Activity do DevOps). Configurável pelo usuário.</summary>
        public List<string> TaskActivityList { get; set; } =
        [
            "Deployment",
            "Design",
            "Development",
            "Documentation",
            "Requirements",
            "Testing"
        ];

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(OrganizationUrl) &&
            !string.IsNullOrWhiteSpace(TeamProject) &&
            !string.IsNullOrWhiteSpace(PersonalAccessToken) &&
            RootWorkItemId > 0;
    }

    /// <summary>
    /// Configuração de campos do DevOps específica por tipo de work item.
    /// Campos nulos herdam o valor global de TfsConnectionOptions.
    /// </summary>
    public sealed class TypeFieldConfig
    {
        /// <summary>Campo de esforço/horas estimadas (ex.: "Effort", "HH Estimado").</summary>
        public string? EffortField { get; set; }

        /// <summary>Campo de data de início (ex.: "Start Date", "Data_Inicio").</summary>
        public string? StartField { get; set; }

        /// <summary>Campo de data de fim (ex.: "Target Date", "Data_Fim").</summary>
        public string? FinishField { get; set; }

        /// <summary>
        /// Os tres campos de horas NESTE tipo. Existem porque o nativo depende do processo: na
        /// Task do Agile/CMMI o NX usa Original Estimate e Completed Work, mas no Scrum a Task de
        /// fabrica tem Remaining Work — e quem precisa de outro campo ali tem de poder dizer qual.
        /// Vazio = usa o campo global da configuracao.
        /// </summary>
        public string? OriginalHoursField { get; set; }

        /// <inheritdoc cref="OriginalHoursField"/>
        public string? RemainingHoursField { get; set; }

        /// <inheritdoc cref="OriginalHoursField"/>
        public string? CurrentHoursField { get; set; }

        /// <summary>Campo de percentual de alocação.</summary>
        public string? PercAlocField { get; set; }

        /// <summary>Campo de percentual de conclusão.</summary>
        public string? PercConclusaoField { get; set; }

        /// <summary>Lista de campos Custom DevOps configurados para este tipo (ex.: Custom.Type).</summary>
        public List<ClassificationFieldDef> CustomDevopsFields { get; set; } = [];
    }

    /// <summary>Definição de um campo Custom DevOps dentro de um TypeFieldConfig.</summary>
    public sealed class ClassificationFieldDef
    {
        /// <summary>Referência do campo no DevOps (ex.: "Custom.Type").</summary>
        public string Field { get; set; } = string.Empty;
        /// <summary>Tipo do campo no DevOps: Picklist, Integer, Text, Date.</summary>
        public string FieldType { get; set; } = "Picklist";
        /// <summary>Valores separados por vírgula; viram combo ao editar classificação.</summary>
        public string? Values { get; set; }

        /// <summary>
        /// Valor gravado ao CRIAR o item quando ele não tem classificação. Sem isto, o NXProject
        /// caía no tipo do work item ("User Story", "Task") — que a picklist muitas vezes nem
        /// aceita, e aí a criação falha por um valor que ninguém escolheu.
        /// </summary>
        public string? DefaultValue { get; set; }
    }

    /// <summary>
    /// Persiste a conexao do TFS (org, projeto, ultimo ID, horas/dia) e,
    /// opcionalmente, o PAT cifrado via DPAPI no escopo do usuario.
    /// </summary>
    public static class TfsConnectionStore
    {
        private sealed class StoredConnection
        {
            public string OrganizationUrl { get; set; } = "";
            public string TeamProject { get; set; } = "";
            public int RootWorkItemId { get; set; }
            public int GitLastRootIssue { get; set; }
            public double HoursPerDay { get; set; } = 8.0;
            public string EffortFieldName { get; set; } = "HH Estimado";
            public string OriginalHoursFieldName { get; set; } = "HH_Original_float";
            public string RemainingHoursFieldName { get; set; } = "HH_Restante_float";
            public string CurrentHoursFieldName { get; set; } = "HH_Atual_float";
            public string StartFieldName { get; set; } = "Data_Inicio";
            public string FinishFieldName { get; set; } = "Data_Fim";
            public string PercAlocFieldName { get; set; } = "Perc_Alocacao";
            public string PercConclusaoFieldName { get; set; } = "Perc_Conclusao";
            public string PriorityFieldName { get; set; } = "Priority";
            public string SyncVersionFieldName { get; set; } = "Sync_version";
            public string SyncNameFieldName { get; set; } = "Sync_Name";
            public string FixedStartTagName { get; set; } = "DT-INI-NEG";

        /// <summary>Tag que marca a Task como NÃO PLANEJADA no DevOps. Aparece em destaque
        /// no card do TaskBoard quando a Task tem essa tag.</summary>
        public string UnplannedTagName { get; set; } = "NP";
        /// <summary>Tag que marca a Task como acima do limite de WIP configurado no TaskBoard.</summary>
        public string WipTagName { get; set; } = "WIP";

        /// <summary>Tag que marca a Story/Task como BLOQUEADA no DevOps.</summary>
        public string BlockedTagName { get; set; } = "BLOCK";

            public bool   BlockDurationFieldEnabled { get; set; }
            public string BlockDurationFieldName { get; set; } = "block_duration_hours";
            public bool   ApprovedFieldEnabled { get; set; } = true;
            public string ApprovedFieldName { get; set; } = "Approved";
            public bool   EpicTypeFieldEnabled { get; set; } = true;
            public string EpicTypeFieldName { get; set; } = "EPIC_TYPE";
            public bool   AdmGroupFieldEnabled { get; set; } = true;
            public string AdmGroupFieldName { get; set; } = "Adm_NX";
            public bool   TaskPriorityRangeEnabled { get; set; }
            public int    TaskPriorityMin { get; set; } = 1;
            public int    TaskPriorityMax { get; set; } = 4;
            public bool SyncPredecessorLinks { get; set; } = true;
            public bool EnforceStoryCompletionWithTasks { get; set; } = true;
            public bool EnableOrgPeopleDiscovery { get; set; }
            public int FutureSprintDays { get; set; } = 90;
            public bool RememberToken { get; set; }
            public string EncryptedToken { get; set; } = string.Empty;
            public string GitOwner { get; set; } = string.Empty;
            public bool GitOwnerIsOrganization { get; set; }
            public int GitProjectNumber { get; set; }
            public string GitEncryptedToken { get; set; } = string.Empty;
            public List<GitProjectClassification> GitClassifications { get; set; } = [];
            public string GitFixedStartTag { get; set; } = "DT-INI-NEG";
            public string GitUnplannedTag { get; set; } = "NP";
            public string GitWipTag { get; set; } = "WIP";
            public string GitBlockedTag { get; set; } = "BLOCK";
            public string GitRepository { get; set; } = string.Empty;

            /// <summary>Nível → nomes aceitos, separados por vírgula. Vazio = os de fábrica.</summary>
            public Dictionary<string, string> LevelNames { get; set; } = [];

            /// <summary>Níveis desligados (time sem Project começa no Epic; sem Task, a folha é a Story).</summary>
            public List<string> DisabledLevels { get; set; } = [];

            /// <summary>
            /// Nome ESCOLHIDO para cada campo do board (chave do catálogo → nome). Guardar só a
            /// escolha, e não a lista inteira de apelidos, é o que mantém os apelidos novos de
            /// cada versão valendo para quem já configurou.
            /// </summary>
            public Dictionary<string, string> GitFieldNames { get; set; } = [];
            public string DevOpsProjectListPath { get; set; } = string.Empty;
            public string LastImportSource { get; set; } = string.Empty;
            public bool? DevOpsEnabled { get; set; }
            public bool? GitProjectEnabled { get; set; }
            public string Language { get; set; } = string.Empty;
            public string CompanyName { get; set; } = string.Empty;
            public string CompanyLogoBase64 { get; set; } = string.Empty;
            public List<StoryStatusMapping> StoryStatusMappings { get; set; } = [];
            public List<string> PortfolioProjectPaths { get; set; } = [];
            public List<PortfolioProjectConfig> PortfolioProjectConfigs { get; set; } = [];
            public bool DebugLogEnabled { get; set; } = false;
            public bool AutoLoadBaseline { get; set; } = true;
            public Dictionary<string, TypeFieldConfig> TypeFieldMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public List<string> ClassificationPicklistValues { get; set; } = [];
            public List<ExtraWorkItemField> ExtraCreateFields { get; set; } = [];
        }

        public static TfsConnectionOptions Load(string storageKey = "NXProject.Community")
        {
            var file = GetSettingsFile(storageKey);
            var options = new TfsConnectionOptions
            {
                HoursPerDay = ProjectCalendarService.WorkingHoursPerDay
            };
            if (!File.Exists(file))
                return options;

            try
            {
                var stored = JsonSerializer.Deserialize<StoredConnection>(File.ReadAllText(file));
                if (stored == null)
                    return options;

                options.OrganizationUrl = string.IsNullOrWhiteSpace(stored.OrganizationUrl)
                    ? options.OrganizationUrl
                    : stored.OrganizationUrl.Trim();
                options.TeamProject = string.IsNullOrWhiteSpace(stored.TeamProject)
                    ? options.TeamProject
                    : stored.TeamProject.Trim();
                options.RootWorkItemId = stored.RootWorkItemId;
                options.GitLastRootIssue = stored.GitLastRootIssue;
                options.HoursPerDay = stored.HoursPerDay <= 0 ? 8.0 : stored.HoursPerDay;
                options.EffortFieldName = string.IsNullOrWhiteSpace(stored.EffortFieldName)
                    ? options.EffortFieldName : stored.EffortFieldName.Trim();
                options.OriginalHoursFieldName = string.IsNullOrWhiteSpace(stored.OriginalHoursFieldName)
                    ? options.OriginalHoursFieldName : stored.OriginalHoursFieldName.Trim();
                options.RemainingHoursFieldName = string.IsNullOrWhiteSpace(stored.RemainingHoursFieldName)
                    ? options.RemainingHoursFieldName : stored.RemainingHoursFieldName.Trim();
                options.CurrentHoursFieldName = string.IsNullOrWhiteSpace(stored.CurrentHoursFieldName)
                    ? options.CurrentHoursFieldName : stored.CurrentHoursFieldName.Trim();
                options.StartFieldName = string.IsNullOrWhiteSpace(stored.StartFieldName)
                    ? options.StartFieldName : stored.StartFieldName.Trim();
                options.FinishFieldName = string.IsNullOrWhiteSpace(stored.FinishFieldName)
                    ? options.FinishFieldName : stored.FinishFieldName.Trim();
                options.PercAlocFieldName = string.IsNullOrWhiteSpace(stored.PercAlocFieldName)
                    ? options.PercAlocFieldName : stored.PercAlocFieldName.Trim();
                // Migração: nome antigo acentuado → ASCII (o campo no TFS é Perc_Alocacao).
                if (string.Equals(options.PercAlocFieldName, "Perc_Alocação", StringComparison.Ordinal))
                    options.PercAlocFieldName = "Perc_Alocacao";
                options.PercConclusaoFieldName = string.IsNullOrWhiteSpace(stored.PercConclusaoFieldName)
                    ? options.PercConclusaoFieldName : stored.PercConclusaoFieldName.Trim();
                options.PriorityFieldName = string.IsNullOrWhiteSpace(stored.PriorityFieldName)
                    ? options.PriorityFieldName : stored.PriorityFieldName.Trim();
                options.SyncVersionFieldName = string.IsNullOrWhiteSpace(stored.SyncVersionFieldName)
                    ? options.SyncVersionFieldName : stored.SyncVersionFieldName.Trim();
                options.SyncNameFieldName = string.IsNullOrWhiteSpace(stored.SyncNameFieldName)
                    ? options.SyncNameFieldName : stored.SyncNameFieldName.Trim();
                options.FixedStartTagName = string.IsNullOrWhiteSpace(stored.FixedStartTagName)
                    ? options.FixedStartTagName : stored.FixedStartTagName.Trim();
                options.UnplannedTagName = string.IsNullOrWhiteSpace(stored.UnplannedTagName)
                    ? options.UnplannedTagName : stored.UnplannedTagName.Trim();
                options.WipTagName = string.IsNullOrWhiteSpace(stored.WipTagName)
                    ? options.WipTagName : stored.WipTagName.Trim();
                options.BlockedTagName = string.IsNullOrWhiteSpace(stored.BlockedTagName)
                    ? options.BlockedTagName : stored.BlockedTagName.Trim();
                // O cronograma lê a tag de bloqueio por um ponto estático (ViewModels não têm
                // as opções em mãos) — mantém os dois lados com o mesmo nome.
                TfsImportService.BlockTagName = options.BlockedTagName;
                options.BlockDurationFieldEnabled = stored.BlockDurationFieldEnabled;
                options.BlockDurationFieldName = string.IsNullOrWhiteSpace(stored.BlockDurationFieldName)
                    ? options.BlockDurationFieldName : stored.BlockDurationFieldName.Trim();
                options.ApprovedFieldEnabled = stored.ApprovedFieldEnabled;
                options.ApprovedFieldName = string.IsNullOrWhiteSpace(stored.ApprovedFieldName)
                    ? options.ApprovedFieldName : stored.ApprovedFieldName.Trim();
                options.EpicTypeFieldEnabled = stored.EpicTypeFieldEnabled;
                options.EpicTypeFieldName = string.IsNullOrWhiteSpace(stored.EpicTypeFieldName)
                    ? options.EpicTypeFieldName : stored.EpicTypeFieldName.Trim();
                options.AdmGroupFieldEnabled = stored.AdmGroupFieldEnabled;
                options.AdmGroupFieldName = string.IsNullOrWhiteSpace(stored.AdmGroupFieldName)
                    ? options.AdmGroupFieldName : stored.AdmGroupFieldName.Trim();
                options.TaskPriorityRangeEnabled = stored.TaskPriorityRangeEnabled;
                options.TaskPriorityMin = stored.TaskPriorityMin >= 1 ? stored.TaskPriorityMin : 1;
                options.TaskPriorityMax = stored.TaskPriorityMax >= options.TaskPriorityMin
                    ? stored.TaskPriorityMax : Math.Max(options.TaskPriorityMin, 4);
                options.SyncPredecessorLinks = stored.SyncPredecessorLinks;
                options.EnforceStoryCompletionWithTasks = stored.EnforceStoryCompletionWithTasks;
                options.EnableOrgPeopleDiscovery = stored.EnableOrgPeopleDiscovery;
                options.FutureSprintDays = stored.FutureSprintDays >= 0 ? stored.FutureSprintDays : 90;
                if (stored.RememberToken)
                    options.PersonalAccessToken = WindowsDataProtection.Decrypt(stored.EncryptedToken);
                options.GitProject = new GitProjectOptions
                {
                    Owner = stored.GitOwner ?? string.Empty,
                    OwnerIsOrganization = stored.GitOwnerIsOrganization,
                    ProjectNumber = stored.GitProjectNumber,
                    Token = string.IsNullOrEmpty(stored.GitEncryptedToken)
                        ? string.Empty : WindowsDataProtection.Decrypt(stored.GitEncryptedToken),
                    Classifications = stored.GitClassifications ?? [],
                    FixedStartTag = stored.GitFixedStartTag ?? "DT-INI-NEG",
                    UnplannedTag = stored.GitUnplannedTag ?? "NP",
                    WipTag = stored.GitWipTag ?? "WIP",
                    BlockedTag = stored.GitBlockedTag ?? "BLOCK",
                    Repository = stored.GitRepository ?? string.Empty
                };
                // Configuração antiga (sem o campo): ligada quando há token — era como o
                // NXProject se comportava.
                options.GitProject.Enabled = stored.GitProjectEnabled
                    ?? !string.IsNullOrWhiteSpace(options.GitProject.Token);
                AplicaNomesGit(options.GitProject, stored.GitFieldNames);
                options.Levels = LeNiveis(stored.LevelNames, stored.DisabledLevels);
                // O mapa é consultado de métodos estáticos espalhados (board, import, sync);
                // publicá-lo aqui é o que faz a configuração valer sem passar por centenas de
                // assinaturas.
                NxLevelMap.Current = options.Levels;
                options.DevOpsProjectListPath = stored.DevOpsProjectListPath ?? string.Empty;
                // Configuração antiga (sem o campo): decide pelo que existe. Com PAT, ligada —
                // é como o NXProject sempre se comportou.
                options.DevOpsEnabled = stored.DevOpsEnabled
                    ?? !string.IsNullOrWhiteSpace(options.PersonalAccessToken);
                options.LastImportSource =
                    Enum.TryParse<NxBackendKind>(stored.LastImportSource, true, out var origem)
                        ? origem : NxBackendKind.AzureDevOps;
                options.Language = stored.Language ?? string.Empty;
                options.CompanyName = stored.CompanyName ?? string.Empty;
                options.CompanyLogoBase64 = stored.CompanyLogoBase64 ?? string.Empty;
                options.StoryStatusMappings = stored.StoryStatusMappings ?? [];
                options.PortfolioProjectPaths = stored.PortfolioProjectPaths ?? [];
                options.PortfolioProjectConfigs = stored.PortfolioProjectConfigs ?? [];
                options.DebugLogEnabled  = stored.DebugLogEnabled;
                options.AutoLoadBaseline = stored.AutoLoadBaseline;
                options.TypeFieldMappings = stored.TypeFieldMappings ?? new(StringComparer.OrdinalIgnoreCase);
                options.ClassificationPicklistValues = stored.ClassificationPicklistValues ?? [];
                options.ExtraCreateFields = stored.ExtraCreateFields ?? [];
            }
            catch
            {
                return new TfsConnectionOptions();
            }

            return options;
        }

        public static void Save(TfsConnectionOptions options, bool rememberToken, string storageKey = "NXProject.Community")
        {
            var directory = GetSettingsDirectory(storageKey);
            Directory.CreateDirectory(directory);

            var payload = new StoredConnection
            {
                OrganizationUrl = options.OrganizationUrl?.Trim() ?? string.Empty,
                TeamProject = options.TeamProject?.Trim() ?? string.Empty,
                RootWorkItemId = options.RootWorkItemId,
                GitLastRootIssue = options.GitLastRootIssue,
                HoursPerDay = options.HoursPerDay <= 0 ? 8.0 : options.HoursPerDay,
                EffortFieldName = string.IsNullOrWhiteSpace(options.EffortFieldName) ? "HH Estimado" : options.EffortFieldName.Trim(),
                StartFieldName = string.IsNullOrWhiteSpace(options.StartFieldName) ? "Data_Inicio" : options.StartFieldName.Trim(),
                FinishFieldName = string.IsNullOrWhiteSpace(options.FinishFieldName) ? "Data_Fim" : options.FinishFieldName.Trim(),
                PercAlocFieldName = string.IsNullOrWhiteSpace(options.PercAlocFieldName) ? "Perc_Alocacao" : options.PercAlocFieldName.Trim(),
                PercConclusaoFieldName = string.IsNullOrWhiteSpace(options.PercConclusaoFieldName) ? "Perc_Conclusao" : options.PercConclusaoFieldName.Trim(),
                PriorityFieldName = string.IsNullOrWhiteSpace(options.PriorityFieldName) ? "Priority" : options.PriorityFieldName.Trim(),
                SyncVersionFieldName = string.IsNullOrWhiteSpace(options.SyncVersionFieldName) ? "Sync_version" : options.SyncVersionFieldName.Trim(),
                SyncNameFieldName = string.IsNullOrWhiteSpace(options.SyncNameFieldName) ? "Sync_Name" : options.SyncNameFieldName.Trim(),
                FixedStartTagName = string.IsNullOrWhiteSpace(options.FixedStartTagName) ? "DT-INI-NEG" : options.FixedStartTagName.Trim(),
                UnplannedTagName = string.IsNullOrWhiteSpace(options.UnplannedTagName) ? "NP" : options.UnplannedTagName.Trim(),
                WipTagName = string.IsNullOrWhiteSpace(options.WipTagName) ? "WIP" : options.WipTagName.Trim(),
                BlockedTagName = string.IsNullOrWhiteSpace(options.BlockedTagName) ? "BLOCK" : options.BlockedTagName.Trim(),
                BlockDurationFieldEnabled = options.BlockDurationFieldEnabled,
                BlockDurationFieldName = string.IsNullOrWhiteSpace(options.BlockDurationFieldName)
                    ? "block_duration_hours" : options.BlockDurationFieldName.Trim(),
                ApprovedFieldEnabled = options.ApprovedFieldEnabled,
                ApprovedFieldName = string.IsNullOrWhiteSpace(options.ApprovedFieldName) ? "Approved" : options.ApprovedFieldName.Trim(),
                EpicTypeFieldEnabled = options.EpicTypeFieldEnabled,
                EpicTypeFieldName = string.IsNullOrWhiteSpace(options.EpicTypeFieldName) ? "EPIC_TYPE" : options.EpicTypeFieldName.Trim(),
                AdmGroupFieldEnabled = options.AdmGroupFieldEnabled,
                AdmGroupFieldName = string.IsNullOrWhiteSpace(options.AdmGroupFieldName) ? "Adm_NX" : options.AdmGroupFieldName.Trim(),
                TaskPriorityRangeEnabled = options.TaskPriorityRangeEnabled,
                TaskPriorityMin = options.TaskPriorityMin >= 1 ? options.TaskPriorityMin : 1,
                TaskPriorityMax = options.TaskPriorityMax >= 1 ? options.TaskPriorityMax : 4,
                SyncPredecessorLinks = options.SyncPredecessorLinks,
                EnforceStoryCompletionWithTasks = options.EnforceStoryCompletionWithTasks,
                EnableOrgPeopleDiscovery = options.EnableOrgPeopleDiscovery,
                FutureSprintDays = options.FutureSprintDays,
                RememberToken = rememberToken,
                EncryptedToken = rememberToken
                    ? WindowsDataProtection.Encrypt(options.PersonalAccessToken ?? string.Empty, "NXProject.Tfs")
                    : string.Empty,
                GitOwner = options.GitProject?.Owner ?? string.Empty,
                GitOwnerIsOrganization = options.GitProject?.OwnerIsOrganization ?? false,
                GitProjectNumber = options.GitProject?.ProjectNumber ?? 0,
                GitClassifications = options.GitProject?.Classifications ?? [],
                GitFixedStartTag = options.GitProject?.FixedStartTag ?? "DT-INI-NEG",
                GitUnplannedTag = options.GitProject?.UnplannedTag ?? "NP",
                GitWipTag = options.GitProject?.WipTag ?? "WIP",
                GitBlockedTag = options.GitProject?.BlockedTag ?? "BLOCK",
                GitRepository = options.GitProject?.Repository ?? string.Empty,
                GitFieldNames = NomesGit(options.GitProject),
                LevelNames = (options.Levels ?? new NxLevelMap()).Names
                    .ToDictionary(kv => kv.Key.ToString(), kv => string.Join(", ", kv.Value)),
                DisabledLevels = (options.Levels ?? new NxLevelMap()).Disabled
                    .Select(d => d.ToString()).ToList(),
                // Mesmo tratamento do PAT: cifrado pelo Windows e so quando o usuario pediu para
                // lembrar. Token do GitHub em texto plano seria o mesmo erro com outro nome.
                GitEncryptedToken = rememberToken && !string.IsNullOrEmpty(options.GitProject?.Token)
                    ? WindowsDataProtection.Encrypt(options.GitProject!.Token, "NXProject.GitProject")
                    : string.Empty,
                DevOpsProjectListPath = options.DevOpsProjectListPath ?? string.Empty,
                LastImportSource = options.LastImportSource.ToString(),
                DevOpsEnabled = options.DevOpsEnabled,
                GitProjectEnabled = options.GitProject?.Enabled ?? true,
                Language = options.Language ?? string.Empty,
                CompanyName = options.CompanyName ?? string.Empty,
                CompanyLogoBase64 = options.CompanyLogoBase64 ?? string.Empty,
                StoryStatusMappings = options.StoryStatusMappings ?? [],
                PortfolioProjectPaths = options.PortfolioProjectPaths ?? [],
                PortfolioProjectConfigs = options.PortfolioProjectConfigs ?? [],
                DebugLogEnabled  = options.DebugLogEnabled,
                AutoLoadBaseline = options.AutoLoadBaseline,
                TypeFieldMappings = options.TypeFieldMappings ?? new(StringComparer.OrdinalIgnoreCase),
                ClassificationPicklistValues = options.ClassificationPicklistValues ?? [],
                ExtraCreateFields = options.ExtraCreateFields ?? []
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(GetSettingsFile(storageKey), json);
        }

        /// <summary>
        /// Monta o mapa de níveis do que foi salvo. Nome em branco volta ao de fábrica: um nível
        /// sem nenhum nome não reconheceria tipo nenhum, e o cronograma abriria vazio sem dizer
        /// por quê.
        /// </summary>
        private static NxLevelMap LeNiveis(Dictionary<string, string>? nomes, List<string>? desligados)
        {
            var mapa = new NxLevelMap();
            foreach (var nivel in NxLevelMap.AllLevels)
            {
                if (nomes == null || !nomes.TryGetValue(nivel.ToString(), out var lista)) continue;
                var itens = (lista ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (itens.Count > 0) mapa.Names[nivel] = itens;
            }
            foreach (var d in desligados ?? [])
                if (Enum.TryParse<NxLevel>(d, true, out var nivel)) mapa.Disabled.Add(nivel);
            return mapa;
        }

        // ── Nomes dos campos do board ───────────────────────────────────────────────────
        //
        // Guarda-se a ESCOLHA (uma por conceito), não a lista de apelidos: os apelidos saem do
        // catálogo e evoluem com o produto; a escolha é da pessoa. Gravar a lista inteira
        // congelaria o catálogo da versão em que a configuração foi salva.

        private static readonly (string Chave, Func<GitProjectOptions, string[]> Campo)[] CamposGit =
        {
            ("effort", o => o.EffortFields),
            ("currentHours", o => o.CurrentHoursFields),
            ("start", o => o.StartDateFields),
            ("finish", o => o.FinishDateFields),
            ("percConclusao", o => o.PercentFields),
            ("priority", o => o.PriorityFields),
            ("percAloc", o => o.AllocationFields),
            ("blockDuration", o => o.BlockHoursFields),
            ("cardType", o => o.LevelFields),
            ("iteration", o => o.IterationFields),
            ("syncVersion", o => o.SyncVersionFields),
            ("syncName", o => o.SyncNameFields),
            ("admGroup", o => o.AdmGroupFields),
            ("epicType", o => o.EpicTypeFields),
        };

        private static Dictionary<string, string> NomesGit(GitProjectOptions? git)
        {
            var mapa = new Dictionary<string, string>();
            if (git == null) return mapa;
            foreach (var (chave, campo) in CamposGit)
                if (GitProjectOptions.Suggested(campo(git)) is { Length: > 0 } nome)
                    mapa[chave] = nome;
            return mapa;
        }

        private static void AplicaNomesGit(GitProjectOptions git, Dictionary<string, string>? nomes)
        {
            if (nomes == null || nomes.Count == 0) return;
            foreach (var (chave, campo) in CamposGit)
            {
                if (!nomes.TryGetValue(chave, out var escolhido) || string.IsNullOrWhiteSpace(escolhido)) continue;
                // O escolhido vai NA FRENTE e os apelidos continuam: o board que já usa outro
                // nome segue sendo reconhecido, e o sugerido passa a ser o da pessoa.
                var lista = new[] { escolhido.Trim() }
                    .Concat(campo(git))
                    .Distinct(StringComparer.CurrentCultureIgnoreCase).ToArray();
                switch (chave)
                {
                    case "effort": git.EffortFields = lista; break;
                    case "currentHours": git.CurrentHoursFields = lista; break;
                    case "start": git.StartDateFields = lista; break;
                    case "finish": git.FinishDateFields = lista; break;
                    case "percConclusao": git.PercentFields = lista; break;
                    case "priority": git.PriorityFields = lista; break;
                    case "percAloc": git.AllocationFields = lista; break;
                    case "blockDuration": git.BlockHoursFields = lista; break;
                    case "cardType": git.LevelFields = lista; break;
                    case "iteration": git.IterationFields = lista; break;
                    case "syncVersion": git.SyncVersionFields = lista; break;
                    case "syncName": git.SyncNameFields = lista; break;
                    case "admGroup": git.AdmGroupFields = lista; break;
                    case "epicType": git.EpicTypeFields = lista; break;
                }
            }
        }

        private static string GetSettingsDirectory(string storageKey)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                string.IsNullOrWhiteSpace(storageKey) ? "NXProject.Community" : storageKey.Trim());
        }

        private static string GetSettingsFile(string storageKey)
        {
            return Path.Combine(GetSettingsDirectory(storageKey), "config_nxproject.json");
        }
    }
}
