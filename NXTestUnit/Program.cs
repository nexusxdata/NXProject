// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using NXProject.Community.Services;
using NXProject.Models;
using NXProject.Services;
using NXProject.ViewModels;
using StoredConv = NXProject.Services.AiChatHistoryStore.StoredConversation;

namespace NXTestUnit;

internal static class Program
{
    private static string _solutionRoot = "";

    private static readonly List<(string Name, Action Test)> ScheduleTests =
    [
        ("Calendario: fim exclusivo aparece como fim inclusivo correto", CalendarInclusiveFinishUsesPreviousWorkingDay),
        ("Calendario: horas uteis ignoram fim de semana", CalendarWorkingHoursSkipWeekend),
        ("Calendario: feriado nao consome capacidade", CalendarHolidayIsNotWorkingCapacity),
        ("Cronograma: HH estimado calcula fim exclusivo", ScheduleEstimatedHoursCalculatesExclusiveFinish),
        ("Cronograma: percentual de alocacao aumenta duracao calendario", ScheduleAllocationPercentChangesCalendarDuration),
        ("Cronograma: matriz de percentual de alocacao do recurso", ScheduleResourceAllocationPercentMatrix),
        ("Cronograma: disponibilidade do recurso aumenta duracao calendario", ScheduleResourceAvailabilityPercentChangesCalendarDuration),
        ("Ausencia: dia de ausencia do recurso empurra o fim da atividade", AbsencePushesTaskFinish),
        ("Ausencia: sem ausencia o fim NAO muda (nao quebra o motor)", AbsenceEmptyKeepsFinish),
        ("Ausencia: com 2 recursos so conta o dia em que AMBOS faltam", AbsenceWithTwoResourcesNeedsBoth),
        ("Ausencia: fim de semana ausente nao muda nada (ja nao era util)", AbsenceOnWeekendChangesNothing),
        ("Arquivo: ausencias do recurso sobrevivem a salvar/abrir", AbsencesSurviveSaveLoad),
        ("Cronograma: tarefa 100% nao recalcula fim", ScheduleCompletedTaskKeepsFinish),
        ("Cronograma: rebuild preserva fim de tarefa 100%", RebuildKeepsCompletedTaskFinish),
        ("Cronograma: No DevOps aceita duracao zero como marco", ScheduleNoDevOpsZeroDurationCreatesMilestone),
        ("Cronograma: Marco-Devops aceita somente duracao zero", ScheduleDevOpsMilestoneAcceptsOnlyZeroDuration),
        ("Cronograma: DevOps nao aceita duracao zero como marco local", ScheduleDevOpsZeroDurationIsIgnored),
        ("Cronograma: resumo soma HH rateado (OriginalEstimated), nao span do calendario", SummaryHoursUseRatedOriginalEstimateNotCalendarSpan),
        ("Recursos: Strings.*.xaml nao tem x:Key duplicada (evita crash no load)", StringsHaveNoDuplicateResourceKeys),
        ("Board local: cronograma vira board com Story, Task e niveis", LocalBoardProjectsScheduleIntoBoard),
        ("Board local: sprint e identificada pelo IterationPath, nao pelo nome", LocalBoardSprintUsesIterationPath),
        ("GitProject: projeto vira board com hierarquia, sprint e HH", GitProjectBoardProjectsIntoBoard),
        ("GitProject: nivel sai do tipo, do campo ou da posicao na arvore", GitProjectLevelFallsBackToTreePosition),
        ("GitProject: sprint vem da iteracao com fim inclusivo", GitProjectIterationEndIsInclusive),
        ("GitProject: nome do campo casa por apelido (nx_, ingles e portugues)", GitProjectFieldNamesMatchByAlias),
        ("GitProject: colunas saem do Status do projeto, nao de um conjunto fixo", GitProjectColumnsComeFromProjectStatus),
        ("Sync GitProject: hierarquia invalida e recusada ANTES de gravar", GitSyncRefusesInvalidHierarchy),
        ("Sync GitProject: item alterado no GitHub vira conflito e nao e gravado", GitSyncConflictSkipsItem),
        ("Sync GitProject: so grava o que mudou, com fim inclusivo", GitSyncWritesOnlyChangedWithInclusiveFinish),
        ("Sync GitProject: 100% fecha a issue; voltar a andar reabre", GitSyncClosesAndReopensIssue),
        ("Sync GitProject: ordem do backlog e board sem controle de versao viram aviso", GitSyncWarnsAboutOrderAndVersioning),
        ("Sync GitProject: item novo sem repositorio nao e criado (erro claro)", GitSyncNeedsRepositoryToCreate),
        ("Niveis: tipo customizado ('Iniciativa') passa a ser reconhecido sem recompilar", LevelMapRecognizesCustomTypeName),
        ("Niveis: nivel desligado e pulado na validacao de hierarquia", LevelMapSkipsDisabledLevel),
        ("Niveis: mesmo nome em dois niveis e apontado como ambiguidade", LevelMapDetectsAmbiguity),
        ("EPIC_GROUP: agrupa EPICs e so entra sob o Work Item Project", EpicGroupIsTheOnlyEpicUnderEpic),
        ("EPIC_GROUP: sob o grupo so vem EPIC, e grupo nao aninha em grupo", EpicGroupRefusesFeatureAndNesting),
        ("EPIC_GROUP: arrasto move EPIC para dentro e para fora do grupo", DragDropMovesEpicIntoGroup),
        ("Import TFS: ramo e decidido pelo TIPO, nao pela profundidade (EPIC_GROUP)", ImportDescendsByTypeNotDepth),
        ("Ordem: a ordem desejada e a da arvore do cronograma", OrderDesiredFollowsScheduleTree),
        ("Ordem: move so o que esta fora de lugar (e nao a fila inteira)", OrderMovesOnlyWhatIsOutOfPlace),
        ("Ordem: item do servidor fora do cronograma fica onde esta", OrderLeavesForeignItemsAlone),
        ("Ordem: rank do DevOps encaixa ENTRE os vizinhos (mesma regra do move)", OrderRankFitsBetweenNeighbours),
        ("BLOCK GitProject: timeline de label vira periodos de bloqueio", GitBlockRebuildsPeriodsFromTimeline),
        ("BLOCK GitProject: bloqueio em aberto NAO e fechado em 'agora'", GitBlockOpenPeriodStaysOpen),
        ("BLOCK GitProject: saida orfa e label de outro assunto sao ignoradas", GitBlockIgnoresOrphanAndOtherLabels),
        ("Responsavel GitProject: login gravado no recurso manda sobre o nome", GitAssigneeUsesStoredLogin),
        ("Responsavel GitProject: nome ambiguo ou sem conta vira aviso, nunca palpite", GitAssigneeRefusesToGuess),
        ("Versao GitProject: edicao pela tela do GitHub (updatedAt) vira conflito", GitRevisionDetectsServerEdit),
        ("Versao GitProject: revisao cabe no campo int e preserva a ordem no tempo", GitRevisionFitsAndKeepsOrder),
        ("Adm_NX GitProject: lista de logins autoriza so quem esta nela", GitAdminListAuthorizes),
        ("Adm_NX GitProject: time da organizacao usa os membros; falha tecnica NAO bloqueia", GitAdminTeamAndFailOpen),
        ("Anexo GitProject: caminho e tipo_numero por nivel, sem titulo", GitAttachmentPathMirrorsHierarchy),
        ("Anexo GitProject: renomear o item NAO muda o caminho do anexo", GitAttachmentReusesFolderAfterRename),
        ("Campos DevOps: padrao PT usa campo proprio, EN usa o nativo quando existe", FieldNamingDefaultsPerLanguage),
        ("Capacidades: cada destino diz o que sabe fazer (sem if por nome)", BackendCapabilitiesPerKind),
        ("WIQL: clausulas do editor voltam para o mesmo WIQL", WiqlModelRoundTripsClauses),
        ("WIQL: WHERE com parenteses nao vira editor visual", WiqlModelRefusesParentheses),
        ("Prioridade: faixa central (config/descoberta) clampa e cicla corretamente", TaskPriorityRangeResolvesAndClamps),
        ("TaskBoard: colunas de estado na ordem canonica (desconhecido ao fim)", TaskboardStatesAreOrderedCanonically),
        ("Processos DevOps: estados de Agile, Scrum e CMMI sao reconhecidos", DevOpsProcessStatesAreRecognized),
        ("Idioma: mensagem de servico sai em PT ou EN conforme o idioma do app", ServiceMessagesFollowLanguage),
        ("TaskBoard: tag Doing/Done troca preservando as demais (andamento)", DoingTagMergePreservesOtherTags),
        ("TaskBoard: estado da Story incoerente com as Tasks vira alerta", StoryStateAlertCatchesMismatchWithTasks),
        ("Import TFS: task fechada nao dobra HH Original dentro do HH Atual", ClosedTaskDoesNotDoubleOriginalIntoCurrent),
        ("Import TFS: folha encerrada nao herda esforco como restante (import principal)", ImportClosedLeafHasNoRemainingHours),
        ("Cronograma: ID negativo NoDevOps aparece como interno", NoDevOpsNegativeTfsIdDisplaysAsInternal),
        ("Cronograma: DevOps pendente continua com ID interno", PendingDevOpsCreateDisplaysAsInternal),
        ("Cronograma: DevOps aceita predecessor I apenas se I tambem for DevOps", DevOpsPredecessorAcceptsInternalDevOpsOnly),
        ("Cronograma: NoDevOps aceita predecessor I de qualquer tipo", NoDevOpsPredecessorAcceptsAnyInternalType),
        ("Cronograma: arrasto permite mover Feature para outro Epic", DragDropMovesFeatureToAnotherEpic),
        ("Cronograma: arrasto permite mover Story para outra Feature", DragDropMovesStoryToAnotherFeature),
        ("Cronograma: mover Story de pai recalcula a data fim das duas Features", DragDropRecalculatesBothFeatureFinishDates),
        ("Cronograma: Feature que perde a ultima Story nao mantem data fim antiga", MovingLastStoryLeavesNoStaleFinishOnOldFeature),
        ("Cronograma: arrasto permite mover Task para outra Story", DragDropMovesTaskToAnotherStory),
        ("Cronograma: arrasto entre pais aceita soltar sobre irmao destino", DragDropMovesHierarchyItemsToSiblingInAnotherParent),
        ("Cronograma: arrasto bloqueia troca fora da hierarquia DevOps", DragDropBlocksInvalidHierarchyMoves),
        ("Import TFS: irmaos entram na ordem do backlog (rank), sem jogar item sem rank pro fim", ImportOrdersSiblingsByBacklogRank),
        ("Import TFS: ordem gravada no cronograma e a ordem recebida do DevOps", ImportedOrderMatchesReceivedOrder),
        ("Import TFS: item sem StackRank recebe rank calculado na posicao recebida", ImportFillsMissingBacklogRank),
        ("Sync TFS: reordenacao do backlog no DevOps gera aviso no relatorio", SyncWarnsWhenBacklogOrderIsRewritten),
        ("Sync TFS: ordem grava em StackRank (Agile) ou BacklogPriority (Scrum)", SyncWritesRankOnTheProcessField),
        ("Cronograma: Feature e Story preservam ordem do DevOps independente da prioridade", RebuildPreservesDevOpsHierarchyOrderIgnoringPriority),
        ("Cronograma: Task ordena por prioridade e desempata pela ordem do DevOps", RebuildOrdersTasksByPriorityThenDevOpsRank),
        ("Cronograma: botao marco cria Marco-Devops irmao para selecao DevOps", AddMilestoneCreatesDevOpsSiblingForDevOpsSelection),
        ("Cronograma: Ctrl botao marco cria Marco-Devops filho", AddMilestoneCreatesDevOpsChildWithCtrl),
        ("Cronograma: Ctrl botao marco nao cria filho em marco", AddMilestoneDoesNotCreateChildUnderMilestone),
        ("Sync TFS: Marco-Devops cria Task com tag MARCO-PROJECT", DevOpsMilestoneCreateOpsAddsMarcoProjectTag),
        ("Sync TFS: data fim usa fim inclusivo", TfsSyncFinishUsesInclusiveDate),
        ("Sync TFS: Task cria descricao no mesmo padrao da Story", TfsSyncTaskCreateOpsIncludesDescription),
        ("Import TFS: estado da Task define percentual padrao", TfsImportTaskStateDefinesDefaultPercent),
        ("Import TFS: Story iniciada/encerrada NAO e replanejada (usa Data_Inicio real)", ImportClosedStoryKeepsExplicitStart),
        ("Cronograma: editar estado da Task ajusta percentual", ScheduleStateEditUpdatesTaskPercent),
        ("Sync TFS: cadeia nova cria Feature, Story e Task no pai correto", TfsSyncNewHierarchyUsesImmediateDevOpsParent),
        ("Sync TFS: Task orfa nao cai no Work Item Project raiz", TfsSyncOrphanTaskDoesNotUseRootProject),
        ("Sync TFS: Marco-Devops usa irmao anterior como predecessora implicita", DevOpsMilestoneUsesPreviousSiblingAsImplicitPredecessor),
        ("Sync TFS: Marco-Devops sem irmao anterior usa pai como predecessora implicita", DevOpsMilestoneUsesParentAsImplicitPredecessor),
        ("Sync TFS: Marco-Devops resolve predecessora explicita com filhos", DevOpsMilestoneResolvesExplicitPredecessorWithChildren),
        ("Import TFS: Marco-Devops ignora predecessora fora da hierarquia para posicionar", DevOpsMilestonePositionIgnoresExternalHierarchyPredecessor),
        ("Import TFS: predecessora externa gera aviso sem erro", ExternalPredecessorWarnsWithoutImportError),
        ("Import TFS: Marco-Devops usa pai como ancora de posicionamento", DevOpsMilestonePositionUsesParentAnchor),
        ("Import TFS: NoDevOps preserva posicao para predecessora virtual", ImportPreservesNoDevOpsSiblingPosition),
        ("Import TFS: atividades internas DevOps sao vinculadas por nome ou preservadas", ImportMatchesOrPreservesInternalDevOpsActivities),
        ("Resumo: datas e percentual consolidam filhos", SummaryRollupUsesChildrenDatesAndHours),
        ("Resumo: atividade 100% absorve o HH Restante (antecipacao)", CompletedTaskAbsorbsRemainingHours),
        ("Alocacao: decompoe HH da Story (restante p/ responsavel, corte se estoura)", AllocationStoryDecompositionFactors),
        ("Alocacao: resumo de tasks por recurso (Closed=Completed, senao Estimate)", TaskAllocationSummaryFromDevOps),
        ("Arquivo: resumo de tasks por recurso sobrevive salvar/abrir", TaskAllocationSummaryRoundTrips),
        ("Predecessor virtual: aplica fila por mesmo recurso e recalcula fim", VirtualPredecessorQueuesSameResourceSiblings),
        ("Predecessor virtual: mudanca de duracao recalcula inicio e fim das seguintes", VirtualPredecessorDurationChangeCascadesFinish),
        ("Predecessor virtual: recalculo geral reposiciona tarefa com andamento", VirtualPredecessorRecalcMovesStartedSibling),
        ("Predecessor virtual: digitacao de HH usa anterior com predecessora explicita", DurationEditUsesPreviousSiblingEvenWhenPreviousHasExplicitPredecessor),
        ("Predecessora explicita: recalculo geral reposiciona tarefa com andamento", ExplicitPredecessorRecalcMovesStartedTask),
        ("Setup update: sem baseline conhecida nao dispara reinstalacao", SetupUpdateNoBaselineDoesNotTrigger),
        ("Setup update: asset igual a baseline nao dispara reinstalacao", SetupUpdateSameTimestampDoesNotTrigger),
        ("Setup update: asset mais antigo que a baseline nao dispara reinstalacao", SetupUpdateOlderAssetDoesNotTrigger),
        ("Setup update: asset mais novo que a baseline dispara reinstalacao", SetupUpdateNewerAssetTriggers),
        ("Import TFS: AvailabilityPercent existente e preservado sobre o valor importado", ImportPreservesExistingResourceAvailabilityPercent),
        ("Arquivo: AvailabilityPercent do recurso sobrevive a salvar/abrir", XmlRoundTripPreservesResourceAvailabilityPercent),
        ("Alocacao: recalcular repetidamente NAO infla o fim (sem compounding)", ResourceAllocationRecalcDoesNotCompoundFinish),
        ("Alocacao: cronograma, import TFS e abertura produzem o MESMO fim", CentralizedFinishCalcIsIdenticalAcrossPaths),
        ("Duracao: tarefa so com HH Atual NAO conta as horas em dobro", EffectiveDurationDoesNotDoubleCountCurrentOnlyHours),
        ("Sync conflito: gravacao do usuario atual libera se TFS aberto", SyncConflictCurrentUserOpenStateReleases),
        ("Sync conflito: NXProject 100% e outro usuario NAO libera no Sync geral", SyncConflictLocal100OtherUserBlocks),
        ("Sync conflito: NXProject 100% e TFS ja Closed NAO libera automaticamente", SyncConflictClosedOtherUserBlocks),
        ("Sync conflito: mesmo usuario e TFS Closed NAO libera automaticamente", SyncConflictCurrentUserClosedStateBlocks),
        ("Sync conflito: NXProject abaixo de 100% e outro usuario NAO libera", SyncConflictBelow100OtherUserBlocks),
        ("Sync conflito: versao a frente sem alteracao nao registra conflito", SyncConflictVersionAheadWithoutPendingWritesDoesNotBlock),
        ("Sync conflito: versao a frente em Feature/Epic nao bloqueia rollup", SyncConflictVersionAheadFeatureEpicDoesNotBlockRollup),
        ("Sync conflito: resolucao manual permite sobrescrever item iniciado", SyncConflictManualOverwriteAllowsStartedItem),
        ("Cronograma: digitacao 100% em Story exige TKs maior que zero", ManualStoryCompletionRequiresDevOpsTasks),
        ("Task Plan: criar e reabrir xlsx preserva colunas e linhas", TaskPlanCreateAndLoadRoundTrip),
        ("Task Plan: cabecalho detectado abaixo do bloco de resumo", TaskPlanDetectsHeaderBelowSummary),
        ("Task Plan: salvar preserva valores e cor de fundo", TaskPlanSavePreservesValuesAndColors),
        ("Task Plan: backup antes de salvar cria copia e aplica retencao", TaskPlanBackupBeforeSaveCreatesCopyAndRetains15Days),
        ("Task Plan: visao de filtro (EPIC/cor/coluna) sobrevive a serializacao", TaskPlanFilterViewRoundTrips),
        ("Task Plan: coluna nova grava no fim com prefixo e volta na posicao", TaskPlanNewColumnKeepsViewPosition),
        ("Task Plan: coluna excluida some da planilha ao salvar", TaskPlanDeletedColumnClearedOnSave),
        ("Task Plan: aplicar cria task interna no padrao do cronograma", TaskPlanApplyCreatesInternalTaskLikeSchedule),
        ("Task Plan IA: esforco aceita sufixo h e chave acentuada", TaskPlanAiResponseAcceptsHourSuffixAndAccentedKeys),
        ("Task Plan IA: responsavel casa nome invertido/parcial e recusa ambiguo", TaskPlanResourceMatcherHandlesCitedNames),
        ("Task Plan: story iniciada NAO tem a duracao ajustada", TaskPlanStartedStoryKeepsDuration),
        ("Task Plan: log de sync atualiza ID interno para ID DevOps na planilha", TaskPlanBackfillIdsFromSyncLog),
        ("DevOps: parser de anexos lê nome e tamanho corretamente", TfsAttachmentParserReadsNameAndSize),
        ("DevOps: URL de upload evita duplicar o projeto no caminho da org", TfsAttachmentUploadUrlDoesNotDuplicateProjectInOrgPath),
        ("Arquivo: gravar com ID interno duplicado e BLOQUEADO com a atividade na mensagem", SaveBlocksDuplicateTaskIds),
        ("Arquivo: leitura normaliza ID interno duplicado de arquivo legado", LoadNormalizesDuplicateTaskIds),
        ("Sync TFS: ID interno duplicado bloqueia a sincronizacao", SyncBlocksDuplicateTaskIds),
        ("Sync TFS: Task so grava sob Story (pai Feature/Epic e bloqueado)", SyncBlocksTaskWithoutStoryParent),
        ("Sync TFS: Marco-Devops pode ficar fora de Story", SyncAllowsDevOpsMilestoneOutsideStory),
        ("Sync TFS: duas Tasks de mesmo nome na Story bloqueiam a sincronizacao", SyncBlocksDuplicateTaskNamesInStory),
        ("IA ETA: historico persiste, adapta a duracao real e escala pelos bytes", AiRunStatsPersistsAdaptsAndScales),
        ("IA Chat: historico por cronograma respeita limite e 0=infinito", AiChatHistoryPersistsPerProjectWithLimit)
    ];

    /// <summary>
    /// Mede, no board de verdade, se a ordem do backlog volta: move um item para o topo, rele a
    /// ordem, confere, e DEVOLVE o item para onde estava.
    ///
    /// Tres perguntas, nesta ordem, porque uma so importa se a anterior passar:
    ///   1. a mutation de posicao existe e e aceita pelo token?
    ///   2. a leitura seguinte reflete a posicao nova?
    ///   3. o efeito persiste (nao e so a resposta da mutation dizendo que sim)?
    /// </summary>
    private static async System.Threading.Tasks.Task<int> ProbeGitProjectOrder()
    {
        Console.WriteLine("NXTestUnit - sonda da ordem no GitHub Projects");
        Console.WriteLine();

        var git = NXProject.Services.TfsConnectionStore.Load("NXProject.Community").GitProject;
        if (git?.IsConfigured != true)
        {
            Console.WriteLine("[SKIP] Conexao do GitProject nao configurada nesta maquina.");
            return 0;
        }

        var cliente = new NXProject.Services.GitProjectClient(git);
        var antes = await cliente.LoadAsync();
        Console.WriteLine($"Board '{antes.Title}': {antes.Items.Count} item(ns) legivel(is), "
            + $"{antes.InaccessibleItems} inacessivel(is) ao token, {antes.Fields.Count} campo(s).");

        // A ordem e dos ITENS DO BOARD: os ids vem mesmo quando o token nao abre as issues.
        // Medir por id e o que permite sondar sem depender do escopo 'repo'.
        var ids = await cliente.ListItemIdsAsync(antes.ProjectId);
        Console.WriteLine($"{ids.Count} item(ns) na fila do board.");
        if (ids.Count < 3)
        {
            Console.WriteLine("[SKIP] Menos de 3 itens: nao da para medir reordenacao.");
            return 0;
        }

        static string Curto(string id) => id.Length <= 8 ? id : id[^8..];
        string Ordem(IReadOnlyList<string> lista) => string.Join(" ", lista.Take(6).Select(Curto));

        Console.WriteLine("Ordem ANTES:   " + Ordem(ids));

        var alvo = ids[2];          // o terceiro vai para o topo
        var posicaoOriginal = ids[1];
        Console.WriteLine($"Movendo ...{Curto(alvo)} para o topo (afterId = null)...");
        try
        {
            await cliente.MoveItemAsync(antes.ProjectId, alvo, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FALHA] A mutation de posicao foi recusada: " + ex.Message);
            Console.WriteLine();
            Console.WriteLine("Conclusao: a ordem NAO pode ser gravada com este token/board.");
            return 1;
        }

        var depois = await cliente.ListItemIdsAsync(antes.ProjectId);
        Console.WriteLine("Ordem DEPOIS:  " + Ordem(depois));

        var noTopo = depois.Count > 0 && depois[0] == alvo;
        var mudou = !depois.SequenceEqual(ids);

        Console.WriteLine();
        Console.WriteLine("1) mutation aceita ................ SIM");
        Console.WriteLine("2) item aparece no topo na releitura " + (noTopo ? "SIM" : "NAO"));
        Console.WriteLine("3) a ordem lida mudou ............. " + (mudou ? "SIM" : "NAO"));

        // Segunda rodada: mover para o MEIO, que e o caso real do sync (cada item depois do
        // irmao anterior). Topo funcionar e meio nao seria a fragilidade que o plano temia.
        var segundoAlvo = depois.Count > 4 ? depois[4] : depois[^1];
        var ancora = depois[1];
        Console.WriteLine($"Movendo ...{Curto(segundoAlvo)} para depois de ...{Curto(ancora)}...");
        var meioOk = false;
        try
        {
            await cliente.MoveItemAsync(antes.ProjectId, segundoAlvo, ancora);
            var terceira = await cliente.ListItemIdsAsync(antes.ProjectId);
            Console.WriteLine("Ordem DEPOIS2: " + Ordem(terceira));
            var i = terceira.IndexOf(ancora);
            meioOk = i >= 0 && i + 1 < terceira.Count && terceira[i + 1] == segundoAlvo;
            Console.WriteLine("4) posicionou logo apos a ancora .. " + (meioOk ? "SIM" : "NAO"));
        }
        catch (Exception ex)
        {
            Console.WriteLine("4) posicionou logo apos a ancora .. ERRO: " + ex.Message);
        }

        // A sonda grava num board de verdade e nao pode deixa-lo diferente de como o encontrou.
        // Restaurar so os itens movidos nao basta: mover um empurra os outros. Reafirma-se a fila
        // INTEIRA, cada item depois do anterior — e isso tambem mede o custo real de gravar ordem.
        try
        {
            var relogio = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 1; i < ids.Count; i++)
                await cliente.MoveItemAsync(antes.ProjectId, ids[i], ids[i - 1]);
            relogio.Stop();
            var final = await cliente.ListItemIdsAsync(antes.ProjectId);
            Console.WriteLine("Ordem RESTAURADA: " + Ordem(final));
            Console.WriteLine($"Fila inteira reescrita: {ids.Count - 1} mutation(s) em "
                + $"{relogio.Elapsed.TotalSeconds:0.0}s ({relogio.ElapsedMilliseconds / Math.Max(1, ids.Count - 1)} ms por item).");
            Console.WriteLine(final.SequenceEqual(ids)
                ? "Board devolvido exatamente como estava."
                : "[AVISO] A ordem final difere da original.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[AVISO] Nao consegui restaurar a ordem original: " + ex.Message);
        }

        Console.WriteLine();
        Console.WriteLine(noTopo && meioOk
            ? "Conclusao: a ordem E gravavel, no topo e no meio, e a leitura a reflete."
            : noTopo
                ? "Conclusao: topo funciona; posicionar no MEIO nao se confirmou."
                : mudou
                    ? "Conclusao: a mutation faz efeito, mas a leitura nao devolve a ordem esperada."
                    : "Conclusao: mutation aceita e nada muda na leitura — ordem nao observavel por aqui.");
        return 0;
    }

    /// <summary>
    /// POC dos tres buracos do conector do GitHub, medidos e nao supostos:
    ///
    ///   1. **Auditoria de BLOCK** — no DevOps ela vem de `/updates`. Aqui a pergunta e se a
    ///      timeline da issue devolve quando a label de bloqueio ENTROU e SAIU, com hora e autor.
    ///   2. **Anexos** — o GitHub nao tem API de anexo de item. A pergunta e qual o melhor
    ///      substituto: arquivo versionado no repositorio + link no comentario.
    ///   3. **Responsavel** — a pergunta nao e a mutation, e se da para casar a pessoa do
    ///      cronograma com uma conta do GitHub sem adivinhar.
    ///
    /// Tudo o que escreve e desfeito no fim.
    /// </summary>
    private static async System.Threading.Tasks.Task<int> ProbeGitProjectGaps(string tokenOverride)
    {
        Console.WriteLine("NXTestUnit - POC: BLOCK, anexos e responsavel no GitHub");
        Console.WriteLine();

        var cfg = NXProject.Services.TfsConnectionStore.Load("NXProject.Community");
        var git = cfg.GitProject;
        if (git?.IsConfigured != true)
        {
            Console.WriteLine("[SKIP] Conexao do GitProject nao configurada nesta maquina.");
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(tokenOverride))
        {
            git = git.Clone();
            git.Token = tokenOverride.Trim();
            Console.WriteLine("(usando o token passado por argumento; nada foi gravado na configuracao)");
        }

        var cliente = new NXProject.Services.GitProjectClient(git);

        // Quem e o token e o que ele pode — ANTES de concluir qualquer coisa sobre o board.
        // Board vazio e token sem escopo parecem a mesma coisa de longe, e so esta linha separa.
        var (quem, escopos) = await cliente.WhoAmIAsync();
        Console.WriteLine($"Token: {quem} | escopos: {escopos}");

        var snap = await cliente.LoadAsync();
        Console.WriteLine($"Board '{snap.Title}': {snap.Items.Count} item(ns) legivel(is), "
            + $"{snap.InaccessibleItems} inacessivel(is) ao token.");
        if (snap.Items.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("[BLOQUEADO] O token nao abre as issues deste board (escopo 'repo').");
            Console.WriteLine("As tres perguntas desta POC sao sobre a ISSUE (timeline, comentario,");
            Console.WriteLine("responsavel), entao nenhuma pode ser respondida com este token.");
            Console.WriteLine($"Escopos deste token: {escopos}.");
            // 'repo:invite' e a armadilha: o nome parece dar acesso ao repositorio, mas trata so
            // de convites de colaboracao. Quem marca as subcaixas em vez da caixa do topo acaba
            // com um token que le o board e nao le nenhuma issue.
            if (escopos.Contains("repo:invite") && !escopos.Split(',').Any(x => x.Trim() == "repo"))
                Console.WriteLine("ATENCAO: 'repo:invite' NAO da acesso as issues — e so convite de "
                    + "colaboracao. Marque a caixa 'repo' do TOPO (Full control of private repositories).");
            Console.WriteLine("Gere um token classico com 'project' + 'repo' e rode de novo:");
            Console.WriteLine("   dotnet run --project NXTestUnit -c Release -- gitpoc");
            Console.WriteLine();
            Console.WriteLine("Enquanto isso, mede-se o que NAO depende do repositorio privado: a");
            Console.WriteLine("FORMA da API, em dado publico. Isso separa 'o GitHub nao sabe fazer'");
            Console.WriteLine("de 'este token nao pode' — que sao problemas bem diferentes.");
            Console.WriteLine();
            await MedeEmDadoPublico(cliente);
            return 2;
        }

        var issue = snap.Items[0];
        // O repositorio sai da propria issue quando nao esta configurado: o board e uma visao
        // sobre issues de varios repos, e o dado ja diz onde cada uma vive.
        var nomeRepo = git.Repository;
        if (string.IsNullOrWhiteSpace(nomeRepo))
        {
            nomeRepo = await cliente.IssueRepositoryAsync(issue.IssueId);
            if (!string.IsNullOrWhiteSpace(nomeRepo))
                Console.WriteLine($"(repositorio deduzido da issue: {nomeRepo})");
        }
        var repo = (nomeRepo ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine($"Issue de teste: #{issue.Number} '{issue.Title}'.");
        Console.WriteLine();
        Console.WriteLine("0) QUEM ALTEROU POR ULTIMO (o Sync_Name vindo do servidor)");
        foreach (var i in snap.Items.Take(5))
            Console.WriteLine($"   #{i.Number} {i.UpdatedAt:dd/MM/yyyy HH:mm} por "
                + (string.IsNullOrWhiteSpace(i.LastChangedBy) ? "(nao informado)" : i.LastChangedBy));
        Console.WriteLine();

        // ── 1. BLOCK ────────────────────────────────────────────────────────────────────
        Console.WriteLine("1) AUDITORIA DE BLOCK");
        try
        {
            // Nao basta ler timeline de issue alheia: o que interessa e o CICLO que o NXProject
            // faria — bloquear, desbloquear e depois reconstruir o periodo. Entao a POC provoca
            // os eventos e os apaga no fim.
            var tag = "BLOCK-POC";
            var antes = (await cliente.LabelHistoryAsync(issue.IssueId)).Count;
            if (repo.Length == 2)
            {
                var (ok, erro) = await cliente.AddLabelAsync(repo[0], repo[1], issue.Number, tag);
                if (!ok) Console.WriteLine("   [FALHA] nao consegui aplicar a label: " + erro);
                else
                {
                    await cliente.RemoveLabelAsync(repo[0], repo[1], issue.Number, tag);
                    Console.WriteLine($"   ciclo exercitado em #{issue.Number}: label aplicada e removida.");
                }
            }

            var eventos = await cliente.LabelHistoryAsync(issue.IssueId);
            Console.WriteLine($"   timeline lida: {eventos.Count} evento(s) de label ({antes} antes do teste).");
            foreach (var e in eventos.TakeLast(6))
                Console.WriteLine($"   {e.CreatedAt:dd/MM/yyyy HH:mm:ss}  {(e.Added ? "+" : "-")}{e.Label}  ({e.Actor})");

            var entrada = eventos.LastOrDefault(e => e.Added && e.Label == tag);
            var saida = eventos.LastOrDefault(e => !e.Added && e.Label == tag);
            if (entrada != null && saida != null)
            {
                var duracao = saida.CreatedAt - entrada.CreatedAt;
                Console.WriteLine($"   periodo de bloqueio reconstruido: {duracao.TotalSeconds:0.0}s "
                    + $"({entrada.CreatedAt:HH:mm:ss} -> {saida.CreatedAt:HH:mm:ss}, por {entrada.Actor}).");
                Console.WriteLine("   => DA para auditar BLOCK como no DevOps: entrada, saida, hora e autor.");
            }
            else
                Console.WriteLine(eventos.Count > antes
                    ? "   => a timeline registrou, mas nao devolveu o par entrada/saida da label de teste."
                    : "   => a timeline nao registrou o ciclo.");

            if (repo.Length == 2) await cliente.DeleteLabelAsync(repo[0], repo[1], tag);
        }
        catch (Exception ex)
        {
            Console.WriteLine("   [FALHA] " + ex.Message);
        }

        // ── 2. Anexos ───────────────────────────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("2) ANEXOS");
        if (repo.Length != 2)
            Console.WriteLine("   [SKIP] Repositorio nao configurado (dono/repo) na conexao do GitProject.");
        else
        {
            try
            {
                var caminho = $"nxproject-anexos/poc-{DateTime.Now:yyyyMMddHHmmss}.txt";
                var (ok, url, sha, erro) = await cliente.PutFileAsync(repo[0], repo[1], caminho,
                    "POC do NXProject: anexo versionado no repositorio.", "NXProject POC (anexo)");
                if (!ok)
                {
                    Console.WriteLine("   [FALHA] nao consegui versionar o arquivo: " + erro);
                    Console.WriteLine("   => sem permissao de escrita no repositorio, anexo nao tem onde morar.");
                }
                else
                {
                    Console.WriteLine("   arquivo versionado: " + url);
                    await cliente.DeleteFileAsync(repo[0], repo[1], caminho, sha, "NXProject POC (limpeza)");
                    Console.WriteLine("   arquivo removido (a POC nao deixa sujeira).");
                    Console.WriteLine("   => MELHOR CASO: anexo = arquivo no repositorio + link no item.");
                    Console.WriteLine("      Versionado e com historico — mais do que o anexo do DevOps tem.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("   [FALHA] " + ex.Message);
            }
        }

        // ── 3. Responsavel ──────────────────────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("3) RESPONSAVEL");
        if (repo.Length != 2)
            Console.WriteLine("   [SKIP] Repositorio nao configurado (dono/repo).");
        else
        {
            try
            {
                var pessoas = await cliente.AssignableUsersAsync(repo[0], repo[1]);
                Console.WriteLine($"   {pessoas.Count} conta(s) atribuivel(is) no repositorio:");
                foreach (var (login, nome) in pessoas.Take(10))
                    Console.WriteLine($"   {login}  ({(string.IsNullOrWhiteSpace(nome) ? "sem nome" : nome)})");

                // A pergunta real: o nome do recurso do cronograma casa com alguma conta?
                var comNome = pessoas.Count(p => !string.IsNullOrWhiteSpace(p.Name));
                Console.WriteLine($"   contas com nome de exibicao preenchido: {comNome} de {pessoas.Count}.");
                Console.WriteLine(comNome == pessoas.Count
                    ? "   => DA para casar por nome; mesmo assim o mapa pessoa->conta tem de ser confirmado."
                    : "   => parte das contas nao tem nome: casar so por nome erraria em silencio.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("   [FALHA] " + ex.Message);
            }
        }

        Console.WriteLine();
        Console.WriteLine("POC concluida.");
        return 0;
    }

    /// <summary>
    /// Mede a forma da API em dado PUBLICO: responde "o GitHub sabe fazer isto?" sem depender de
    /// permissao no repositorio do usuario. O que sobra para o token e so "voce pode?".
    /// </summary>
    private static async System.Threading.Tasks.Task MedeEmDadoPublico(NXProject.Services.GitProjectClient cliente)
    {
        Console.WriteLine("1) AUDITORIA DE BLOCK (forma da API, em issue publica)");
        try
        {
            var (id, onde) = await cliente.AnyPublicLabeledIssueAsync("is:issue label:bug is:public sort:updated-desc");
            if (string.IsNullOrEmpty(id)) { Console.WriteLine("   [SKIP] nenhuma issue publica encontrada."); }
            else
            {
                var eventos = await cliente.LabelHistoryAsync(id);
                Console.WriteLine($"   issue {onde}: {eventos.Count} evento(s) de label.");
                foreach (var e in eventos.Take(4))
                    Console.WriteLine($"   {e.CreatedAt:dd/MM/yyyy HH:mm}  {(e.Added ? "+" : "-")}{e.Label}  ({e.Actor})");
                Console.WriteLine(eventos.Count > 0
                    ? "   => A API DEVOLVE entrada/saida de label com hora e autor: a auditoria de"
                      + " BLOCK e possivel, do mesmo jeito que o /updates do DevOps permite."
                    : "   => a issue sorteada nao teve troca de label; forma confirmada, sem dado.");
            }
        }
        catch (Exception ex) { Console.WriteLine("   [FALHA] " + ex.Message); }

        Console.WriteLine();
        Console.WriteLine("2) ANEXOS (forma da API)");
        Console.WriteLine("   O GitHub NAO tem API de anexo de item: na tela o arquivo vai para um");
        Console.WriteLine("   CDN pelo corpo do comentario, e esse caminho nao e automatizavel.");
        Console.WriteLine("   O substituto e a API de conteudo do repositorio (PUT /contents), que");
        Console.WriteLine("   versiona o arquivo e devolve link — ela exige escrita no repositorio,");
        Console.WriteLine("   entao so da para confirmar com o token com escopo 'repo'.");

        Console.WriteLine();
        Console.WriteLine("3) RESPONSAVEL (forma da API, em repositorio publico)");
        try
        {
            var pessoas = await cliente.AssignableUsersAsync("github", "docs");
            Console.WriteLine($"   assignableUsers devolveu {pessoas.Count} conta(s) num repo publico.");
            var comNome = pessoas.Count(p => !string.IsNullOrWhiteSpace(p.Name));
            Console.WriteLine($"   com nome de exibicao preenchido: {comNome} de {pessoas.Count}.");
            Console.WriteLine("   => A lista de candidatos EXISTE. O que falta nao e API: e o mapa");
            Console.WriteLine("      pessoa do cronograma -> conta, que precisa ser confirmado por");
            Console.WriteLine("      alguem. Casar so por nome erraria em silencio.");
        }
        catch (Exception ex) { Console.WriteLine("   [FALHA] " + ex.Message); }
    }

    /// <summary>
    /// Monta o board como a tela monta e imprime a hierarquia que ele resolveu para cada Story.
    /// <paramref name="sprint"/> vazio = todas as sprints.
    /// </summary>
    /// <summary>Mostra os campos crus de um work item — serve para conferir o EPIC_TYPE.</summary>
    private static async System.Threading.Tasks.Task<int> ProbeItem(string idTexto)
    {
        var cfg = NXProject.Services.TfsConnectionStore.Load("NXProject.Community");
        if (!int.TryParse(idTexto, out var id) || string.IsNullOrWhiteSpace(cfg.PersonalAccessToken))
        {
            Console.WriteLine("uso: NXTestUnit item <id>   (com a conexao do DevOps configurada)");
            return 0;
        }

        var org = (cfg.OrganizationUrl ?? "").TrimEnd('/');
        using var http = new System.Net.Http.HttpClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(":" + cfg.PersonalAccessToken)));
        var url = $"{org}/_apis/wit/workitems/{id}?$expand=all&api-version=7.1";
        var resp = await http.GetAsync(url);
        var texto = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) { Console.WriteLine($"HTTP {(int)resp.StatusCode}: {texto[..Math.Min(300, texto.Length)]}"); return 1; }

        // MESMO endpoint que o board usa (workitemsbatch + $expand=all): e aqui que se descobre
        // se o campo chega na leitura em lote ou so na leitura individual.
        var corpoLote = $"{{\"ids\":[{id}],\"$expand\":\"all\"}}";
        using (var reqLote = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post,
                   $"{org}/_apis/wit/workitemsbatch?api-version=7.1")
               { Content = new System.Net.Http.StringContent(corpoLote, System.Text.Encoding.UTF8, "application/json") })
        {
            var respLote = await http.SendAsync(reqLote);
            var txtLote = await respLote.Content.ReadAsStringAsync();
            Console.WriteLine($"[batch] HTTP {(int)respLote.StatusCode} | tem EPIC_TYPE: "
                + txtLote.Contains("EPIC_TYPE", StringComparison.OrdinalIgnoreCase));
        }

        using var doc = System.Text.Json.JsonDocument.Parse(texto);
        var f = doc.RootElement.GetProperty("fields");
        Console.WriteLine($"#{id}");
        foreach (var prop in f.EnumerateObject())
        {
            var nome = prop.Name;
            if (nome.Contains("EPIC", StringComparison.OrdinalIgnoreCase)
                || nome is "System.Title" or "System.WorkItemType" or "System.Parent"
                || nome.Contains("Tipo", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"   {nome} = {prop.Value}");
        }
        return 0;
    }

    private static async System.Threading.Tasks.Task<int> ProbeBoard(string sprint, string filtro)
    {
        Console.WriteLine("NXTestUnit - sonda do TaskBoard (hierarquia resolvida por linha)");
        Console.WriteLine();

        var cfg = NXProject.Services.TfsConnectionStore.Load("NXProject.Community");
        if (string.IsNullOrWhiteSpace(cfg.OrganizationUrl) || string.IsNullOrWhiteSpace(cfg.TeamProject)
            || string.IsNullOrWhiteSpace(cfg.PersonalAccessToken))
        {
            Console.WriteLine("[SKIP] Conexao do Azure DevOps nao configurada nesta maquina.");
            return 0;
        }

        var paths = string.IsNullOrWhiteSpace(sprint) ? new List<string>() : new List<string> { sprint };
        var board = await NXProject.Services.TfsImportService.BuildSprintBoardAsync(cfg, paths);

        Console.WriteLine($"{board.Stories.Count} linha(s) no board.");
        Console.WriteLine();
        Console.WriteLine("Story                         | Projeto         | Grupo           | EPIC            | Feature");
        Console.WriteLine(new string('-', 110));

        static string Col(string? t, int id, int largura)
        {
            var txt = string.IsNullOrWhiteSpace(t) ? "(vazio)" : t.Trim();
            if (id > 0) txt = $"#{id} {txt}";
            return txt.Length <= largura ? txt.PadRight(largura) : txt.Substring(0, largura - 1) + "…";
        }

        // Filtro opcional: "board <sprint> <texto>" mostra so as linhas cujo EPIC, grupo ou
        // titulo contenham o texto — util quando o board tem centenas de linhas.
        var linhas = board.Stories.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(filtro))
            linhas = linhas.Where(x =>
                (x.Title ?? "").Contains(filtro, StringComparison.CurrentCultureIgnoreCase)
                || (x.FeatureEpicTitle ?? "").Contains(filtro, StringComparison.CurrentCultureIgnoreCase)
                || (x.FeatureEpicGroupTitle ?? "").Contains(filtro, StringComparison.CurrentCultureIgnoreCase));

        foreach (var st in linhas.Take(40))
            Console.WriteLine(
                $"{Col(st.Title, st.Id, 29)} | {Col(st.FeatureProjectTitle, st.FeatureProjectId, 15)} | "
                + $"{Col(st.FeatureEpicGroupTitle, st.FeatureEpicGroupId, 15)} | "
                + $"{Col(st.FeatureEpicTitle, st.FeatureEpicId, 15)} | {Col(st.FeatureTitle, st.FeatureId, 20)}");

        var semFeature = board.Stories.Count(s => s.FeatureId <= 0);
        var semEpic = board.Stories.Count(s => s.FeatureEpicId <= 0);
        var comGrupo = board.Stories.Count(s => s.FeatureEpicGroupId > 0);
        Console.WriteLine();
        Console.WriteLine($"sem Feature: {semFeature} | sem EPIC: {semEpic} | com EPIC_GROUP: {comGrupo}");
        return 0;
    }

    private static int Main(string[] args)
    {
        var category = args.Length > 0 ? args[0] : "schedule";
        if (string.Equals(category, "simulate-openai", StringComparison.OrdinalIgnoreCase))
        {
            SimulateOpenAi();
            return 0;
        }
        if (string.Equals(category, "ai-sim", StringComparison.OrdinalIgnoreCase))
            return RunAiIncludeSimulation(
                args.Length > 1 ? args[1] : "",
                args.Length > 2 ? args[2] : "",
                args.Length > 3 ? args[3] : "").GetAwaiter().GetResult();
        // Sonda da ordem no GitHub Projects. Fica FORA da suite normal porque GRAVA num board
        // real: o plano pedia um prototipo que reordenasse e relesse, e supor fragilidade sem
        // medir seria o oposto do que o resto deste arquivo faz.
        if (string.Equals(category, "gitorder", StringComparison.OrdinalIgnoreCase))
            return ProbeGitProjectOrder().GetAwaiter().GetResult();
        // POC dos tres itens que o plano dava como "nao fazer": auditoria de BLOCK, anexos e
        // responsavel. Mesma receita da sonda de ordem — medir no servidor de verdade, porque foi
        // assim que a suposicao sobre a ordem caiu.
        if (string.Equals(category, "gitpoc", StringComparison.OrdinalIgnoreCase))
            // Token opcional por argumento: permite medir com um token de escopo maior SEM
            // grava-lo na configuracao da maquina. O que e so para medir nao deve virar
            // credencial persistida.
            return ProbeGitProjectGaps(args.Length > 1 ? args[1] : "").GetAwaiter().GetResult();
        // Sonda do TaskBoard: mostra, linha a linha, que Projeto/EPIC/Grupo/Feature o board
        // resolveu para cada Story. Serve para separar "a hierarquia nao tem Feature" de "o
        // board nao achou a Feature" — duas causas que na tela parecem a mesma coisa.
        if (string.Equals(category, "item", StringComparison.OrdinalIgnoreCase))
            return ProbeItem(args.Length > 1 ? args[1] : "").GetAwaiter().GetResult();
        if (string.Equals(category, "board", StringComparison.OrdinalIgnoreCase))
            return ProbeBoard(args.Length > 1 ? args[1] : "", args.Length > 2 ? args[2] : "").GetAwaiter().GetResult();
        if (string.Equals(category, "xml-diagnose", StringComparison.OrdinalIgnoreCase))
            return DiagnoseXml(args.Length > 1 ? args[1] : "");
        _solutionRoot = args.Length > 1 ? args[1] : Directory.GetCurrentDirectory();

        List<(string Name, Action Test)> tests = category.ToLowerInvariant() switch
        {
            "packaging-community" =>
            [
                ("Empacotamento: NXProject.Community-Release.zip contem arquivos essenciais", ValidateCommunityReleaseZip),
                ("Empacotamento: Release.zip nao vira standalone", ValidateCommunityReleaseIsNotStandalone),
                ("Empacotamento: toda DLL de terceiros do publish esta no NXProject-Setup.zip", ValidateThirdPartyLibsAreInSetupZip)
            ],
            "packaging-setup" =>
            [
                ("Empacotamento: NXProject-Setup.zip contem runtime e libs essenciais", ValidateSetupZip),
                ("Empacotamento: NXProject-Setup.zip contem DLLs necessarias", ValidateSetupZipKeepsRequiredDlls),
                ("Setup: timestamp e intrinseco ao zip e igual ao embutido no build", ValidateSetupTimestampIntrinsic),
                ("Empacotamento: toda DLL de terceiros do publish esta no NXProject-Setup.zip", ValidateThirdPartyLibsAreInSetupZip)
            ],
            "packaging" => PackagingTests,
            "ai" => AiIntegrationTests,
            _ => ScheduleTests
        };

        // Integração com IA Local: só roda quando os recursos estão instalados na máquina
        // de compilação (pasta configurada com llama.dll + modelo .gguf); senão, pula.
        if (ReferenceEquals(tests, AiIntegrationTests) && !IsLocalAiInstalled())
        {
            Console.WriteLine("NXTestUnit - integracao com IA Local");
            Console.WriteLine();
            Console.WriteLine("[SKIP] IA Local nao instalada nesta maquina - bloco de integracao ignorado.");
            return 0;
        }

        // Qualquer categoria desconhecida cai nos testes de cronograma (default do switch),
        // entao "isSchedule" deve seguir a MESMA regra — senao os testes de cronograma
        // rodam sem ResetCalendar() e usam um calendario default (datas +1 dia).
        var isSchedule = ReferenceEquals(tests, ScheduleTests);

        if (isSchedule)
            SetCurrentCalendar(new ProjectCalendar());

        var failures = new List<string>();
        Console.WriteLine(isSchedule
            ? "NXTestUnit - testes criticos de cronograma"
            : ReferenceEquals(tests, AiIntegrationTests)
                ? "NXTestUnit - integracao com IA Local"
                : "NXTestUnit - validacao de empacotamento (zips de release)");
        Console.WriteLine();

        foreach (var (name, test) in tests)
        {
            try
            {
                if (isSchedule)
                    ResetCalendar();
                test();
                Console.WriteLine($"[OK]   {name}");
            }
            catch (Exception ex)
            {
                failures.Add($"{name}: {ex.Message}");
                Console.WriteLine($"[FAIL] {name}");
                Console.WriteLine($"       {ex.Message}");
            }
        }

        Console.WriteLine();
        var suiteLabel = ReferenceEquals(tests, AiIntegrationTests) ? "Integracao IA Local" : "NXTestUnit";
        if (failures.Count == 0)
        {
            Console.WriteLine($"{suiteLabel} concluido: {tests.Count} testes passaram.");
            return 0;
        }

        Console.WriteLine($"{suiteLabel} falhou: {failures.Count} de {tests.Count} testes falharam.");
        foreach (var failure in failures)
            Console.WriteLine($" - {failure}");

        return 1;
    }

    private static int DiagnoseXml(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            Console.WriteLine("Uso: dotnet run --project NXTestUnit -- xml-diagnose <arquivo.xml>");
            Console.WriteLine($"Arquivo nao encontrado: {filePath}");
            return 1;
        }

        Console.WriteLine($"Arquivo: {filePath}");
        try
        {
            var doc = System.Xml.Linq.XDocument.Load(filePath);
            var root = doc.Root;
            Console.WriteLine($"Raiz: {root?.Name.LocalName}");
            Console.WriteLine($"Namespace: {root?.Name.NamespaceName}");
            Console.WriteLine($"Filhos raiz: {string.Join(", ", root?.Elements().Take(20).Select(e => e.Name.LocalName) ?? [])}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"XML invalido: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }

        TryImport("NXProject nativo", () => XmlProjectService.Load(filePath));
        TryImport("Fluxo Abrir/MainViewModel", () =>
        {
            var vm = new MainViewModel("NXProject.Community");
            vm.LoadProjectFromPath(filePath);
            return vm.Project;
        });
        TryImport("MS Project MSPDI", () => MspdiImportService.Import(filePath));
        TryImport("Excel XML 2003", () => ExcelXmlService.Import(filePath));
        return 0;
    }

    private static void TryImport(string label, Func<Project> load)
    {
        Console.WriteLine();
        Console.WriteLine($"[{label}]");
        try
        {
            var project = load();
            var tasks = FlattenForDiagnostics(project.Tasks).ToList();
            Console.WriteLine($"Projeto: {project.Name}");
            Console.WriteLine($"HiddenColumns: '{project.HiddenColumns}'");
            Console.WriteLine($"HiddenColumnsExpanded: '{project.HiddenColumnsExpanded}'");
            Console.WriteLine($"Tarefas: {tasks.Count}");
            Console.WriteLine($"Tarefas sem nome: {tasks.Count(t => string.IsNullOrWhiteSpace(t.Name))}");
            Console.WriteLine($"Tarefas com tag Block: {tasks.Count(t => HasBlockTagForDiagnostics(t.Tags))}");
            Console.WriteLine($"Tarefas bloqueadas por filha: {tasks.Count(t => t.BlockedByChild)}");
            Console.WriteLine($"Tarefas sem datas validas: {tasks.Count(t => t.Start == default || t.Finish == default)}");
            Console.WriteLine($"Recursos: {project.Resources.Count}");
            foreach (var task in tasks.Take(8))
            {
                Console.WriteLine($" - ID={task.Id}; Nivel={task.Level}; Nome='{task.Name}'; Inicio={task.Start:yyyy-MM-dd}; Fim={task.Finish:yyyy-MM-dd}; Tipo='{task.TfsType}'; TfsId={task.TfsId}; Tags='{task.Tags}'; BlockedByChild={task.BlockedByChild}");
            }
            foreach (var task in tasks.Where(t => HasBlockTagForDiagnostics(t.Tags) || t.BlockedByChild).Take(8))
                Console.WriteLine($" * BLOQUEIO ID={task.Id}; Nome='{task.Name}'; Tags='{task.Tags}'; BlockedByChild={task.BlockedByChild}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERRO: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool HasBlockTagForDiagnostics(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
            return false;

        return tags.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(tag => string.Equals(tag, "Block", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(tag, "Blocked", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<ProjectTask> FlattenForDiagnostics(IEnumerable<ProjectTask> tasks)
    {
        foreach (var task in tasks)
        {
            yield return task;
            foreach (var child in FlattenForDiagnostics(task.Children))
                yield return child;
        }
    }

    private static readonly List<(string Name, Action Test)> PackagingTests =
    [
        ("Empacotamento: NXProject.Community-Release.zip contem arquivos essenciais", ValidateCommunityReleaseZip),
        ("Empacotamento: Release.zip nao vira standalone", ValidateCommunityReleaseIsNotStandalone),
        ("Empacotamento: NXProject-Setup.zip contem runtime e libs essenciais", ValidateSetupZip),
        ("Empacotamento: NXProject-Setup.zip contem DLLs necessarias", ValidateSetupZipKeepsRequiredDlls),
        ("Setup: timestamp e intrinseco ao zip e igual ao embutido no build", ValidateSetupTimestampIntrinsic),
        ("Empacotamento: toda DLL de terceiros do publish esta no NXProject-Setup.zip", ValidateThirdPartyLibsAreInSetupZip)
    ];

    // ── Integração com IA Local (LLaMA) — roda só com os recursos instalados ─────
    private static readonly List<(string Name, Action Test)> AiIntegrationTests =
    [
        ("IA Local: instalacao valida (llama.dll carrega e modelo GGUF integro)", AiLocalInstallationIsValid),
        ("IA Local: inferencia responde a um prompt minimo", AiLocalInferenceResponds)
    ];

    private static bool IsLocalAiInstalled()
    {
        var folder = LocalAIResourceStore.LoadFolder();
        return !string.IsNullOrWhiteSpace(folder)
               && Directory.Exists(folder)
               && File.Exists(Path.Combine(folder, LocalAIResourceStore.NativeDllName))
               && Directory.EnumerateFiles(folder, "*.gguf").Any();
    }

    private static void AiLocalInstallationIsValid()
    {
        var folder = LocalAIResourceStore.LoadFolder();
        var r = LocalAIResourceStore.Validate(folder);
        if (!r.NativePresent || !r.NativeLoads)
            throw new InvalidOperationException(r.NativeMessage ?? "llama.dll nao carregou.");
        if (!r.ModelPresent || !r.ModelValid)
            throw new InvalidOperationException(r.ModelMessage ?? "modelo GGUF invalido.");
    }

    /// <summary>
    /// Simulação da ação "Incluir Tasks na Planilha" com a IA Local, no formato de contexto
    /// OFICIAL do Task Plan (Stories em aberto, tabela compacta "id | nome | feature | epic",
    /// sem tasks da planilha). Mede prefill (contexto), geração (tokens/s) e tempo total.
    /// Uso: NXTestUnit.exe ai-sim "cronograma.xml" "texto.txt" [feature-nova|story-nova]
    /// </summary>
    private static async Task<int> RunAiIncludeSimulation(string xmlPath, string textPath, string mode = "")
    {
        if (!File.Exists(xmlPath) || !File.Exists(textPath))
        {
            Console.WriteLine($"Arquivos nao encontrados:\n  {xmlPath}\n  {textPath}");
            return 1;
        }

        var project = XmlProjectService.Load(xmlPath);
        var text = File.ReadAllText(textPath);

        var stories = new List<ProjectTask>();
        void Collect(IEnumerable<ProjectTask> tasks)
        {
            foreach (var t in tasks)
            {
                if (TfsImportService.IsStoryTypePublic(t.TfsType) && t.PercentComplete < 100)
                    stories.Add(t);
                Collect(t.Children);
            }
        }
        Collect(project.Tasks);

        static string AncestorName(ProjectTask t, string type)
        {
            for (var p = t.Parent; p != null; p = p.Parent)
                if (string.Equals(p.TfsType?.Trim(), type, StringComparison.OrdinalIgnoreCase))
                    return p.Name ?? "";
            return "";
        }

        var resources = project.Resources
            .Where(r => r.Type == ResourceType.Work)
            .Select(r => string.IsNullOrWhiteSpace(r.Name) ? (r.DisplayName ?? "").TrimStart('*').Trim() : r.Name.Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Modos "Story nova"/"Feature nova" (checkboxes do painel): contexto e prompt
        // iguais aos do Task Plan — Features ou EPICs no lugar das Stories.
        var newFeature = string.Equals(mode, "feature-nova", StringComparison.OrdinalIgnoreCase);
        var newStory = newFeature || string.Equals(mode, "story-nova", StringComparison.OrdinalIgnoreCase);

        var allNodes = new List<ProjectTask>();
        void CollectAll(IEnumerable<ProjectTask> tasks)
        {
            foreach (var t in tasks) { allNodes.Add(t); CollectAll(t.Children); }
        }
        CollectAll(project.Tasks);
        bool IsType(ProjectTask t, string type) => string.Equals(t.TfsType?.Trim(), type, StringComparison.OrdinalIgnoreCase);

        var ctx = new System.Text.StringBuilder();
        if (newFeature)
        {
            var epics = allNodes.Where(t => IsType(t, "Epic")).ToList();
            ctx.AppendLine("EPICS (id | nome) — EPICs existentes no cronograma:");
            foreach (var ep in epics) ctx.AppendLine($"{ep.Id} | {ep.Name}");
        }
        else if (newStory)
        {
            var features = allNodes.Where(t => IsType(t, "Feature") && t.PercentComplete < 100).ToList();
            ctx.AppendLine("FEATURES (id | nome | epic) — Features existentes no cronograma:");
            foreach (var f in features) ctx.AppendLine($"{f.Id} | {f.Name} | {AncestorName(f, "Epic")}");
        }
        else
        {
            ctx.AppendLine("STORIES (id | nome | feature | epic) — somente Stories em aberto:");
            foreach (var s in stories)
                ctx.AppendLine($"{s.Id} | {s.Name} | {AncestorName(s, "Feature")} | {AncestorName(s, "Epic")}");
        }
        ctx.AppendLine().AppendLine("RECURSOS: " + string.Join(" | ", resources));
        ctx.AppendLine().AppendLine("TEXTO:").AppendLine(text);

        Console.WriteLine($"Cronograma: {stories.Count} stories em aberto | recursos: {resources.Count} | contexto: {ctx.Length:N0} chars"
            + (newFeature ? " | modo FEATURE NOVA" : newStory ? " | modo STORY NOVA" : ""));
        Console.WriteLine();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        var monitor = new System.Timers.Timer(10000);
        monitor.Elapsed += (_, _) => Console.WriteLine($"   [{sw.Elapsed:mm\\:ss}] {LocalLlamaService.CurrentStatus ?? "..."}");
        monitor.Start();
        try
        {
            var systemPrompt = AISettingsStore.PlanIncludeActionPrompt
                + (newFeature ? AISettingsStore.PlanIncludeNewFeatureSuffix
                    : newStory ? AISettingsStore.PlanIncludeNewStorySuffix : "");
            var userMsg = newFeature
                ? "Encontre o EPIC de cada Feature nova do TEXTO e devolva o JSON."
                : newStory
                    ? "Encontre a Feature de cada Story nova do TEXTO e devolva o JSON."
                    : "Encontre a Story de cada atividade do TEXTO e devolva o JSON.";
            var answer = await LocalLlamaService.GenerateAsync(systemPrompt, userMsg + "\n\n" + ctx, cts.Token);
            monitor.Stop();
            sw.Stop();
            Console.WriteLine($"Tempo total: {sw.Elapsed:mm\\:ss} | backend: {LocalLlamaService.ActiveBackendLabel ?? "?"} | resposta: {answer.Length:N0} chars");
            Console.WriteLine("Resposta: " + answer);

            // Mesmo pós-processamento do Task Plan: extração com reparo + leitura dos campos.
            var (json, truncated) = TaskPlanScheduleRules.ExtractJsonArray(answer);
            if (json == null)
            {
                Console.WriteLine("RESULTADO: nenhum JSON de atividades recuperável — o Task Plan não incluiria nada.");
                return 1;
            }
            if (truncated)
                Console.WriteLine("AVISO: resposta truncada — itens completos aproveitados pelo reparo.");
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var n = 0;
            var unmatched = 0;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var task = TaskPlanScheduleRules.GetJsonString(item, "task");
                if (string.IsNullOrWhiteSpace(task)) continue;
                n++;
                var story = TaskPlanScheduleRules.GetJsonString(item, "story");
                var hours = TaskPlanScheduleRules.ParseEstimatedHours(
                    TaskPlanScheduleRules.GetJsonString(item, "esforco", "esforço")) ?? 1.0;
                var resp = TaskPlanScheduleRules.GetJsonString(item, "responsavel", "responsável");
                // Mesmo casamento do Task Plan: sem recurso, o nome cai na Observação.
                var matched = TaskPlanResourceMatcher.Find(project.Resources, resp);
                var respText = resp == null ? ""
                    : matched != null
                        ? $" — Responsável: {TaskPlanResourceMatcher.PlanName(matched)}"
                        : $" — SEM recurso (vai para Observação): \"{resp}\"";
                if (matched == null && resp != null) unmatched++;
                Console.WriteLine($"  {n,2}. [{story}] {task} — {hours:0.##}h{respText}");
            }
            Console.WriteLine($"RESULTADO: {n} task(s) seriam incluídas na planilha"
                + (unmatched > 0 ? $"; {unmatched} com responsável NÃO casado (Observação)." : "; responsáveis todos casados."));
            // IDs de hierarquia: no modo Feature/Story nova só o nível EXISTENTE tem ID —
            // Feature/Story novas nascem no cronograma no Aplicar (CreateStoryPath).
            if (newStory)
                Console.WriteLine(newFeature
                    ? "IDs: só o ID do EPIC é preenchido; Feature e Story novas ficam sem ID até o Aplicar."
                    : "IDs: só os IDs de EPIC/Feature são preenchidos; a Story nova fica sem ID até o Aplicar.");
            return 0;
        }
        catch (Exception ex)
        {
            monitor.Stop();
            Console.WriteLine($"FALHOU apos {sw.Elapsed:mm\\:ss}: {ex.Message}");
            return 1;
        }
    }

    private static void AiLocalInferenceResponds()
    {
        // Prompt mínimo: valida o ciclo completo (carga do modelo + geração) em CPU.
        var answer = LocalLlamaService.GenerateAsync(
                "Responda SEMPRE com uma unica palavra: OK",
                "Diga OK.",
                new CancellationTokenSource(TimeSpan.FromMinutes(5)).Token)
            .GetAwaiter().GetResult();

        if (string.IsNullOrWhiteSpace(answer))
            throw new InvalidOperationException("A inferencia local devolveu resposta vazia.");
    }

    /// <summary>Garante que a identidade do Setup (timestamp) FICA NO proprio Setup e que a
    /// release so cria a tag, sem recarimbar. O valor gravado dentro do NXProject-Setup.zip
    /// (setup-build-timestamp.txt) deve ser identico ao embutido no build do Community
    /// (known-setup-timestamp.txt). Se um dia a release voltar a gerar o timestamp por
    /// conta propria (ex.: mtime do arquivo ou UpdatedAt do GitHub), os dois divergem e
    /// este teste falha — evitando reinstalacoes falsas do Setup a cada release.</summary>
    private static void ValidateSetupTimestampIntrinsic()
    {
        var zipPath = Path.Combine(_solutionRoot, "dist", "setup", "NXProject-Setup.zip");
        var embeddedPath = Path.Combine(_solutionRoot, "NXProject.Community", "Assets", "known-setup-timestamp.txt");

        if (!File.Exists(embeddedPath))
            throw new InvalidOperationException(
                "known-setup-timestamp.txt ausente — o release-nxproject-setup.ps1 deve grava-lo (identidade intrinseca do Setup).");

        var embedded = File.ReadAllText(embeddedPath).Trim();

        using var zip = ZipFile.OpenRead(zipPath);
        var stampEntry = zip.Entries.FirstOrDefault(e =>
            string.Equals(e.Name, "setup-build-timestamp.txt", StringComparison.OrdinalIgnoreCase));
        if (stampEntry is null)
            throw new InvalidOperationException(
                "'NXProject-Setup.zip' nao contem setup-build-timestamp.txt — o timestamp deve ficar DENTRO do proprio Setup.");

        using var reader = new StreamReader(stampEntry.Open());
        var inZip = reader.ReadToEnd().Trim();

        if (!string.Equals(embedded, inZip, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Timestamp do Setup divergente: embutido no build = '{embedded}', dentro do zip = '{inZip}'. " +
                "A release nao pode recarimbar o timestamp; ele deve ficar no Setup e so a tag muda.");
    }

    private static void ValidateCommunityReleaseZip()
    {
        var zipPath = Path.Combine(_solutionRoot, "dist", "community", "NXProject.Community-Release.zip");
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "PackagingManifests", "release-zip-required-files.json");
        ValidateZipAgainstManifest(zipPath, manifestPath);
        ValidateSelfContainedNotSingleFile(zipPath, "NXProject.Community.exe", "NXProject.Community.dll", "NXProject.Community.runtimeconfig.json");
    }

    private static void ValidateCommunityReleaseIsNotStandalone()
    {
        var zipPath = Path.Combine(_solutionRoot, "dist", "community", "NXProject.Community-Release.zip");
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "PackagingManifests", "release-zip-forbidden-standalone-files.json");
        ValidateZipDoesNotContainManifest(zipPath, manifestPath);
    }

    private static void SimulateOpenAi()
    {
        Console.WriteLine("Simulacao: parse do retorno do script JS e fluxo de tratamento.");
        var samples = new Dictionary<string,string?>()
        {
            {"Valid text","{\"text\":\"Ola do assistente\"}"},
            {"Empty text","{\"text\":\"\"}"},
            {"Error http","{\"error\":\"http-403\",\"detail\":\"forbidden\"}"},
            {"Empty object","{}"},
            {"Missing text key","{\"foo\":\"bar\"}"}
        };

        foreach (var kv in samples)
        {
            Console.WriteLine();
            Console.WriteLine($"--- Sample: {kv.Key}");
            var raw = kv.Value;
            Console.WriteLine($"raw: {raw}");
            try
            {
                using var doc = JsonDocument.Parse(raw ?? "{}");
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var err))
                {
                    var detail = root.TryGetProperty("detail", out var d) ? d.GetString() : null;
                    var msg = "Falha no envio (" + err.GetString() + "). " + (string.IsNullOrWhiteSpace(detail) ? string.Empty : detail);
                    Console.WriteLine(msg);
                    continue;
                }

                var text = root.TryGetProperty("text", out var t) ? t.GetString() : null;
                if (string.IsNullOrWhiteSpace(text))
                {
                    Console.WriteLine("A IA nao retornou conteudo. Tente novamente.");
                    continue;
                }

                Console.WriteLine($"Resposta recebida ({text.Length} caracteres): {text}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Erro ao parsear raw: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// O Release.zip do Community leva SO os binarios do nucleo; as bibliotecas de
    /// terceiros vem do Setup. Este teste garante que TODA .dll de terceiros presente
    /// no publish atual existe dentro do NXProject-Setup.zip — pega dependencia NuGet
    /// nova (ex.: ClosedXML) sem o Setup regenerado, antes de publicar.
    /// </summary>
    private static void ValidateThirdPartyLibsAreInSetupZip()
    {
        var publishDir = Path.Combine(_solutionRoot, "dist", "community", "publish-win-x64");
        var setupZip = Path.Combine(_solutionRoot, "dist", "setup", "NXProject-Setup.zip");
        if (!Directory.Exists(publishDir))
            throw new InvalidOperationException($"Publish nao encontrado: {publishDir}. Rode a release do Community antes.");
        if (!File.Exists(setupZip))
            throw new InvalidOperationException($"Setup nao encontrado: {setupZip}. Rode release-nxproject-setup.ps1.");

        using var zip = ZipFile.OpenRead(setupZip);
        var setupEntries = zip.Entries.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = Directory.EnumerateFiles(publishDir, "*.dll", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .Where(n => n != null
                && !n.StartsWith("NXProject.Community", StringComparison.OrdinalIgnoreCase)
                && !n.StartsWith("NXProject.Shared", StringComparison.OrdinalIgnoreCase)
                && !setupEntries.Contains(n!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();

        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"{missing.Count} DLL(s) de terceiros do publish NAO estao no NXProject-Setup.zip " +
                $"(regenerar com release-nxproject-setup.ps1): {string.Join(", ", missing)}");
    }

    private static void ValidateSetupZip()
    {
        var zipPath = Path.Combine(_solutionRoot, "dist", "setup", "NXProject-Setup.zip");
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "PackagingManifests", "setup-zip-required-files.json");
        ValidateZipAgainstManifest(zipPath, manifestPath);
        ValidateSelfContainedNotSingleFile(zipPath, "NXProject-Setup.exe", "NXProject-Setup.dll", "NXProject-Setup.runtimeconfig.json");
    }

    private static void ValidateSetupZipKeepsRequiredDlls()
    {
        var zipPath = Path.Combine(_solutionRoot, "dist", "setup", "NXProject-Setup.zip");
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "PackagingManifests", "dlls-necessarias.md");
        ValidateZipContainsMarkdownDllList(zipPath, manifestPath);
    }

    /// <summary>Confirma que o publish foi self-contained (runtimeconfig.json com
    /// "includedFrameworks", nao "frameworks") e nao foi PublishSingleFile (o .dll
    /// companheiro do .exe existe como entrada separada no zip, nao embutido).</summary>
    private static void ValidateSelfContainedNotSingleFile(string zipPath, string exeName, string dllName, string runtimeConfigName)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var zipLabel = Path.GetFileName(zipPath);

        var dllEntry = zip.Entries.FirstOrDefault(e => string.Equals(e.Name, dllName, StringComparison.OrdinalIgnoreCase));
        if (dllEntry is null)
            throw new InvalidOperationException(
                $"'{zipLabel}': '{dllName}' nao encontrado como arquivo separado — parece que o publish usou PublishSingleFile.");

        var configEntry = zip.Entries.FirstOrDefault(e => string.Equals(e.Name, runtimeConfigName, StringComparison.OrdinalIgnoreCase));
        if (configEntry is null)
            throw new InvalidOperationException($"'{zipLabel}': '{runtimeConfigName}' nao encontrado no zip.");

        using var stream = configEntry.Open();
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement.GetProperty("runtimeOptions");

        if (root.TryGetProperty("frameworks", out _) && !root.TryGetProperty("includedFrameworks", out _))
            throw new InvalidOperationException(
                $"'{zipLabel}': '{runtimeConfigName}' usa 'frameworks' (framework-dependent) em vez de 'includedFrameworks' (self-contained) — {exeName} exige .NET pre-instalado no sistema.");

        if (!root.TryGetProperty("includedFrameworks", out _))
            throw new InvalidOperationException(
                $"'{zipLabel}': '{runtimeConfigName}' nao contem 'includedFrameworks' — publish nao parece self-contained.");
    }

    private static void ValidateZipAgainstManifest(string zipPath, string manifestPath)
    {
        if (!File.Exists(zipPath))
            throw new InvalidOperationException($"Zip nao encontrado: {zipPath}");
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException($"Manifest nao encontrado: {manifestPath}");

        var requiredNames = JsonSerializer.Deserialize<string[]>(File.ReadAllText(manifestPath))
            ?? throw new InvalidOperationException($"Manifest vazio ou invalido: {manifestPath}");

        using var zip = ZipFile.OpenRead(zipPath);
        var entryNames = zip.Entries.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = requiredNames.Where(n => !entryNames.Contains(n)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"'{Path.GetFileName(zipPath)}' esta incompleto. Arquivos faltando: {string.Join(", ", missing)}");
    }

    private static void ValidateZipDoesNotContainManifest(string zipPath, string manifestPath)
    {
        if (!File.Exists(zipPath))
            throw new InvalidOperationException($"Zip nao encontrado: {zipPath}");
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException($"Manifest nao encontrado: {manifestPath}");

        var forbiddenNames = JsonSerializer.Deserialize<string[]>(File.ReadAllText(manifestPath))
            ?? throw new InvalidOperationException($"Manifest vazio ou invalido: {manifestPath}");

        using var zip = ZipFile.OpenRead(zipPath);
        var entryNames = zip.Entries.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var present = forbiddenNames.Where(n => entryNames.Contains(n)).ToList();
        if (present.Count > 0)
            throw new InvalidOperationException(
                $"'{Path.GetFileName(zipPath)}' virou pacote standalone/base. " +
                $"Arquivos que pertencem ao NXProject-Setup.zip encontrados: {string.Join(", ", present)}");
    }

    private static void ValidateZipContainsMarkdownDllList(string zipPath, string manifestPath)
    {
        if (!File.Exists(zipPath))
            throw new InvalidOperationException($"Zip nao encontrado: {zipPath}");
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException($"Lista de DLLs necessarias nao encontrada: {manifestPath}");

        var requiredDlls = ReadRequiredDllsFromMarkdown(manifestPath);
        if (requiredDlls.Count == 0)
            throw new InvalidOperationException($"Lista de DLLs necessarias vazia: {manifestPath}");

        using var zip = ZipFile.OpenRead(zipPath);
        var entryNames = zip.Entries.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = requiredDlls.Where(n => !entryNames.Contains(n)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"'{Path.GetFileName(zipPath)}' nao contem DLL(s) necessarias: {string.Join(", ", missing)}. " +
                $"Atualize release-nxproject-setup.ps1 ou revise {Path.GetFileName(manifestPath)}.");
    }

    private static List<string> ReadRequiredDllsFromMarkdown(string manifestPath) =>
        File.ReadLines(manifestPath)
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("- ", StringComparison.Ordinal))
            .Select(l => l[2..].Trim())
            .Where(l => l.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void ResetCalendar()
    {
        SetCurrentCalendar(new ProjectCalendar
        {
            WorkingHoursPerDay = 8,
            TreatSaturdayAsWorkday = false,
            TreatSundayAsWorkday = false
        });
    }

    private static void CalendarInclusiveFinishUsesPreviousWorkingDay()
    {
        var start = new DateTime(2026, 7, 3); // sexta-feira
        var finish = new DateTime(2026, 7, 7); // terca-feira 00:00, fim exclusivo apos segunda

        var inclusive = ProjectCalendarService.GetInclusiveFinishDate(start, finish);

        AssertEqual(new DateTime(2026, 7, 6), inclusive, "O fim exibido deve ser a segunda-feira trabalhada, nao a terca exclusiva.");
    }

    private static void CalendarWorkingHoursSkipWeekend()
    {
        var start = new DateTime(2026, 7, 3); // sexta-feira
        var finish = ProjectCalendarService.AddWorkingHours(start, 16);

        AssertEqual(new DateTime(2026, 7, 7), finish, "16h iniciando sexta devem terminar na terca 00:00.");
        AssertEqual(16, ProjectCalendarService.CountWorkingHours(start, finish), "A contagem deve ignorar sabado e domingo.");
    }

    private static void CalendarHolidayIsNotWorkingCapacity()
    {
        SetCurrentCalendar(new ProjectCalendar
        {
            WorkingHoursPerDay = 8,
            Holidays = { new ProjectHoliday { Date = new DateTime(2026, 7, 6), Name = "Feriado teste" } }
        });

        var start = new DateTime(2026, 7, 3); // sexta-feira
        var finish = ProjectCalendarService.AddWorkingHours(start, 16);

        AssertEqual(new DateTime(2026, 7, 8), finish, "Com segunda feriado, 16h iniciando sexta devem terminar quarta 00:00.");
        AssertEqual(new DateTime(2026, 7, 7), ProjectCalendarService.GetInclusiveFinishDate(start, finish), "O fim inclusivo deve pular o feriado.");
    }

    private static void ScheduleEstimatedHoursCalculatesExclusiveFinish()
    {
        var task = new ProjectTask
        {
            Name = "Task 16h",
            Start = new DateTime(2026, 7, 6),
            EstimatedHours = 16
        };

        task.Finish = TaskScheduleService.CalculateFinishFromAssignments(task, task.Start);

        AssertEqual(new DateTime(2026, 7, 8), task.Finish, "16h devem ocupar dois dias uteis e terminar no limite exclusivo do terceiro dia.");
        AssertEqual(new DateTime(2026, 7, 7), ProjectCalendarService.GetInclusiveFinishDate(task.Start, task.Finish), "A data exibida deve ser o segundo dia util.");
    }

    private static void ScheduleAllocationPercentChangesCalendarDuration()
    {
        var task = new ProjectTask
        {
            Name = "Task 8h a 50%",
            Start = new DateTime(2026, 7, 6)
        };
        task.Resources.Add(new TaskResource { AllocationPercent = 50, EstimatedHours = 8 });

        var finish = TaskScheduleService.CalculateFinishFromAssignments(task, task.Start);

        AssertEqual(new DateTime(2026, 7, 8), finish, "8h a 50% devem virar 16h de calendario.");
    }

    private static void ScheduleResourceAllocationPercentMatrix()
    {
        AssertAllocationFinish(25, 8, new DateTime(2026, 7, 10), "8h a 25% devem virar 32h de calendario.");
        AssertAllocationFinish(50, 8, new DateTime(2026, 7, 8), "8h a 50% devem virar 16h de calendario.");
        AssertAllocationFinish(100, 8, new DateTime(2026, 7, 7), "8h a 100% devem ocupar 1 dia util.");
        AssertAllocationFinish(120, 12, new DateTime(2026, 7, 7, 6, 0, 0), "12h a 120% devem virar 10h de calendario.");
    }

    private static void ScheduleResourceAvailabilityPercentChangesCalendarDuration()
    {
        var resource = new Resource { Id = 1, Name = "Dev 50%", AvailabilityPercent = 50 };
        var task = new ProjectTask
        {
            Name = "Task 8h com pessoa 50%",
            Start = new DateTime(2026, 7, 6)
        };
        task.Resources.Add(new TaskResource
        {
            Resource = resource,
            ResourceId = resource.Id,
            AllocationPercent = 100,
            EstimatedHours = 8
        });

        var finish = TaskScheduleService.CalculateFinishFromAssignments(task, task.Start);

        AssertEqual(new DateTime(2026, 7, 8), finish, "Recurso disponivel 50% no projeto deve dobrar a duracao calendario.");
    }

    // 16h POR pessoa: com 1 ou 2 recursos a duracao calendario e sempre 2 dias uteis
    // (as horas dobram junto com a capacidade). Assim o cenario cobre o 2o dia, que e
    // onde a ausencia precisa ser sentida.
    private static ProjectTask BuildAbsenceTask(params Resource[] people)
    {
        var task = new ProjectTask { Name = "Task 16h/pessoa", Start = new DateTime(2026, 7, 6) }; // segunda
        foreach (var r in people)
            task.Resources.Add(new TaskResource
            {
                Resource = r,
                ResourceId = r.Id,
                AllocationPercent = 100,
                EstimatedHours = 16
            });
        return task;
    }

    private static void AbsenceEmptyKeepsFinish()
    {
        var r = new Resource { Id = 1, Name = "Dev" };
        var task = BuildAbsenceTask(r);

        var finish = TaskScheduleService.CalculateFinishFromAssignments(task, task.Start);

        AssertEqual(new DateTime(2026, 7, 8), finish,
            "Sem ausencia cadastrada o fim deve continuar igual ao comportamento atual.");
    }

    private static void AbsencePushesTaskFinish()
    {
        var r = new Resource { Id = 1, Name = "Dev" };
        r.Absences.Add(new ResourceAbsence { Date = new DateTime(2026, 7, 7), Reason = "Feriado municipal" });
        var task = BuildAbsenceTask(r);

        var finish = TaskScheduleService.CalculateFinishFromAssignments(task, task.Start);

        AssertEqual(new DateTime(2026, 7, 9), finish,
            "Um dia de ausencia no meio deve empurrar o fim em um dia util.");
    }

    private static void AbsenceWithTwoResourcesNeedsBoth()
    {
        // So um falta => a atividade continua andando (o outro trabalha).
        var a = new Resource { Id = 1, Name = "Dev A" };
        a.Absences.Add(new ResourceAbsence { Date = new DateTime(2026, 7, 7) });
        var b = new Resource { Id = 2, Name = "Dev B" };
        b.Absences.Add(new ResourceAbsence { Date = new DateTime(2026, 7, 10) });
        var soloTask = BuildAbsenceTask(a, b);

        var soloFinish = TaskScheduleService.CalculateFinishFromAssignments(soloTask, soloTask.Start);

        // Ambos faltam no MESMO dia => ai sim o dia nao produz.
        var c = new Resource { Id = 3, Name = "Dev C" };
        c.Absences.Add(new ResourceAbsence { Date = new DateTime(2026, 7, 7) });
        var d = new Resource { Id = 4, Name = "Dev D" };
        d.Absences.Add(new ResourceAbsence { Date = new DateTime(2026, 7, 7) });
        var bothTask = BuildAbsenceTask(c, d);

        var bothFinish = TaskScheduleService.CalculateFinishFromAssignments(bothTask, bothTask.Start);

        AssertTrue(bothFinish > soloFinish,
            "Dia em que TODOS faltam deve atrasar; falta de so um recurso nao pode atrasar.");
    }

    private static void AbsenceOnWeekendChangesNothing()
    {
        var r = new Resource { Id = 1, Name = "Dev" };
        r.Absences.Add(new ResourceAbsence { Date = new DateTime(2026, 7, 11) }); // sabado
        var task = BuildAbsenceTask(r);

        var finish = TaskScheduleService.CalculateFinishFromAssignments(task, task.Start);

        AssertEqual(new DateTime(2026, 7, 8), finish,
            "Ausencia em dia nao util nao pode alterar o fim.");
    }

    private static void AbsencesSurviveSaveLoad()
    {
        var project = new Project { Name = "Projeto ausencias" };
        var r = new Resource { Id = 1, Name = "Dev" };
        r.Absences.Add(new ResourceAbsence { Date = new DateTime(2026, 7, 7), Reason = "Ferias" });
        r.Absences.Add(new ResourceAbsence { Date = new DateTime(2026, 7, 8) });
        project.Resources.Add(r);

        var path = Path.Combine(Path.GetTempPath(), $"nx_absence_{Guid.NewGuid():N}.xml");
        try
        {
            XmlProjectService.Save(project, path);
            var loaded = XmlProjectService.Load(path);
            var lr = loaded.Resources.First(x => x.Id == 1);

            AssertEqual(2, lr.Absences.Count, "As duas ausencias devem voltar do arquivo.");
            AssertEqual(new DateTime(2026, 7, 7), lr.Absences[0].Date, "Data da ausencia deve sobreviver.");
            AssertEqual("Ferias", lr.Absences[0].Reason, "Motivo da ausencia deve sobreviver.");
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    private static void ScheduleCompletedTaskKeepsFinish()
    {
        var originalFinish = new DateTime(2026, 7, 7);
        var task = new ProjectTask
        {
            Name = "Task fechada",
            Start = new DateTime(2026, 7, 6),
            Finish = originalFinish,
            EstimatedHours = 80,
            PercentComplete = 100
        };

        TaskScheduleService.RecalculateFinishFromAssignments(task);

        AssertEqual(originalFinish, task.Finish, "Task 100% nao deve ter fim recalculado por HH restante.");
    }

    private static void RebuildKeepsCompletedTaskFinish()
    {
        var originalFinish = new DateTime(2026, 7, 7);
        var project = new Project { Name = "Teste", StartDate = new DateTime(2026, 7, 6) };
        var task = new ProjectTask
        {
            Id = 1,
            Name = "Atividade sincronizada",
            TfsType = "Story",
            Start = new DateTime(2026, 7, 6),
            Finish = originalFinish,
            EstimatedHours = 320,
            PercentComplete = 100
        };
        project.Tasks.Add(task);

        var vm = new MainViewModel("NXTestUnit") { Project = project };
        vm.RebuildFlatTasks();

        AssertEqual(originalFinish, task.Finish, "Rebuild apos sync/import nao deve empurrar tarefa 100% por HH/alocacao.");
    }

    private static void ScheduleNoDevOpsZeroDurationCreatesMilestone()
    {
        var start = new DateTime(2026, 7, 6);
        var task = new ProjectTask
        {
            Name = "Marco local",
            TfsType = "NODEVOPS",
            Start = start,
            Finish = ProjectCalendarService.AddWorkingHours(start, 8),
            EstimatedHours = 8,
            OriginalEstimatedHours = 8
        };
        var vm = new TaskViewModel(task);

        vm.DurationText = "0";

        AssertEqual(0, vm.DurationHours, "No DevOps com duracao zero deve ficar com 0h.");
        AssertEqual(start, task.Finish, "Marco deve terminar na mesma data de inicio.");
        if (!task.IsMilestone)
            throw new InvalidOperationException("No DevOps com duracao zero deve ser marcado como milestone.");
    }

    private static void ScheduleDevOpsZeroDurationIsIgnored()
    {
        var start = new DateTime(2026, 7, 6);
        var finish = ProjectCalendarService.AddWorkingHours(start, 8);
        var task = new ProjectTask
        {
            Name = "Story DevOps",
            TfsType = "Story",
            Start = start,
            Finish = finish,
            EstimatedHours = 8,
            OriginalEstimatedHours = 8
        };
        var vm = new TaskViewModel(task);

        vm.DurationText = "0";

        AssertEqual(8, vm.DurationHours, "Atividade DevOps nao deve aceitar duracao zero.");
        AssertEqual(finish, task.Finish, "Atividade DevOps deve manter o fim original.");
        if (task.IsMilestone)
            throw new InvalidOperationException("Atividade DevOps nao deve virar milestone local com duracao zero.");
    }

    // Regressao do "duracao louca" (699h): um resumo cujas folhas so tem
    // OriginalEstimatedHours (HH veio do rateio; CurrentHours=EstimatedHours=0) deve
    // somar essas horas rateadas, e NAO o span do calendario entre Start e Finish.
    // Uma x:Key duplicada num ResourceDictionary estoura em runtime ("DeferrableContent
    // iniciou uma exceção"). Este teste varre os Strings.*.xaml e falha se houver duplicata,
    // sugerindo qualificar com a tela/fonte (ver ResourceKeyRegistry).
    // Crítico: as colunas do taskboard precisam sair na ordem de fluxo (New→Active→Resolved→
    // Closed…), com estados desconhecidos ao fim — senão o board fica ilegível.
    /// <summary>
    /// O NXProject foi exercitado em producao com o processo AGILE. Scrum e CMMI sao tratados no
    /// codigo, e este teste guarda a parte que mais silenciosamente erraria: o vocabulario de
    /// estados. "Approved" (Scrum) e "Proposed" (CMMI) ja passaram despercebidos uma vez.
    /// </summary>
    private static void DevOpsProcessStatesAreRecognized()
    {
        foreach (var aberto in new[] { "New", "Active", "In Progress",      // Agile
                                       "Approved", "Committed", "To Do",     // Scrum
                                       "Proposed" })                          // CMMI
            if (!TfsImportService.IsOpenStatePublic(aberto))
                throw new Exception($"'{aberto}' e estado de item ABERTO em algum dos processos do DevOps.");

        foreach (var fechado in new[] { "Closed", "Resolved", "Done", "Completed" })
            if (!TfsImportService.IsCompletedStatePublic(fechado))
                throw new Exception($"'{fechado}' e estado de item ENCERRADO.");

        // Nenhum estado pode ser as duas coisas, senao a Story encerrada com Task aberta nao
        // seria detectada — ou toda Story viraria alerta.
        foreach (var e in new[] { "New", "Active", "Approved", "Committed", "Proposed" })
            if (TfsImportService.IsCompletedStatePublic(e))
                throw new Exception($"'{e}' nao pode contar como encerrado.");

        // As colunas do board aceitam o vocabulario do Scrum na ordem certa.
        var colunas = TfsImportService.OrderTaskboardStates(new[] { "Done", "Committed", "Approved", "New" });
        if (colunas[0] != "New" || colunas[1] != "Approved" || colunas[2] != "Committed" || colunas[3] != "Done")
            throw new Exception("Colunas do Scrum fora de ordem: " + string.Join(" | ", colunas));
    }

    /// <summary>
    /// As telas usam Strings.*.xaml; os SERVICOS (NXProject.Shared) nao enxergam esses arquivos, e
    /// por isso todo aviso de import e de sync nascia fixo em portugues — inclusive para quem usa
    /// o app em ingles. O NxText fecha essa fronteira.
    /// </summary>
    private static void ServiceMessagesFollowLanguage()
    {
        var anterior = NXProject.Services.NxText.Language;
        try
        {
            NXProject.Services.NxText.Language = "pt-BR";
            if (NXProject.Services.NxText.T("ola", "hello") != "ola")
                throw new Exception("Em portugues, vale o texto PT.");
            if (NXProject.Services.NxText.T("item {0} de {1}", "item {0} of {1}", 2, 5) != "item 2 de 5")
                throw new Exception("Os parametros tem de ser formatados depois de escolher o idioma.");

            NXProject.Services.NxText.Language = "en-US";
            if (NXProject.Services.NxText.T("ola", "hello") != "hello")
                throw new Exception("Em ingles, vale o texto EN.");
            if (NXProject.Services.NxText.T("item {0} de {1}", "item {0} of {1}", 2, 5) != "item 2 of 5")
                throw new Exception("A versao EN tambem tem de ser formatada.");

            // Texto com chave desbalanceada nao pode derrubar a operacao: a mensagem e o menos
            // importante quando algo ja deu errado.
            if (NXProject.Services.NxText.T("{0 quebrado", "{0 broken", 1) != "{0 broken")
                throw new Exception("Erro de formatacao deve devolver o modelo, nao lancar.");
        }
        finally { NXProject.Services.NxText.Language = anterior; }
    }

    private static void TaskboardStatesAreOrderedCanonically()
    {
        var ordered = TfsImportService.OrderTaskboardStates(
            new[] { "Closed", "New", "Zebra", "Active", "Resolved", "New" });
        AssertEqual("New", ordered[0], "New deve vir primeiro.");
        AssertEqual("Active", ordered[1], "Active depois de New.");
        AssertEqual("Resolved", ordered[2], "Resolved depois de Active.");
        AssertEqual("Closed", ordered[3], "Closed depois de Resolved.");
        AssertEqual("Zebra", ordered[4], "Estado desconhecido vai para o fim.");
        AssertEqual(5, ordered.Count, "Duplicatas (New) sao removidas.");
    }

    // Crítico: marcar/tirar Doing e virar Done não pode perder as outras tags do work item.
    private static void DoingTagMergePreservesOtherTags()
    {
        AssertEqual("MARCO-PROJECT; Doing",
            TfsImportService.MergeDoingTag("MARCO-PROJECT", "Doing"),
            "Adiciona Doing preservando a tag existente.");
        AssertEqual("MARCO-PROJECT; Done",
            TfsImportService.MergeDoingTag("MARCO-PROJECT; Doing", "Done"),
            "Doing vira Done (troca), mantendo as demais.");
        AssertEqual("MARCO-PROJECT",
            TfsImportService.MergeDoingTag("MARCO-PROJECT; Done", null),
            "Remover (null) tira Done e mantem as demais.");
        AssertEqual("Doing",
            TfsImportService.MergeDoingTag("", "Doing"),
            "Sem tags previas, fica so a Doing.");
        AssertEqual("A; B",
            TfsImportService.MergeDoingTag("A; Doing; B", null),
            "Remove Doing do meio, preserva ordem das demais.");
    }

    // Aviso do board: o estado da Story tem que acompanhar o das Tasks. Story parada em New
    // com Task ja iniciada, e Story em Active sem nenhuma Task em Active, sao os dois desalinhos
    // que o usuario precisa enxergar no card.
    private static void StoryStateAlertCatchesMismatchWithTasks()
    {
        AssertEqual("NewWithStartedTask",
            TfsImportService.CheckStoryStateAlert("New", new[] { "New", "Active" }).ToString(),
            "Story em New com Task fora de New deve alertar.");
        AssertEqual("None",
            TfsImportService.CheckStoryStateAlert("New", new[] { "New", "New" }).ToString(),

            "Story em New com todas as Tasks em New esta coerente.");
        AssertEqual("None",
            TfsImportService.CheckStoryStateAlert("New", new[] { "New", "Removed" }).ToString(),

            "Task removida nao conta como trabalho iniciado.");
        AssertEqual("ActiveWithoutActiveTask",
            TfsImportService.CheckStoryStateAlert("Active", new[] { "New", "Closed" }).ToString(),

            "Story em Active sem Task em Active deve alertar.");
        AssertEqual("ActiveWithoutActiveTask",
            TfsImportService.CheckStoryStateAlert("Active", System.Array.Empty<string>()).ToString(),
            "Story em Active sem nenhuma Task tambem alerta.");
        AssertEqual("None",
            TfsImportService.CheckStoryStateAlert("Active", new[] { "Closed", "Active" }).ToString(),

            "Story em Active com Task em Active esta coerente.");
        AssertEqual("None",
            TfsImportService.CheckStoryStateAlert("Closed", new[] { "New" }).ToString(),

            "Story encerrada nao entra na checagem (o alerta e so de New/Active).");
    }

    private static void TaskPriorityRangeResolvesAndClamps()
    {
        // Sem config habilitada → padrão 1..9 (o do formulário do DevOps).
        var def = TaskPriorityRange.FromOptions(null);
        AssertEqual(1, def.Min, "Faixa padrao comeca em 1.");
        AssertEqual(9, def.Max, "Faixa padrao vai ate 9.");
        AssertEqual(9, def.Clamp(100), "Clamp limita ao maximo.");
        AssertEqual(1, def.Clamp(0), "Clamp limita ao minimo.");
        AssertEqual(1, def.Next(9), "Ao ciclar do maximo volta ao minimo.");
        AssertEqual(4, def.Next(3), "Ciclo incrementa dentro da faixa.");

        // Config habilitada (2..5) manda quando nao ha descoberta.
        var opts = new TfsConnectionOptions { TaskPriorityRangeEnabled = true, TaskPriorityMin = 2, TaskPriorityMax = 5 };
        var cfg = TaskPriorityRange.FromOptions(opts);
        AssertEqual(2, cfg.Min, "Config define o minimo.");
        AssertEqual(5, cfg.Max, "Config define o maximo.");

        // Maximo descoberto no template manda sobre o da config.
        var disc = TaskPriorityRange.FromOptions(opts, discoveredMax: 9);
        AssertEqual(9, disc.Max, "Maximo descoberto (validateOnly) sobrepoe o da config.");
    }

    /// <summary>
    /// O editor visual de query so pode existir se ler e reescrever o WIQL sem alterar o que a
    /// pessoa tinha: campo, operador, valor, conector, e o SELECT/ORDER BY intactos.
    /// </summary>
    /// <summary>
    /// O TaskBoard no modo Projeto Local le o cronograma ABERTO, sem servidor: a hierarquia do
    /// arquivo (Projeto -> EPIC -> Feature -> Story -> Task) tem de virar board com os niveis nas
    /// colunas de cima, a Story como linha e a Task como card — com estado, responsavel e HH que
    /// o proprio .nxproject ja guarda.
    /// </summary>
    private static void LocalBoardProjectsScheduleIntoBoard()
    {
        var ana = new NXProject.Models.Resource { Id = 1, Name = "Ana" };
        var bruno = new NXProject.Models.Resource { Id = 2, Name = "Bruno" };

        var task = new NXProject.Models.ProjectTask
        {
            Id = 40, Name = "Ajustar view", TfsType = "Task", TfsState = "Active",
            EstimatedHours = 5, CurrentHours = 2, TfsIterationPath = "Sprint 1",
            Resources = { new NXProject.Models.TaskResource { ResourceId = 2, Resource = bruno } }
        };
        var story = new NXProject.Models.ProjectTask
        {
            Id = 30, Name = "Carga de dados", TfsType = "User Story", TfsState = "Active",
            TfsIterationPath = "Sprint 1",
            Resources = { new NXProject.Models.TaskResource { ResourceId = 1, Resource = ana } },
            Children = { task }
        };
        var feature = new NXProject.Models.ProjectTask
        { Id = 20, Name = "Integracao", TfsType = "Feature", Children = { story } };
        var epic = new NXProject.Models.ProjectTask
        { Id = 10, Name = "Suprimentos", TfsType = "Epic", Children = { feature } };
        var project = new NXProject.Models.Project();
        project.Tasks.Add(epic);

        var board = NXProject.Services.LocalBoardService.Build(project);

        if (board.Stories.Count != 1)
            throw new Exception($"Esperava 1 Story no board local, veio {board.Stories.Count}.");
        var row = board.Stories[0];
        if (row.Id != 30 || row.Title != "Carga de dados")
            throw new Exception("A Story do cronograma nao virou linha do board.");
        if (row.AssignedTo != "Ana")
            throw new Exception($"Responsavel da Story deveria vir do recurso: veio '{row.AssignedTo}'.");
        if (row.FeatureId != 20 || row.FeatureTitle != "Integracao")
            throw new Exception("A Feature do cronograma nao foi para a coluna da Feature.");
        if (row.FeatureEpicId != 10)
            throw new Exception("O EPIC do cronograma nao foi para a coluna do EPIC.");
        if (row.Tasks.Count != 1 || row.Tasks[0].Id != 40)
            throw new Exception("A Task filha nao virou card.");
        if (row.Tasks[0].AssignedTo != "Bruno" || row.Tasks[0].EstimateHours != 5
            || row.Tasks[0].CompletedHours != 2)
            throw new Exception("O card da Task perdeu responsavel ou horas do cronograma.");
        if (!board.People.Contains("Ana") || !board.People.Contains("Bruno"))
            throw new Exception("As pessoas do board local saem dos recursos do cronograma.");
        if (!board.LevelItems.Any(i => i.Id == 20 && i.Kind == "Feature"))
            throw new Exception("A Feature deveria entrar como item de nivel.");

        // Sem TfsState, o estado vem do percentual — o cronograma sempre teve esse dado.
        var semEstado = new NXProject.Models.ProjectTask { Id = 50, Name = "X", PercentComplete = 100 };
        if (NXProject.Services.LocalBoardService.StateOf(semEstado) != "Closed")
            throw new Exception("Atividade 100% sem TfsState deveria ser Closed no board local.");
    }

    /// <summary>
    /// A sprint do board local e identificada pelo IterationPath, nao pelo nome: e por ele que a
    /// atividade guarda a sua sprint. Com o nome, filtrar por sprint no modo local nao achava
    /// nada — e a sprint salva nao se reconhecia ao voltar para o DevOps.
    /// </summary>
    /// <summary>
    /// O projeto do GitHub vira board: Story como linha, sub-issue como card, os niveis acima nas
    /// colunas e os campos do board (HH, datas) nos lugares certos. Sem rede: a projecao recebe
    /// dados ja carregados, e e so isso que este teste exercita.
    /// </summary>
    /// <summary>
    /// O nome do campo do board casa por APELIDO. E a falha mais silenciosa do conector: nome que
    /// nao casa nao da erro nenhum, so devolve HH e datas em branco — e a suspeita cai no
    /// mapeamento, nao no nome.
    ///
    /// Trava as tres formas que convivem: o prefixo nx_ (padrao novo), o nome padrao do DevOps
    /// (Start Date, Finish Date, Original Estimate — o que a Task ja usa) e o nome em portugues
    /// de quem montou o board copiando o vocabulario do DevOps.
    /// </summary>
    /// <summary>
    /// O padrao de campos (PT x EN) nao e traducao: e escolher entre o campo PROPRIO do
    /// NXProject e o campo NATIVO do processo do DevOps.
    ///
    /// A regra que este teste trava e a que evita o pior estrago: no padrao ingles so vira
    /// nativo o que TEM nativo naquele tipo. Start Date e Finish Date existem na Task e na
    /// Story; horas so na Task. Se o HH da Story virasse "nativo" sem existir, o NXProject
    /// gravaria num campo inexistente; e se o inverso acontecesse na Task, a atividade acabaria
    /// com duas datas de inicio — a nativa e a do NX — sem ninguem saber qual vale.
    /// </summary>
    private static void FieldNamingDefaultsPerLanguage()
    {
        var pt = NXProject.Services.NxDevOpsFieldCatalog.NxFieldNaming.Portuguese;
        var en = NXProject.Services.NxDevOpsFieldCatalog.NxFieldNaming.English;
        string Campo(string chave, string tipo, NXProject.Services.NxDevOpsFieldCatalog.NxFieldNaming n)
            => NXProject.Services.NxDevOpsFieldCatalog.DefaultFieldFor(chave, tipo, n);

        // Portugues: campo proprio do NX nos dois tipos.
        if (Campo("start", "Task", pt) != "Data_Inicio" || Campo("start", "User Story", pt) != "Data_Inicio")
            throw new Exception("No padrao PT a data de inicio deveria ser o campo proprio Data_Inicio.");
        if (Campo("finish", "User Story", pt) != "Data_Fim")
            throw new Exception("No padrao PT a data de fim deveria ser o campo proprio Data_Fim.");

        // Ingles: nativo onde existe — inclusive na Story, que tem Start/Finish Date de fabrica.
        if (Campo("start", "Task", en) != "Microsoft.VSTS.Scheduling.StartDate")
            throw new Exception("No padrao EN a Task deveria usar o Start Date nativo.");
        if (Campo("start", "User Story", en) != "Microsoft.VSTS.Scheduling.StartDate")
            throw new Exception("No padrao EN a Story tambem deveria usar o Start Date nativo.");
        if (Campo("finish", "User Story", en) != "Microsoft.VSTS.Scheduling.FinishDate")
            throw new Exception("No padrao EN a Story deveria usar o Finish Date nativo.");
        if (Campo("effort", "Task", en) != "Microsoft.VSTS.Scheduling.OriginalEstimate")
            throw new Exception("No padrao EN o HH da Task deveria usar o Original Estimate nativo.");

        // No DevOps o campo e do processo e so depois associado ao tipo: no padrao ingles a
        // Story usa o MESMO Original Estimate da Task, em vez de um campo proprio para a mesma
        // coisa. O Setup associa o nativo a Story quando ele ainda nao estiver la.
        if (Campo("effort", "User Story", en) != "Microsoft.VSTS.Scheduling.OriginalEstimate")
            throw new Exception("No padrao EN a Story deveria usar o mesmo Original Estimate da Task.");
        if (Campo("effort", "User Story", pt) != "HH Estimado")
            throw new Exception("No padrao PT a Story segue com o campo proprio de horas.");
        if (!NXProject.Services.NxDevOpsFieldCatalog.HasStandardAlternative("effort", "User Story"))
            throw new Exception("A Story deveria anunciar a alternativa nativa de horas.");
        if (!NXProject.Services.NxDevOpsFieldCatalog.HasStandardAlternative("start", "User Story"))
            throw new Exception("A Story tem Start Date nativo e deveria anunciar a alternativa.");

        // O que e invencao do NX nao tem nativo em tipo nenhum: a combo nao decide nada ali.
        foreach (var chave in new[] { "percAloc", "percConclusao", "syncVersion", "admGroup" })
            foreach (var tipo in new[] { "Task", "User Story", "Feature", "Epic" })
                if (NXProject.Services.NxDevOpsFieldCatalog.HasStandardAlternative(chave, tipo))
                    throw new Exception($"'{chave}' nao tem equivalente nativo em {tipo}.");
    }

    /// <summary>
    /// As colunas do board sao as opcoes do campo Status DO PROJETO, na ordem dele. Um projeto
    /// com "Todo / In Progress / Done" nao pode abrir com as colunas New/Active/Closed do
    /// NXProject — e foi assim que o conector se comportou no primeiro teste contra um projeto
    /// real, misturando os dois conjuntos.
    ///
    /// Estado que aparece num item mas nao esta na lista entra no fim: card sem coluna some.
    /// </summary>
    // -- Sync do GitProject --------------------------------------------------------------
    //
    // Montam um board de mentira e conferem o PLANO: o que seria gravado e o que seria recusado.
    // A decisao e separada da gravacao justamente para caber em teste sem rede.

    private static (NXProject.Models.Project, NXProject.Models.ProjectTask) ProjetoComStory()
    {
        var story = new NXProject.Models.ProjectTask
        {
            Id = 2, Name = "Story A", TfsId = 20, TfsType = "User Story",
            Start = new DateTime(2026, 3, 2), Finish = new DateTime(2026, 3, 6),
            EstimatedHours = 16, PercentComplete = 50
        };
        var feature = new NXProject.Models.ProjectTask
        { Id = 1, Name = "Feature A", TfsId = 10, TfsType = "Feature" };
        feature.Children.Add(story);
        var p = new NXProject.Models.Project { Name = "P" };
        p.Tasks.Add(feature);
        return (p, story);
    }

    private static Dictionary<int, NXProject.Services.GitProjectItem> Board(
        params NXProject.Services.GitProjectItem[] itens) => itens.ToDictionary(i => i.Number);

    private static void GitSyncRefusesInvalidHierarchy()
    {
        var (p, story) = ProjetoComStory();
        // Task pendurada direto na Feature: o GitHub aceita, o NXProject nao.
        var task = new NXProject.Models.ProjectTask
        { Id = 3, Name = "Task solta", TfsId = 30, TfsType = "Task" };
        p.Tasks[0].Children.Add(task);

        var board = Board(
            new NXProject.Services.GitProjectItem { Number = 10, IssueState = "OPEN" },
            new NXProject.Services.GitProjectItem { Number = 20, IssueState = "OPEN" },
            new NXProject.Services.GitProjectItem { Number = 30, IssueState = "OPEN" });

        var plano = NXProject.Services.GitProjectSyncService.Build(
            p, board, new NXProject.Services.GitProjectOptions(), temControleDeVersao: true);

        if (plano.Updates.Any(o => o.Number == 30))
            throw new Exception("Item em hierarquia invalida NAO pode receber gravacao.");
        if (!plano.Errors.Any(e => e.Contains("Task solta")))
            throw new Exception("A recusa tem de aparecer no relatorio, com o nome do item.");
        if (!plano.Updates.Any(o => o.Number == 20))
            throw new Exception("O item valido deve continuar sendo sincronizado.");
        if (!NXProject.Services.GitProjectSyncService.HierarchyOk("Feature", "User Story")
            || NXProject.Services.GitProjectSyncService.HierarchyOk("Feature", "Task"))
            throw new Exception("A hierarquia aceita e pai imediatamente acima, nada mais.");
        _ = story;
    }

    private static void GitSyncConflictSkipsItem()
    {
        var (p, story) = ProjetoComStory();
        story.SyncVersion = 3;
        var board = Board(
            new NXProject.Services.GitProjectItem { Number = 10, IssueState = "OPEN" },
            new NXProject.Services.GitProjectItem { Number = 20, IssueState = "OPEN", SyncVersion = 5 });

        var plano = NXProject.Services.GitProjectSyncService.Build(
            p, board, new NXProject.Services.GitProjectOptions(), temControleDeVersao: true);

        if (!plano.Conflicts.Contains(story))
            throw new Exception("Versao do board a frente da local tem de virar conflito.");
        if (plano.Updates.Any(o => o.Number == 20))
            throw new Exception("Item em conflito nao pode ter NENHUM campo gravado.");

        // Mesma versao: ninguem mexeu depois, o sync segue.
        board[20].SyncVersion = 3;
        var plano2 = NXProject.Services.GitProjectSyncService.Build(
            p, board, new NXProject.Services.GitProjectOptions(), temControleDeVersao: true);
        if (plano2.Conflicts.Count > 0)
            throw new Exception("Versao igual nao e conflito.");
    }

    private static void GitSyncWritesOnlyChangedWithInclusiveFinish()
    {
        var (p, story) = ProjetoComStory();
        var item = new NXProject.Services.GitProjectItem
        {
            Number = 20, IssueState = "OPEN",
            StartDate = new DateTime(2026, 3, 2),   // ja igual: nao deve ser regravado
            FinishDate = new DateTime(2026, 3, 1),
            Effort = 16,                            // ja igual
            Percent = 10                            // diferente
        };
        var plano = NXProject.Services.GitProjectSyncService.Build(
            p, Board(new NXProject.Services.GitProjectItem { Number = 10 }, item),
            new NXProject.Services.GitProjectOptions(), temControleDeVersao: true);

        var doItem = plano.Updates.Where(o => o.Number == 20).ToList();
        if (doItem.Any(o => o.Key == "start")) throw new Exception("Data igual nao deve ser regravada.");
        if (doItem.Any(o => o.Key == "effort")) throw new Exception("HH igual nao deve ser regravado.");
        if (!doItem.Any(o => o.Key == "percConclusao")) throw new Exception("Percentual diferente tem de ser gravado.");

        var fim = doItem.FirstOrDefault(o => o.Key == "finish");
        if (fim == null) throw new Exception("Fim diferente tem de ser gravado.");
        // O motor guarda fim EXCLUSIVO (06/03); o board recebe o inclusivo (05/03).
        if ((DateTime)fim.Value != new DateTime(2026, 3, 5))
            throw new Exception("O fim gravado tem de ser o INCLUSIVO: " + fim.Value);
        _ = story;
    }

    private static void GitSyncClosesAndReopensIssue()
    {
        var (p, story) = ProjetoComStory();
        story.PercentComplete = 100;
        var aberta = new NXProject.Services.GitProjectItem { Number = 20, IssueState = "OPEN", Percent = 100 };
        var plano = NXProject.Services.GitProjectSyncService.Build(
            p, Board(new NXProject.Services.GitProjectItem { Number = 10 }, aberta),
            new NXProject.Services.GitProjectOptions(), temControleDeVersao: true);
        var fechar = plano.Updates.FirstOrDefault(o => o.Number == 20 && o.Key == "state");
        if (fechar == null || (bool)fechar.Value != true)
            throw new Exception("Atividade 100% tem de fechar a issue.");

        // Voltou a andar: a issue fechada no GitHub nao pode ficar desmentindo o cronograma.
        story.PercentComplete = 40;
        var fechada = new NXProject.Services.GitProjectItem { Number = 20, IssueState = "CLOSED", Percent = 40 };
        var plano2 = NXProject.Services.GitProjectSyncService.Build(
            p, Board(new NXProject.Services.GitProjectItem { Number = 10 }, fechada),
            new NXProject.Services.GitProjectOptions(), temControleDeVersao: true);
        var reabrir = plano2.Updates.FirstOrDefault(o => o.Number == 20 && o.Key == "state");
        if (reabrir == null || (bool)reabrir.Value != false)
            throw new Exception("Atividade abaixo de 100% com issue fechada tem de reabrir.");
    }

    private static void GitSyncWarnsAboutOrderAndVersioning()
    {
        var (p, _) = ProjetoComStory();
        var board = Board(new NXProject.Services.GitProjectItem { Number = 10 },
                          new NXProject.Services.GitProjectItem { Number = 20 });

        var semControle = NXProject.Services.GitProjectSyncService.Build(
            p, board, new NXProject.Services.GitProjectOptions(), temControleDeVersao: false);
        if (!semControle.Warnings.Any(w => w.Contains("concorr")))
            throw new Exception("Board sem os campos de versao tem de avisar que nao ha controle.");
        // Durante um tempo este teste exigia um aviso de "a ordem nao vai para o GitHub". A sonda
        // `NXTestUnit gitorder` mediu o contrario: a mutation de posicao funciona no topo e no
        // meio, e a releitura a reflete. O aviso saiu, e o que fica e a exigencia oposta — nao
        // avisar algo que deixou de ser verdade.
        if (semControle.Warnings.Any(w => w.Contains("ordem do backlog")))
            throw new Exception("A ordem passou a ser gravada: nao deve mais haver aviso dizendo que nao vai.");

        var comControle = NXProject.Services.GitProjectSyncService.Build(
            p, board, new NXProject.Services.GitProjectOptions(), temControleDeVersao: true);
        if (comControle.Warnings.Any(w => w.Contains("concorr")))
            throw new Exception("Com os campos presentes, o aviso de concorrencia nao deve aparecer.");
    }

    private static void GitSyncNeedsRepositoryToCreate()
    {
        var (p, story) = ProjetoComStory();
        var nova = new NXProject.Models.ProjectTask
        { Id = 4, Name = "Task nova", TfsType = "Task" };     // sem TfsId: nasce no sync
        story.Children.Add(nova);
        var board = Board(new NXProject.Services.GitProjectItem { Number = 10 },
                          new NXProject.Services.GitProjectItem { Number = 20 });

        var sem = NXProject.Services.GitProjectSyncService.Build(
            p, board, new NXProject.Services.GitProjectOptions(), temControleDeVersao: true);
        if (sem.Creates.Count != 0) throw new Exception("Sem repositorio nao da para criar issue.");
        if (!sem.Errors.Any(e => e.Contains("reposit")))
            throw new Exception("A falta do repositorio tem de ser dita, nao um item que some calado.");

        var com = NXProject.Services.GitProjectSyncService.Build(
            p, board, new NXProject.Services.GitProjectOptions { Repository = "dono/repo" },
            temControleDeVersao: true);
        if (!com.Creates.Contains(nova)) throw new Exception("Com repositorio, o item novo entra para criacao.");
    }

    // -- Mapa de niveis -------------------------------------------------------------------
    //
    // O mapa e estatico (NxLevelMap.Current) porque e consultado de metodos estaticos espalhados.
    // Os testes o restauram no finally: deixar um mapa de teste publicado quebraria os seguintes.

    private static void ComMapa(NXProject.Services.NxLevelMap mapa, Action corpo)
    {
        var anterior = NXProject.Services.NxLevelMap.Current;
        NXProject.Services.NxLevelMap.Current = mapa;
        try { corpo(); } finally { NXProject.Services.NxLevelMap.Current = anterior; }
    }

    private static void LevelMapRecognizesCustomTypeName()
    {
        var padrao = new NXProject.Services.NxLevelMap();
        // De fabrica: os mesmos nomes que estavam presos no codigo, com acento e caixa livres.
        if (padrao.LevelOf("epico") != NXProject.Services.NxLevel.Epic
            || padrao.LevelOf("Épico") != NXProject.Services.NxLevel.Epic
            || padrao.LevelOf("User Story") != NXProject.Services.NxLevel.Story)
            throw new Exception("Os nomes de fabrica tem de continuar valendo (sem acento, sem caixa).");
        if (padrao.LevelOf("Iniciativa") != null)
            throw new Exception("'Iniciativa' nao e nome de fabrica de nenhum nivel.");

        var mapa = new NXProject.Services.NxLevelMap();
        mapa.Names[NXProject.Services.NxLevel.Project] = new List<string> { "Iniciativa", "Project" };
        ComMapa(mapa, () =>
        {
            if (!NXProject.Services.TfsImportService.IsBoardProjectType("Iniciativa"))
                throw new Exception("Tipo configurado tem de ser reconhecido pelo conector do DevOps.");
            if (!NXProject.Services.GitProjectSyncService.HierarchyOk("Iniciativa", "Epic"))
                throw new Exception("O mesmo mapa tem de valer para a hierarquia do GitProject.");
        });
    }

    private static void LevelMapSkipsDisabledLevel()
    {
        var mapa = new NXProject.Services.NxLevelMap();
        mapa.Disabled.Add(NXProject.Services.NxLevel.Feature);
        ComMapa(mapa, () =>
        {
            if (!mapa.ParentChildOk("Epic", "User Story"))
                throw new Exception("Com Feature desligada, o Epic passa a aceitar Story direto.");
            if (mapa.ParentChildOk("Epic", "Feature"))
                throw new Exception("Nivel desligado nao pode ser reconhecido como tipo.");
            if (mapa.ParentChildOk("User Story", "Task") != true)
                throw new Exception("O resto da hierarquia continua valendo.");
        });

        var semTask = new NXProject.Services.NxLevelMap();
        semTask.Disabled.Add(NXProject.Services.NxLevel.Task);
        if (!semTask.IsLeaf(NXProject.Services.NxLevel.Story))
            throw new Exception("Sem o nivel Task, a folha passa a ser a Story.");
        if (semTask.ParentChildOk("User Story", "Task"))
            throw new Exception("Com Task desligada, nada pode ficar sob a Story.");
    }

    private static void LevelMapDetectsAmbiguity()
    {
        var mapa = new NXProject.Services.NxLevelMap();
        mapa.Names[NXProject.Services.NxLevel.Feature] = new List<string> { "Feature", "Entrega" };
        mapa.Names[NXProject.Services.NxLevel.Epic] = new List<string> { "Epic", "entrega" };
        var ambiguas = mapa.Ambiguities();
        if (ambiguas.Count == 0)
            throw new Exception("O mesmo nome em dois niveis tem de ser apontado antes de salvar.");
        if (new NXProject.Services.NxLevelMap().Ambiguities().Count != 0)
            throw new Exception("O mapa de fabrica nao pode ter ambiguidade.");
    }

    // -- Ordem do backlog -----------------------------------------------------------------
    //
    // A ordem e a MESMA nos dois destinos; so muda como cada servidor a guarda (numero no Azure
    // DevOps, posicao na view no GitHub). Por isso a regra e testada uma vez so, aqui.

    private static NXProject.Models.ProjectTask Tar(int tfsId, string nome) =>
        new() { Id = tfsId, Name = nome, TfsId = tfsId, TfsType = "User Story" };

    private static void OrderDesiredFollowsScheduleTree()
    {
        var p = new NXProject.Models.Project { Name = "P" };
        var f = new NXProject.Models.ProjectTask
        { Id = 1, Name = "Feature", TfsId = 1, TfsType = "Feature" };
        f.Children.Add(Tar(2, "A"));
        f.Children.Add(Tar(3, "B"));
        p.Tasks.Add(f);
        p.Tasks.Add(Tar(4, "C"));
        // Sem vinculo no servidor: nao tem posicao para ocupar.
        p.Tasks.Add(new NXProject.Models.ProjectTask { Id = 9, Name = "Nova" });

        var ordem = NXProject.Services.BacklogOrderService.DesiredOrder(p);
        if (!ordem.SequenceEqual(new[] { 1, 2, 3, 4 }))
            throw new Exception("A ordem desejada e a arvore achatada, pai antes dos filhos: "
                + string.Join(",", ordem));
    }

    private static void OrderMovesOnlyWhatIsOutOfPlace()
    {
        var desejada = new[] { 1, 2, 3, 4, 5 };

        // Ja na ordem: nenhum movimento. E o caso que mais acontece, e o que custa ~2s por item
        // se alguem resolver reescrever a fila inteira.
        if (NXProject.Services.BacklogOrderService.PlanMoves(desejada, new[] { 1, 2, 3, 4, 5 }).Count != 0)
            throw new Exception("Fila ja correta nao pode gerar movimento nenhum.");

        // Um item fora de lugar: um movimento so.
        var movs = NXProject.Services.BacklogOrderService.PlanMoves(desejada, new[] { 1, 2, 4, 3, 5 });
        if (movs.Count != 1 || movs[0].Id != 3 || movs[0].AfterId != 2)
            throw new Exception("Deveria mover so o #3 para depois do #2: "
                + string.Join(" | ", movs.Select(m => $"{m.Id} apos {m.AfterId}")));

        // O plano, aplicado, tem de produzir a ordem desejada — senao ele estaria certo "por
        // engano" num caso simples e errado nos outros.
        var fila = new List<int> { 5, 4, 3, 2, 1 };
        foreach (var m in NXProject.Services.BacklogOrderService.PlanMoves(desejada, fila))
        {
            fila.Remove(m.Id);
            var destino = m.AfterId == 0 ? 0 : fila.IndexOf(m.AfterId) + 1;
            fila.Insert(destino, m.Id);
        }
        if (!fila.SequenceEqual(desejada))
            throw new Exception("Aplicar o plano tem de resultar na ordem desejada: " + string.Join(",", fila));
    }

    private static void OrderLeavesForeignItemsAlone()
    {
        // 90 e 91 sao de outra equipe no mesmo board: nao estao no cronograma.
        var desejada = new[] { 1, 2, 3 };
        var atual = new[] { 90, 2, 1, 91, 3 };
        var movs = NXProject.Services.BacklogOrderService.PlanMoves(desejada, atual);
        if (movs.Any(m => m.Id is 90 or 91 || m.AfterId is 90 or 91))
            throw new Exception("Item que o cronograma nao conhece nao e nosso para reposicionar.");

        var fila = atual.ToList();
        foreach (var m in movs)
        {
            fila.Remove(m.Id);
            var destino = m.AfterId == 0 ? 0 : fila.IndexOf(m.AfterId) + 1;
            fila.Insert(destino, m.Id);
        }
        var nossos = fila.Where(n => n < 90).ToList();
        if (!nossos.SequenceEqual(desejada))
            throw new Exception("Os itens do cronograma tem de ficar na ordem dele: " + string.Join(",", fila));
    }

    private static void OrderRankFitsBetweenNeighbours()
    {
        var meio = NXProject.Services.BacklogOrderService.NextRank(1000, 2000);
        if (meio <= 1000 || meio >= 2000)
            throw new Exception("Com os dois vizinhos, o rank encaixa no MEIO — e so o item movido muda.");
        if (NXProject.Services.BacklogOrderService.NextRank(1000, null) <= 1000)
            throw new Exception("Sem proximo, encadeia depois do anterior.");
        if (NXProject.Services.BacklogOrderService.NextRank(null, 2000) >= 2000)
            throw new Exception("Sem anterior, entra antes do proximo.");
        if (NXProject.Services.BacklogOrderService.NextRank(null, null) <= 0)
            throw new Exception("Grupo sem rank nenhum precisa de um ponto de partida valido.");
    }

    // -- Auditoria de BLOCK no GitProject ---------------------------------------------------
    //
    // O plano dava isto como "talvez nunca fique igual". A POC mediu o contrario: a timeline
    // devolve label, hora e autor. O que se testa aqui e a traducao disso em periodos — e os dois
    // casos em que e facil produzir numero errado com cara de certo.

    private static NXProject.Services.GitProjectClient.LabelEvent Ev(string dia, string label, bool entrou, string quem = "ana")
        => new(DateTime.Parse("2026-03-" + dia), label, entrou, quem);

    private static void GitBlockRebuildsPeriodsFromTimeline()
    {
        var item = new NXProject.Services.GitProjectItem { Number = 7, Title = "Story", IssueState = "OPEN" };
        var eventos = new List<NXProject.Services.GitProjectClient.LabelEvent>
        {
            Ev("02 09:00", "BLOCK", true),
            Ev("04 17:00", "BLOCK", false, "bruno"),
            Ev("09 08:00", "BLOCK", true),
            Ev("10 12:00", "BLOCK", false),
        };

        var audit = NXProject.Services.GitProjectBlockAuditService.Build(item, eventos, "BLOCK");
        if (audit.Periods.Count != 2)
            throw new Exception("Duas idas e voltas da label sao DOIS periodos: " + audit.Periods.Count);
        if (audit.Periods[0].Start != DateTime.Parse("2026-03-02 09:00")
            || audit.Periods[0].End != DateTime.Parse("2026-03-04 17:00"))
            throw new Exception("O periodo tem de ser entrada -> saida da label.");
        if (audit.Periods[0].BlockedBy != "ana" || audit.Periods[0].UnblockedBy != "bruno")
            throw new Exception("Quem bloqueou e quem desbloqueou vem da timeline, e nao pode se perder.");
        if (audit.StillBlocked)
            throw new Exception("Ultima saida fecha o bloqueio: o item nao esta mais bloqueado.");
    }

    private static void GitBlockOpenPeriodStaysOpen()
    {
        var item = new NXProject.Services.GitProjectItem { Number = 8, Title = "Task", IssueState = "OPEN" };
        var eventos = new List<NXProject.Services.GitProjectClient.LabelEvent>
        {
            Ev("02 09:00", "BLOCK", true),
            // Reaplicar a label nao recomeca o bloqueio: ele nunca parou.
            Ev("03 10:00", "BLOCK", true),
        };

        var audit = NXProject.Services.GitProjectBlockAuditService.Build(item, eventos, "BLOCK");
        if (audit.Periods.Count != 1)
            throw new Exception("Duas entradas seguidas sao UM bloqueio so: " + audit.Periods.Count);
        if (audit.Periods[0].Start != DateTime.Parse("2026-03-02 09:00"))
            throw new Exception("Vale a PRIMEIRA entrada; a segunda nao reinicia a contagem.");
        if (!audit.Periods[0].Open || !audit.StillBlocked)
            throw new Exception("Sem saida, o periodo fica ABERTO — fecha-lo em 'agora' faria o "
                + "bloqueio parecer resolvido.");
    }

    private static void GitBlockIgnoresOrphanAndOtherLabels()
    {
        var item = new NXProject.Services.GitProjectItem { Number = 9, Title = "Story", IssueState = "OPEN" };
        var eventos = new List<NXProject.Services.GitProjectClient.LabelEvent>
        {
            // Saida sem entrada: a entrada ficou fora da pagina da timeline. Inventar um inicio
            // aqui produziria um periodo gigante e falso.
            Ev("01 08:00", "BLOCK", false),
            Ev("02 09:00", "bug", true),
            Ev("03 09:00", "bug", false),
            Ev("05 09:00", "BLOCK", true),
            Ev("06 09:00", "BLOCK", false),
        };

        var audit = NXProject.Services.GitProjectBlockAuditService.Build(item, eventos, "BLOCK");
        if (audit.Periods.Count != 1)
            throw new Exception("So o par completo da label de bloqueio vira periodo: " + audit.Periods.Count);
        if (audit.Periods[0].Start != DateTime.Parse("2026-03-05 09:00"))
            throw new Exception("Label de outro assunto nao pode entrar na conta do bloqueio.");

        // A tag e configuravel: quem batizou de "Impedimento" tem de ser atendido igual.
        var outra = NXProject.Services.GitProjectBlockAuditService.Build(item, eventos, "Impedimento");
        if (outra.Periods.Count != 0)
            throw new Exception("Com outra tag configurada, os eventos de BLOCK nao contam.");
    }

    // -- Responsavel no GitProject ----------------------------------------------------------
    //
    // Ler responsavel e facil; GRAVAR exige o ID da conta. Como nome pode estar vazio, pode
    // repetir e e digitado a mao, a identidade vem do LOGIN gravado no recurso durante o import.
    // Responsavel errado muda a fila de trabalho de duas pessoas de uma vez — entao o que nao
    // casar com seguranca tem de virar aviso.

    private static readonly List<NXProject.Services.GitProjectPeopleService.Account> Contas =
    [
        new("U1", "asilva", "Ana Silva"),
        new("U2", "asilva2", "Ana Silva"),
        new("U3", "bcosta", "Bruno Costa"),
        new("U4", "semnome", ""),
    ];

    private static void GitAssigneeUsesStoredLogin()
    {
        // Duas "Ana Silva": so o login desempata — e ele veio do proprio import.
        var r = NXProject.Services.GitProjectPeopleService.Resolve("Ana Silva", Contas, "asilva2");
        if (!r.Ok || r.Account!.Id != "U2")
            throw new Exception("O login gravado no recurso tem de mandar sobre o nome.");

        // Conta com @ na frente (como se escreve no GitHub) continua casando.
        if (NXProject.Services.GitProjectPeopleService.Resolve("qualquer", Contas, "@bcosta").Account?.Id != "U3")
            throw new Exception("O @ na frente do login nao pode atrapalhar.");

        // Pessoa sem nome de exibicao: so o login a identifica — e isso tem de bastar.
        if (NXProject.Services.GitProjectPeopleService.Resolve("", Contas, "semnome").Account?.Id != "U4")
            throw new Exception("Conta sem nome de exibicao ainda e identificavel pelo login.");

        // Login que perdeu acesso ao repositorio nao vira outra pessoa: vira aviso.
        var saiu = NXProject.Services.GitProjectPeopleService.Resolve("Ana Silva", Contas, "antiga");
        if (saiu.Ok || !saiu.Problem.Contains("acesso"))
            throw new Exception("Login sem acesso tem de avisar, nao cair no nome parecido.");
    }

    private static void GitAssigneeRefusesToGuess()
    {
        // Sem login gravado, "Ana Silva" e ambiguo: duas contas atendem.
        var ambiguo = NXProject.Services.GitProjectPeopleService.Resolve("Ana Silva", Contas);
        if (ambiguo.Ok) throw new Exception("Nome ambiguo NAO pode virar palpite.");
        if (!ambiguo.Problem.Contains("asilva") || !ambiguo.Problem.Contains("asilva2"))
            throw new Exception("O aviso tem de dizer QUAIS contas colidiram, senao nao da para resolver.");

        // Nome unico resolve sozinho.
        if (NXProject.Services.GitProjectPeopleService.Resolve("Bruno Costa", Contas).Account?.Id != "U3")
            throw new Exception("Nome completo unico pode casar.");

        // Quem nao e colaborador do repositorio simplesmente nao pode ser responsavel la.
        var fora = NXProject.Services.GitProjectPeopleService.Resolve("Carla Dias", Contas);
        if (fora.Ok || !fora.Problem.Contains("colaborador"))
            throw new Exception("Pessoa sem conta no repositorio tem de virar aviso explicito.");

        // Recurso vazio nao e problema: e so uma atividade sem responsavel.
        var vazio = NXProject.Services.GitProjectPeopleService.Resolve("", Contas);
        if (vazio.Ok || vazio.Problem.Length != 0)
            throw new Exception("Atividade sem responsavel nao deve gerar aviso nenhum.");
    }

    // -- Controle de versao no GitProject ---------------------------------------------------
    //
    // O Sync_version do GitProject ESPELHA a revisao do servidor (updatedAt), em vez de ser um
    // contador do NXProject. A diferenca e exatamente o caso que mais acontece: alguem editar
    // pela tela do GitHub. Um contador nosso nao veria isso; o relogio do GitHub ve.

    private static void GitRevisionDetectsServerEdit()
    {
        var (p, story) = ProjetoComStory();
        // A atividade foi importada quando a issue estava na revisao de 01/03.
        story.SyncVersion = NXProject.Services.GitProjectSyncService.Revision(
            new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc));

        // Alguem mexeu na issue pela tela do GitHub depois disso.
        var item = new NXProject.Services.GitProjectItem
        {
            Number = 20, IssueState = "OPEN",
            UpdatedAt = new DateTime(2026, 3, 5, 9, 0, 0, DateTimeKind.Utc)
        };

        var plano = NXProject.Services.GitProjectSyncService.Build(
            p, Board(new NXProject.Services.GitProjectItem { Number = 10 }, item),
            new NXProject.Services.GitProjectOptions(), temControleDeVersao: false);

        if (!plano.Conflicts.Contains(story))
            throw new Exception("Edicao feita no GitHub depois do import tem de virar conflito, "
                + "mesmo sem os campos Sync_version/Sync_Name no board.");
        if (plano.Updates.Any(o => o.Number == 20))
            throw new Exception("Item em conflito nao pode receber gravacao nenhuma.");

        // Ninguem mexeu desde o import: o sync segue normalmente.
        item.UpdatedAt = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        var ok = NXProject.Services.GitProjectSyncService.Build(
            p, Board(new NXProject.Services.GitProjectItem { Number = 10 }, item),
            new NXProject.Services.GitProjectOptions(), temControleDeVersao: false);
        if (ok.Conflicts.Count != 0)
            throw new Exception("Mesma revisao nao e conflito.");
    }

    private static void GitRevisionFitsAndKeepsOrder()
    {
        var antes = NXProject.Services.GitProjectSyncService.Revision(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        var depois = NXProject.Services.GitProjectSyncService.Revision(new DateTime(2026, 3, 1, 0, 0, 1, DateTimeKind.Utc));
        if (depois <= antes)
            throw new Exception("Um segundo a mais tem de dar numero maior: e assim que 'mexeu depois' e detectado.");

        // O campo do cronograma e int: a contagem tem de caber nele por decadas. O tempo Unix
        // estoura em 2038 — foi por isso que a contagem comeca em 2000.
        var longe = NXProject.Services.GitProjectSyncService.Revision(new DateTime(2060, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        if (longe <= 0) throw new Exception("Data distante nao pode estourar o int e virar negativo.");

        if (NXProject.Services.GitProjectSyncService.Revision(null) != 0)
            throw new Exception("Sem revisao lida, o valor e 0 — e 0 significa 'nao sei', nao 'antigo'.");
    }

    // -- Quem pode sincronizar (Adm_NX) no GitProject ---------------------------------------
    //
    // No DevOps o campo guarda um GRUPO e o servidor resolve. No GitHub, time so existe em
    // organizacao — board de conta pessoal nao tem nenhum. Entao o campo aceita as duas formas, e
    // autorizacao e a regra que mais machuca quando erra nos dois sentidos: travar quem pode e
    // liberar quem nao pode.

    private static void GitAdminListAuthorizes()
    {
        var regra = "ana, @bruno , carla";
        if (!NXProject.Services.GitProjectAdminService.IsAllowed(regra, "bruno"))
            throw new Exception("Login na lista autoriza — inclusive escrito com @ e espacos.");
        if (!NXProject.Services.GitProjectAdminService.IsAllowed(regra, "ANA"))
            throw new Exception("Login nao diferencia maiusculas.");
        if (NXProject.Services.GitProjectAdminService.IsAllowed(regra, "daniel"))
            throw new Exception("Quem nao esta na lista NAO pode sincronizar.");

        // Campo vazio libera, como no DevOps: board sem a regra configurada nao trava ninguem.
        if (!NXProject.Services.GitProjectAdminService.IsAllowed("", "daniel"))
            throw new Exception("Sem regra configurada, todo mundo pode.");
        if (!NXProject.Services.GitProjectAdminService.IsAllowed(null, "daniel"))
            throw new Exception("Campo ausente e o mesmo que vazio.");
    }

    private static void GitAdminTeamAndFailOpen()
    {
        var time = "@nexus-xdata/projetos";
        if (!NXProject.Services.GitProjectAdminService.IsTeam(time))
            throw new Exception("'@org/time' tem de ser reconhecido como time, nao como lista.");
        var (org, slug) = NXProject.Services.GitProjectAdminService.ParseTeam(time);
        if (org != "nexus-xdata" || slug != "projetos")
            throw new Exception("Organizacao e time saem do proprio valor: " + org + "/" + slug);
        if (NXProject.Services.GitProjectAdminService.IsTeam("ana, bruno"))
            throw new Exception("Lista de logins NAO e time.");

        var membros = new[] { "ana", "bruno" };
        if (!NXProject.Services.GitProjectAdminService.IsAllowed(time, "ana", membros))
            throw new Exception("Membro do time pode sincronizar.");
        if (NXProject.Services.GitProjectAdminService.IsAllowed(time, "daniel", membros))
            throw new Exception("Quem nao e membro NAO pode.");

        // Membros nao lidos (token sem read:org, rede fora) e falha TECNICA: libera, como ja vale
        // no DevOps. Transformar falha de leitura em bloqueio de trabalho seria pior.
        if (!NXProject.Services.GitProjectAdminService.IsAllowed(time, "daniel", null))
            throw new Exception("Sem conseguir ler os membros, nao se bloqueia ninguem.");
    }

    // -- Anexo no GitProject ----------------------------------------------------------------
    //
    // O GitHub nao tem API de anexo de item (medido). O substituto e versionar no repositorio — e
    // o caminho espelha o backlog, para o anexo ser achado navegando, nao procurando por id.

    private static void GitAttachmentPathMirrorsHierarchy()
    {
        var hierarquia = new List<NXProject.Services.GitProjectAttachmentService.Step>
        {
            new("Project", 45, "Portal de Vendas"),
            new("Epic", 46, "Integração com ERP"),
            new("Story", 60, "Login"),
        };

        var caminho = NXProject.Services.GitProjectAttachmentService.PathFor(hierarquia, "proposta.pdf");
        if (caminho != "nxproject/anexos/project_45/epic_46/story_60/proposta.pdf")
            throw new Exception("Caminho inesperado: " + caminho);

        // Nome de arquivo com barra iria parar noutra pasta: tem de ser neutralizado.
        var perigoso = NXProject.Services.GitProjectAttachmentService.PathFor(
            hierarquia.Take(1).ToList(), "../../etc/senha.txt");
        if (perigoso.Contains("..") || perigoso.Split('/').Length != 4)
            throw new Exception("Nome de arquivo nao pode escapar da pasta do item: " + perigoso);

        // Nivel com espaco ("User Story") nao pode quebrar o caminho.
        var comEspaco = NXProject.Services.GitProjectAttachmentService.PathFor(
            [new("User Story", 7, "")], "a.txt");
        if (comEspaco != "nxproject/anexos/user-story_7/a.txt")
            throw new Exception("Nivel com espaco deveria virar hifen: " + comEspaco);
    }

    private static void GitAttachmentReusesFolderAfterRename()
    {
        // O titulo NAO entra no caminho: renomear o item no GitHub nao pode fazer o anexo sumir
        // do lugar onde o NXProject vai procura-lo. Tipo e numero nao mudam; titulo muda sempre.
        var antes = NXProject.Services.GitProjectAttachmentService.PathFor(
            [new("Project", 45, "Portal de Vendas")], "b.pdf");
        var depois = NXProject.Services.GitProjectAttachmentService.PathFor(
            [new("Project", 45, "Portal de Vendas RENOMEADO")], "b.pdf");
        if (antes != depois)
            throw new Exception("Renomear o item mudou o caminho do anexo: " + antes + " -> " + depois);

        // Numero diferente e item diferente — 45 nao pode cair na pasta do 450.
        var outro = NXProject.Services.GitProjectAttachmentService.PathFor(
            [new("Project", 450, "")], "b.pdf");
        if (outro == antes) throw new Exception("Itens diferentes nao podem compartilhar pasta.");
    }

    // -- EPIC_GROUP ------------------------------------------------------------------------
    //
    // E a UNICA excecao a regra "o pai e o nivel imediatamente acima": um EPIC que agrupa EPICs
    // (as Melhorias abertas por sprint, por exemplo). A regra vale para os dois destinos porque
    // mora no NxLevelMap — import e sync, DevOps e GitProject, perguntam a ele.

    private static void EpicGroupIsTheOnlyEpicUnderEpic()
    {
        var mapa = new NXProject.Services.NxLevelMap();
        const string G = NXProject.Models.EpicTypes.Group;

        // Epic sob Epic so quando o de cima e grupo.
        if (!mapa.ParentChildOk("Epic", "Epic", G, NXProject.Models.EpicTypes.Delivery))
            throw new Exception("EPIC_GROUP tem de aceitar EPIC embaixo.");
        if (mapa.ParentChildOk("Epic", "Epic", NXProject.Models.EpicTypes.Delivery, null))
            throw new Exception("Epic comum NAO pode ter Epic embaixo — senao a hierarquia vira arvore livre.");

        // O grupo entra sob o Work Item Project, como qualquer EPIC.
        if (!mapa.ParentChildOk("Project", "Epic", null, G))
            throw new Exception("O grupo tem de entrar sob o Work Item Project.");

        // E a hierarquia normal continua valendo igual.
        if (!mapa.ParentChildOk("Feature", "User Story") || !mapa.ParentChildOk("User Story", "Task"))
            throw new Exception("O resto da hierarquia nao pode ter mudado.");

        // A mesma regra responde pelo GitProject (import e sync passam por aqui).
        if (!NXProject.Services.GitProjectSyncService.HierarchyOk("Epic", "Epic", G, null))
            throw new Exception("O GitProject tem de enxergar o EPIC_GROUP do mesmo jeito.");
    }

    private static void EpicGroupRefusesFeatureAndNesting()
    {
        var mapa = new NXProject.Services.NxLevelMap();
        const string G = NXProject.Models.EpicTypes.Group;

        // Feature direto no grupo pularia o EPIC: o cronograma mostraria entrega sem o EPIC que
        // a explica.
        if (mapa.ParentChildOk("Epic", "Feature", G, null))
            throw new Exception("Sob o EPIC_GROUP so vem EPIC.");

        // Um nivel de agrupamento, nao uma escada.
        if (mapa.ParentChildOk("Epic", "Epic", G, G))
            throw new Exception("Grupo dentro de grupo NAO.");

        // E o grupo nao pode ficar pendurado num EPIC comum.
        if (mapa.ParentChildOk("Epic", "Epic", NXProject.Models.EpicTypes.Delivery, G))
            throw new Exception("O grupo so entra sob o Work Item Project.");

        // O valor e reconhecido sem depender de caixa.
        if (!NXProject.Models.EpicTypes.IsGroup("epic_group"))
            throw new Exception("EPIC_GROUP nao pode diferenciar maiusculas.");
        if (NXProject.Models.EpicTypes.IsGroup(NXProject.Models.EpicTypes.Backlog))
            throw new Exception("BACKLOG nao e grupo.");
    }

    private static void DragDropMovesEpicIntoGroup()
    {
        var proj = new ProjectTask
        { Id = 800, Name = "Work Item Project", TfsType = "Project", Level = 0, IsSummary = true };
        var grupo = new ProjectTask
        {
            Id = 801, Name = "Melhorias", TfsType = "Epic", EpicType = NXProject.Models.EpicTypes.Group,
            Parent = proj, Level = 1, IsSummary = true
        };
        var epicSolto = new ProjectTask
        { Id = 802, Name = "Melhorias Sprint 12", TfsType = "Epic", Parent = proj, Level = 1, IsSummary = true };
        var epicNoGrupo = new ProjectTask
        { Id = 803, Name = "Melhorias Sprint 11", TfsType = "Epic", Parent = grupo, Level = 2, IsSummary = true };
        var feature = new ProjectTask
        { Id = 804, Name = "Entrega", TfsType = "Feature", Parent = proj, Level = 1 };

        grupo.Children.Add(epicNoGrupo);
        proj.Children.Add(grupo);
        proj.Children.Add(epicSolto);
        proj.Children.Add(feature);

        var project = new Project { Name = "EPIC_GROUP", StartDate = new DateTime(2026, 7, 6) };
        project.Tasks.Add(proj);

        var vm = new MainViewModel("NXTestUnit") { Project = project };
        vm.RebuildFlatTasks();

        // Solta o EPIC sobre o grupo: entra nele.
        AssertDrop(vm, epicSolto.Id, grupo.Id, "EPIC deveria entrar no EPIC_GROUP.");
        AssertEqual(grupo.Id, epicSolto.Parent?.Id ?? -1, "O pai novo tem de ser o grupo.");

        // Solta sobre um EPIC que esta direto no Project: sai do grupo e vira irmao dele.
        AssertDrop(vm, epicNoGrupo.Id, feature.Id, "Soltar EPIC sobre Feature nao e movimento valido.",
                   esperado: false);
        AssertEqual(grupo.Id, epicNoGrupo.Parent?.Id ?? -1, "EPIC bloqueado continua no grupo.");

        // O proprio grupo nao entra em outro grupo.
        var outroGrupo = new ProjectTask
        {
            Id = 805, Name = "Outro grupo", TfsType = "Epic", EpicType = NXProject.Models.EpicTypes.Group,
            Parent = proj, Level = 1, IsSummary = true
        };
        proj.Children.Add(outroGrupo);
        vm.RebuildFlatTasks();
        // Soltar um grupo sobre OUTRO GRUPO irmao e reordenacao — isso e valido e continua
        // valendo. O que nao pode e o grupo virar FILHO do outro: um nivel de agrupamento, nao
        // uma escada.
        AssertDrop(vm, grupo.Id, outroGrupo.Id, "Reordenar dois grupos irmaos deveria ser aceito.");
        AssertEqual(proj.Id, grupo.Parent?.Id ?? -1,
            "Grupo dentro de grupo NAO: ele continua sob o Work Item Project.");
    }

    /// <summary>Solta um item sobre outro e confere se o movimento foi aceito (ou recusado).</summary>
    private static void AssertDrop(MainViewModel vm, int sourceId, int targetId, string msg, bool esperado = true)
    {
        var source = vm.FlatTasks.First(t => t.Id == sourceId);
        var target = vm.FlatTasks.First(t => t.Id == targetId);
        if (vm.MoveTaskByDrop(source, target, insertAfter: true) != esperado) throw new Exception(msg);
        vm.RebuildFlatTasks();
    }

    /// <summary>
    /// Defeito encontrado em uso: com um EPIC_GROUP no topo, os EPICs apareciam no cronograma
    /// VAZIOS — sem Feature e sem Story.
    ///
    /// A causa era uma conta de profundidade: o import so montava ramo ate o nivel 2, assumindo
    /// Epic(0) → Feature(1) → Story(2). O grupo acrescenta um nivel, a Feature caiu no 2 e foi
    /// descartada em silencio. Agora quem decide e o TIPO.
    /// </summary>
    private static void ImportDescendsByTypeNotDepth()
    {
        // Com EPIC_GROUP: grupo(0) → Epic(1) → Feature(2) → Story(3).
        if (!TfsImportService.ImportShouldDescend("Feature", 2))
            throw new Exception("Feature no nivel 2 e o caso do EPIC_GROUP: tem de virar ramo.");
        if (!TfsImportService.ImportShouldDescend("User Story", 3))
            throw new Exception("Story abaixo da Feature do grupo tem de entrar.");
        if (!TfsImportService.ImportShouldDescend("Epic", 1))
            throw new Exception("EPIC dentro do grupo e ramo.");

        // Sem o grupo, a hierarquia de sempre continua valendo.
        if (!TfsImportService.ImportShouldDescend("Epic", 0)
            || !TfsImportService.ImportShouldDescend("Feature", 1)
            || !TfsImportService.ImportShouldDescend("User Story", 2))
            throw new Exception("A hierarquia tradicional nao pode ter mudado.");

        // Story entra em qualquer profundidade: ha backlog com Story direto no Epic.
        if (!TfsImportService.ImportShouldDescend("User Story", 1))
            throw new Exception("Story pendurada direto no Epic tem de entrar.");

        // Task nunca vira ramo (ela entra por outro caminho, como folha).
        if (TfsImportService.ImportShouldDescend("Task", 2))
            throw new Exception("Task nao e ramo do cronograma.");

        // Tipo que o NXProject nao modela fica de fora, em qualquer nivel.
        if (TfsImportService.ImportShouldDescend("Impediment", 1))
            throw new Exception("Tipo fora do mapa de niveis nao vira ramo.");

        // O limite alto continua protegendo de ciclo de links no servidor.
        if (TfsImportService.ImportShouldDescend("Feature", 99))
            throw new Exception("Profundidade absurda tem de parar — protecao contra ciclo.");

        // O tipo tambem sai do MAPA: processo que chama o Epic de outra coisa continua valendo.
        var mapa = new NXProject.Services.NxLevelMap();
        mapa.Names[NXProject.Services.NxLevel.Epic] = new List<string> { "Iniciativa" };
        ComMapa(mapa, () =>
        {
            if (!TfsImportService.ImportShouldDescend("Iniciativa", 0))
                throw new Exception("Tipo configurado como Epic tem de virar ramo.");
            if (TfsImportService.ImportShouldDescend("Epic", 0))
                throw new Exception("Nome que saiu do mapa deixa de ser reconhecido.");
        });
    }

    private static void GitProjectColumnsComeFromProjectStatus()
    {
        var o = new NXProject.Services.GitProjectOptions();
        var itens = new List<NXProject.Services.GitProjectItem>
        {
            new() { Number = 1, Title = "H1", IssueType = "Story", Status = "In Progress" },
            new() { Number = 2, Title = "H2", IssueType = "Story", Status = "Revisao" }
        };
        var comStatus = new NXProject.Services.GitProjectClient.ProjectSnapshot(
            "P", "B", new List<NXProject.Services.GitIteration>(), itens, new List<string>())
        { StatusOptions = new[] { "Todo", "In Progress", "Done" } };

        var board = NXProject.Services.GitProjectBoardService.Build(comStatus, o);
        var cols = board.States.ToList();
        if (cols.Count < 3 || cols[0] != "Todo" || cols[1] != "In Progress" || cols[2] != "Done")
            throw new Exception("As tres primeiras colunas deveriam ser as do projeto, na ordem: " + string.Join(" | ", cols));
        if (cols.Contains("New") || cols.Contains("Active"))
            throw new Exception("Projeto com Status proprio nao deve ganhar as colunas fixas do NXProject.");
        if (!cols.Contains("Revisao"))
            throw new Exception("Estado presente num card tem de virar coluna, senao o card some.");

        // Sem campo Status, vale o conjunto fixo — projeto simples ainda abre.
        var semStatus = new NXProject.Services.GitProjectClient.ProjectSnapshot(
            "P", "B", new List<NXProject.Services.GitIteration>(),
            new List<NXProject.Services.GitProjectItem>
            { new() { Number = 3, Title = "H3", IssueType = "Story", IssueState = "OPEN" } },
            new List<string>());
        var board2 = NXProject.Services.GitProjectBoardService.Build(semStatus, o);
        if (!board2.States.Contains("New") || !board2.States.Contains("Closed"))
            throw new Exception("Projeto sem Status deveria cair no conjunto fixo do NXProject.");
    }

    private static void GitProjectFieldNamesMatchByAlias()
    {
        var o = new NXProject.Services.GitProjectOptions();

        foreach (var nome in new[] { "Start Date", "Data_Inicio", "data_inicio", "  Start Date  " })
            if (!NXProject.Services.GitProjectOptions.Matches(nome, o.StartDateFields))
                throw new Exception($"'{nome}' deveria casar com a data de inicio.");
        foreach (var nome in new[] { "Finish Date", "Data_Fim", "Target Date" })
            if (!NXProject.Services.GitProjectOptions.Matches(nome, o.FinishDateFields))
                throw new Exception($"'{nome}' deveria casar com a data de fim.");
        foreach (var nome in new[] { "Original Estimate", "HH Estimado", "Esforço Estimado" })
            if (!NXProject.Services.GitProjectOptions.Matches(nome, o.EffortFields))
                throw new Exception($"'{nome}' deveria casar com o HH estimado.");
        foreach (var nome in new[] { "card_type", "nx_card_type", "nx_level", "Level", "Nível" })
            if (!NXProject.Services.GitProjectOptions.Matches(nome, o.LevelFields))
                throw new Exception($"'{nome}' deveria casar com o campo de nivel.");

        // O sugerido ao CRIAR e sempre o primeiro, e o padrao novo e em ingles.
        if (NXProject.Services.GitProjectOptions.Suggested(o.StartDateFields) != "Start Date")
            throw new Exception("O nome sugerido para a data de inicio deveria ser Start Date.");
        if (NXProject.Services.GitProjectOptions.Suggested(o.LevelFields) != "card_type")
            throw new Exception("O nivel do item no GitProject deveria sugerir card_type.");
        // E o card_type nao pode vazar para o DevOps: la o tipo do work item e nativo.
        if (NXProject.Services.NxDevOpsFieldCatalog.DevOpsFields.Any(f => f.Key == "cardType"))
            throw new Exception("card_type e so do GitProject: criar no DevOps duplicaria o tipo nativo.");
        if (!NXProject.Services.NxDevOpsFieldCatalog.GitProjectFields.Any(f => f.Key == "cardType"))
            throw new Exception("card_type deveria estar no catalogo do GitProject.");

        // E o que nao e de ninguem nao pode casar por engano.
        if (NXProject.Services.GitProjectOptions.Matches("Status", o.StartDateFields)
            || NXProject.Services.GitProjectOptions.Matches("", o.StartDateFields)
            || NXProject.Services.GitProjectOptions.Matches("Start", o.StartDateFields))
            throw new Exception("Nome diferente (ou vazio) nao pode casar com a data de inicio.");
    }

    private static void GitProjectBoardProjectsIntoBoard()
    {
        var opcoes = new NXProject.Services.GitProjectOptions { Owner = "nexus", ProjectNumber = 1, Token = "x" };
        var itens = new List<NXProject.Services.GitProjectItem>
        {
            new() { Number = 10, Title = "Suprimentos", IssueType = "Epic", IssueState = "OPEN" },
            new() { Number = 20, Title = "Integracao", IssueType = "Feature", ParentNumber = 10, IssueState = "OPEN" },
            new() { Number = 30, Title = "Carga de dados", IssueType = "Story", ParentNumber = 20,
                    IssueState = "OPEN", Assignee = "Ana", IterationTitle = "Sprint 5", Status = "Active" },
            new() { Number = 40, Title = "Ajustar view", IssueType = "Task", ParentNumber = 30,
                    IssueState = "OPEN", Assignee = "Bruno", Effort = 5, CurrentHours = 2, Status = "Active" }
        };
        var snap = new NXProject.Services.GitProjectClient.ProjectSnapshot(
            "PVT_1", "Board", new List<NXProject.Services.GitIteration>(), itens, new List<string>());

        var board = NXProject.Services.GitProjectBoardService.Build(snap, opcoes);

        if (board.Stories.Count != 1) throw new Exception($"Esperava 1 Story, veio {board.Stories.Count}.");
        var row = board.Stories[0];
        if (row.Id != 30 || row.AssignedTo != "Ana")
            throw new Exception("A Story do GitHub nao virou linha do board com o responsavel certo.");
        if (row.State != "Active")
            throw new Exception($"O estado deve vir da coluna do board (Status), veio '{row.State}'.");
        if (row.FeatureId != 20 || row.FeatureEpicId != 10)
            throw new Exception("A hierarquia de sub-issues nao preencheu Feature e EPIC.");
        if (row.IterationPath != "Sprint 5")
            throw new Exception("A sprint do item deveria virar o IterationPath da linha.");
        if (row.Tasks.Count != 1 || row.Tasks[0].Id != 40 || row.Tasks[0].EstimateHours != 5
            || row.Tasks[0].CompletedHours != 2)
            throw new Exception("A sub-issue nao virou card com os campos de HH do board.");
        if (!board.People.Contains("Ana") || !board.People.Contains("Bruno"))
            throw new Exception("As pessoas do board saem dos responsaveis das issues.");
        if (!board.LevelItems.Any(i => i.Id == 20 && i.Kind == "Feature"))
            throw new Exception("A Feature deveria entrar como item de nivel.");

        // Recorte por sprint: o titulo da iteracao e a identidade.
        var so5 = NXProject.Services.GitProjectBoardService.Build(snap, opcoes, new[] { "Sprint 5" });
        if (so5.Stories.Count != 1) throw new Exception("O recorte pela sprint do item nao casou.");
        var outra = NXProject.Services.GitProjectBoardService.Build(snap, opcoes, new[] { "Sprint 6" });
        if (outra.Stories.Count != 0) throw new Exception("Sprint diferente nao deveria trazer a Story.");
    }

    /// <summary>
    /// O GitHub nao valida hierarquia: o nivel vem do Issue Type (so em organizacao), do campo do
    /// board, ou — sem nenhum dos dois — da POSICAO na arvore de sub-issues. Sem esse ultimo
    /// recurso, um projeto de conta pessoal sem campo nenhum abriria o board vazio.
    /// </summary>
    private static void GitProjectLevelFallsBackToTreePosition()
    {
        var opcoes = new NXProject.Services.GitProjectOptions { Owner = "nexus", ProjectNumber = 1, Token = "x" };
        var itens = new List<NXProject.Services.GitProjectItem>
        {
            new() { Number = 1, Title = "Projeto", IssueState = "OPEN" },
            new() { Number = 2, Title = "Epico", ParentNumber = 1, IssueState = "OPEN" },
            new() { Number = 3, Title = "Entrega", ParentNumber = 2, IssueState = "OPEN" },
            new() { Number = 4, Title = "Historia", ParentNumber = 3, IssueState = "OPEN" },
            new() { Number = 5, Title = "Tarefa", ParentNumber = 4, IssueState = "CLOSED" }
        };
        var porNumero = itens.ToDictionary(i => i.Number);
        string Nivel(int n) => NXProject.Services.GitProjectBoardService.LevelKindOf(porNumero[n], porNumero, opcoes);

        if (Nivel(1) != "Project" || Nivel(2) != "Epic" || Nivel(3) != "Feature"
            || Nivel(4) != "Story" || Nivel(5) != "Task")
            throw new Exception("A posicao na arvore de sub-issues deveria definir o nivel.");

        // O campo do board vence a posicao: e mais explicito que deduzir.
        var comCampo = new NXProject.Services.GitProjectItem
        { Number = 9, Title = "X", ParentNumber = 4, LevelValue = "Historia" };
        porNumero[9] = comCampo;
        if (NXProject.Services.GitProjectBoardService.LevelKindOf(comCampo, porNumero, opcoes) != "Story")
            throw new Exception("O campo de nivel do board deveria vencer a posicao na arvore.");

        // Pai FORA do projeto (um EPIC de ideias, outro Work Item Project): a profundidade
        // daria zero e o item viraria "Project", uma coluna de topo tirada de um buraco. Sem
        // como deduzir, vale Story — a folha do cronograma, onde o trabalho aparece como linha.
        var paiDeFora = new NXProject.Services.GitProjectItem
        { Number = 50, Title = "Orfa", ParentNumber = 999999 };
        porNumero[50] = paiDeFora;
        if (NXProject.Services.GitProjectBoardService.LevelKindOf(paiDeFora, porNumero, opcoes) != "Story")
            throw new Exception("Item com pai fora do projeto deveria valer como Story, nunca como Project.");

        // Sem coluna no board, o estado cai no OPEN/CLOSED da propria issue.
        if (NXProject.Services.GitProjectBoardService.StateOf(porNumero[5]) != "Closed")
            throw new Exception("Issue CLOSED sem campo Status deveria virar Closed no board.");
        if (NXProject.Services.GitProjectBoardService.StateOf(porNumero[1]) != "New")
            throw new Exception("Issue OPEN sem campo Status deveria virar New no board.");
    }

    /// <summary>
    /// O GitHub guarda a iteracao como inicio + duracao em dias; o NXProject mostra sprint com
    /// fim INCLUSIVO em toda a interface. Uma sprint de 14 dias que comeca dia 5 termina dia 18,
    /// nao dia 19 — errar isso desloca o recorte "sprint atual" em um dia.
    /// </summary>
    private static void GitProjectIterationEndIsInclusive()
    {
        var it = new NXProject.Services.GitIteration("I_1", "Sprint 5", new DateTime(2026, 1, 5), 14);
        if (it.End != new DateTime(2026, 1, 18))
            throw new Exception($"Fim inclusivo deveria ser 18/01, veio {it.End:dd/MM}.");

        var snap = new NXProject.Services.GitProjectClient.ProjectSnapshot(
            "PVT_1", "Board", new[] { it }, new List<NXProject.Services.GitProjectItem>(), new List<string>());
        var sprints = NXProject.Services.GitProjectBoardService.Sprints(snap);
        if (sprints.Count != 1 || sprints[0].Path != "Sprint 5" || sprints[0].End != new DateTime(2026, 1, 18))
            throw new Exception("A sprint do board deveria vir da iteracao, com fim inclusivo.");
    }

    private static void LocalBoardSprintUsesIterationPath()
    {
        var project = new NXProject.Models.Project();
        project.Sprints.Add(new NXProject.Models.Sprint
        {
            Number = 5, DisplayName = "Sprint 5", Path = @"Projeto\Release 1\Sprint 5",
            Start = new DateTime(2026, 1, 5), End = new DateTime(2026, 1, 16)
        });
        // Cronograma que nunca viu DevOps: sem caminho, o nome e a identidade possivel.
        project.Sprints.Add(new NXProject.Models.Sprint
        {
            Number = 6, Start = new DateTime(2026, 1, 19), End = new DateTime(2026, 1, 30)
        });

        var sprints = NXProject.Services.LocalBoardService.Sprints(project);
        if (sprints.Count != 2)
            throw new Exception($"Esperava 2 sprints do cronograma, veio {sprints.Count}.");
        if (sprints[0].Path != @"Projeto\Release 1\Sprint 5")
            throw new Exception($"A sprint deveria ser identificada pelo IterationPath: veio '{sprints[0].Path}'.");
        if (sprints[0].Name != "Sprint 5")
            throw new Exception("O nome exibido da sprint continua sendo o nome, nao o caminho.");
        if (sprints[1].Path != "Sprint 6")
            throw new Exception($"Sem IterationPath, a identidade cai no nome: veio '{sprints[1].Path}'.");

        // E o filtro tem de casar com o que a atividade guarda: card da sprint 5 entra, o de fora nao.
        var dentro = new NXProject.Models.ProjectTask
        {
            Id = 70, Name = "Dentro", TfsType = "User Story",
            TfsIterationPath = @"Projeto\Release 1\Sprint 5"
        };
        var fora = new NXProject.Models.ProjectTask
        {
            Id = 71, Name = "Fora", TfsType = "User Story",
            TfsIterationPath = @"Projeto\Release 1\Sprint 4"
        };
        var feature = new NXProject.Models.ProjectTask
        { Id = 60, Name = "F", TfsType = "Feature", Children = { dentro, fora } };
        project.Tasks.Add(feature);

        var board = NXProject.Services.LocalBoardService.Build(project, new[] { sprints[0].Path });
        if (board.Stories.Count != 1 || board.Stories[0].Id != 70)
            throw new Exception("O filtro de sprint local deveria casar com o IterationPath da atividade.");
    }

    /// <summary>
    /// A tela pergunta pela CAPACIDADE, nunca pelo nome do destino. Este teste trava o contrato:
    /// anexo no servidor no DevOps e no GitProject (la, arquivo no repositorio), em pasta no
    /// Local; e o Local
    /// exigindo cronograma aberto e pasta do projeto.
    /// </summary>
    private static void BackendCapabilitiesPerKind()
    {
        var devops = NXProject.Services.NxBackendCapabilities.For(NXProject.Services.NxBackendKind.AzureDevOps);
        var git = NXProject.Services.NxBackendCapabilities.For(NXProject.Services.NxBackendKind.GitProject);
        var local = NXProject.Services.NxBackendCapabilities.For(NXProject.Services.NxBackendKind.Local);

        if (devops.Attachments != NXProject.Services.NxAttachmentStore.Server)
            throw new Exception("No DevOps o anexo e do servidor.");
        // O GitHub nao tem API de ANEXO DE ITEM (medido na POC), mas tem onde guardar o arquivo:
        // o repositorio da issue, versionado, com o link comentado no item. Entao o anexo existe
        // e e do servidor — so nao e um anexo "do work item" como no DevOps.
        if (git.Attachments != NXProject.Services.NxAttachmentStore.Server)
            throw new Exception("No GitProject o anexo vai para o repositorio da issue.");
        if (local.Attachments != NXProject.Services.NxAttachmentStore.LocalFolder)
            throw new Exception("No modo Local o anexo vai para a pasta do projeto.");
        if (!local.BlockAudit)
            throw new Exception("No modo Local a auditoria de BLOCK vem do log da pasta.");
        if (local.OpenInBrowser || local.Comments || local.ConcurrencyControl)
            throw new Exception("Modo Local nao tem navegador, comentario nem controle de concorrencia.");
        if (!local.RequiresOpenSchedule || !local.RequiresProjectFolder)
            throw new Exception("Modo Local exige cronograma aberto E pasta do projeto.");
        // Default segue o padrao de hoje: igual ao DevOps.
        var padrao = NXProject.Services.NxBackendCapabilities.For(NXProject.Services.NxBackendKind.Default);
        if (padrao != devops)
            throw new Exception("Default deveria ter as mesmas capacidades do Azure DevOps.");
    }

    private static void WiqlModelRoundTripsClauses()
    {
        const string wiql =
            "SELECT [System.Id], [System.Title] FROM WorkItems " +
            "WHERE [System.WorkItemType] = 'Feature' AND [Custom.Type] = 'Hotfix' " +
            "AND [System.IterationPath] Under 'Solucoes - Suprimentos' AND [System.State] = 'Closed' " +
            "ORDER BY [System.Id]";

        var model = NXProject.Services.WiqlQueryModel.Parse(wiql);
        if (!(model.Editable)) throw new Exception("WIQL simples deveria ser editavel em clausulas");
        if (!(model.Clauses.Count == 4)) throw new Exception($"esperava 4 clausulas, veio {model.Clauses.Count}");
        if (!(model.Clauses[0].Field == "System.WorkItemType")) throw new Exception("campo da 1a clausula");
        if (!(model.Clauses[0].Value == "Feature")) throw new Exception("valor da 1a clausula");
        if (!(model.Clauses[2].Operator == "Under")) throw new Exception("operador Under preservado");
        if (!(model.Clauses[2].Value == "Solucoes - Suprimentos")) throw new Exception("valor com espacos preservado");
        if (!(model.Clauses[3].Connector == "And")) throw new Exception("conector da 4a clausula");
        if (!(model.OrderByPart.StartsWith("ORDER BY", StringComparison.OrdinalIgnoreCase))) throw new Exception("ORDER BY preservado");

        // Reescrever e ler de novo tem de dar o MESMO conjunto de clausulas.
        var again = NXProject.Services.WiqlQueryModel.Parse(model.Build());
        if (!(again.Editable)) throw new Exception("WIQL gerado deveria continuar editavel");
        if (!(again.Clauses.Count == 4)) throw new Exception("round-trip mudou a quantidade de clausulas");
        for (var i = 0; i < 4; i++)
        {
            if (!(again.Clauses[i].Field == model.Clauses[i].Field)) throw new Exception($"campo mudou na clausula {i}");
            if (!(again.Clauses[i].Operator == model.Clauses[i].Operator)) throw new Exception($"operador mudou na clausula {i}");
            if (!(again.Clauses[i].Value == model.Clauses[i].Value)) throw new Exception($"valor mudou na clausula {i}");
        }
    }

    /// <summary>
    /// Com parenteses o editor visual nao garante reescrever igual: tem de recusar e deixar a
    /// query no modo texto, em vez de devolver uma consulta diferente da original.
    /// </summary>
    private static void WiqlModelRefusesParentheses()
    {
        const string wiql =
            "SELECT [System.Id] FROM WorkItems " +
            "WHERE [System.State] = 'Active' AND ([System.AssignedTo] = @Me OR [System.State] = 'New')";
        var model = NXProject.Services.WiqlQueryModel.Parse(wiql);
        if (!(!model.Editable)) throw new Exception("WHERE com parenteses NAO pode virar editor visual");
        if (!(model.NotEditableReason.Length > 0)) throw new Exception("deveria dizer por que nao e editavel");
    }

    private static void StringsHaveNoDuplicateResourceKeys()
    {
        static string? FindUp(string rel)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var p = Path.Combine(dir.FullName, rel.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(p)) return p;
                dir = dir.Parent;
            }
            return null;
        }

        var files = new[]
        {
            "NXProject.Community/Strings/Strings.pt-BR.xaml",
            "NXProject.Community/Strings/Strings.en-US.xaml"
        };

        var problems = new List<string>();
        var checkedAny = false;
        foreach (var rel in files)
        {
            var path = FindUp(rel);
            if (path == null) continue; // fora do checkout (ex.: rodando do zip) → ignora
            checkedAny = true;
            var content = File.ReadAllText(path);
            var firstLine = new Dictionary<string, int>();
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(content, "x:Key=\"([^\"]+)\""))
            {
                var key = m.Groups[1].Value;
                var line = content.Take(m.Index).Count(ch => ch == '\n') + 1;
                if (firstLine.TryGetValue(key, out var prev))
                    problems.Add($"{Path.GetFileName(path)}: chave duplicada '{key}' (linhas {prev} e {line})");
                else
                    firstLine[key] = line;
            }
        }

        if (!checkedAny)
            return; // não achou os fontes (execução fora do repositório): não falha
        if (problems.Count > 0)
            throw new InvalidOperationException(
                "Chaves de recurso duplicadas (qualifique com o nome da tela/fonte):\n  "
                + string.Join("\n  ", problems));
    }

    private static void SummaryHoursUseRatedOriginalEstimateNotCalendarSpan()
    {
        var start = new DateTime(2026, 8, 12);
        var finish = new DateTime(2026, 8, 31); // intervalo largo de proposito
        var story = new ProjectTask
        {
            Id = 2200,
            Name = "Story rateada",
            TfsType = "Story",
            Level = 0,
            IsSummary = true,
            Start = start,
            Finish = finish
        };
        // Tres tasks fechadas sem HH do DevOps, so com a fracao do rateio.
        foreach (var i in new[] { 1, 2, 3 })
        {
            story.Children.Add(new ProjectTask
            {
                Id = 2200 + i,
                Name = "Task rateada " + i,
                TfsType = "Task",
                Parent = story,
                Level = 1,
                Start = start,
                Finish = finish,
                CurrentHours = 0,
                EstimatedHours = 0,
                OriginalEstimatedHours = 12.333333333333334
            });
        }

        var vm = new TaskViewModel(story);

        // 3 x 12,3333 = 37h (rateio), nao ~440h de span do calendario.
        AssertEqual(37.0, Math.Round(vm.DurationHours, 3),
            "Resumo deve somar OriginalEstimatedHours das folhas, nao o span do calendario.");
    }

    // Regressao do "rateio + task" (task fechada dobrando o HH): uma Task fechada com
    // CompletedWork=30 e OriginalEstimate=30 nao pode virar 60. O Original nao e "restante";
    // o AbsorbRemainingHoursWhenComplete somaria Original em cima do Atual e dobraria.
    private static void ClosedTaskDoesNotDoubleOriginalIntoCurrent()
    {
        // Fechada com apontamento: Atual=Completed, Restante=0 (nao 30+30).
        var (cur, est) = TfsImportService.ResolveTaskScheduleHours(
            originalEstimate: 30, completedWork: 30, percentComplete: 100);
        AssertEqual(30.0, cur ?? -1, "Task fechada: HH Atual = CompletedWork (nao Completed+Original).");
        if (est != null)
            throw new InvalidOperationException("Task fechada nao tem HH Restante (Original nao e restante).");

        // Fechada sem apontamento: usa o Original como esforco concluido.
        var (cur2, est2) = TfsImportService.ResolveTaskScheduleHours(30, 0, 100);
        AssertEqual(30.0, cur2 ?? -1, "Task fechada sem CompletedWork: usa o Original como Atual.");
        if (est2 != null)
            throw new InvalidOperationException("Task fechada sem apontamento tambem tem restante 0.");

        // Em andamento: mantem Original como restante e Completed como atual.
        var (cur3, est3) = TfsImportService.ResolveTaskScheduleHours(30, 10, 50);
        AssertEqual(10.0, cur3 ?? -1, "Task em andamento: HH Atual = CompletedWork.");
        AssertEqual(30.0, est3 ?? -1, "Task em andamento: HH Restante = Original.");

        // Nova (0%): sem atual, restante = Original.
        var (cur4, est4) = TfsImportService.ResolveTaskScheduleHours(30, 0, 0);
        if (cur4 != null)
            throw new InvalidOperationException("Task nova nao tem HH Atual.");
        AssertEqual(30.0, est4 ?? -1, "Task nova: HH Restante = Original.");

        // Resumo com duas tasks fechadas (30 e 10) deve dar 40 no cronograma, nao 80.
        var story = new ProjectTask { Id = 3300, Name = "Story", TfsType = "Story", IsSummary = true,
            Start = new DateTime(2026, 8, 4), Finish = new DateTime(2026, 8, 10) };
        foreach (var (orig, comp, id) in new[] { (30.0, 30.0, 3301), (10.0, 10.0, 3302) })
        {
            var (c, e) = TfsImportService.ResolveTaskScheduleHours(orig, comp, 100);
            story.Children.Add(new ProjectTask { Id = id, Name = "Task " + id, TfsType = "Task",
                Parent = story, PercentComplete = 100, CurrentHours = c, EstimatedHours = e,
                Start = story.Start, Finish = story.Finish });
        }
        AssertEqual(40.0, new TaskViewModel(story).DurationHours,
            "Resumo de tasks fechadas deve somar CompletedWork (40), nao dobrar com o Original (80).");
    }

    // Regressao do import principal (BuildStory): a folha encerrada sem RemainingWork explicito
    // nao pode herdar o esforco estimado como "restante" — senao AbsorbRemaining o soma no HH
    // Atual (CompletedWork) e dobra (ex.: 30 vira 60). Trava a regra ResolveImportRemainingHours.
    private static void ImportClosedLeafHasNoRemainingHours()
    {
        // Encerrada sem RemainingWork explicito: restante = null (nao o estimado de 30).
        if (TfsImportService.ResolveImportRemainingHours(remainingWork: null, plannedHours: 30, isClosed: true) != null)
            throw new InvalidOperationException("Folha encerrada sem RemainingWork nao tem restante (evita dobrar o HH Atual).");

        // Encerrada com RemainingWork explicito: respeita o valor informado.
        AssertEqual(5.0,
            TfsImportService.ResolveImportRemainingHours(5, 30, true) ?? -1,
            "Folha encerrada com RemainingWork explicito usa o valor informado.");

        // Em andamento/nova: restante = valor planejado (RemainingWork ou esforco).
        AssertEqual(30.0,
            TfsImportService.ResolveImportRemainingHours(null, 30, false) ?? -1,
            "Folha em andamento usa o HH planejado como restante.");
    }

    private static void ScheduleDevOpsMilestoneAcceptsOnlyZeroDuration()
    {
        var start = new DateTime(2026, 7, 6);
        var task = new ProjectTask
        {
            Name = "Marco DevOps",
            TfsType = "Marco-Devops",
            Start = start,
            Finish = ProjectCalendarService.AddWorkingHours(start, 8),
            EstimatedHours = 8,
            OriginalEstimatedHours = 8
        };
        var vm = new TaskViewModel(task);

        vm.DurationText = "0";

        AssertEqual(0, vm.DurationHours, "Marco-Devops deve aceitar duracao zero.");
        AssertEqual(start, task.Finish, "Marco-Devops deve terminar na mesma data de inicio.");
        if (!task.IsMilestone)
            throw new InvalidOperationException("Marco-Devops com duracao zero deve ser marcado como milestone.");

        vm.DurationText = "8";

        AssertEqual(0, vm.DurationHours, "Marco-Devops nao deve aceitar duracao positiva.");
        AssertEqual(start, task.Finish, "Marco-Devops deve continuar como marco apos tentativa de duracao positiva.");
    }

    private static void NoDevOpsNegativeTfsIdDisplaysAsInternal()
    {
        var task = new ProjectTask
        {
            Id = 42,
            TfsId = -1,
            TfsType = "NoDevops",
            Name = "Atividade interna"
        };
        var vm = new TaskViewModel(task);

        if (task.HasTfsLink)
            throw new InvalidOperationException("TfsId negativo nao deve ser tratado como vinculo DevOps.");
        if (vm.HasDevOpsLink)
            throw new InvalidOperationException("ViewModel nao deve indicar vinculo DevOps para TfsId negativo.");
        if (vm.DisplayId != "42:I")
            throw new InvalidOperationException($"ID negativo NoDevOps deve aparecer como 42:I. Atual: {vm.DisplayId}.");
    }

    private static void PendingDevOpsCreateDisplaysAsInternal()
    {
        var task = new ProjectTask
        {
            Id = 43,
            TfsId = null,
            TfsType = "Story",
            IsPendingTfsCreate = true,
            Name = "Atividade pendente DevOps"
        };
        var vm = new TaskViewModel(task);

        if (task.HasTfsLink)
            throw new InvalidOperationException("Atividade pendente sem TfsId positivo nao deve ser tratada como vinculo DevOps.");
        if (vm.DisplayId != "43:I")
            throw new InvalidOperationException($"Atividade DevOps pendente deve continuar como 43:I ate sincronizar. Atual: {vm.DisplayId}.");

        task.TfsId = 1234;
        task.IsPendingTfsCreate = false;
        vm.TfsId = 1234;

        if (vm.DisplayId != "1234:T")
            throw new InvalidOperationException($"Apos receber TfsId positivo, deve aparecer como 1234:T. Atual: {vm.DisplayId}.");
    }

    private static void DevOpsPredecessorAcceptsInternalDevOpsOnly()
    {
        var internalDevOps = new ProjectTask { Id = 11, TfsType = "Story", Name = "Interna DevOps" };
        var internalNoDevOps = new ProjectTask { Id = 12, TfsId = -1, TfsType = "NoDevops", Name = "Interna local" };
        var target = new ProjectTask { Id = 13, TfsId = 1300, TfsType = "Story", Name = "Story destino" };

        var internalDevOpsVm = new TaskViewModel(internalDevOps);
        var internalNoDevOpsVm = new TaskViewModel(internalNoDevOps);
        var targetVm = new TaskViewModel(target);
        ConfigurePredecessorLookups(targetVm, internalDevOpsVm, internalNoDevOpsVm, targetVm);

        targetVm.PredecessorsText = "I:11,I:12";

        AssertEqual(1, target.PredecessorIds.Count, "Story DevOps deve aceitar apenas predecessor interno que tambem seja DevOps.");
        AssertEqual(11, target.PredecessorIds[0], "Story DevOps deve manter o predecessor I:11.");

        targetVm.PredecessorsText = "12";
        AssertEqual(0, target.PredecessorIds.Count, "Numero interno puro de NoDevOps tambem deve ser bloqueado para Story DevOps.");
    }

    private static void NoDevOpsPredecessorAcceptsAnyInternalType()
    {
        var internalDevOps = new ProjectTask { Id = 21, TfsType = "Story", Name = "Interna DevOps" };
        var internalNoDevOps = new ProjectTask { Id = 22, TfsId = -1, TfsType = "No DevOps", Name = "Interna local" };
        var target = new ProjectTask { Id = 23, TfsId = -2, TfsType = "NoDevops", Name = "Marco local" };

        var internalDevOpsVm = new TaskViewModel(internalDevOps);
        var internalNoDevOpsVm = new TaskViewModel(internalNoDevOps);
        var targetVm = new TaskViewModel(target);
        ConfigurePredecessorLookups(targetVm, internalDevOpsVm, internalNoDevOpsVm, targetVm);

        targetVm.PredecessorsText = "I:21,I:22";

        AssertEqual(2, target.PredecessorIds.Count, "NoDevOps deve aceitar predecessor interno de qualquer tipo.");
        AssertEqual(21, target.PredecessorIds[0], "NoDevOps deve aceitar predecessor interno DevOps.");
        AssertEqual(22, target.PredecessorIds[1], "NoDevOps deve aceitar predecessor interno NoDevOps.");
    }

    private static void DragDropMovesFeatureToAnotherEpic()
    {
        var epicA = new ProjectTask
        {
            Id = 500,
            Name = "Epic A",
            TfsType = "Epic",
            Level = 0,
            IsSummary = true
        };
        var epicB = new ProjectTask
        {
            Id = 600,
            Name = "Epic B",
            TfsType = "Epic",
            Level = 0,
            IsSummary = true
        };
        var feature = new ProjectTask
        {
            Id = 501,
            Name = "Feature movida",
            TfsType = "Feature",
            Parent = epicA,
            Level = 1,
            IsSummary = true,
            Start = new DateTime(2026, 7, 6),
            Finish = new DateTime(2026, 7, 7)
        };
        var story = new ProjectTask
        {
            Id = 502,
            Name = "Story filha",
            TfsType = "Story",
            Parent = feature,
            Level = 2,
            Start = new DateTime(2026, 7, 6),
            Finish = new DateTime(2026, 7, 7),
            EstimatedHours = 8
        };
        var existingFeature = new ProjectTask
        {
            Id = 601,
            Name = "Feature destino",
            TfsType = "Feature",
            Parent = epicB,
            Level = 1,
            Start = new DateTime(2026, 7, 6),
            Finish = new DateTime(2026, 7, 7),
            EstimatedHours = 8
        };
        feature.Children.Add(story);
        epicA.Children.Add(feature);
        epicB.Children.Add(existingFeature);

        var project = new Project { Name = "Teste drag feature", StartDate = new DateTime(2026, 7, 6) };
        project.Tasks.Add(epicA);
        project.Tasks.Add(epicB);

        var vm = new MainViewModel("NXTestUnit") { Project = project };
        vm.RebuildFlatTasks();
        var sourceVm = vm.FlatTasks.First(t => t.Id == feature.Id);
        var targetVm = vm.FlatTasks.First(t => t.Id == epicB.Id);

        if (!vm.MoveTaskByDrop(sourceVm, targetVm, insertAfter: true))
            throw new InvalidOperationException($"Arrasto deve aceitar Feature para outro Epic. Status: {vm.StatusMessage}");

        AssertEqual(epicB.Id, feature.Parent?.Id ?? -1, "Feature arrastada deve trocar para o Epic destino.");
        AssertEqual(0, epicA.Children.Count, "Epic antigo deve perder a Feature.");
        AssertEqual(2, epicB.Children.Count, "Epic destino deve receber a Feature.");
        AssertEqual(1, feature.Level, "Feature movida deve manter nivel abaixo do Epic.");
        AssertEqual(2, story.Level, "Filhos da Feature movida devem acompanhar o novo nivel.");
        if (!project.IsDirty)
            throw new InvalidOperationException("Mover Feature entre Epics deve marcar o projeto como alterado.");
    }

    private static void DragDropMovesStoryToAnotherFeature()
    {
        var featureA = new ProjectTask
        {
            Id = 100,
            Name = "Feature A",
            TfsType = "Feature",
            Level = 0,
            IsSummary = true
        };
        var featureB = new ProjectTask
        {
            Id = 200,
            Name = "Feature B",
            TfsType = "Feature",
            Level = 0,
            IsSummary = true
        };
        var story = new ProjectTask
        {
            Id = 101,
            Name = "Story movida",
            TfsType = "Story",
            Parent = featureA,
            Level = 1,
            Start = new DateTime(2026, 7, 6),
            Finish = new DateTime(2026, 7, 7),
            EstimatedHours = 8
        };
        var existingStory = new ProjectTask
        {
            Id = 201,
            Name = "Story destino",
            TfsType = "Story",
            Parent = featureB,
            Level = 1,
            Start = new DateTime(2026, 7, 6),
            Finish = new DateTime(2026, 7, 7),
            EstimatedHours = 8
        };
        featureA.Children.Add(story);
        featureB.Children.Add(existingStory);

        var project = new Project { Name = "Teste drag", StartDate = new DateTime(2026, 7, 6) };
        project.Tasks.Add(featureA);
        project.Tasks.Add(featureB);

        var vm = new MainViewModel("NXTestUnit") { Project = project };
        vm.RebuildFlatTasks();
        var sourceVm = vm.FlatTasks.First(t => t.Id == story.Id);
        var targetVm = vm.FlatTasks.First(t => t.Id == featureB.Id);

        if (!vm.MoveTaskByDrop(sourceVm, targetVm, insertAfter: true))
            throw new InvalidOperationException($"Arrasto deve aceitar Story para outra Feature. Status: {vm.StatusMessage}");

        AssertEqual(featureB.Id, story.Parent?.Id ?? -1, "Story arrastada deve trocar para a Feature destino.");
        AssertEqual(0, featureA.Children.Count, "Feature antiga deve perder a Story.");
        AssertEqual(2, featureB.Children.Count, "Feature destino deve receber a Story.");
        AssertEqual(1, story.Level, "Story movida deve manter nivel abaixo da Feature.");
        if (!project.IsDirty)
            throw new InvalidOperationException("Mover Story entre Features deve marcar o projeto como alterado.");
    }

    // Trocar a Story de Feature tem de deixar as DUAS Features com a data fim certa
    // sem depender do botao "Recalcular" geral. Cobre o caso em que a fila por recurso
    // reagenda a Story DEPOIS do move (o pai precisa acompanhar esse reagendamento).
    private static void DragDropRecalculatesBothFeatureFinishDates()
    {
        var featureA = new ProjectTask { Id = 100, Name = "Feature A", TfsType = "Feature", Level = 0, IsSummary = true };
        var featureB = new ProjectTask { Id = 200, Name = "Feature B", TfsType = "Feature", Level = 0, IsSummary = true };

        // MESMO recurso nas duas: ao cair depois da curta, a fila por recurso reagenda a longa.
        var dev = new Resource { Id = 1, Name = "Dev" };
        var longStory = new ProjectTask
        {
            Id = 101, Name = "Story longa", TfsType = "Story", Parent = featureA, Level = 1,
            Start = new DateTime(2026, 7, 6), Finish = new DateTime(2026, 7, 17), EstimatedHours = 80
        };
        longStory.Resources.Add(new TaskResource { Resource = dev, ResourceId = dev.Id, AllocationPercent = 100, EstimatedHours = 80 });
        var keepInA = new ProjectTask
        {
            Id = 102, Name = "Story que fica em A", TfsType = "Story", Parent = featureA, Level = 1,
            Start = new DateTime(2026, 7, 6), Finish = new DateTime(2026, 7, 8), EstimatedHours = 16
        };
        var shortStory = new ProjectTask
        {
            Id = 201, Name = "Story curta", TfsType = "Story", Parent = featureB, Level = 1,
            Start = new DateTime(2026, 7, 6), Finish = new DateTime(2026, 7, 7), EstimatedHours = 8
        };
        shortStory.Resources.Add(new TaskResource { Resource = dev, ResourceId = dev.Id, AllocationPercent = 100, EstimatedHours = 8 });
        featureA.Children.Add(longStory);
        featureA.Children.Add(keepInA);
        featureB.Children.Add(shortStory);

        var project = new Project { Name = "Teste recalculo", StartDate = new DateTime(2026, 7, 6) };
        project.Tasks.Add(featureA);
        project.Tasks.Add(featureB);
        project.Resources.Add(dev);
        featureA.RecalcSummary();
        featureB.RecalcSummary();

        var vm = new MainViewModel("NXTestUnit") { Project = project };
        vm.RebuildFlatTasks();
        var sourceVm = vm.FlatTasks.First(t => t.Id == longStory.Id);
        var targetVm = vm.FlatTasks.First(t => t.Id == featureB.Id);

        if (!vm.MoveTaskByDrop(sourceVm, targetVm, insertAfter: true))
            throw new InvalidOperationException($"Arrasto deve aceitar Story para outra Feature. Status: {vm.StatusMessage}");

        // Sem chamar nenhum "Recalcular" geral: as duas Features ja devem estar certas.
        AssertEqual(longStory.Finish, featureB.Finish,
            "Feature destino deve terminar junto com a Story recebida, sem recalculo manual.");
        AssertEqual(keepInA.Finish, featureA.Finish,
            "Feature origem deve encolher para a maior Story que sobrou, sem recalculo manual.");
    }

    // Feature que perde a ULTIMA Story nao pode continuar exibindo a data fim da Story que saiu.
    private static void MovingLastStoryLeavesNoStaleFinishOnOldFeature()
    {
        var featureA = new ProjectTask { Id = 100, Name = "Feature A", TfsType = "Feature", Level = 0, IsSummary = true };
        var featureB = new ProjectTask { Id = 200, Name = "Feature B", TfsType = "Feature", Level = 0, IsSummary = true };
        var only = new ProjectTask
        {
            Id = 101, Name = "Unica Story", TfsType = "Story", Parent = featureA, Level = 1,
            Start = new DateTime(2026, 7, 6), Finish = new DateTime(2026, 7, 17), EstimatedHours = 80
        };
        var other = new ProjectTask
        {
            Id = 201, Name = "Story em B", TfsType = "Story", Parent = featureB, Level = 1,
            Start = new DateTime(2026, 7, 6), Finish = new DateTime(2026, 7, 7), EstimatedHours = 8
        };
        featureA.Children.Add(only);
        featureB.Children.Add(other);

        var project = new Project { Name = "Teste feature vazia", StartDate = new DateTime(2026, 7, 6) };
        project.Tasks.Add(featureA);
        project.Tasks.Add(featureB);
        featureA.RecalcSummary();
        featureB.RecalcSummary();
        var staleFinish = featureA.Finish;

        var vm = new MainViewModel("NXTestUnit") { Project = project };
        vm.RebuildFlatTasks();
        var sourceVm = vm.FlatTasks.First(t => t.Id == only.Id);
        var targetVm = vm.FlatTasks.First(t => t.Id == featureB.Id);

        if (!vm.MoveTaskByDrop(sourceVm, targetVm, insertAfter: true))
            throw new InvalidOperationException($"Arrasto deve aceitar Story para outra Feature. Status: {vm.StatusMessage}");

        AssertEqual(0, featureA.Children.Count, "Feature origem deve ficar sem filhos.");
        if (featureA.Finish >= staleFinish)
            throw new InvalidOperationException(
                $"Feature sem filhos manteve o periodo da Story que saiu (fim {featureA.Finish:dd/MM/yyyy}, "
                + $"esperado colapsar antes de {staleFinish:dd/MM/yyyy}).");
    }

    private static void DragDropMovesTaskToAnotherStory()
    {
        var storyA = new ProjectTask
        {
            Id = 300,
            Name = "Story A",
            TfsType = "Story",
            Level = 0,
            IsSummary = true
        };
        var storyB = new ProjectTask
        {
            Id = 400,
            Name = "Story B",
            TfsType = "Story",
            Level = 0,
            IsSummary = true
        };
        var task = new ProjectTask
        {
            Id = 301,
            Name = "Task movida",
            TfsType = "Task",
            Parent = storyA,
            Level = 1,
            Start = new DateTime(2026, 7, 6),
            Finish = new DateTime(2026, 7, 7),
            EstimatedHours = 8
        };
        var existingTask = new ProjectTask
        {
            Id = 401,
            Name = "Task destino",
            TfsType = "Task",
            Parent = storyB,
            Level = 1,
            Start = new DateTime(2026, 7, 6),
            Finish = new DateTime(2026, 7, 7),
            EstimatedHours = 8
        };
        storyA.Children.Add(task);
        storyB.Children.Add(existingTask);

        var project = new Project { Name = "Teste drag task", StartDate = new DateTime(2026, 7, 6) };
        project.Tasks.Add(storyA);
        project.Tasks.Add(storyB);

        var vm = new MainViewModel("NXTestUnit") { Project = project };
        vm.RebuildFlatTasks();
        var sourceVm = vm.FlatTasks.First(t => t.Id == task.Id);
        var targetVm = vm.FlatTasks.First(t => t.Id == storyB.Id);

        if (!vm.MoveTaskByDrop(sourceVm, targetVm, insertAfter: true))
            throw new InvalidOperationException($"Arrasto deve aceitar Task para outra Story. Status: {vm.StatusMessage}");

        AssertEqual(storyB.Id, task.Parent?.Id ?? -1, "Task arrastada deve trocar para a Story destino.");
        AssertEqual(0, storyA.Children.Count, "Story antiga deve perder a Task.");
        AssertEqual(2, storyB.Children.Count, "Story destino deve receber a Task.");
        AssertEqual(1, task.Level, "Task movida deve manter nivel abaixo da Story.");
        if (!project.IsDirty)
            throw new InvalidOperationException("Mover Task entre Stories deve marcar o projeto como alterado.");
    }

    private static void DragDropMovesHierarchyItemsToSiblingInAnotherParent()
    {
        var epicA = new ProjectTask { Id = 700, Name = "Epic A", TfsType = "Epic", Level = 0, IsSummary = true };
        var epicB = new ProjectTask { Id = 800, Name = "Epic B", TfsType = "Epic", Level = 0, IsSummary = true };
        var featureA = new ProjectTask { Id = 701, Name = "Feature A", TfsType = "Feature", Parent = epicA, Level = 1, IsSummary = true };
        var featureB1 = new ProjectTask { Id = 801, Name = "Feature B1", TfsType = "Feature", Parent = epicB, Level = 1 };
        var featureB2 = new ProjectTask { Id = 802, Name = "Feature B2", TfsType = "Feature", Parent = epicB, Level = 1 };
        var storyA = new ProjectTask { Id = 702, Name = "Story A", TfsType = "Story", Parent = featureA, Level = 2, IsSummary = true };
        var storyB1 = new ProjectTask { Id = 803, Name = "Story B1", TfsType = "Story", Parent = featureB1, Level = 2 };
        var storyB2 = new ProjectTask { Id = 804, Name = "Story B2", TfsType = "Story", Parent = featureB1, Level = 2 };
        var taskA = new ProjectTask { Id = 703, Name = "Task A", TfsType = "Task", Parent = storyA, Level = 3, Priority = 2 };
        var taskB1 = new ProjectTask { Id = 805, Name = "Task B1", TfsType = "Task", Parent = storyB1, Level = 3, Priority = 1 };
        var taskB2 = new ProjectTask { Id = 806, Name = "Task B2", TfsType = "Task", Parent = storyB1, Level = 3, Priority = 3 };

        storyA.Children.Add(taskA);
        featureA.Children.Add(storyA);
        epicA.Children.Add(featureA);
        storyB1.Children.Add(taskB1);
        storyB1.Children.Add(taskB2);
        featureB1.Children.Add(storyB1);
        featureB1.Children.Add(storyB2);
        epicB.Children.Add(featureB1);
        epicB.Children.Add(featureB2);

        var project = new Project { Name = "Teste drag irmao", StartDate = new DateTime(2026, 7, 6) };
        project.Tasks.Add(epicA);
        project.Tasks.Add(epicB);

        var vm = new MainViewModel("NXTestUnit") { Project = project };
        vm.RebuildFlatTasks();

        MoveByDrop(vm, featureA.Id, featureB1.Id, insertAfter: true, "Feature deve poder ser solta apos Feature irma no Epic destino.");
        AssertEqual(epicB.Id, featureA.Parent?.Id ?? -1, "Feature deve trocar para o Epic da Feature alvo.");
        AssertEqual(featureA.Id, epicB.Children[1].Id, "Feature deve entrar logo apos a Feature alvo.");

        MoveByDrop(vm, storyA.Id, storyB1.Id, insertAfter: false, "Story deve poder ser solta antes de Story irma na Feature destino.");
        AssertEqual(featureB1.Id, storyA.Parent?.Id ?? -1, "Story deve trocar para a Feature da Story alvo.");
        AssertEqual(storyA.Id, featureB1.Children[0].Id, "Story deve entrar antes da Story alvo.");

        MoveByDrop(vm, taskA.Id, taskB1.Id, insertAfter: true, "Task deve poder ser solta apos Task irma na Story destino.");
        AssertEqual(storyB1.Id, taskA.Parent?.Id ?? -1, "Task deve trocar para a Story da Task alvo.");
        AssertEqual(taskA.Id, storyB1.Children[1].Id, "Task deve entrar logo apos a Task alvo.");
        AssertEqual(1, featureA.Level, "Feature movida deve manter nivel abaixo do Epic.");
        AssertEqual(2, storyA.Level, "Story movida deve manter nivel abaixo da Feature.");
        AssertEqual(3, taskA.Level, "Task movida deve manter nivel abaixo da Story.");
    }

    private static void DragDropBlocksInvalidHierarchyMoves()
    {
        var epic = new ProjectTask { Id = 900, Name = "Epic", TfsType = "Epic", Level = 0, IsSummary = true };
        var feature = new ProjectTask { Id = 901, Name = "Feature", TfsType = "Feature", Parent = epic, Level = 1, IsSummary = true };
        var story = new ProjectTask { Id = 902, Name = "Story", TfsType = "Story", Parent = feature, Level = 2, IsSummary = true };
        var task = new ProjectTask { Id = 903, Name = "Task", TfsType = "Task", Parent = story, Level = 3 };
        var otherEpic = new ProjectTask { Id = 910, Name = "Outro Epic", TfsType = "Epic", Level = 0, IsSummary = true };
        var otherFeature = new ProjectTask { Id = 911, Name = "Outra Feature", TfsType = "Feature", Parent = otherEpic, Level = 1 };
        var otherStory = new ProjectTask { Id = 912, Name = "Outra Story", TfsType = "Story", Parent = otherFeature, Level = 2 };

        story.Children.Add(task);
        feature.Children.Add(story);
        epic.Children.Add(feature);
        otherFeature.Children.Add(otherStory);
        otherEpic.Children.Add(otherFeature);

        var project = new Project { Name = "Teste drag invalido", StartDate = new DateTime(2026, 7, 6) };
        project.Tasks.Add(epic);
        project.Tasks.Add(otherEpic);

        var vm = new MainViewModel("NXTestUnit") { Project = project };
        vm.RebuildFlatTasks();

        AssertBlockedDrop(vm, task.Id, otherFeature.Id, "Task nao deve ser movida diretamente para Feature.");
        AssertBlockedDrop(vm, story.Id, otherEpic.Id, "Story nao deve ser movida diretamente para Epic.");
        AssertBlockedDrop(vm, feature.Id, otherStory.Id, "Feature nao deve ser movida diretamente para Story.");
        AssertEqual(story.Id, task.Parent?.Id ?? -1, "Task bloqueada deve continuar na Story original.");
        AssertEqual(feature.Id, story.Parent?.Id ?? -1, "Story bloqueada deve continuar na Feature original.");
        AssertEqual(epic.Id, feature.Parent?.Id ?? -1, "Feature bloqueada deve continuar no Epic original.");
    }

    // Guarda da ORDEM vinda do TFS: irmãos entram no cronograma na ordem do backlog
    // (StackRank/BacklogPriority) e quem não tem rank NÃO pode ser jogado para o fim.
    private static void ImportOrdersSiblingsByBacklogRank()
    {
        static string Ids(IEnumerable<int> ids) => string.Join(",", ids);
        static void AssertOrder(string expected, string actual, string message)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                throw new InvalidOperationException($"{message} Esperado: {expected}; Atual: {actual}.");
        }

        // Ordem do backlog manda, mesmo que a consulta traga embaralhado.
        AssertOrder("30,10,20",
            Ids(TfsImportService.OrderSiblingsByBacklogRank([(10, 2000), (20, 3000), (30, 1000)])),
            "Irmaos devem entrar na ordem do rank do backlog do DevOps.");

        // Item SEM rank no meio: fica onde estava (depois do irmao ranqueado anterior),
        // e nao no fim do grupo — este era o defeito que tirava a hierarquia de ordem.
        AssertOrder("10,20,30",
            Ids(TfsImportService.OrderSiblingsByBacklogRank([(10, 1000), (20, null), (30, 2000)])),
            "Irmao sem rank deve seguir o ranqueado anterior, nao ir para o fim.");

        // Sem nenhum ranqueado antes: preserva a posicao da consulta.
        AssertOrder("10,20,30",
            Ids(TfsImportService.OrderSiblingsByBacklogRank([(10, null), (20, 1000), (30, 2000)])),
            "Item sem rank antes de qualquer ranqueado deve ficar no comeco.");

        // Grupo inteiro sem rank: ordem da consulta preservada (nada de cair para ID).
        AssertOrder("30,10,20",
            Ids(TfsImportService.OrderSiblingsByBacklogRank([(30, null), (10, null), (20, null)])),
            "Grupo sem rank deve preservar a ordem da consulta de hierarquia.");

        // Ranks iguais: empate mantem a ordem da consulta (ordenacao estavel).
        AssertOrder("20,10",
            Ids(TfsImportService.OrderSiblingsByBacklogRank([(20, 1000), (10, 1000)])),
            "Rank empatado deve manter a ordem da consulta.");
    }

    // ORDEM RECEBIDA (consulta de hierarquia do DevOps) x ORDEM GRAVADA (cronograma):
    // reproduz uma resposta do DevOps com ranks e monta a hierarquia como o import faz,
    // conferindo cada grupo de irmãos. Caso real: item sem rank ia para o fim do grupo.
    private static void ImportedOrderMatchesReceivedOrder()
    {
        // Recebido do DevOps (ordem da consulta) com o rank de cada item.
        var recebidoFeatures = new (int Id, double? Rank)[]
        {
            (101, 1000000000),  // Especificacao
            (102, null),        // Desenvolvimento — SEM rank (campo vazio no work item)
            (103, 1000032622),  // Quantitativo
        };
        var nomes = new Dictionary<int, string>
        {
            [101] = "Especificacao", [102] = "Desenvolvimento", [103] = "Quantitativo",
            [201] = "Ingestao", [202] = "Implementacao", [203] = "Homologacao",
        };
        string Gravada(IEnumerable<(int Id, double? Rank)> recebido) => string.Join(" | ",
            TfsImportService.OrderSiblingsByBacklogRank(recebido.ToList()).Select(id => nomes[id]));

        AssertOrderEquals("Especificacao | Desenvolvimento | Quantitativo", Gravada(recebidoFeatures),
            "A ordem gravada deve ser a ordem RECEBIDA do DevOps — item sem rank nao pode ir para o fim.");

        // Consulta fora de ordem: aí sim o rank manda (é a ordem do backlog).
        var foraDeOrdem = new (int Id, double? Rank)[] { (203, 3000), (201, 1000), (202, 2000) };
        AssertOrderEquals("Ingestao | Implementacao | Homologacao", Gravada(foraDeOrdem),
            "Com todos ranqueados, a ordem gravada segue o rank do backlog.");

        // Todos sem rank: a ordem recebida é preservada integralmente.
        var semRank = new (int Id, double? Rank)[] { (203, null), (201, null), (202, null) };
        AssertOrderEquals("Homologacao | Ingestao | Implementacao", Gravada(semRank),
            "Grupo inteiro sem rank deve preservar a ordem recebida na consulta.");
    }

    // Import: item que veio do DevOps SEM StackRank tem o rank CALCULADO pela posição
    // recebida (ordem estável na tela) e entra na lista de "precisa sincronizar".
    private static void ImportFillsMissingBacklogRank()
    {
        // O EPIC ja vem ranqueado do DevOps: so a Feature do meio esta sem rank.
        var epic = new ProjectTask { Id = 1, Name = "Epic", TfsType = "Epic", TfsId = 900, TfsStackRank = 285680480, IsSummary = true };
        ProjectTask Feature(int id, string name, double? rank)
        {
            var f = new ProjectTask { Id = id, Name = name, TfsType = "Feature", TfsId = 1000 + id, TfsStackRank = rank, Parent = epic };
            epic.Children.Add(f);
            return f;
        }
        var primeira = Feature(2, "Especificacao", 1000000000);
        var semRank  = Feature(3, "Desenvolvimento", null);   // veio sem rank do DevOps
        var ultima   = Feature(4, "Quantitativo", 1000032622);

        var raiz = new System.Collections.ObjectModel.ObservableCollection<ProjectTask> { epic };
        var calculados = TfsImportService.FillMissingBacklogRanks(raiz);

        AssertEqual(1, calculados.Count, "So o item sem rank deve ser calculado.");
        if (calculados[0] != "Desenvolvimento")
            throw new InvalidOperationException("O item calculado deve ser o que veio sem rank.");
        if (semRank.TfsStackRank is not { } novo)
            throw new InvalidOperationException("Item sem rank deve receber um rank calculado.");
        if (!(novo > primeira.TfsStackRank!.Value && novo < ultima.TfsStackRank!.Value))
            throw new InvalidOperationException(
                $"O rank calculado ({novo}) deve manter a POSICAO recebida, entre {primeira.TfsStackRank} e {ultima.TfsStackRank}.");

        // Rodar de novo não recalcula nada (idempotente) — nada de rank novo a sincronizar.
        AssertEqual(0, TfsImportService.FillMissingBacklogRanks(raiz).Count,
            "Segunda passada nao pode recalcular rank ja preenchido.");

        // Grupo inteiro sem rank: escala criada do zero, preservando a ordem recebida.
        var story = new ProjectTask { Id = 10, Name = "Story", TfsType = "Story", TfsId = 800, TfsStackRank = 500, IsSummary = true };
        var a = new ProjectTask { Id = 11, Name = "A", TfsId = 801, Parent = story };
        var b = new ProjectTask { Id = 12, Name = "B", TfsId = 802, Parent = story };
        story.Children.Add(a); story.Children.Add(b);
        TfsImportService.FillMissingBacklogRanks(new System.Collections.ObjectModel.ObservableCollection<ProjectTask> { story });
        if (!(a.TfsStackRank < b.TfsStackRank))
            throw new InvalidOperationException("Grupo sem nenhum rank deve receber escala crescente na ordem recebida.");
    }

    // A ordem tem que ser gravada no campo que o PROCESSO usa: Agile/CMMI = StackRank,
    // Scrum = BacklogPriority. Gravar campo inexistente faz o PATCH falhar.
    private static void SyncWritesRankOnTheProcessField()
    {
        static System.Text.Json.JsonElement Fields(string json) =>
            System.Text.Json.JsonDocument.Parse(json).RootElement;

        var agile = TfsImportService.BacklogRankFieldsToWrite(
            Fields("""{"Microsoft.VSTS.Common.StackRank": 1000, "System.Title": "x"}"""));
        AssertOrderEquals("Microsoft.VSTS.Common.StackRank", string.Join(",", agile),
            "Work item com StackRank deve gravar em StackRank.");

        var scrum = TfsImportService.BacklogRankFieldsToWrite(
            Fields("""{"Microsoft.VSTS.Common.BacklogPriority": 1000, "System.Title": "x"}"""));
        AssertOrderEquals("Microsoft.VSTS.Common.BacklogPriority", string.Join(",", scrum),
            "Work item so com BacklogPriority (Scrum) deve gravar em BacklogPriority.");

        var ambos = TfsImportService.BacklogRankFieldsToWrite(
            Fields("""{"Microsoft.VSTS.Common.StackRank": 1, "Microsoft.VSTS.Common.BacklogPriority": 2}"""));
        AssertOrderEquals("Microsoft.VSTS.Common.StackRank,Microsoft.VSTS.Common.BacklogPriority",
            string.Join(",", ambos), "Com os dois campos, a ordem vai para ambos.");

        var nenhum = TfsImportService.BacklogRankFieldsToWrite(Fields("""{"System.Title": "x"}"""));
        AssertOrderEquals("Microsoft.VSTS.Common.StackRank", string.Join(",", nenhum),
            "Sem nenhum campo de ordem, usa StackRank como padrao.");

        var criacao = TfsImportService.BacklogRankFieldsToWrite(null);
        AssertOrderEquals("Microsoft.VSTS.Common.StackRank", string.Join(",", criacao),
            "Na criacao (sem campos para inspecionar) usa StackRank.");
    }

    // O Sync avisa quando vai REESCREVER a posição do item no backlog do DevOps.
    private static void SyncWarnsWhenBacklogOrderIsRewritten()
    {
        var report = new TfsImportService.SyncReport();
        report.LogWarning("⚠ Ordem do backlog: 1 item(ns) terao a POSICAO reescrita no DevOps");
        var texto = report.ToString();
        if (!texto.Contains("Ordem do backlog", StringComparison.Ordinal))
            throw new InvalidOperationException("O aviso de reordenacao deve aparecer no relatorio do Sync.");
        if (report.Messages.Count == 0)
            throw new InvalidOperationException("O aviso deve entrar na lista de mensagens (nao-sucesso) do relatorio.");
    }

    private static void AssertOrderEquals(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"{message} Esperado: {expected}; Atual: {actual}.");
    }

    private static void RebuildPreservesDevOpsHierarchyOrderIgnoringPriority()
    {
        var epic = new ProjectTask { Id = 1000, Name = "Epic", TfsType = "Epic", Level = 0, IsSummary = true };
        var featureFirst = new ProjectTask
        {
            Id = 1001,
            Name = "Feature primeira no DevOps",
            TfsType = "Feature",
            Parent = epic,
            Level = 1,
            Priority = 4,
            TfsStackRank = 2000,
            IsSummary = true
        };
        var featureSecond = new ProjectTask
        {
            Id = 1002,
            Name = "Feature segunda no DevOps",
            TfsType = "Feature",
            Parent = epic,
            Level = 1,
            Priority = 1,
            TfsStackRank = 1000,
            IsSummary = true
        };
        var storyFirst = new ProjectTask
        {
            Id = 1003,
            Name = "Story primeira no DevOps",
            TfsType = "Story",
            Parent = featureFirst,
            Level = 2,
            Priority = 4,
            TfsStackRank = 2000
        };
        var storySecond = new ProjectTask
        {
            Id = 1004,
            Name = "Story segunda no DevOps",
            TfsType = "Story",
            Parent = featureFirst,
            Level = 2,
            Priority = 1,
            TfsStackRank = 1000
        };

        featureFirst.Children.Add(storyFirst);
        featureFirst.Children.Add(storySecond);
        epic.Children.Add(featureFirst);
        epic.Children.Add(featureSecond);

        var project = new Project { Name = "Ordem hierarquia", StartDate = new DateTime(2026, 7, 6) };
        project.Tasks.Add(epic);
        var vm = new MainViewModel("NXTestUnit") { Project = project };

        vm.RebuildFlatTasks();

        AssertEqual(featureFirst.Id, epic.Children[0].Id,
            "Feature deve preservar a sequencia importada do DevOps, mesmo com prioridade maior.");
        AssertEqual(featureSecond.Id, epic.Children[1].Id,
            "Feature com prioridade menor nao deve subir se o DevOps deixou depois.");
        AssertEqual(storyFirst.Id, featureFirst.Children[0].Id,
            "Story deve preservar a sequencia importada do DevOps, mesmo com prioridade maior.");
        AssertEqual(storySecond.Id, featureFirst.Children[1].Id,
            "Story com prioridade menor nao deve subir se o DevOps deixou depois.");
    }

    private static void RebuildOrdersTasksByPriorityThenDevOpsRank()
    {
        var story = new ProjectTask
        {
            Id = 1100,
            Name = "Story",
            TfsType = "Story",
            Level = 0,
            IsSummary = true,
            Start = new DateTime(2026, 7, 6),
            Finish = new DateTime(2026, 7, 9),
            OriginalEstimatedHours = 24
        };
        var lowPriorityEarlyRank = new ProjectTask
        {
            Id = 1101,
            Name = "Prioridade 3 rank cedo",
            TfsType = "Task",
            Parent = story,
            Level = 1,
            Priority = 3,
            TfsStackRank = 100,
            OriginalEstimatedHours = 8
        };
        var highPriorityLateRank = new ProjectTask
        {
            Id = 1102,
            Name = "Prioridade 1 rank tarde",
            TfsType = "Task",
            Parent = story,
            Level = 1,
            Priority = 1,
            TfsStackRank = 900,
            OriginalEstimatedHours = 8
        };
        var samePriorityLateRank = new ProjectTask
        {
            Id = 1103,
            Name = "Prioridade 2 rank tarde",
            TfsType = "Task",
            Parent = story,
            Level = 1,
            Priority = 2,
            TfsStackRank = 800,
            OriginalEstimatedHours = 8
        };
        var samePriorityEarlyRank = new ProjectTask
        {
            Id = 1104,
            Name = "Prioridade 2 rank cedo",
            TfsType = "Task",
            Parent = story,
            Level = 1,
            Priority = 2,
            TfsStackRank = 200,
            OriginalEstimatedHours = 8
        };

        story.Children.Add(lowPriorityEarlyRank);
        story.Children.Add(samePriorityLateRank);
        story.Children.Add(samePriorityEarlyRank);
        story.Children.Add(highPriorityLateRank);

        var project = new Project { Name = "Ordem tasks", StartDate = new DateTime(2026, 7, 6) };
        project.Tasks.Add(story);
        var vm = new MainViewModel("NXTestUnit") { Project = project };

        vm.RebuildFlatTasks();

        var orderedIds = story.Children.Select(t => t.Id).ToList();
        var expected = new[] { highPriorityLateRank.Id, samePriorityEarlyRank.Id, samePriorityLateRank.Id, lowPriorityEarlyRank.Id };
        if (!orderedIds.SequenceEqual(expected))
        {
            throw new InvalidOperationException(
                "Tasks devem ordenar por Prioridade e, na mesma Prioridade, pela sequencia/rank do DevOps. " +
                $"Esperado: {string.Join(", ", expected)}; Atual: {string.Join(", ", orderedIds)}.");
        }
    }

    private static void MoveByDrop(MainViewModel vm, int sourceId, int targetId, bool insertAfter, string message)
    {
        var sourceVm = vm.FlatTasks.First(t => t.Id == sourceId);
        var targetVm = vm.FlatTasks.First(t => t.Id == targetId);
        if (!vm.MoveTaskByDrop(sourceVm, targetVm, insertAfter))
            throw new InvalidOperationException($"{message} Status: {vm.StatusMessage}");
    }

    private static void AssertBlockedDrop(MainViewModel vm, int sourceId, int targetId, string message)
    {
        var sourceVm = vm.FlatTasks.First(t => t.Id == sourceId);
        var targetVm = vm.FlatTasks.First(t => t.Id == targetId);
        if (vm.MoveTaskByDrop(sourceVm, targetVm, insertAfter: true))
            throw new InvalidOperationException(message);
    }

    private static void AddMilestoneCreatesDevOpsSiblingForDevOpsSelection()
    {
        var project = new Project { Name = "Teste", StartDate = new DateTime(2026, 7, 6) };
        var story = new ProjectTask
        {
            Id = 1,
            Name = "Story",
            TfsType = "Story",
            TfsId = 1001,
            Start = new DateTime(2026, 7, 6),
            Finish = new DateTime(2026, 7, 8)
        };
        project.Tasks.Add(story);

        var vm = new MainViewModel("NXTestUnit") { Project = project };
        vm.RebuildFlatTasks();
        vm.SelectedTask = vm.FlatTasks.First(t => t.Id == story.Id);

        vm.AddMilestone(asChild: false);

        AssertEqual(2, project.Tasks.Count, "Botao de marco deve criar irmao abaixo da selecao.");
        var marco = project.Tasks[1];
        if (marco.TfsType != "Marco-Devops")
            throw new InvalidOperationException($"Selecao DevOps deve criar Marco-Devops. Atual: {marco.TfsType}.");
        AssertEqual(0, marco.EstimatedHours ?? -1, "Marco deve nascer com duracao zero.");
        AssertEqual(story.Id, marco.PredecessorIds.Single(), "Marco irmao deve usar atividade selecionada como predecessora.");
        if (!marco.IsMilestone || !marco.IsPendingTfsCreate || marco.TfsId != 0)
            throw new InvalidOperationException("Marco-Devops irmao deve nascer como milestone pendente de criacao no DevOps.");
    }

    private static void AddMilestoneCreatesDevOpsChildWithCtrl()
    {
        var project = new Project { Name = "Teste", StartDate = new DateTime(2026, 7, 6) };
        var feature = new ProjectTask
        {
            Id = 10,
            Name = "Feature",
            TfsType = "Feature",
            TfsId = 1010,
            Start = new DateTime(2026, 7, 6),
            Finish = new DateTime(2026, 7, 10),
            IsSummary = true
        };
        var story = new ProjectTask
        {
            Id = 11,
            Name = "Story filha",
            TfsType = "Story",
            TfsId = 1011,
            Parent = feature,
            Level = 1,
            Start = new DateTime(2026, 7, 6),
            Finish = new DateTime(2026, 7, 8)
        };
        feature.Children.Add(story);
        project.Tasks.Add(feature);

        var vm = new MainViewModel("NXTestUnit") { Project = project };
        vm.RebuildFlatTasks();
        vm.SelectedTask = vm.FlatTasks.First(t => t.Id == feature.Id);

        vm.AddMilestone(asChild: true);

        AssertEqual(2, feature.Children.Count, "Ctrl+botao marco deve criar filho abaixo dos filhos existentes.");
        var marco = feature.Children[1];
        if (marco.TfsType != "Marco-Devops")
            throw new InvalidOperationException($"Filho de selecao DevOps deve ser Marco-Devops. Atual: {marco.TfsType}.");
        AssertEqual(feature.Id, marco.Parent?.Id ?? -1, "Marco filho deve manter o pai selecionado.");
        AssertEqual(1, marco.Level, "Marco filho deve nascer no nivel abaixo do pai.");
        AssertEqual(story.Id, marco.PredecessorIds.Single(), "Marco filho deve usar irmao anterior como predecessora.");
    }

    private static void AddMilestoneDoesNotCreateChildUnderMilestone()
    {
        var project = new Project { Name = "Teste", StartDate = new DateTime(2026, 7, 6) };
        var marco = new ProjectTask
        {
            Id = 20,
            Name = "Marco existente",
            TfsType = "Marco-Devops",
            TfsId = 1020,
            IsMilestone = true,
            Start = new DateTime(2026, 7, 6),
            Finish = new DateTime(2026, 7, 6)
        };
        project.Tasks.Add(marco);

        var vm = new MainViewModel("NXTestUnit") { Project = project };
        vm.RebuildFlatTasks();
        vm.SelectedTask = vm.FlatTasks.First(t => t.Id == marco.Id);

        vm.AddMilestone(asChild: true);

        AssertEqual(0, marco.Children.Count, "Ctrl+botao marco nao deve criar filho dentro de outro marco.");
        AssertEqual(1, project.Tasks.Count, "Bloqueio de marco filho nao deve criar atividade em outro nivel.");
        if (!vm.StatusMessage.Contains("Marco nao pode ter outro marco como filho", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Status deve explicar bloqueio de marco filho. Atual: {vm.StatusMessage}");
    }

    private static void DevOpsMilestoneCreateOpsAddsMarcoProjectTag()
    {
        var task = new ProjectTask
        {
            Id = 31,
            Name = "Marco de aceite",
            TfsType = "Marco-Devops",
            Tags = "Planejamento",
            Start = new DateTime(2026, 7, 6),
            Finish = new DateTime(2026, 7, 6),
            IsMilestone = true,
            EstimatedHours = 0
        };

        var ops = TfsImportService.BuildCreateOpsForTests(
            task,
            parentId: 1000,
            tasksById: new Dictionary<int, ProjectTask> { [task.Id] = task },
            syncPredecessorLinks: false);

        var text = string.Join("\n", ops.Select(o => o.ToString()));
        if (!text.Contains("System.Tags") || !text.Contains("MARCO-PROJECT"))
            throw new InvalidOperationException("Marco-Devops deve criar Task no DevOps com tag MARCO-PROJECT.");
    }

    private static void TfsSyncFinishUsesInclusiveDate()
    {
        var task = new ProjectTask
        {
            Name = "Atividade concluida",
            Start = new DateTime(2026, 7, 3),
            Finish = new DateTime(2026, 7, 4),
            PercentComplete = 100,
            TfsType = "Story"
        };

        var tfsFinish = TfsImportService.GetTfsFinishDateForTests(task);

        if (!tfsFinish.HasValue)
            throw new InvalidOperationException("Sync TFS deve calcular uma data fim para atividade com Finish valido.");
        AssertEqual(new DateTime(2026, 7, 3), tfsFinish.Value, "Sync TFS deve enviar a data fim inclusiva, nao o limite exclusivo interno.");
    }

    private static void TfsSyncTaskCreateOpsIncludesDescription()
    {
        var story = new ProjectTask { Id = 1, TfsId = 1000, TfsType = "User Story", Name = "Story pai" };
        var task = new ProjectTask
        {
            Id = 2,
            Name = "Task com descricao",
            TfsType = "Task",
            TfsId = 0,
            Parent = story,
            Description = "<p>Descricao vinda do Task Plan</p>",
            EstimatedHours = 8
        };
        story.Children.Add(task);

        var ops = TfsImportService.BuildCreateOpsForTests(
            task,
            parentId: story.TfsId!.Value,
            tasksById: new Dictionary<int, ProjectTask> { [story.Id] = story, [task.Id] = task },
            syncPredecessorLinks: false);
        var json = JsonSerializer.Serialize(ops);

        if (!json.Contains("System.Description", StringComparison.OrdinalIgnoreCase) ||
            !json.Contains("Descricao vinda do Task Plan", StringComparison.Ordinal))
            throw new InvalidOperationException("Task deve criar System.Description no DevOps, igual ao padrão de Story.");
    }

    private static void TfsImportTaskStateDefinesDefaultPercent()
    {
        AssertEqual(0, TfsImportService.PercentCompleteFromState("New"), "Task New importada deve ficar 0%.");
        AssertEqual(0, TfsImportService.PercentCompleteFromState("New", completedHours: 8, estimatedHours: 8),
            "Task New deve ficar 0% mesmo se o DevOps trouxer CompletedWork preenchido.");
        AssertEqual(100, TfsImportService.PercentCompleteFromState("Closed"), "Task Closed importada deve ficar 100%.");
        AssertEqual(10, TfsImportService.PercentCompleteFromState("Active"), "Task Active importada sem horas deve iniciar com 10%.");
        AssertEqual(10, TfsImportService.PercentCompleteFromState("Actived"), "Task Actived importada sem horas deve iniciar com 10%.");
        AssertEqual(50, TfsImportService.PercentCompleteFromState("Active", completedHours: 4, estimatedHours: 8),
            "Quando houver CompletedWork calculável, o percentual importado deve respeitar HH realizado/estimado.");
    }

    // Trabalho iniciado ou encerrado tem data REAL: replanejar pela fila jogaria para o futuro
    // algo que já aconteceu, abrindo buraco (gap) no período em que foi de fato executado.
    private static void ImportClosedStoryKeepsExplicitStart()
    {
        var real = new DateTime(2026, 7, 14);
        var fila = new DateTime(2026, 7, 20);

        AssertEqual(real, TfsImportService.ResolveImportStart(real, fila, false, "Closed"),
            "Story Closed com Data_Inicio deve manter a data real, nao a posicao da fila.");
        AssertEqual(real, TfsImportService.ResolveImportStart(real, fila, false, "Closed", percentComplete: 100),
            "Story Closed a 100% NAO pode ter o inicio empurrado pela fila.");
        AssertEqual(real, TfsImportService.ResolveImportStart(real, fila, false, "Done"),
            "Estado concluido equivalente (Done) tambem mantem a data real.");
        AssertEqual(real, TfsImportService.ResolveImportStart(real, fila, false, "Active"),
            "Story em andamento mantem a data real de inicio.");
        AssertEqual(real, TfsImportService.ResolveImportStart(real, fila, false, "New", percentComplete: 20),
            "Story com % de conclusao > 0 ja comecou: mantem a data real.");
        AssertEqual(real, TfsImportService.ResolveImportStart(real, fila, true, "New"),
            "Data fixada pelo usuario (tag) manda em qualquer estado.");

        AssertEqual(fila, TfsImportService.ResolveImportStart(real, fila, false, "New"),
            "Story nao iniciada (New, 0%) continua sendo planejada pela fila (pessoa/sprint).");
        AssertEqual(fila, TfsImportService.ResolveImportStart(null, fila, false, "Closed"),
            "Sem Data_Inicio no DevOps, mesmo encerrada, a Story cai na fila.");
    }

    private static void ScheduleStateEditUpdatesTaskPercent()
    {
        var task = new ProjectTask
        {
            Id = 91,
            Name = "Task editada",
            TfsType = "Task",
            PercentComplete = 0
        };
        var vm = new TaskViewModel(task);

        vm.TfsState = "Active";
        AssertEqual(10, task.PercentComplete, "Editar estado para Active no cronograma deve ajustar para 10%.");

        vm.TfsState = "Closed";
        AssertEqual(100, task.PercentComplete, "Editar estado para Closed no cronograma deve ajustar para 100%.");

        task.TfsState = "Active";
        task.PercentComplete = 25;
        vm.PercentComplete = 100;
        if (!string.Equals(task.TfsState, "Closed", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Digitar 100% em Task no cronograma deve mudar o estado para Closed.");

        task.TfsState = "Active";
        task.PercentComplete = 100;
        vm.PercentComplete = 100;
        if (!string.Equals(task.TfsState, "Closed", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Confirmar 100% em Task ja concluida deve corrigir o estado para Closed.");
    }

    private static void TfsSyncNewHierarchyUsesImmediateDevOpsParent()
    {
        var epic = new ProjectTask { Id = 1, Name = "Epic existente", TfsType = "Epic", TfsId = 1000 };
        var feature = new ProjectTask { Id = 2, Name = "Feature nova", TfsType = "Feature", TfsId = 0, Parent = epic };
        var story = new ProjectTask { Id = 3, Name = "Story nova", TfsType = "User Story", TfsId = 0, Parent = feature };
        var task = new ProjectTask { Id = 4, Name = "Task nova", TfsType = "Task", TfsId = 0, Parent = story, EstimatedHours = 8 };
        epic.Children.Add(feature);
        feature.Children.Add(story);
        story.Children.Add(task);

        var tasksById = new Dictionary<int, ProjectTask>
        {
            [epic.Id] = epic,
            [feature.Id] = feature,
            [story.Id] = story,
            [task.Id] = task
        };

        AssertEqual(1000, TfsImportService.ResolveDesiredParentForTests(feature, 999),
            "Feature nova deve ser criada sob o Epic existente, nao no Project raiz.");
        AssertCreateParent(feature, 1000, tasksById, "Feature nova deve apontar para o Epic.");

        feature.TfsId = 2000;
        AssertEqual(2000, TfsImportService.ResolveDesiredParentForTests(story, 999),
            "Story nova deve ser criada sob a Feature recem-criada.");
        AssertCreateParent(story, 2000, tasksById, "Story nova deve apontar para a Feature.");

        story.TfsId = 3000;
        AssertEqual(3000, TfsImportService.ResolveDesiredParentForTests(task, 999),
            "Task nova deve ser criada sob a Story recem-criada ou existente.");
        AssertCreateParent(task, 3000, tasksById, "Task nova deve apontar para a Story.");
    }

    private static void TfsSyncOrphanTaskDoesNotUseRootProject()
    {
        var task = new ProjectTask { Id = 10, Name = "Task sem pai", TfsType = "Task", TfsId = 0 };
        var story = new ProjectTask { Id = 11, Name = "Story sem pai", TfsType = "User Story", TfsId = 0 };
        var feature = new ProjectTask { Id = 12, Name = "Feature sem pai", TfsType = "Feature", TfsId = 0 };
        var epic = new ProjectTask { Id = 13, Name = "Epic sem pai", TfsType = "Epic", TfsId = 0 };

        AssertEqual(0, TfsImportService.ResolveDesiredParentForTests(task, 999),
            "Task sem Parent local deve ser pulada, nunca criada no Work Item Project raiz.");
        AssertEqual(0, TfsImportService.ResolveDesiredParentForTests(story, 999),
            "Story sem Parent local deve ser pulada, nunca criada no Work Item Project raiz.");
        AssertEqual(0, TfsImportService.ResolveDesiredParentForTests(feature, 999),
            "Feature sem Parent local deve ser pulada, nunca criada no Work Item Project raiz.");
        AssertEqual(999, TfsImportService.ResolveDesiredParentForTests(epic, 999),
            "Apenas Epic de topo pode ser criada sob o Work Item Project raiz.");
    }

    private static void AssertCreateParent(
        ProjectTask task,
        int expectedParentId,
        Dictionary<int, ProjectTask> tasksById,
        string message)
    {
        var ops = TfsImportService.BuildCreateOpsForTests(task, expectedParentId, tasksById, syncPredecessorLinks: false);
        var json = JsonSerializer.Serialize(ops);
        if (!json.Contains("System.LinkTypes.Hierarchy-Reverse", StringComparison.OrdinalIgnoreCase) ||
            !json.Contains($"/workItems/{expectedParentId}", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(message);
    }

    private static void DevOpsMilestoneUsesPreviousSiblingAsImplicitPredecessor()
    {
        var parent = new ProjectTask { Id = 40, TfsId = 4000, TfsType = "Feature", Name = "Feature" };
        var previous = new ProjectTask { Id = 41, TfsId = 4100, TfsType = "Story", Name = "Story anterior", Parent = parent };
        var marco = new ProjectTask
        {
            Id = 42,
            TfsType = "Marco-Devops",
            Name = "Marco",
            Parent = parent,
            IsMilestone = true,
            Start = new DateTime(2026, 7, 6),
            Finish = new DateTime(2026, 7, 6)
        };
        parent.Children.Add(previous);
        parent.Children.Add(marco);

        var ops = TfsImportService.BuildCreateOpsForTests(
            marco,
            parentId: parent.TfsId!.Value,
            tasksById: new Dictionary<int, ProjectTask>
            {
                [parent.Id] = parent,
                [previous.Id] = previous,
                [marco.Id] = marco
            });
        var json = JsonSerializer.Serialize(ops);

        AssertEqual(1, CountOccurrences(json, "System.LinkTypes.Dependency-Reverse"), "Marco-Devops deve criar uma predecessora implicita.");
        if (!json.Contains("/workItems/4100"))
            throw new InvalidOperationException("Marco-Devops deve usar o irmao anterior como predecessora implicita.");
    }

    private static void DevOpsMilestoneUsesParentAsImplicitPredecessor()
    {
        var parent = new ProjectTask { Id = 50, TfsId = 5000, TfsType = "Feature", Name = "Feature" };
        var marco = new ProjectTask
        {
            Id = 51,
            TfsType = "Marco-Devops",
            Name = "Marco inicial",
            Parent = parent,
            IsMilestone = true,
            Start = new DateTime(2026, 7, 6),
            Finish = new DateTime(2026, 7, 6)
        };
        parent.Children.Add(marco);

        var ops = TfsImportService.BuildCreateOpsForTests(
            marco,
            parentId: parent.TfsId!.Value,
            tasksById: new Dictionary<int, ProjectTask>
            {
                [parent.Id] = parent,
                [marco.Id] = marco
            });
        var json = JsonSerializer.Serialize(ops);

        AssertEqual(1, CountOccurrences(json, "System.LinkTypes.Dependency-Reverse"), "Marco-Devops sem irmao anterior deve criar predecessora implicita no pai.");
        if (!json.Contains("/workItems/5000"))
            throw new InvalidOperationException("Marco-Devops sem irmao anterior deve usar o pai como predecessora implicita.");
    }

    private static void DevOpsMilestoneResolvesExplicitPredecessorWithChildren()
    {
        var parent = new ProjectTask { Id = 100, TfsId = 1000, TfsType = "Feature", Name = "Feature" };
        var predecessor = new ProjectTask
        {
            Id = 101,
            TfsId = 1076961,
            TfsType = "Story",
            Name = "Power BI / Dashboard",
            Parent = parent
        };
        predecessor.Children.Add(new ProjectTask
        {
            Id = 102,
            TfsId = 1077000,
            TfsType = "Task",
            Name = "Atividade filha",
            Parent = predecessor
        });
        var marco = new ProjectTask
        {
            Id = 103,
            TfsType = "Marco-Devops",
            Name = "Condicao de Infraestrutura",
            Parent = parent,
            IsMilestone = true,
            Start = new DateTime(2026, 11, 11),
            Finish = new DateTime(2026, 11, 11)
        };
        marco.PredecessorIds.Add(101);
        parent.Children.Add(predecessor);
        parent.Children.Add(marco);

        var ops = TfsImportService.BuildCreateOpsForTests(
            marco,
            parentId: parent.TfsId!.Value,
            tasksById: new Dictionary<int, ProjectTask>
            {
                [parent.Id] = parent,
                [predecessor.Id] = predecessor,
                [marco.Id] = marco
            });
        var json = JsonSerializer.Serialize(ops);

        AssertEqual(1, CountOccurrences(json, "System.LinkTypes.Dependency-Reverse"), "Marco-Devops deve criar uma predecessora explicita.");
        if (!json.Contains("/workItems/1076961"))
            throw new InvalidOperationException("Marco-Devops deve resolver predecessor interno 101 para o TfsId 1076961.");
    }

    private static void DevOpsMilestonePositionIgnoresExternalHierarchyPredecessor()
    {
        var parent = new ProjectTask { Id = 110, TfsId = 1100, TfsType = "Feature", Name = "Feature" };
        var previousEarly = new ProjectTask
        {
            Id = 111,
            TfsId = 1110,
            TfsType = "Story",
            Name = "Anterior menor",
            Parent = parent,
            Start = new DateTime(2026, 7, 6),
            Finish = new DateTime(2026, 7, 7)
        };
        var previousLate = new ProjectTask
        {
            Id = 112,
            TfsId = 1120,
            TfsType = "Story",
            Name = "Anterior maior",
            Parent = parent,
            Start = new DateTime(2026, 7, 6),
            Finish = new DateTime(2026, 7, 10)
        };
        var marco = new ProjectTask
        {
            Id = 113,
            TfsId = 1130,
            TfsType = "Marco-Devops",
            Name = "Marco",
            Parent = parent,
            IsMilestone = true,
            Start = new DateTime(2026, 7, 11),
            Finish = new DateTime(2026, 7, 11)
        };

        var otherParent = new ProjectTask { Id = 120, TfsId = 1200, TfsType = "Feature", Name = "Outra Feature" };
        var outside = new ProjectTask
        {
            Id = 121,
            TfsId = 1210,
            TfsType = "Story",
            Name = "Fora da hierarquia",
            Parent = otherParent,
            Start = new DateTime(2026, 7, 6),
            Finish = new DateTime(2026, 7, 20)
        };

        marco.PredecessorIds.Add(outside.Id);
        marco.PredecessorIds.Add(previousEarly.Id);
        marco.PredecessorIds.Add(previousLate.Id);
        parent.Children.Add(previousEarly);
        parent.Children.Add(marco);
        parent.Children.Add(previousLate);
        otherParent.Children.Add(outside);

        var roots = new System.Collections.ObjectModel.ObservableCollection<ProjectTask> { parent, otherParent };
        TfsImportService.RepositionMarcosAfterPredecessorsForTests(roots);

        AssertEqual(2, parent.Children.IndexOf(marco), "Marco-Devops deve ser posicionado apos a irma predecessora que termina mais tarde.");
        AssertEqual(previousLate.Id, parent.Children[1].Id, "Predecessora fora da hierarquia nao deve influenciar a posicao do marco.");
    }

    private static void ExternalPredecessorWarnsWithoutImportError()
    {
        var parent = new ProjectTask { Id = 200, TfsId = 2000, TfsType = "Feature", Name = "Feature" };
        var marco = new ProjectTask
        {
            Id = 201,
            TfsId = 2010,
            TfsType = "Marco-Devops",
            Name = "Marco",
            Parent = parent,
            IsMilestone = true
        };
        parent.Children.Add(marco);

        var roots = new System.Collections.ObjectModel.ObservableCollection<ProjectTask> { parent };
        var external = TfsImportService.ApplyTfsPredecessorsForTests(
            roots,
            new List<(int predecessor, int successor)> { (1013393, 2010) });

        AssertEqual(1, external.Count, "Predecessora externa deve ser reportada como aviso PRED EXTERNA.");
        AssertEqual(1013393, marco.PredecessorIds.Single(), "Marco-Devops deve manter o TfsId externo da predecessora.");

        var previous = new ProjectTask
        {
            Id = 202,
            TfsId = 2020,
            TfsType = "Story",
            Name = "Irmao anterior",
            Parent = parent
        };
        var anchoredMarco = new ProjectTask
        {
            Id = 203,
            TfsId = 2030,
            TfsType = "Marco-Devops",
            Name = "Marco ancorado",
            Parent = parent,
            IsMilestone = true
        };
        parent.Children.Clear();
        parent.Children.Add(previous);
        parent.Children.Add(anchoredMarco);

        external = TfsImportService.ApplyTfsPredecessorsForTests(
            roots,
            new List<(int predecessor, int successor)> { (1013393, 2020) });

        AssertEqual(1, external.Count, "Predecessora externa no irmao anterior do Marco tambem deve ser reportada como aviso.");
        AssertEqual(1013393, previous.PredecessorIds.Single(), "Irmao anterior do Marco deve manter o TfsId externo da predecessora.");

        var report = new TfsImportService.ImportReport();
        report.LogWarning("[PRED EXTERNA] #1013393 \"Inteligência de Riscos - Fases 2 e 3\" fora de escopo (type=Ideia, state=L2 - Planejada).");
        if (report.HasIssues)
            throw new InvalidOperationException("Aviso de predecessora externa nao deve fazer o import ser tratado como erro.");
    }

    private static void DevOpsMilestonePositionUsesParentAnchor()
    {
        var parent = new ProjectTask { Id = 130, TfsId = 1300, TfsType = "Feature", Name = "Feature" };
        var first = new ProjectTask { Id = 131, TfsId = 1310, TfsType = "Story", Name = "Story", Parent = parent };
        var marco = new ProjectTask
        {
            Id = 132,
            TfsId = 1320,
            TfsType = "Marco-Devops",
            Name = "Marco inicial",
            Parent = parent,
            IsMilestone = true,
            Start = new DateTime(2026, 7, 6),
            Finish = new DateTime(2026, 7, 6)
        };

        marco.PredecessorIds.Add(parent.Id);
        parent.Children.Add(first);
        parent.Children.Add(marco);

        var roots = new System.Collections.ObjectModel.ObservableCollection<ProjectTask> { parent };
        TfsImportService.RepositionMarcosAfterPredecessorsForTests(roots);

        AssertEqual(0, parent.Children.IndexOf(marco), "Marco-Devops com predecessora no pai deve ficar no inicio dos filhos.");
    }

    private static void ImportPreservesNoDevOpsSiblingPosition()
    {
        var resource = new Resource { Id = 1, Name = "Dev", AvailabilityPercent = 100 };
        var currentParent = new ProjectTask
        {
            Id = 100,
            TfsId = 1000,
            TfsType = "Feature",
            Name = "Feature",
            IsSummary = true,
            Start = new DateTime(2026, 7, 6),
            Finish = new DateTime(2026, 7, 9)
        };
        var currentA = CreateAssignedTask(101, "Story A", resource, new DateTime(2026, 7, 6), currentParent);
        currentA.TfsId = 1001;
        currentA.TfsType = "Story";
        var localMarker = CreateAssignedTask(102, "Marco local", resource, new DateTime(2026, 7, 7), currentParent);
        localMarker.TfsId = -1;
        localMarker.TfsType = "NoDevops";
        var currentB = CreateAssignedTask(103, "Story B", resource, new DateTime(2026, 7, 8), currentParent);
        currentB.TfsId = 1002;
        currentB.TfsType = "Story";
        currentParent.Children.Add(currentA);
        currentParent.Children.Add(localMarker);
        currentParent.Children.Add(currentB);

        var currentProject = new Project
        {
            Name = "Atual",
            StartDate = new DateTime(2026, 7, 6)
        };
        currentProject.Resources.Add(resource);
        currentProject.Tasks.Add(currentParent);

        var vm = new MainViewModel("NXTestUnit") { Project = currentProject };
        vm.RebuildFlatTasks();

        var importedParent = new ProjectTask
        {
            Id = 200,
            TfsId = 1000,
            TfsType = "Feature",
            Name = "Feature",
            IsSummary = true,
            Start = new DateTime(2026, 7, 6),
            Finish = new DateTime(2026, 7, 8)
        };
        var importedA = CreateAssignedTask(201, "Story A", resource, new DateTime(2026, 7, 6), importedParent);
        importedA.TfsId = 1001;
        importedA.TfsType = "Story";
        var importedB = CreateAssignedTask(202, "Story B", resource, new DateTime(2026, 7, 6), importedParent);
        importedB.TfsId = 1002;
        importedB.TfsType = "Story";
        importedParent.Children.Add(importedA);
        importedParent.Children.Add(importedB);

        var importedProject = new Project
        {
            Name = "Importado",
            StartDate = new DateTime(2026, 7, 6)
        };
        importedProject.Resources.Add(resource);
        importedProject.Tasks.Add(importedParent);

        vm.ApplyImportedProject(importedProject);

        var restoredParent = vm.Project.Tasks.Single(t => t.TfsId == 1000);
        var names = string.Join("|", restoredParent.Children.Select(t => t.Name));
        if (names != "Story A|Marco local|Story B")
            throw new InvalidOperationException($"NoDevOps deveria voltar na mesma posicao relativa. Ordem atual: {names}.");

        AssertEqual(new DateTime(2026, 7, 7), localMarker.Start, "Marco local deve ficar depois da Story A pela predecessora virtual.");
        AssertEqual(new DateTime(2026, 7, 8), importedB.Start, "Story B deve ficar depois do NoDevOps restaurado pela predecessora virtual.");
    }

    private static void ImportMatchesOrPreservesInternalDevOpsActivities()
    {
        var resource = new Resource { Id = 1, Name = "Dev", AvailabilityPercent = 100 };
        var currentParent = new ProjectTask
        {
            Id = 300,
            TfsId = 3000,
            TfsType = "Feature",
            Name = "Feature",
            IsSummary = true,
            Start = new DateTime(2026, 7, 6),
            Finish = new DateTime(2026, 7, 9)
        };
        var currentA = CreateAssignedTask(301, "Story A", resource, new DateTime(2026, 7, 6), currentParent);
        currentA.TfsId = 3001;
        currentA.TfsType = "Story";
        var internalSameName = CreateAssignedTask(302, "Story B", resource, new DateTime(2026, 7, 7), currentParent);
        internalSameName.TfsId = 0;
        internalSameName.TfsType = "Story";
        internalSameName.IsPendingTfsCreate = true;
        var internalNew = CreateAssignedTask(303, "Story C nova", resource, new DateTime(2026, 7, 8), currentParent);
        internalNew.TfsId = 0;
        internalNew.TfsType = "Story";
        internalNew.IsPendingTfsCreate = true;
        currentParent.Children.Add(currentA);
        currentParent.Children.Add(internalNew);
        currentParent.Children.Add(internalSameName);

        var currentProject = new Project { Name = "Atual", StartDate = new DateTime(2026, 7, 6) };
        currentProject.Resources.Add(resource);
        currentProject.Tasks.Add(currentParent);

        var vm = new MainViewModel("NXTestUnit") { Project = currentProject };
        vm.RebuildFlatTasks();

        var importedParent = new ProjectTask
        {
            Id = 400,
            TfsId = 3000,
            TfsType = "Feature",
            Name = "Feature",
            IsSummary = true,
            Start = new DateTime(2026, 7, 6),
            Finish = new DateTime(2026, 7, 8)
        };
        var importedA = CreateAssignedTask(401, "Story A", resource, new DateTime(2026, 7, 6), importedParent);
        importedA.TfsId = 3001;
        importedA.TfsType = "Story";
        var importedB = CreateAssignedTask(402, "Story B", resource, new DateTime(2026, 7, 7), importedParent);
        importedB.TfsId = 3002;
        importedB.TfsType = "Story";
        importedParent.Children.Add(importedA);
        importedParent.Children.Add(importedB);

        var importedProject = new Project { Name = "Importado", StartDate = new DateTime(2026, 7, 6) };
        importedProject.Resources.Add(resource);
        importedProject.Tasks.Add(importedParent);

        vm.ApplyImportedProject(importedProject);

        var restoredParent = vm.Project.Tasks.Single(t => t.TfsId == 3000);
        var names = string.Join("|", restoredParent.Children.Select(t => t.Name));
        if (names != "Story A|Story C nova|Story B")
            throw new InvalidOperationException($"Import deve preservar Story C nova e vincular Story B por nome. Ordem atual: {names}.");

        AssertEqual(1, restoredParent.Children.Count(t => t.Name == "Story B"), "Atividade I com mesmo nome da importada nao deve duplicar.");
        var restoredB = restoredParent.Children.Single(t => t.Name == "Story B");
        AssertEqual(3002, restoredB.TfsId ?? 0, "Atividade I com mesmo nome deve virar a atividade T importada.");
        var restoredNew = restoredParent.Children.Single(t => t.Name == "Story C nova");
        if (restoredNew.HasTfsLink)
            throw new InvalidOperationException("Atividade DevOps nova sem match no TFS deve continuar interna.");
    }

    private static void XmlRoundTripPreservesResourceAvailabilityPercent()
    {
        var resource = new Resource { Id = 1, Name = "Dev Meio Periodo", AvailabilityPercent = 62.5 };
        var task = new ProjectTask
        {
            Id = 10,
            Name = "Tarefa",
            Start = new DateTime(2026, 7, 6),
            Finish = new DateTime(2026, 7, 7),
            EstimatedHours = 8
        };
        task.Resources.Add(new TaskResource { Resource = resource, ResourceId = resource.Id, AllocationPercent = 100, EstimatedHours = 8 });

        var project = new Project { Name = "RoundTrip", StartDate = new DateTime(2026, 7, 6) };
        project.Resources.Add(resource);
        project.Tasks.Add(task);

        var tempFile = Path.Combine(Path.GetTempPath(), $"nxtest_roundtrip_{Guid.NewGuid():N}.xml");
        try
        {
            XmlProjectService.Save(project, tempFile);
            var loaded = XmlProjectService.Load(tempFile);

            var loadedResource = loaded.Resources.Single(r => r.Id == resource.Id);
            AssertEqual(62.5, loadedResource.AvailabilityPercent,
                "AvailabilityPercent do recurso deve sobreviver ao salvar e reabrir o arquivo (nao voltar para o padrao 100).");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // Regressao do bug "32h viram meses": recalcular a alocacao repetidamente inflava a
    // duracao porque o span de calendario era re-semeado como horas de trabalho e re-dividido
    // pelo fator a cada edicao. Deve ser idempotente.
    private static void ResourceAllocationRecalcDoesNotCompoundFinish()
    {
        // Cenario com HH explicito: recalcular repetidamente deve ser idempotente.
        var r1 = new Resource { Id = 1, Name = "Dev", AvailabilityPercent = 50 };
        var withHours = new ProjectTask { Id = 1, Name = "Tarefa 32h", Start = new DateTime(2026, 7, 6), EstimatedHours = 32 };
        withHours.Resources.Add(new TaskResource { Resource = r1, ResourceId = 1, AllocationPercent = 50, EstimatedHours = 32 });
        var p1 = new Project { Name = "P", StartDate = new DateTime(2026, 7, 6) };
        p1.Resources.Add(r1); p1.Tasks.Add(withHours);
        var vm1 = new MainViewModel("NXTestUnit") { Project = p1 };
        vm1.RebuildFlatTasks();
        var tvm1 = vm1.FlatTasks.First(t => t.Id == 1);
        tvm1.RecalcFinishFromPercAloc();
        var finishA = withHours.Finish;
        tvm1.RecalcFinishFromPercAloc();
        tvm1.RecalcFinishFromPercAloc();
        AssertEqual(finishA, withHours.Finish, "Recalcular alocacao varias vezes NAO deve mudar o fim (compounding).");
        if (withHours.Finish > new DateTime(2026, 8, 15))
            throw new InvalidOperationException($"32h a 50%/50% deveria terminar em ~julho/2026, mas terminou em {withHours.Finish:yyyy-MM-dd}.");

        // Cenario que disparava o bug: tarefa SEM HH explicito, so com um span de calendario.
        // O codigo antigo semeava EstimatedHours com o span e re-dividia pelo fator a cada
        // recalculo, inflando sem parar. Deve permanecer estavel.
        var r2 = new Resource { Id = 1, Name = "Dev", AvailabilityPercent = 50 };
        var noHours = new ProjectTask
        {
            Id = 1, Name = "Tarefa sem HH",
            Start = new DateTime(2026, 7, 6),
            Finish = new DateTime(2026, 7, 10) // span de ~4 dias uteis, sem EstimatedHours
        };
        noHours.Resources.Add(new TaskResource { Resource = r2, ResourceId = 1, AllocationPercent = 50 });
        var p2 = new Project { Name = "P2", StartDate = new DateTime(2026, 7, 6) };
        p2.Resources.Add(r2); p2.Tasks.Add(noHours);
        var vm2 = new MainViewModel("NXTestUnit") { Project = p2 };
        vm2.RebuildFlatTasks();
        var tvm2 = vm2.FlatTasks.First(t => t.Id == 1);
        tvm2.RecalcFinishFromPercAloc();
        var finishB = noHours.Finish;
        tvm2.RecalcFinishFromPercAloc();
        tvm2.RecalcFinishFromPercAloc();
        tvm2.RecalcFinishFromPercAloc();
        AssertEqual(finishB, noHours.Finish, "Tarefa sem HH: recalcular alocacao varias vezes NAO deve inflar o fim (compounding).");
        if (noHours.Finish > new DateTime(2026, 9, 1))
            throw new InvalidOperationException($"Tarefa sem HH (span ~4 dias) inflou para {noHours.Finish:yyyy-MM-dd} apos varios recalculos.");
    }

    // Regressao direta do double-count: GetEffectiveDurationHours (restante) tinha um
    // fallback que retornava task.DurationHours (= HH Atual + HH Restante). Numa tarefa
    // SO com HH Atual (restante = 0), esse fallback devolvia o HH Atual como se fosse
    // restante, e o mesmo HH Atual era contado de novo em GetEffectiveCurrentDurationHours.
    private static void EffectiveDurationDoesNotDoubleCountCurrentOnlyHours()
    {
        var resource = new Resource { Id = 1, Name = "Dev", AvailabilityPercent = 100 };
        var task = new ProjectTask
        {
            Id = 1, Name = "So HH Atual",
            Start = new DateTime(2026, 7, 6),
            EstimatedHours = 0,   // sem HH Restante
            CurrentHours = 24,    // 24h de HH Atual
            PercentComplete = 40
        };
        task.Resources.Add(new TaskResource { Resource = resource, ResourceId = 1, AllocationPercent = 100, EstimatedHours = 0 });

        // Restante = 0 (nao ha HH Restante).
        AssertEqual(0.0, TaskScheduleService.GetEffectiveDurationHours(task),
            "Sem HH Restante, a duracao de trabalho restante deve ser 0 (nao o span nem o HH Atual).");

        // Atual = 24h a 100% de fator = 24h de calendario.
        AssertEqual(24.0, TaskScheduleService.GetEffectiveCurrentDurationHours(task),
            "HH Atual deve ser contado uma unica vez em GetEffectiveCurrentDurationHours.");

        // Total = 24h (nao 48h). Se contasse em dobro, daria 48h.
        AssertEqual(24.0, TaskScheduleService.GetEffectiveTotalDurationHours(task),
            "Tarefa so com HH Atual: total deve ser 24h (contagem unica), nunca 48h (dobro).");
    }

    // O fim calculado deve ser IDENTICO entre o cronograma (edicao), o import TFS e a
    // abertura de arquivo — todos usam TaskScheduleService.CalculateFinishFromAssignments.
    // Cobre os tres cenarios de horas: so HH Restante, so HH Atual, e HH Atual + HH Restante.
    private static void CentralizedFinishCalcIsIdenticalAcrossPaths()
    {
        AssertCentralizedFinishConsistent("so HH Restante",       remaining: 24, current: null);
        AssertCentralizedFinishConsistent("so HH Atual",          remaining: 0,  current: 24);
        AssertCentralizedFinishConsistent("HH Atual + Restante",  remaining: 24, current: 8);
    }

    private static void AssertCentralizedFinishConsistent(string caseName, double remaining, double? current)
    {
        ProjectTask NewTask()
        {
            var resource = new Resource { Id = 1, Name = "Dev", AvailabilityPercent = 50 };
            var t = new ProjectTask
            {
                Id = 1, Name = "T",
                Start = new DateTime(2026, 7, 6),
                EstimatedHours = remaining,
                CurrentHours = current,
                PercentComplete = current is > 0 ? 40 : 0
            };
            t.Resources.Add(new TaskResource { Resource = resource, ResourceId = 1, AllocationPercent = 50, EstimatedHours = remaining });
            return t;
        }

        // Caminho 1: cálculo direto (o que o import TFS e a abertura de arquivo usam)
        var t1 = NewTask();
        var finishDirect = TaskScheduleService.CalculateFinishFromAssignments(t1, t1.Start);

        // Caminho 2: edição no cronograma (RecalcFinishFromPercAloc)
        var t2 = NewTask();
        var project = new Project { Name = "P", StartDate = new DateTime(2026, 7, 6) };
        project.Resources.Add(t2.Resources[0].Resource!);
        project.Tasks.Add(t2);
        var vm = new MainViewModel("NXTestUnit") { Project = project };
        vm.RebuildFlatTasks();
        vm.FlatTasks.First(t => t.Id == 1).RecalcFinishFromPercAloc();
        var finishGrid = t2.Finish;

        AssertEqual(finishDirect, finishGrid,
            $"[{caseName}] O fim do cronograma deve ser identico ao do import/abertura (mesma classe central).");

        // Sanidade: o fator de alocacao/disponibilidade tem que estender a duracao.
        // remaining/(0.5x0.5) + current/(0.5x0.5) horas de trabalho -> calendario.
        var expectedCalendarHours = (remaining + (current ?? 0)) / 0.25;
        var expectedFinish = ProjectCalendarService.AddWorkingHours(t1.Start, expectedCalendarHours);
        AssertEqual(expectedFinish, finishDirect,
            $"[{caseName}] HH (Atual+Restante) deve passar pelo fator alocacao x disponibilidade.");
    }

    private static void ImportPreservesExistingResourceAvailabilityPercent()
    {
        // Recurso configurado com 50% de disponibilidade no projeto ATUAL (em memoria).
        var currentResource = new Resource { Id = 1, Name = "Dev", AvailabilityPercent = 50 };
        var currentTask = CreateAssignedTask(1, "Story A", currentResource, new DateTime(2026, 7, 6), null!);
        currentTask.TfsId = 1001;
        currentTask.TfsType = "Story";

        var currentProject = new Project { Name = "Atual", StartDate = new DateTime(2026, 7, 6) };
        currentProject.Resources.Add(currentResource);
        currentProject.Tasks.Add(currentTask);

        var vm = new MainViewModel("NXTestUnit") { Project = currentProject };
        vm.RebuildFlatTasks();

        // Reimport do TFS traz o MESMO recurso (mesmo nome/chave) com o padrao 100%
        // (como normalmente vem de uma importacao fresca, sem configuracao manual).
        var importedResource = new Resource { Id = 1, Name = "Dev", AvailabilityPercent = 100 };
        var importedTask = CreateAssignedTask(2, "Story A", importedResource, new DateTime(2026, 7, 6), null!);
        importedTask.TfsId = 1001;
        importedTask.TfsType = "Story";

        var importedProject = new Project { Name = "Importado", StartDate = new DateTime(2026, 7, 6) };
        importedProject.Resources.Add(importedResource);
        importedProject.Tasks.Add(importedTask);

        vm.ApplyImportedProject(importedProject);

        var restoredResource = vm.Project.Resources.Single(r => r.Name == "Dev");
        AssertEqual(50, restoredResource.AvailabilityPercent,
            "Reimportar do TFS nao deve sobrescrever o AvailabilityPercent ja configurado no projeto atual com o padrao 100% do import.");
    }

    private static void TaskAllocationSummaryFromDevOps()
    {
        var tasks = new List<TfsImportService.DevOpsTaskInfo>
        {
            // Closed → usa Completed (5), ignora Estimate (8).
            new() { TfsId = 1, State = "Closed", EstimatedHours = 8, CompletedHours = 5, AssignedToDisplay = "Maria" },
            // Aberta → usa Estimate (3).
            new() { TfsId = 2, State = "Active", EstimatedHours = 3, CompletedHours = 1, AssignedToDisplay = "Joao" },
            // Mesma pessoa acumula (Maria +2 estimate aberta).
            new() { TfsId = 3, State = "New", EstimatedHours = 2, CompletedHours = 0, AssignedToDisplay = "Maria" },
            // Sem responsável → ignorada.
            new() { TfsId = 4, State = "Active", EstimatedHours = 4, CompletedHours = 0, AssignedToDisplay = "" },
            // Zero horas → ignorada.
            new() { TfsId = 5, State = "Active", EstimatedHours = 0, CompletedHours = 0, AssignedToDisplay = "Ana" },
        };

        var summary = TfsImportService.BuildTaskAllocationSummary(tasks);
        double HoursOf(string r) => summary.Where(a => a.Resource == r).Sum(a => a.Hours);

        if (Math.Abs(HoursOf("Maria") - 7) > 0.0001)   // 5 (closed) + 2 (aberta)
            throw new InvalidOperationException($"Maria deveria ter 7h, veio {HoursOf("Maria")}.");
        if (Math.Abs(HoursOf("Joao") - 3) > 0.0001)
            throw new InvalidOperationException($"Joao deveria ter 3h, veio {HoursOf("Joao")}.");
        if (summary.Any(a => a.Resource == "Ana"))
            throw new InvalidOperationException("Ana (0h) não deveria entrar no resumo.");
        // Agrupa por recurso + estado: Maria vira 2 entradas (Closed 5h + New 2h), Joao 1 (Active).
        if (summary.Count != 3)
            throw new InvalidOperationException($"Resumo deveria ter 3 entradas (recurso+estado), veio {summary.Count}.");
        int TasksOf(string r) => summary.Where(a => a.Resource == r).Sum(a => a.Tasks);
        if (TasksOf("Maria") != 2)   // 2 tasks (1 closed + 1 aberta) contam para Maria
            throw new InvalidOperationException($"Maria deveria ter 2 tasks, veio {TasksOf("Maria")}.");
        if (TasksOf("Joao") != 1)
            throw new InvalidOperationException($"Joao deveria ter 1 task, veio {TasksOf("Joao")}.");
        double MariaState(string s) => summary.Where(a => a.Resource == "Maria" && a.State == s).Sum(a => a.Hours);
        if (Math.Abs(MariaState("Closed") - 5) > 0.0001 || Math.Abs(MariaState("New") - 2) > 0.0001)
            throw new InvalidOperationException("Estado das tasks de Maria (Closed=5, New=2) não bateu.");
        if (summary.First(a => a.Resource == "Joao").State != "Active")
            throw new InvalidOperationException("Joao deveria estar como Active.");
    }

    private static void TaskAllocationSummaryRoundTrips()
    {
        var project = new Project { Name = "Alloc", StartDate = new DateTime(2026, 6, 1) };
        var story = new ProjectTask { Id = 10, TfsId = 1015217, TfsType = "User Story", Name = "Story A" };
        story.TaskAllocations.Add(new TaskAllocationSummary { Resource = "Maria", Hours = 7, Tasks = 2, State = "Closed" });
        story.TaskAllocations.Add(new TaskAllocationSummary { Resource = "Joao", Hours = 3, Tasks = 1, State = "Active" });
        project.Tasks.Add(story);

        var path = Path.Combine(Path.GetTempPath(), $"nx-alloc-{Guid.NewGuid():N}.nxp");
        try
        {
            XmlProjectService.Save(project, path);
            var loaded = XmlProjectService.Load(path);
            var s = loaded.Tasks.First(t => t.TfsId == 1015217);
            if (s.TaskAllocations.Count != 2)
                throw new InvalidOperationException($"Esperados 2 resumos, veio {s.TaskAllocations.Count}.");
            var maria = s.TaskAllocations.FirstOrDefault(a => a.Resource == "Maria");
            if (maria == null || Math.Abs(maria.Hours - 7) > 0.0001)
                throw new InvalidOperationException("Resumo de Maria (7h) não sobreviveu ao salvar/abrir.");
            if (maria.Tasks != 2)
                throw new InvalidOperationException($"Qtd de tasks de Maria (2) não sobreviveu ao salvar/abrir, veio {maria.Tasks}.");
            if (maria.State != "Closed")
                throw new InvalidOperationException($"Estado de Maria (Closed) não sobreviveu ao salvar/abrir, veio '{maria.State}'.");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static void AllocationStoryDecompositionFactors()
    {
        static void Near(double actual, double expected, string what)
        {
            if (Math.Abs(actual - expected) > 0.0001)
                throw new InvalidOperationException($"{what}: esperado {expected:0.####}, veio {actual:0.####}.");
        }

        // Story 40h, tasks somam 22h (cabem): responsavel fica com 18/40; tasks inteiras.
        Near(NXProject.Services.TaskScheduleService.StoryResponsibleFactor(40, 22), 18.0 / 40.0, "restante responsavel (cabe)");
        Near(NXProject.Services.TaskScheduleService.StoryTaskCutFactor(40, 22), 1.0, "corte task (cabe)");

        // Tasks somam 50h (estouram 40): responsavel 0; tasks cortadas por 40/50.
        Near(NXProject.Services.TaskScheduleService.StoryResponsibleFactor(40, 50), 0.0, "restante responsavel (estoura)");
        Near(NXProject.Services.TaskScheduleService.StoryTaskCutFactor(40, 50), 40.0 / 50.0, "corte task (estoura)");

        // Sem tasks: responsavel fica com a Story inteira.
        Near(NXProject.Services.TaskScheduleService.StoryResponsibleFactor(40, 0), 1.0, "restante sem tasks");

        // Uma unica task maior que a Story: cortada para caber (trava).
        Near(NXProject.Services.TaskScheduleService.StoryTaskCutFactor(40, 60), 40.0 / 60.0, "trava task > story");

        // Story sem estimativa (0): nao capa nada.
        Near(NXProject.Services.TaskScheduleService.StoryResponsibleFactor(0, 10), 1.0, "story sem estimativa (resp)");
        Near(NXProject.Services.TaskScheduleService.StoryTaskCutFactor(0, 10), 1.0, "story sem estimativa (task)");

        // Total fecha: responsavel + tasks(cortadas) = HH da Story.
        double storyHours = 40, taskSum = 50;
        double respHours = storyHours * NXProject.Services.TaskScheduleService.StoryResponsibleFactor(storyHours, taskSum);
        double tasksHours = taskSum * NXProject.Services.TaskScheduleService.StoryTaskCutFactor(storyHours, taskSum);
        Near(respHours + tasksHours, storyHours, "total = HH da Story (estoura)");

        storyHours = 40; taskSum = 22;
        respHours = storyHours * NXProject.Services.TaskScheduleService.StoryResponsibleFactor(storyHours, taskSum);
        tasksHours = taskSum * NXProject.Services.TaskScheduleService.StoryTaskCutFactor(storyHours, taskSum);
        Near(respHours + tasksHours, storyHours, "total = HH da Story (cabe)");
    }

    private static void SummaryRollupUsesChildrenDatesAndHours()
    {
        var summary = new ProjectTask
        {
            Name = "Feature",
            IsSummary = true
        };
        var first = new ProjectTask
        {
            Name = "A",
            Parent = summary,
            Start = new DateTime(2026, 7, 6),
            Finish = new DateTime(2026, 7, 7),
            CurrentHours = 2,
            EstimatedHours = 6
        };
        var second = new ProjectTask
        {
            Name = "B",
            Parent = summary,
            Start = new DateTime(2026, 7, 7),
            Finish = new DateTime(2026, 7, 9),
            CurrentHours = 4,
            EstimatedHours = 4
        };
        summary.Children.Add(first);
        summary.Children.Add(second);

        summary.RecalcSummary();

        AssertEqual(new DateTime(2026, 7, 6), summary.Start, "Resumo deve iniciar no menor inicio dos filhos.");
        AssertEqual(new DateTime(2026, 7, 9), summary.Finish, "Resumo deve terminar no maior fim exclusivo dos filhos.");
        AssertEqual(37.5, summary.PercentComplete, "Percentual do resumo deve usar HH Atual / (HH Atual + HH Restante).");
    }

    // Caso real (A2026 - Inteligência de Riscos): Story em 100% com HH Restante lançado
    // deixava a Feature em 94% (43,2 de 48h). O restante deve ser ABSORVIDO pelo HH Atual
    // (antecipação/esforço extra) sem mudar a duração total.
    private static void CompletedTaskAbsorbsRemainingHours()
    {
        SetCurrentCalendar(new ProjectCalendar());
        var feature = new ProjectTask
        {
            Id = 1, Name = "Ajuste Power BI BSC Fornecedor - Desenvolvimento",
            TfsType = "Feature", IsSummary = true,
            Start = new DateTime(2026, 7, 6), Finish = new DateTime(2026, 7, 10)
        };
        ProjectTask Story(int id, string name, double cur, double est, double pct)
        {
            var s = new ProjectTask
            {
                Id = id, Name = name, TfsType = "User Story", Parent = feature,
                Start = new DateTime(2026, 7, 6), Finish = new DateTime(2026, 7, 10),
                CurrentHours = cur, EstimatedHours = est, OriginalEstimatedHours = cur + est,
                PercentComplete = pct
            };
            feature.Children.Add(s);
            return s;
        }
        Story(2, "Implementar Ingestao", 24, 0, 100);
        var early = Story(3, "Realizar implementacao Power BI", 43.2, 4.8, 100); // 100% com restante
        Story(4, "Realizar Homologacao", 8, 0, 100);
        Story(5, "Atualizar Documentacao", 8, 0, 100);

        var project = new Project { Name = "Riscos", StartDate = new DateTime(2026, 7, 6) };
        project.Tasks.Add(feature);
        var vm = new MainViewModel("NXTestUnit") { Project = project };

        // Invariante: duração TOTAL (HH Atual + HH Restante) não pode mudar com a absorção —
        // só a repartição entre os dois campos muda.
        var durationBefore = TaskScheduleService.GetEffectiveTotalDurationHours(early);
        vm.RebuildFlatTasks();

        AssertEqual(48, early.CurrentHours ?? -1, "HH Restante de atividade 100% deve ser somado ao HH Atual.");
        AssertEqual(0, early.EstimatedHours ?? -1, "HH Restante deve ficar zerado apos a absorcao.");
        AssertEqual(durationBefore, TaskScheduleService.GetEffectiveTotalDurationHours(early),
            "A absorcao NAO pode mudar a duracao total da atividade.");
        AssertEqual(100, feature.PercentComplete, "Feature com todas as filhas 100% deve fechar em 100%.");

        // Atividade em andamento (< 100%) continua com o restante intacto.
        var running = Story(6, "Em andamento", 5, 5, 50);
        vm.RebuildFlatTasks();
        AssertEqual(5, running.CurrentHours ?? -1, "Atividade abaixo de 100% mantem o HH Atual.");
        AssertEqual(5, running.EstimatedHours ?? -1, "Atividade abaixo de 100% mantem o HH Restante.");
    }

    private static void VirtualPredecessorQueuesSameResourceSiblings()
    {
        var resource = new Resource { Id = 1, Name = "Dev", AvailabilityPercent = 100 };
        var (vm, first, second, third) = CreateVirtualPredecessorScenario(resource);

        vm.ApplyVirtualPredecessorsToAll();

        AssertEqual(new DateTime(2026, 7, 6), first.Start, "A primeira tarefa deve manter o inicio original.");
        AssertEqual(new DateTime(2026, 7, 7), first.Finish, "A primeira tarefa de 8h deve terminar no limite exclusivo do dia seguinte.");
        AssertEqual(new DateTime(2026, 7, 7), second.Start, "A segunda tarefa deve iniciar no proximo dia util apos o fim visivel da primeira.");
        AssertEqual(new DateTime(2026, 7, 8), second.Finish, "A segunda tarefa deve ter o fim recalculado a partir do novo inicio.");
        AssertEqual(new DateTime(2026, 7, 8), third.Start, "A terceira tarefa deve encadear apos a segunda.");
        AssertEqual(new DateTime(2026, 7, 9), third.Finish, "A terceira tarefa deve ter o fim recalculado a partir do novo inicio.");
    }

    private static void VirtualPredecessorDurationChangeCascadesFinish()
    {
        var resource = new Resource { Id = 1, Name = "Dev", AvailabilityPercent = 100 };
        var (vm, first, second, third) = CreateVirtualPredecessorScenario(resource);
        vm.ApplyVirtualPredecessorsToAll();

        var firstVm = vm.FlatTasks.First(t => ReferenceEquals(t.Model, first));
        firstVm.DurationHours = 16;

        AssertEqual(new DateTime(2026, 7, 8), first.Finish, "Ao aumentar a primeira para 16h, seu fim exclusivo deve mudar.");
        AssertEqual(new DateTime(2026, 7, 8), second.Start, "A segunda deve ser empurrada pela predecessora virtual recalculada.");
        AssertEqual(new DateTime(2026, 7, 9), second.Finish, "A segunda deve recalcular o fim a partir do novo inicio.");
        AssertEqual(new DateTime(2026, 7, 9), third.Start, "A terceira deve acompanhar a cascata da segunda.");
        AssertEqual(new DateTime(2026, 7, 10), third.Finish, "A terceira deve recalcular o fim a partir do novo inicio.");
    }

    private static void VirtualPredecessorRecalcMovesStartedSibling()
    {
        var resource = new Resource { Id = 1, Name = "Dev", AvailabilityPercent = 100 };
        var (vm, first, second, _) = CreateVirtualPredecessorScenario(resource);

        second.Start = new DateTime(2026, 7, 24);
        second.Finish = new DateTime(2026, 7, 25);
        second.CurrentHours = 4;
        second.EstimatedHours = 4;
        second.PercentComplete = 50;
        second.Resources[0].EstimatedHours = 4;

        vm.ApplyVirtualPredecessorsToAll();

        AssertEqual(new DateTime(2026, 7, 6), first.Start, "A primeira tarefa deve manter o inicio original.");
        AssertEqual(new DateTime(2026, 7, 7), second.Start, "O recalculo geral deve reposicionar a tarefa iniciada pela predecessora virtual.");
        AssertEqual(new DateTime(2026, 7, 8), second.Finish, "O fim deve ser recalculado a partir do novo inicio da tarefa iniciada.");
    }

    private static void ExplicitPredecessorRecalcMovesStartedTask()
    {
        var resource = new Resource { Id = 1, Name = "Dev", AvailabilityPercent = 100 };
        var (vm, first, second, _) = CreateVirtualPredecessorScenario(resource);

        second.PredecessorIds.Add(first.Id);
        second.Start = new DateTime(2026, 7, 24);
        second.Finish = new DateTime(2026, 7, 25);
        second.CurrentHours = 4;
        second.EstimatedHours = 4;
        second.PercentComplete = 50;
        second.Resources[0].EstimatedHours = 4;

        vm.ApplyVirtualPredecessorsToAll();

        AssertEqual(new DateTime(2026, 7, 7), second.Start, "O recalculo geral deve reposicionar a tarefa iniciada pela predecessora explicita.");
        AssertEqual(new DateTime(2026, 7, 8), second.Finish, "O fim da tarefa com predecessora explicita deve acompanhar o novo inicio.");
    }

    private static void DurationEditUsesPreviousSiblingEvenWhenPreviousHasExplicitPredecessor()
    {
        var resource = new Resource { Id = 1, Name = "Dev", AvailabilityPercent = 100 };
        var (vm, first, second, third) = CreateVirtualPredecessorScenario(resource);

        second.PredecessorIds.Add(first.Id);
        vm.ApplyVirtualPredecessorsToAll();

        third.Start = new DateTime(2026, 7, 24);
        third.Finish = new DateTime(2026, 7, 25);
        third.CurrentHours = 4;
        third.EstimatedHours = 4;
        third.PercentComplete = 50;
        third.Resources[0].EstimatedHours = 4;

        var thirdVm = vm.FlatTasks.First(t => ReferenceEquals(t.Model, third));
        thirdVm.DurationHours = 8;

        AssertEqual(new DateTime(2026, 7, 8), third.Start, "Ao digitar HH, a tarefa deve iniciar apos o fim da anterior do mesmo recurso.");
        AssertEqual(new DateTime(2026, 7, 9), third.Finish, "O fim deve ser recalculado a partir do inicio reposicionado.");
    }

    private static (MainViewModel Vm, ProjectTask First, ProjectTask Second, ProjectTask Third) CreateVirtualPredecessorScenario(Resource resource)
    {
        var parent = new ProjectTask
        {
            Id = 10,
            Name = "Story",
            IsSummary = true,
            Start = new DateTime(2026, 7, 6),
            Finish = new DateTime(2026, 7, 7)
        };
        var first = CreateAssignedTask(1, "A", resource, new DateTime(2026, 7, 6), parent);
        var second = CreateAssignedTask(2, "B", resource, new DateTime(2026, 7, 6), parent);
        var third = CreateAssignedTask(3, "C", resource, new DateTime(2026, 7, 6), parent);
        parent.Children.Add(first);
        parent.Children.Add(second);
        parent.Children.Add(third);

        var project = new Project
        {
            Name = "Teste predecessor virtual",
            StartDate = new DateTime(2026, 7, 6)
        };
        project.Resources.Add(resource);
        project.Tasks.Add(parent);

        var vm = new MainViewModel("NXTestUnit")
        {
            Project = project
        };
        vm.RebuildFlatTasks();

        return (vm, first, second, third);
    }

    private static ProjectTask CreateAssignedTask(int id, string name, Resource resource, DateTime start, ProjectTask parent)
    {
        var task = new ProjectTask
        {
            Id = id,
            Name = name,
            Parent = parent,
            Start = start,
            Finish = start.AddDays(1),
            EstimatedHours = 8
        };
        task.Resources.Add(new TaskResource
        {
            Resource = resource,
            ResourceId = resource.Id,
            AllocationPercent = 100,
            EstimatedHours = 8
        });
        return task;
    }

    private static void AssertAllocationFinish(double allocationPercent, double estimatedHours, DateTime expectedFinish, string message)
    {
        var resource = new Resource { Id = 1, Name = $"Dev {allocationPercent:0}%", AvailabilityPercent = 100 };
        var task = new ProjectTask
        {
            Name = $"Task {estimatedHours:0.#}h a {allocationPercent:0}%",
            Start = new DateTime(2026, 7, 6)
        };
        task.Resources.Add(new TaskResource
        {
            Resource = resource,
            ResourceId = resource.Id,
            AllocationPercent = allocationPercent,
            EstimatedHours = estimatedHours
        });

        var finish = TaskScheduleService.CalculateFinishFromAssignments(task, task.Start);

        AssertEqual(expectedFinish, finish, message);
    }

    private static int CountOccurrences(string value, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }

    private static void ConfigurePredecessorLookups(TaskViewModel target, params TaskViewModel[] tasks)
    {
        var byInternalId = tasks.ToDictionary(t => t.Model.Id);
        target.FindByInternalId = id => byInternalId.TryGetValue(id, out var vm) ? vm : null;
        target.FindByDisplayId = displayId =>
        {
            if (displayId.StartsWith("I:", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(displayId[2..], out var internalId) &&
                byInternalId.ContainsKey(internalId))
                return internalId;

            if (displayId.StartsWith("T:", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(displayId[2..], out var tfsId))
            {
                var match = tasks.FirstOrDefault(t => t.Model.TfsId == tfsId);
                return match?.Model.Id;
            }

            if (displayId.EndsWith(":I", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(displayId[..^2], out var gridInternalId) &&
                byInternalId.ContainsKey(gridInternalId))
                return gridInternalId;

            if (displayId.EndsWith(":T", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(displayId[..^2], out var gridTfsId))
            {
                var match = tasks.FirstOrDefault(t => t.Model.TfsId == gridTfsId);
                return match?.Model.Id;
            }

            if (int.TryParse(displayId, out var rawId) && byInternalId.ContainsKey(rawId))
                return rawId;

            return null;
        };
    }

    private static void SetupUpdateNoBaselineDoesNotTrigger()
    {
        var remote = DateTimeOffset.UtcNow;
        var result = UpdateService.ShouldTriggerSetupUpdate(null, remote);
        if (result)
            throw new InvalidOperationException("Sem baseline conhecida, nao deveria disparar reinstalacao (evita falso positivo).");
    }

    private static void SetupUpdateSameTimestampDoesNotTrigger()
    {
        var t = DateTimeOffset.UtcNow;
        var result = UpdateService.ShouldTriggerSetupUpdate(t, t);
        if (result)
            throw new InvalidOperationException("Asset com o mesmo timestamp da baseline nao deveria disparar reinstalacao.");
    }

    private static void SetupUpdateOlderAssetDoesNotTrigger()
    {
        var known = DateTimeOffset.UtcNow;
        var remote = known.AddHours(-1);
        var result = UpdateService.ShouldTriggerSetupUpdate(known, remote);
        if (result)
            throw new InvalidOperationException("Asset mais antigo que a baseline nao deveria disparar reinstalacao.");
    }

    private static void SetupUpdateNewerAssetTriggers()
    {
        var known = DateTimeOffset.UtcNow;
        var remote = known.AddHours(1);
        var result = UpdateService.ShouldTriggerSetupUpdate(known, remote);
        if (!result)
            throw new InvalidOperationException("Asset mais novo que a baseline deveria disparar reinstalacao.");
    }

    // ── Sincronização: regra "fechamento vence" no conflito de versão ──────────────
    private static void SyncConflictCurrentUserOpenStateReleases()
    {
        // Última gravação foi do usuário atual e TFS ainda aberto → libera.
        if (!TfsImportService.ShouldReleaseSyncConflict(isCurrentSyncUser: true, localPercentComplete: 40, tfsState: "Active"))
            throw new InvalidOperationException("Conflito com última gravação do usuário atual e TFS aberto deveria liberar.");
    }

    private static void SyncConflictLocal100OtherUserBlocks()
    {
        // Se a versao do TFS esta a frente e foi outro usuario, 100% local nao libera no Sync geral.
        if (TfsImportService.ShouldReleaseSyncConflict(isCurrentSyncUser: false, localPercentComplete: 100, tfsState: "Active"))
            throw new InvalidOperationException("NXProject 100% com TFS a frente deve registrar conflito no Sync geral.");
    }

    private static void SyncConflictClosedOtherUserBlocks()
    {
        // NXProject 100% e TFS ja Closed tambem nao libera se a versao do TFS esta a frente.
        if (TfsImportService.ShouldReleaseSyncConflict(isCurrentSyncUser: false, localPercentComplete: 100, tfsState: "Closed"))
            throw new InvalidOperationException("Com TFS 100%/Closed e versao a frente, o conflito deve ficar rosa para resolucao manual.");
    }

    private static void SyncConflictCurrentUserClosedStateBlocks()
    {
        // Mesmo usuario: se o TFS ja esta Closed/100% e a versao esta a frente, o Sync geral nao deve reabrir/sobrescrever.
        if (TfsImportService.ShouldReleaseSyncConflict(isCurrentSyncUser: true, localPercentComplete: 40, tfsState: "Closed"))
            throw new InvalidOperationException("Mesmo usuario com TFS Closed e versao a frente deve registrar conflito para resolucao manual.");
    }

    private static void SyncConflictBelow100OtherUserBlocks()
    {
        // NXProject abaixo de 100% e gravação de outro usuário → conflito real, bloqueia.
        if (TfsImportService.ShouldReleaseSyncConflict(isCurrentSyncUser: false, localPercentComplete: 60, tfsState: "Active"))
            throw new InvalidOperationException("NXProject abaixo de 100% com outro usuário NÃO deveria liberar (conflito real).");
    }

    private static void SyncConflictVersionAheadWithoutPendingWritesDoesNotBlock()
    {
        if (TfsImportService.ShouldRegisterSyncConflict(
                tfsVersionAhead: true,
                hasPendingWrites: false,
                isStoryOrTask: true,
                isCurrentSyncUser: false,
                tfsState: "Active"))
            throw new InvalidOperationException("Versao TFS a frente sem atributo diferente a gravar nao deve registrar conflito.");
    }

    private static void SyncConflictVersionAheadFeatureEpicDoesNotBlockRollup()
    {
        if (TfsImportService.ShouldRegisterSyncConflict(
                tfsVersionAhead: true,
                hasPendingWrites: true,
                isStoryOrTask: false,
                isCurrentSyncUser: false,
                tfsState: "Active"))
            throw new InvalidOperationException("Feature/Epic com rollup a gravar nao deve bloquear pela regra de conflito de Story/Task.");

        if (!TfsImportService.ShouldRegisterSyncConflict(
                tfsVersionAhead: true,
                hasPendingWrites: true,
                isStoryOrTask: true,
                isCurrentSyncUser: false,
                tfsState: "Active"))
            throw new InvalidOperationException("Story/Task com versao a frente e atributo diferente deve registrar conflito.");
    }

    private static void SyncConflictManualOverwriteAllowsStartedItem()
    {
        var startedTask = new ProjectTask
        {
            Id = 1,
            Name = "Story iniciada",
            TfsId = 101,
            PercentComplete = 100
        };
        var notStartedTask = new ProjectTask
        {
            Id = 2,
            Name = "Story nao iniciada",
            TfsId = 102,
            PercentComplete = 0
        };

        var automaticStarted = new TfsImportService.SyncConflictItem
        {
            Task = startedTask,
            AllowStartedOverwrite = false
        };
        var manualStarted = new TfsImportService.SyncConflictItem
        {
            Task = startedTask,
            AllowStartedOverwrite = true
        };
        var automaticNotStarted = new TfsImportService.SyncConflictItem
        {
            Task = notStartedTask,
            AllowStartedOverwrite = false
        };

        if (automaticStarted.CanOverwrite)
            throw new InvalidOperationException("Fluxo automatico nao deve sobrescrever item iniciado.");
        if (!manualStarted.CanOverwrite)
            throw new InvalidOperationException("Resolucao manual deve permitir sobrescrever item iniciado/concluido.");
        if (!automaticNotStarted.CanOverwrite)
            throw new InvalidOperationException("Item nao iniciado deve continuar selecionavel no fluxo automatico.");
    }

    private static void ManualStoryCompletionRequiresDevOpsTasks()
    {
        var storyWithoutCount = new ProjectTask
        {
            Id = 1,
            Name = "Story sem TKs calculado",
            TfsId = 1001,
            TfsType = "Story",
            DevopsTaskCount = null
        };
        if (!TfsImportService.ShouldBlockManualStoryCompletionWithoutDevOpsTasks(storyWithoutCount, 100))
            throw new InvalidOperationException("Story vinculada com TKs nulo deve bloquear a digitacao manual de 100%.");

        var storyWithoutTasks = new ProjectTask
        {
            Id = 2,
            Name = "Story sem Tasks",
            TfsId = 1002,
            TfsType = "User Story",
            DevopsTaskCount = 0
        };
        if (!TfsImportService.ShouldBlockManualStoryCompletionWithoutDevOpsTasks(storyWithoutTasks, 100))
            throw new InvalidOperationException("Story vinculada com TKs = 0 deve bloquear a digitacao manual de 100%.");

        if (TfsImportService.ShouldBlockManualStoryCompletionWithoutDevOpsTasks(
                storyWithoutTasks,
                100,
                enforceStoryCompletionWithTasks: false))
            throw new InvalidOperationException("Configuracao 'Encerrar Story somente com Task' desmarcada deve liberar 100% mesmo com TKs = 0.");

        storyWithoutTasks.DevopsTaskCount = 1;
        if (TfsImportService.ShouldBlockManualStoryCompletionWithoutDevOpsTasks(storyWithoutTasks, 100))
            throw new InvalidOperationException("Story com TKs > 0 deve permitir a digitacao manual de 100%.");

        storyWithoutTasks.DevopsTaskCount = 0;
        if (TfsImportService.ShouldBlockManualStoryCompletionWithoutDevOpsTasks(storyWithoutTasks, 99))
            throw new InvalidOperationException("A trava deve valer apenas para digitacao de 100%.");

        var task = new ProjectTask
        {
            Id = 3,
            Name = "Task filha",
            TfsId = 1003,
            TfsType = "Task",
            DevopsTaskCount = 0
        };
        if (TfsImportService.ShouldBlockManualStoryCompletionWithoutDevOpsTasks(task, 100))
            throw new InvalidOperationException("A trava deve valer para Story, nao para Task.");

        var localStory = new ProjectTask
        {
            Id = 4,
            Name = "Story local",
            TfsType = "Story",
            DevopsTaskCount = 0
        };
        if (TfsImportService.ShouldBlockManualStoryCompletionWithoutDevOpsTasks(localStory, 100))
            throw new InvalidOperationException("Story sem vinculo DevOps nao deve exigir TKs.");

        var noDevOpsActivity = new ProjectTask
        {
            Id = 5,
            Name = "Atividade NoDevOps",
            TfsId = -1,
            TfsType = "NoDevops",
            DevopsTaskCount = 0
        };
        if (TfsImportService.ShouldBlockManualStoryCompletionWithoutDevOpsTasks(noDevOpsActivity, 100))
            throw new InvalidOperationException("Atividade NoDevOps nao deve exigir TKs para digitar 100%.");
    }

    // Garante que a estimativa de tempo da IA (relógio de contagem regressiva) realmente
    // persiste em disco, ADAPTA à duração real observada e ESCALA pelo volume de bytes.
    // Regressão do bug "sempre o mesmo tempo e nao muda".
    private static void AiRunStatsPersistsAdaptsAndScales()
    {
        var storageKey = $"NXProject.Test.Stats-{Guid.NewGuid():N}";
        var file = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            storageKey, "ai-run-stats.json");
        try
        {
            const string prov = "Codex", act = "chat", sched = "Projeto X";

            if (NXProject.Services.AiRunStatsStore.EstimateSeconds(storageKey, prov, act, sched, 1000) != null)
                throw new InvalidOperationException("Sem historico deveria retornar null.");

            // 1ª amostra: 40s com 1000 bytes -> estima ~40s para o mesmo tamanho.
            NXProject.Services.AiRunStatsStore.Record(storageKey, prov, act, sched, 40, 1000);
            if (!File.Exists(file))
                throw new InvalidOperationException("O historico NAO foi gravado em disco.");
            var first = NXProject.Services.AiRunStatsStore.EstimateSeconds(storageKey, prov, act, sched, 1000)
                        ?? throw new InvalidOperationException("Historico gravado mas leitura retornou null.");
            AssertEqual(40, first, "Primeira estimativa deve refletir a duracao gravada.", 1);

            // Execucoes reais bem mais rapidas: a estimativa tem de CAIR (adaptar), nao ficar fixa.
            for (int i = 0; i < 5; i++)
                NXProject.Services.AiRunStatsStore.Record(storageKey, prov, act, sched, 5, 1000);
            var adapted = NXProject.Services.AiRunStatsStore.EstimateSeconds(storageKey, prov, act, sched, 1000)
                          ?? throw new InvalidOperationException("Leitura retornou null apos varias amostras.");
            if (adapted >= first)
                throw new InvalidOperationException($"A estimativa nao adaptou: continuou {adapted}s (era {first}s).");

            // Escala pelo volume: o DOBRO de bytes deve estimar mais tempo que o tamanho base.
            var baseEta = NXProject.Services.AiRunStatsStore.EstimateSeconds(storageKey, prov, act, sched, 1000)!.Value;
            var bigEta = NXProject.Services.AiRunStatsStore.EstimateSeconds(storageKey, prov, act, sched, 2000)!.Value;
            if (bigEta <= baseEta)
                throw new InvalidOperationException($"Estimativa nao escalou por bytes: {bigEta}s <= {baseEta}s.");

            // Provedor diferente NAO deve herdar o historico (chave por tipo de IA).
            if (NXProject.Services.AiRunStatsStore.EstimateSeconds(storageKey, "IA Local", act, sched, 1000) != null)
                throw new InvalidOperationException("Outro provedor nao deveria ter historico (chave por IA).");
        }
        finally
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
            try { var d = Path.GetDirectoryName(file); if (d != null && Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch { }
        }
    }

    // Histórico do chat de IA: guarda por CRONOGRAMA (projectKey), respeita o limite N
    // (mais recentes primeiro) e 0 = infinito. Chaves diferentes não se misturam.
    private static void AiChatHistoryPersistsPerProjectWithLimit()
    {
        var storageKey = $"NXProject.Test.Chat-{Guid.NewGuid():N}";
        var file = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            storageKey, "ai-chat-history.json");
        try
        {
            StoredConv Make(string title) => new()
            {
                Title = title,
                Messages = { new NXProject.Services.AiChatHistoryStore.StoredMessage { Role = "Usuário", Text = title } }
            };

            if (NXProject.Services.AiChatHistoryStore.Load(storageKey, "TFS-100").Count != 0)
                throw new InvalidOperationException("Projeto sem historico deveria vir vazio.");

            // 3 conversas com limite 2 -> guarda só as 2 primeiras (mais recentes).
            var convs = new[] { Make("c3"), Make("c2"), Make("c1") };
            NXProject.Services.AiChatHistoryStore.Save(storageKey, "TFS-100", convs, limit: 2);
            var back = NXProject.Services.AiChatHistoryStore.Load(storageKey, "TFS-100");
            if (back.Count != 2 || back[0].Title != "c3" || back[1].Title != "c2")
                throw new InvalidOperationException($"Limite 2 falhou: {back.Count} conversas ({string.Join(",", back.Select(c => c.Title))}).");

            // Limite 0 = infinito: guarda todas.
            NXProject.Services.AiChatHistoryStore.Save(storageKey, "TFS-100", convs, limit: 0);
            if (NXProject.Services.AiChatHistoryStore.Load(storageKey, "TFS-100").Count != 3)
                throw new InvalidOperationException("Limite 0 deveria guardar todas as conversas.");

            // Outro cronograma NAO herda o historico (chave por projeto).
            if (NXProject.Services.AiChatHistoryStore.Load(storageKey, "NXProject").Count != 0)
                throw new InvalidOperationException("Outro cronograma nao deveria ter historico.");

            // Conversa vazia nao e guardada.
            NXProject.Services.AiChatHistoryStore.Save(storageKey, "NXProject", new[] { new StoredConv { Title = "vazia" } }, limit: 10);
            if (NXProject.Services.AiChatHistoryStore.Load(storageKey, "NXProject").Count != 0)
                throw new InvalidOperationException("Conversa sem mensagens nao deveria ser gravada.");
        }
        finally
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
            try { var d = Path.GetDirectoryName(file); if (d != null && Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch { }
        }
    }

    private static void AssertEqual(DateTime expected, DateTime actual, string message)
    {
        if (expected != actual)
            throw new InvalidOperationException($"{message} Esperado: {expected:yyyy-MM-dd HH:mm}; Atual: {actual:yyyy-MM-dd HH:mm}.");
    }

    private static void AssertEqual(double expected, double actual, string message, double tolerance = 0.0001)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{message} Esperado: {expected:0.####}; Atual: {actual:0.####}.");
    }

    private static void AssertEqual(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"{message} Esperado: '{expected}'; Atual: '{actual}'.");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertEqual(int expected, int actual, string message)
    {
        if (expected != actual)
            throw new InvalidOperationException($"{message} Esperado: {expected}; Atual: {actual}.");
    }

    // ── Task Plan (Excel/ClosedXML): funções base ────────────────────────────

    private static string NewTempXlsx() =>
        Path.Combine(Path.GetTempPath(), $"nx-taskplan-{Guid.NewGuid():N}.xlsx");

    private static TaskPlanData NewPlan(params string[] cols)
    {
        var table = new System.Data.DataTable();
        foreach (var c in cols) table.Columns.Add(c, typeof(string));
        var data = new TaskPlanData { Table = table, SheetName = "Tarefas", HeaderRow = 1 };
        for (int i = 0; i < cols.Length; i++) data.ColumnSheetMap[cols[i]] = i + 1;
        return data;
    }

    private static void TaskPlanBackfillIdsFromSyncLog()
    {
        var path = NewTempXlsx();
        try
        {
            var data = NewPlan("Task", "ID Feature", "ID Story", "ID Task");
            var r = data.Table.NewRow();
            r["Task"] = "Tarefa A"; r["ID Feature"] = "5:I"; r["ID Story"] = "9:I"; r["ID Task"] = "115:I";
            data.Table.Rows.Add(r);
            ExcelTaskPlanService.CreateNew(path, data);

            var entries = new List<NXProject.Community.Services.BackfillEntry>
            {
                new() { TaskKey = "115:I", NewTaskId = "1234:T", NewStoryId = "900:T", NewFeatureId = "800:T" }
            };

            // Log lateral: grava, lê de volta e confere o nome do arquivo.
            ExcelTaskPlanService.WritePendingSidecar(path, entries);
            var sidecar = ExcelTaskPlanService.SidecarPath(path);
            if (!System.IO.Path.GetFileName(sidecar).EndsWith("_Sync_NXProject.xml"))
                throw new InvalidOperationException($"Nome do log inesperado: {System.IO.Path.GetFileName(sidecar)}");
            var read = ExcelTaskPlanService.ReadPendingSidecar(path);
            if (read == null || read.Count != 1 || read[0].NewTaskId != "1234:T")
                throw new InvalidOperationException("Log lateral não voltou corretamente.");

            // Aplica direto no .xlsx: linha com ID Task "115:I" vira "1234:T" (idem Story/Feature).
            var n = ExcelTaskPlanService.TryBackfillIds(path, entries);
            if (n != 1) throw new InvalidOperationException($"Esperada 1 linha atualizada, obtidas {n}.");

            var loaded = ExcelTaskPlanService.Load(path);
            var row = loaded.Table.Rows[0];
            if (row["ID Task"]?.ToString() != "1234:T") throw new InvalidOperationException("ID Task não foi atualizado.");
            if (row["ID Story"]?.ToString() != "900:T") throw new InvalidOperationException("ID Story não foi atualizado.");
            if (row["ID Feature"]?.ToString() != "800:T") throw new InvalidOperationException("ID Feature não foi atualizado.");

            ExcelTaskPlanService.DeletePendingSidecar(path);
            if (ExcelTaskPlanService.ReadPendingSidecar(path) != null)
                throw new InvalidOperationException("Log lateral deveria ter sido removido.");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            var sc = ExcelTaskPlanService.SidecarPath(path);
            if (File.Exists(sc)) File.Delete(sc);
        }
    }

    private static void TaskPlanCreateAndLoadRoundTrip()
    {
        var path = NewTempXlsx();
        try
        {
            var data = NewPlan("Task", "Story", "ID Devops");
            data.Table.Rows.Add("Tarefa A", "Story 1", "100:T");
            data.Table.Rows.Add("Tarefa B", "Story 1", "5:I");
            ExcelTaskPlanService.CreateNew(path, data);

            var loaded = ExcelTaskPlanService.Load(path);
            AssertEqual(1, loaded.HeaderRow, "Cabecalho do arquivo novo deve estar na linha 1.");
            AssertEqual(2, loaded.Table.Rows.Count, "As duas linhas devem voltar.");
            if (!loaded.Table.Columns.Contains("Task") || !loaded.Table.Columns.Contains("ID Devops"))
                throw new InvalidOperationException("Colunas Task/ID Devops nao voltaram do arquivo.");
            if (loaded.Table.Rows[0]["ID Devops"]?.ToString() != "100:T")
                throw new InvalidOperationException("Valor do ID Devops (:T) nao foi preservado.");
        }
        finally { File.Delete(path); }
    }

    private static void TaskPlanDetectsHeaderBelowSummary()
    {
        var path = NewTempXlsx();
        try
        {
            using (var wb = new ClosedXML.Excel.XLWorkbook())
            {
                var ws = wb.AddWorksheet("Tarefas");
                ws.Cell(1, 1).Value = "Plano de Tasks";
                ws.Cell(3, 5).Value = "EPIC X";          // bloco de resumo
                ws.Cell(5, 1).Value = "Task";            // linha de titulos
                ws.Cell(5, 2).Value = "Story";
                ws.Cell(5, 3).Value = "ID Devops";
                ws.Cell(6, 1).Value = "Tarefa A";
                ws.Cell(6, 2).Value = "Story 1";
                ws.Cell(7, 1).Value = "Tarefa B";
                ws.Cell(7, 2).Value = "Story 2";
                wb.SaveAs(path);
            }

            var loaded = ExcelTaskPlanService.Load(path);
            AssertEqual(5, loaded.HeaderRow, "A linha de titulos deve ser reconhecida abaixo do resumo.");
            AssertEqual(2, loaded.Table.Rows.Count, "As linhas de dados devem ser lidas apos o cabecalho.");
            if (!loaded.Table.Columns.Contains("Story"))
                throw new InvalidOperationException("Coluna Story nao reconhecida no cabecalho.");
        }
        finally { File.Delete(path); }
    }

    private static void TaskPlanSavePreservesValuesAndColors()
    {
        var path = NewTempXlsx();
        try
        {
            var data = NewPlan("Task", "Story");
            data.Table.Rows.Add("Tarefa A", "Story 1");
            ExcelTaskPlanService.CreateNew(path, data);

            var loaded = ExcelTaskPlanService.Load(path);
            loaded.Table.Rows[0]["Task"] = "Tarefa A editada";
            loaded.Table.Columns.Add(ExcelTaskPlanService.ColorColPrefix + "Task", typeof(string));
            loaded.Table.Rows[0][ExcelTaskPlanService.ColorColPrefix + "Task"] = "#FFFF00";
            ExcelTaskPlanService.Save(path, loaded);

            var again = ExcelTaskPlanService.Load(path);
            if (again.Table.Rows[0]["Task"]?.ToString() != "Tarefa A editada")
                throw new InvalidOperationException("Valor editado nao foi gravado.");
            var color = again.Table.Columns.Contains(ExcelTaskPlanService.ColorColPrefix + "Task")
                ? again.Table.Rows[0][ExcelTaskPlanService.ColorColPrefix + "Task"]?.ToString()
                : null;
            if (!string.Equals(color, "#FFFF00", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Cor de fundo nao voltou do arquivo (obtido: '{color}').");
        }
        finally { File.Delete(path); }
    }

    private static void TaskPlanFilterViewRoundTrips()
    {
        var settings = new TaskPlanSettings();
        var view = new TaskPlanFilterView { Epic = "EPIC A", Feature = "Feature 1", OpenTasksOnly = true };
        view.ColumnFilters["Status"] = new System.Collections.Generic.List<string> { "Active", "New" };
        view.ColorFilters["Task"] = "#FFF2CC";
        settings.SetFilterView(@"C:\Planos\Plano Oficial.xlsx", view);

        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var back = System.Text.Json.JsonSerializer.Deserialize<TaskPlanSettings>(json)
            ?? throw new InvalidOperationException("Desserializacao retornou nulo.");

        // A chave deve resolver por caminho normalizado, case-insensitive.
        var got = back.GetFilterView(@"c:\planos\plano oficial.xlsx")
            ?? throw new InvalidOperationException("Visao de filtro nao sobreviveu a serializacao.");
        if (got.Epic != "EPIC A") throw new InvalidOperationException($"EPIC perdido: '{got.Epic}'.");
        if (got.Feature != "Feature 1") throw new InvalidOperationException($"Feature perdida: '{got.Feature}'.");
        if (!got.OpenTasksOnly) throw new InvalidOperationException("OpenTasksOnly perdido.");
        if (!got.ColumnFilters.TryGetValue("Status", out var st) || st.Count != 2)
            throw new InvalidOperationException("Filtro de coluna Status perdido.");
        if (!got.ColorFilters.TryGetValue("Task", out var cor) || cor != "#FFF2CC")
            throw new InvalidOperationException("Filtro de cor perdido.");

        settings.RemoveFilterView(@"C:\PLANOS\PLANO OFICIAL.XLSX");
        if (settings.GetFilterView(@"C:\Planos\Plano Oficial.xlsx") != null)
            throw new InvalidOperationException("RemoveFilterView nao removeu por caminho case-insensitive.");
    }

    private static void TaskPlanBackupBeforeSaveCreatesCopyAndRetains15Days()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"nx-taskplan-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "Plano Oficial.xlsx");
        try
        {
            var data = NewPlan("Aprovada", "Task", "ID Task");
            data.Table.Rows.Add("Nao", "Tarefa interna", "77:I");
            ExcelTaskPlanService.CreateNew(path, data);

            var backupDir = Path.Combine(dir, "Backup");
            Directory.CreateDirectory(backupDir);
            var oldBackup = Path.Combine(backupDir, "Plano_Oficial_bkp_2026_01_01_10_00_00_merge_user.xlsx");
            File.Copy(path, oldBackup);
            File.SetLastWriteTime(oldBackup, new DateTime(2026, 6, 1, 10, 0, 0));

            var backup = ExcelTaskPlanService.CreateBackupBeforeSave(
                path,
                "merge",
                @"DOMINIO\usuario teste",
                new DateTime(2026, 7, 20, 12, 34, 56));

            if (!File.Exists(backup))
                throw new InvalidOperationException("Backup nao foi criado.");
            var name = Path.GetFileName(backup);
            if (name != "Plano_Oficial_bkp_2026_07_20_12_34_56_merge_DOMINIO_usuario_teste.xlsx")
                throw new InvalidOperationException($"Nome do backup inesperado: {name}");
            if (File.Exists(oldBackup))
                throw new InvalidOperationException("Backup antigo deveria ter sido removido pela retencao de 15 dias.");

            data.Table.Rows[0]["ID Task"] = "123:T";
            ExcelTaskPlanService.Save(path, data);

            var backupData = ExcelTaskPlanService.Load(backup);
            if (backupData.Table.Rows[0]["ID Task"]?.ToString() != "77:I")
                throw new InvalidOperationException("Backup deve preservar o conteudo anterior ao salvar.");
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    private static void TaskPlanNewColumnKeepsViewPosition()
    {
        var path = NewTempXlsx();
        try
        {
            var data = NewPlan("Task", "Story");
            data.Table.Rows.Add("Tarefa A", "Story 1");
            ExcelTaskPlanService.CreateNew(path, data);

            var loaded = ExcelTaskPlanService.Load(path);
            loaded.FixedNameColumns.Add("Task");
            loaded.FixedNameColumns.Add("Story");
            var nota = loaded.Table.Columns.Add("Nota", typeof(string));
            nota.SetOrdinal(1);   // visão: Task, Nota, Story
            ExcelTaskPlanService.Save(path, loaded);

            // Na planilha, a coluna nova fica no FIM (posicao 3) com o prefixo da visão.
            using (var wb = new ClosedXML.Excel.XLWorkbook(path))
            {
                var header = wb.Worksheet("Tarefas").Cell(1, 3).GetString();
                if (header != "2#_Nota")
                    throw new InvalidOperationException($"Cabecalho da coluna nova deveria ser '2#_Nota' no fim da aba (obtido: '{header}').");
            }

            // Ao reabrir, volta com o nome limpo e na posicao da visão.
            var again = ExcelTaskPlanService.Load(path);
            if (!again.Table.Columns.Contains("Nota"))
                throw new InvalidOperationException("Coluna nova nao voltou com o nome limpo.");
            AssertEqual(1, again.Table.Columns["Nota"]!.Ordinal, "Coluna nova deve voltar na posicao da visão (indice 1).");
        }
        finally { File.Delete(path); }
    }

    private static void TaskPlanDeletedColumnClearedOnSave()
    {
        var path = NewTempXlsx();
        try
        {
            var data = NewPlan("Task", "Story", "Obs");
            data.Table.Rows.Add("Tarefa A", "Story 1", "obs 1");
            ExcelTaskPlanService.CreateNew(path, data);

            var loaded = ExcelTaskPlanService.Load(path);
            loaded.RemovedSheetColumns.Add(loaded.ColumnSheetMap["Obs"]);
            loaded.ColumnSheetMap.Remove("Obs");
            loaded.Table.Columns.Remove("Obs");
            ExcelTaskPlanService.Save(path, loaded);

            var again = ExcelTaskPlanService.Load(path);
            if (again.Table.Columns.Contains("Obs"))
                throw new InvalidOperationException("Coluna excluida nao deveria voltar do arquivo.");
            AssertEqual(1, again.Table.Rows.Count, "As linhas restantes devem continuar integras.");
        }
        finally { File.Delete(path); }
    }

    // Simulação com resposta REAL da IA (modo Feature nova): sufixo "h" no esforço e
    // chaves acentuadas ("esforço", "responsável") — antes viravam 1h e responsável perdido.
    private static void TaskPlanAiResponseAcceptsHourSuffixAndAccentedKeys()
    {
        AssertEqual(4, TaskPlanScheduleRules.ParseEstimatedHours("4h") ?? -1, "'4h' deve virar 4 horas.");
        AssertEqual(3, TaskPlanScheduleRules.ParseEstimatedHours("3 horas") ?? -1, "'3 horas' deve virar 3 horas.");
        AssertEqual(6.5, TaskPlanScheduleRules.ParseEstimatedHours("6,5") ?? -1, "'6,5' segue aceito.");
        AssertEqual(2 * ProjectCalendarService.WorkingHoursPerDay,
            TaskPlanScheduleRules.ParseEstimatedHours("2 dias") ?? -1, "'2 dias' converte pelo expediente.");
        if (TaskPlanScheduleRules.ParseEstimatedHours("horas") != null)
            throw new InvalidOperationException("Unidade sem número não pode virar estimativa.");

        // Trecho literal da resposta da IA registrada em produção (itens 1 e 3 do log).
        const string raw = """
            [{"epic_id":14,"feature":"BI de Torre de Controle – Planos Outbound","story":"User Story 1 – Organizar a base","task":"Corrigir nomes e informações das colunas","responsavel":"Oliveira, Alice De Muylder (Contractor)","esforco":"4h","obs":""},
             {"epic_id":14,"feature":"BI de Torre de Controle – Planos Outbound","story":"User Story 3 – Configurar os filtros","task":"Sincronizar todos os filtros","responsável":"Oliveira, Alice De Muylder (Contractor)","esforço":"2h","obs":""}]
            """;
        using var doc = System.Text.Json.JsonDocument.Parse(raw);
        var items = doc.RootElement.EnumerateArray().ToList();

        var h1 = TaskPlanScheduleRules.ParseEstimatedHours(
            TaskPlanScheduleRules.GetJsonString(items[0], "esforco", "esforço"));
        AssertEqual(4, h1 ?? -1, "Item com chave 'esforco' e sufixo h deve dar 4 horas.");

        var h2 = TaskPlanScheduleRules.ParseEstimatedHours(
            TaskPlanScheduleRules.GetJsonString(items[1], "esforco", "esforço"));
        AssertEqual(2, h2 ?? -1, "Item com chave ACENTUADA 'esforço' deve dar 2 horas (fallback).");

        var resp = TaskPlanScheduleRules.GetJsonString(items[1], "responsavel", "responsável");
        if (resp != "Oliveira, Alice De Muylder (Contractor)")
            throw new InvalidOperationException("Chave acentuada 'responsável' deve ser lida pelo fallback.");

        // Resposta TRUNCADA pelo teto de tokens (final real do log de produção: corta em
        // "...,{"epic_id"): o reparo aproveita os objetos completos em vez de descartar tudo.
        const string truncatedRaw =
            """[{"epic_id":24,"story":"Formatar o painel","task":"Padronizar as cores","esforço":"3"},{"epic_id""";
        var (json, truncated) = TaskPlanScheduleRules.ExtractJsonArray(truncatedRaw);
        if (json == null || !truncated)
            throw new InvalidOperationException("Resposta truncada deve ser reparada (itens completos aproveitados).");
        using var repaired = System.Text.Json.JsonDocument.Parse(json);
        AssertEqual(1, repaired.RootElement.GetArrayLength(), "Reparo deve manter só o objeto completo.");

        var (okJson, okTrunc) = TaskPlanScheduleRules.ExtractJsonArray("""bla [{"task":"x"}] bla""");
        if (okJson == null || okTrunc)
            throw new InvalidOperationException("Array íntegro não pode ser marcado como truncado.");
        if (TaskPlanScheduleRules.ExtractJsonArray("sem json aqui").Json != null)
            throw new InvalidOperationException("Texto sem array não pode devolver JSON.");
    }

    // Casamento do responsável citado com o recurso do cronograma (nomes reais do
    // cadastro "Sobrenome, Nome (Contractor)"): exato, invertido, sem acento, só o
    // primeiro nome e citado no meio de uma frase. Ambiguidade NAO vira palpite.
    private static void TaskPlanResourceMatcherHandlesCitedNames()
    {
        var people = new List<Resource>
        {
            new() { Id = 1, Name = "Domingues, Joao Pedro Araujo", Type = ResourceType.Work },
            new() { Id = 2, Name = "Melo, Carmo C (Contractor)", Type = ResourceType.Work },
            new() { Id = 3, Name = "Fenoci, Mateus Rezende (Contractor)", Type = ResourceType.Work },
            new() { Id = 4, Name = "Monteiro, Emneh Dias (Contractor)", Type = ResourceType.Work },
            new() { Id = 5, Name = "Rodrig, Caio Siquara Nascimento (Contractor)", Type = ResourceType.Work },
            new() { Id = 6, Name = "Oliveira, Alice De Muylder (Contractor)", Type = ResourceType.Work },
        };

        void Match(string cited, int expectedId, string why)
        {
            var r = TaskPlanResourceMatcher.Find(people, cited);
            if (r == null) throw new InvalidOperationException($"\"{cited}\" deveria casar ({why}), mas nao casou.");
            AssertEqual(expectedId, r.Id, $"\"{cited}\" deveria casar com o recurso {expectedId} ({why}).");
        }

        Match("Oliveira, Alice De Muylder (Contractor)", 6, "nome exato do cadastro");
        Match("Oliveira, Alice De Muylder", 6, "sem o sufixo (Contractor)");
        Match("Alice Oliveira", 6, "nome invertido, sem sobrenome do meio");
        Match("alice de muylder oliveira", 6, "minusculas e ordem trocada");
        Match("Alice", 6, "so o primeiro nome, unico no projeto");
        Match("Joao Pedro", 1, "primeiro nome composto");
        Match("João Pedro Domingues", 1, "com acento contra cadastro sem acento");
        Match("Falar com Fenoci, Mateus Rezende (Contractor) sobre a carga", 3, "nome citado dentro da frase");

        if (TaskPlanResourceMatcher.Find(people, "Fulano da Silva") != null)
            throw new InvalidOperationException("Nome de fora do projeto nao pode casar com ninguem.");
        if (TaskPlanResourceMatcher.Find(people, "") != null)
            throw new InvalidOperationException("Nome vazio nao pode casar.");

        // Ambiguidade: dois "Carmo" no projeto — melhor Observacao do que errar a pessoa.
        var ambiguous = new List<Resource>(people)
        {
            new() { Id = 7, Name = "Souza, Carmo T (Contractor)", Type = ResourceType.Work },
        };
        if (TaskPlanResourceMatcher.Find(ambiguous, "Carmo") != null)
            throw new InvalidOperationException("Primeiro nome ambiguo nao pode escolher um recurso no palpite.");
        AssertEqual(2, TaskPlanResourceMatcher.Find(ambiguous, "Carmo Melo")?.Id ?? -1,
            "Nome + sobrenome desempata entre dois recursos de mesmo primeiro nome.");

        // Recurso material (nao-pessoa) nunca casa.
        var material = new List<Resource> { new() { Id = 9, Name = "Licenca Power BI", Type = ResourceType.Material } };
        if (TaskPlanResourceMatcher.Find(material, "Licenca Power BI") != null)
            throw new InvalidOperationException("Recurso Material nao pode virar responsavel.");
    }

    private static void TaskPlanApplyCreatesInternalTaskLikeSchedule()
    {
        SetCurrentCalendar(new ProjectCalendar());
        var story = new ProjectTask
        {
            Id = 10, TfsId = 500, TfsType = "User Story", Name = "Story Nova",
            TfsState = "New", PercentComplete = 0, Level = 2,
            TfsIterationPath = @"Proj\Sprint 01", SprintNumber = 1,
            Start = new DateTime(2026, 7, 6), Finish = new DateTime(2026, 7, 7)
        };

        // "2d" convertido pelo calendário do cronograma (dias úteis → horas).
        var hours = TaskPlanScheduleRules.ParseEstimatedHours("2d")
            ?? throw new InvalidOperationException("'2d' deveria ser aceito como estimativa.");
        AssertEqual(2 * ProjectCalendarService.WorkingHoursPerDay, hours, "'2d' deve virar 2 dias úteis em horas.");

        var task = TaskPlanScheduleRules.CreateInternalTask(story, 99, "Task interna", "desc", hours);
        if (task.Description != "desc")
            throw new InvalidOperationException("Task Plan deve levar a descrição da Task para o cronograma.");
        AssertEqual(0, task.TfsId ?? -1, "Task interna nasce com TfsId=0 ('criar no TFS'), padrão do AddSubtask.");
        if (task.TfsState != "New" || task.TfsType != "Task")
            throw new InvalidOperationException("Task interna deve nascer como Task em estado New.");
        if (task.HasTfsLink)
            throw new InvalidOperationException("Task interna não pode ter vínculo TFS (DisplayId deve ser '{Id}:I').");
        if (task.SprintNumber != story.SprintNumber || task.TfsIterationPath != story.TfsIterationPath)
            throw new InvalidOperationException("Task interna deve herdar sprint/iteração da Story.");
        // Story em New: a duração pode ser ajustada (fim calculado pode passar do fim da Story).
        AssertEqual(ProjectCalendarService.AddWorkingHours(story.Start, hours), task.Finish,
            "Story em New: o fim da task segue a estimativa, mesmo além do fim da Story.");
    }

    private static void TaskPlanStartedStoryKeepsDuration()
    {
        SetCurrentCalendar(new ProjectCalendar());
        var story = new ProjectTask
        {
            Id = 11, TfsId = 501, TfsType = "User Story", Name = "Story Ativa",
            TfsState = "Active", PercentComplete = 40, Level = 2,
            Start = new DateTime(2026, 7, 6), Finish = new DateTime(2026, 7, 7)
        };

        if (TaskPlanScheduleRules.CanAdjustStoryDuration(story))
            throw new InvalidOperationException("Story iniciada (Active/40%) não pode ter a duração ajustada.");

        // Estimativa maior que o período da Story: o fim fica contido no fim da Story.
        var task = TaskPlanScheduleRules.CreateInternalTask(story, 100, "Task interna", null, 40);
        if (task.Finish > story.Finish)
            throw new InvalidOperationException("Com a Story iniciada, o fim da task não pode passar do fim da Story.");
    }

    private static void TfsAttachmentParserReadsNameAndSize()
    {
        const string payload = """
        {
          "value": [
            {
              "id": "abc123",
              "name": "manual.pdf",
              "size": 2048,
              "url": "https://dev.azure.com/org/project/_apis/wit/workItems/42/attachments/abc123"
            }
          ]
        }
        """;

        var result = NXProject.Services.TfsAttachmentService.ParseAttachmentsList(payload);
        if (result.Count != 1) throw new Exception($"Esperava 1 anexo, veio {result.Count}.");
        if (!string.Equals(result[0].Name, "manual.pdf", StringComparison.OrdinalIgnoreCase))
            throw new Exception($"Nome do anexo inválido: {result[0].Name}");
        if (result[0].SizeBytes != 2048)
            throw new Exception($"Tamanho do anexo inválido: {result[0].SizeBytes}");
    }

    private static void TfsAttachmentUploadUrlDoesNotDuplicateProjectInOrgPath()
    {
        var options = new NXProject.Services.TfsConnectionOptions
        {
            OrganizationUrl = "https://dev.azure.com/minha-org/MeuProjeto",
            TeamProject = "MeuProjeto",
            PersonalAccessToken = "token" 
        };

        var url = NXProject.Services.TfsAttachmentService.BuildUploadUrl(options, 42, "manual.pdf");
        if (url.Contains("/MeuProjeto/MeuProjeto/_apis/", StringComparison.OrdinalIgnoreCase))
            throw new Exception($"URL de upload duplicou o projeto na org: {url}");

        // O upload vai para a rota de anexos do projeto (o work item e ligado depois, via PATCH);
        // "workitems/{id}/attachments" nao existe no Azure DevOps e respondia 404.
        if (!url.Contains("/MeuProjeto/_apis/wit/attachments?fileName=manual.pdf&", StringComparison.OrdinalIgnoreCase))
            throw new Exception($"URL de upload não tem o formato esperado: {url}");
        if (url.Contains("/workitems/", StringComparison.OrdinalIgnoreCase))
            throw new Exception($"URL de upload não pode incluir o work item: {url}");
    }

    // ── ID interno duplicado: gravar bloqueia, ler normaliza, sync recusa ────

    private static Project NewProjectWithDuplicateIds()
    {
        var project = new Project { Name = "Dup", StartDate = new DateTime(2026, 7, 6) };
        var epic = new ProjectTask { Id = 1, TfsId = 900, TfsType = "Epic", Name = "Epic A" };
        var s1 = new ProjectTask { Id = 115, TfsId = 901, TfsType = "User Story", Name = "Story 1", Parent = epic };
        var s2 = new ProjectTask { Id = 115, TfsId = 0, TfsType = "User Story", Name = "Story duplicada", Parent = epic };
        epic.Children.Add(s1);
        epic.Children.Add(s2);
        epic.IsSummary = true;
        project.Tasks.Add(epic);
        return project;
    }

    private static void SaveBlocksDuplicateTaskIds()
    {
        var project = NewProjectWithDuplicateIds();
        var path = Path.Combine(Path.GetTempPath(), $"nx-dup-{Guid.NewGuid():N}.nxp");
        try
        {
            XmlProjectService.Save(project, path);
            throw new InvalidOperationException("Save deveria BLOQUEAR projeto com ID duplicado.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("MESMO ID"))
        {
            if (!ex.Message.Contains("115") || !ex.Message.Contains("Story duplicada"))
                throw new InvalidOperationException("A mensagem deve dizer o ID (115) e a atividade gravada errada.");
            if (File.Exists(path))
                throw new InvalidOperationException("Nenhum arquivo deveria ter sido gravado.");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static void LoadNormalizesDuplicateTaskIds()
    {
        // Simula um arquivo LEGADO (gravado antes da trava) com dois IDs 115:
        // grava com IDs válidos e duplica no XML por texto.
        var project = NewProjectWithDuplicateIds();
        var all = new List<ProjectTask> { project.Tasks[0] };
        all.AddRange(project.Tasks[0].Children);
        project.Tasks[0].Children[1].Id = 777;   // deixa válido para gravar

        var path = Path.Combine(Path.GetTempPath(), $"nx-dup-{Guid.NewGuid():N}.nxp");
        try
        {
            XmlProjectService.Save(project, path);
            File.WriteAllText(path, File.ReadAllText(path).Replace(">777<", ">115<"));

            var loaded = XmlProjectService.Load(path);
            var ids = new List<int>();
            void Walk(IEnumerable<ProjectTask> ts) { foreach (var t in ts) { ids.Add(t.Id); Walk(t.Children); } }
            Walk(loaded.Tasks);

            if (ids.Count != 3)
                throw new InvalidOperationException($"Esperadas 3 atividades, obtidas {ids.Count}.");
            if (ids.Distinct().Count() != ids.Count)
                throw new InvalidOperationException("A leitura deve normalizar IDs duplicados (todos distintos).");
            if (!ids.Contains(115))
                throw new InvalidOperationException("O primeiro ID 115 deve ser preservado.");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static void SyncBlocksDuplicateTaskIds()
    {
        var project = NewProjectWithDuplicateIds();
        try
        {
            TfsImportService.EnsureNoDuplicateTaskIds(project);
            throw new InvalidOperationException("Sync deveria BLOQUEAR projeto com ID duplicado.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Sincronização bloqueada"))
        {
            if (!ex.Message.Contains("115"))
                throw new InvalidOperationException("A mensagem do sync deve citar o ID duplicado.");
        }
    }

    private static void SyncBlocksTaskWithoutStoryParent()
    {
        var epic    = new ProjectTask { Id = 1, TfsId = 900, TfsType = "Epic", Name = "Epic A" };
        var feature = new ProjectTask { Id = 2, TfsId = 901, TfsType = "Feature", Name = "Feature A", Parent = epic };
        var story   = new ProjectTask { Id = 3, TfsId = 0, TfsType = "User Story", Name = "Story ainda sem ID", Parent = feature };
        var task    = new ProjectTask { Id = 4, TfsId = 0, TfsType = "Task", Name = "Task nova", Parent = story };

        // Story pai ainda sem ID DevOps: o ancestral vinculado mais próximo é a Feature —
        // a Task NÃO pode ser criada/reparentada ali.
        var violation = TfsImportService.TaskParentViolation(task);
        if (violation == null || !violation.Contains("Feature"))
            throw new InvalidOperationException($"Task sob Story sem ID deveria acusar o pai Feature (obtido: '{violation}').");

        // Com a Story vinculada, a Task é aceita.
        story.TfsId = 902;
        if (TfsImportService.TaskParentViolation(task) != null)
            throw new InvalidOperationException("Task sob Story vinculada deve ser aceita.");

        // Story/Feature não são afetadas pela regra (só Task).
        if (TfsImportService.TaskParentViolation(story) != null)
            throw new InvalidOperationException("A regra vale apenas para o tipo Task.");
    }

    private static void SyncAllowsDevOpsMilestoneOutsideStory()
    {
        var feature = new ProjectTask { Id = 1, TfsId = 901, TfsType = "Feature", Name = "Feature A" };
        var marcoFeature = new ProjectTask
        {
            Id = 2,
            TfsId = 0,
            TfsType = "Marco-Devops",
            Tags = "MARCO-PROJECT",
            Name = "Marco sob Feature",
            Parent = feature
        };
        feature.Children.Add(marcoFeature);

        if (TfsImportService.TaskParentViolation(marcoFeature) != null)
            throw new InvalidOperationException("Marco-Devops deve poder ficar sob Feature, mesmo sendo criado como Task no DevOps.");
        AssertEqual(901, TfsImportService.ResolveDesiredParentForTests(marcoFeature, 999),
            "Marco-Devops sob Feature deve usar a Feature como pai DevOps.");

        var epic = new ProjectTask { Id = 3, TfsId = 1000, TfsType = "Epic", Name = "Epic comum" };
        var marcoEpic = new ProjectTask
        {
            Id = 4,
            TfsId = 0,
            TfsType = "Marco-DevOps",
            Tags = "MARCO-PROJECT",
            Name = "Marco sob Epic",
            Parent = epic
        };
        epic.Children.Add(marcoEpic);

        if (TfsImportService.TaskParentViolation(marcoEpic) != null)
            throw new InvalidOperationException("Marco-Devops deve poder ficar sob Epic comum quando estiver no nivel de Feature.");
        AssertEqual(1000, TfsImportService.ResolveDesiredParentForTests(marcoEpic, 999),
            "Marco-Devops sob Epic comum deve usar o Epic como pai DevOps.");

        var rootWorkItemProject = new ProjectTask { Id = 5, TfsId = 999, TfsType = "Epic", Name = "Work Item Project" };
        var marcoRoot = new ProjectTask
        {
            Id = 6,
            TfsId = 0,
            TfsType = "Marco-DevOps",
            Tags = "MARCO-PROJECT",
            Name = "Marco no raiz",
            Parent = rootWorkItemProject
        };
        rootWorkItemProject.Children.Add(marcoRoot);

        AssertEqual(0, TfsImportService.ResolveDesiredParentForTests(marcoRoot, 999),
            "Marco-Devops nao pode ser criado direto no Work Item Project raiz.");

        var ideia = new ProjectTask { Id = 7, TfsId = 1013393, TfsType = "Ideia", Name = "Ideia vinculada" };
        var marcoIdeia = new ProjectTask
        {
            Id = 8,
            TfsId = 0,
            TfsType = "Marco-DevOps",
            Tags = "MARCO-PROJECT",
            Name = "Marco sob Ideia",
            Parent = ideia
        };
        ideia.Children.Add(marcoIdeia);

        if (TfsImportService.TaskParentViolation(marcoIdeia) != null)
            throw new InvalidOperationException("Marco-Devops deve poder ficar sob tipo customizado vinculado, como Ideia.");
        AssertEqual(1013393, TfsImportService.ResolveDesiredParentForTests(marcoIdeia, 999),
            "Marco-Devops sob Ideia deve usar a Ideia como pai DevOps.");
    }

    private static void SyncBlocksDuplicateTaskNamesInStory()
    {
        var project = new Project { Name = "Dup nomes", StartDate = new DateTime(2026, 7, 6) };
        var epic    = new ProjectTask { Id = 1, TfsId = 900, TfsType = "Epic", Name = "Epic A" };
        var story   = new ProjectTask { Id = 2, TfsId = 901, TfsType = "User Story", Name = "Story A", Parent = epic };
        var t1      = new ProjectTask { Id = 3, TfsId = 0, TfsType = "Task", Name = "Ajustar view", Parent = story };
        var t2      = new ProjectTask { Id = 4, TfsId = 0, TfsType = "Task", Name = "ajustar view", Parent = story }; // mesmo nome (case-insensitive)
        var t3      = new ProjectTask { Id = 5, TfsId = 0, TfsType = "Task", Name = "Outra task", Parent = story };
        story.Children.Add(t1); story.Children.Add(t2); story.Children.Add(t3);
        epic.Children.Add(story);
        project.Tasks.Add(epic);

        try
        {
            TfsImportService.EnsureNoDuplicateTaskNamesInStory(project);
            throw new InvalidOperationException("Sync deveria BLOQUEAR duas Tasks de mesmo nome na Story.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("MESMO nome"))
        {
            if (!ex.Message.Contains("Story A") || !ex.Message.Contains("Ajustar view"))
                throw new InvalidOperationException("A mensagem deve citar a Story e o nome da Task duplicada.");
        }

        // Sem duplicidade → passa.
        t2.Name = "Ajustar outra view";
        TfsImportService.EnsureNoDuplicateTaskNamesInStory(project);
    }

    private static void SetCurrentCalendar(ProjectCalendar calendar)
    {
        var property = typeof(ProjectCalendarService).GetProperty(nameof(ProjectCalendarService.Current))
            ?? throw new InvalidOperationException("ProjectCalendarService.Current nao encontrado.");
        property.SetValue(null, calendar);
    }
}
