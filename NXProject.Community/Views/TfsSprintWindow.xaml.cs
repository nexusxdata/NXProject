// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using NXProject.Community.Services;
using NXProject.Services;

namespace NXProject.Views
{
    /// <summary>
    /// Visão de Sprint (Taskboard): Stories em linhas, Tasks como cards nas colunas por estado,
    /// com filtro por pessoa e "somente do cronograma", e um resumo por estado. Só leitura.
    /// Ver TfsImportService.BuildSprintBoardAsync.
    /// </summary>
    public partial class TfsSprintWindow : Window
    {
        private readonly TfsConnectionOptions _options;
        private readonly IReadOnlySet<int> _scheduleIds;
        // Work item raiz do cronograma aberto no NX (0 = nenhum): no filtro esse no vem
        // marcado e rotulado com "(Aberto no NX)".
        private readonly int _openRootId;
        // Grupos da arvore do filtro (pai -> folhas), da folha para a raiz: o estado de cada
        // pai (marcado / desmarcado / parcial) e recalculado a cada clique.
        private readonly List<(CheckBox Parent, List<CheckBox> Leaves)> _filterGroups = new();
        // Id do work item → posição no cronograma aberto (vazio quando aberto sem cronograma).
        private readonly Dictionary<int, int> _scheduleRank = new();
        private readonly Action<int>? _openInSchedule;
        private readonly string? _preferredSprint;
        private List<TfsImportService.SprintInfo> _sprints = new();
        private TfsImportService.SprintBoard? _board;
        private const string AllPeople = "— Todos —";
        // Filtro múltiplo por Story (vazio = todas) e lookup id→Story.
        private readonly HashSet<int> _selectedStoryIds = new();
        // Filtro múltiplo por Pessoa (vazio = todas).
        private readonly HashSet<string> _selectedPeople = new(StringComparer.CurrentCultureIgnoreCase);
        private Dictionary<int, TfsImportService.SprintStoryRow> _storyById = new();
        // Filtro por estado (vazio = todos), nome do usuário atual e edição de cards.
        private readonly HashSet<string> _hiddenStates = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Padrao do corte de Closed (dias). Usado na abertura e no "limpar filtros".</summary>
        private const int DefaultClosedDays = 15;
        private int _closedDays = DefaultClosedDays; // Closed exibe só os últimos N dias (0 = todos).
        private int _discoveredPrioMax; // máximo de Priority aceito pelo template (via validateOnly).

        /// <summary>
        /// Medicao por etapa da abertura (load-perf.txt). DESLIGADA por padrao: serviu para achar
        /// o gargalo e nao precisa rodar na versao publicada. Para ligar sem recompilar, defina a
        /// variavel de ambiente NXPROJECT_LOADPERF=1 antes de abrir o NX.
        /// </summary>
        private static readonly bool LoadPerfEnabled =
            Environment.GetEnvironmentVariable("NXPROJECT_LOADPERF") == "1";

        private static void AppendLoadPerf(string line)
        {
            if (!LoadPerfEnabled) return;
            try
            {
                var perfFile = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NXProject.Community", "load-perf.txt");
                System.IO.File.AppendAllText(perfFile, line + Environment.NewLine + Environment.NewLine);
            }
            catch { /* medicao nunca derruba a carga */ }
        }
        // ── DIAGNOSTICO do filtro do board (DESLIGADO por padrao) ────────────────────────
        // Grava, a cada desenho, o estado dos filtros e o motivo de cada Task NAO aparecer.
        // Serve para responder "por que esta Task nao aparece" sem ficar adivinhando. Liga com
        // a variavel de ambiente NXPROJECT_FILTERLOG=1; o arquivo sai em filter-log.txt.
        private static readonly bool FilterLogEnabled =
            Environment.GetEnvironmentVariable("NXPROJECT_FILTERLOG") == "1";

        private static string FilterLogPath => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NXProject.Community", "filter-log.txt");

        private void DumpFilterLog()
        {
            if (!FilterLogEnabled || _board == null) return;
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"=== {DateTime.Now:dd/MM/yyyy HH:mm:ss} — desenho do board ===");
                sb.AppendLine($"Sprints carregadas : {string.Join(" | ", _sprintPaths)}");
                sb.AppendLine($"Closed ultimos dias: {_closedDays} (0 = todos)   Hoje: {DateTime.Today:dd/MM/yyyy}");
                sb.AppendLine($"Estados ocultos    : {(_hiddenStates.Count == 0 ? "(nenhum)" : string.Join(", ", _hiddenStates))}");
                sb.AppendLine($"Pessoas filtradas  : {(_selectedPeople.Count == 0 ? "(todas)" : string.Join(", ", _selectedPeople))}");
                sb.AppendLine($"Stories filtradas  : {(_selectedStoryIds.Count == 0 ? "(todas)" : string.Join(", ", _selectedStoryIds))}");
                sb.AppendLine($"Busca              : '{SearchQuery()}'");
                sb.AppendLine($"OnlySchedule={OnlyScheduleCheck.IsChecked} OnlyBlocked={OnlyBlockedCheck.IsChecked} "
                            + $"OnlyUnplanned={OnlyUnplannedCheck.IsChecked} OnlyDoing={OnlyDoingCheck.IsChecked} "
                            + $"OnlyDoneActive={OnlyDoneActiveCheck.IsChecked} OnlyTaskActive={OnlyTaskActiveCheck.IsChecked}");

                var all = EffectiveStories().SelectMany(x => x.Tasks).ToList();
                var hidden = all.Where(t => !PassesFilters(t)).ToList();
                sb.AppendLine($"Tasks carregadas: {all.Count} — visiveis: {all.Count - hidden.Count} — escondidas: {hidden.Count}");
                foreach (var t in hidden.Take(200))
                {
                    var why = WhyHidden(t);
                    sb.AppendLine($"  #{t.Id} [{EffState(t)}] {t.Title}");
                    sb.AppendLine($"      resp={t.AssignedTo} pai={EffTaskParent(t)} sprint={t.IterationPath}");
                    sb.AppendLine($"      ClosedDate={(t.ClosedDate is DateTime cdl ? cdl.ToString("dd/MM/yyyy HH:mm") : "(nulo)")} "
                                + $"StateChange={(t.StateChangeDate is DateTime scl ? scl.ToString("dd/MM/yyyy HH:mm") : "(nulo)")}");
                    sb.AppendLine($"      motivo: {(string.IsNullOrWhiteSpace(why) ? "(WhyHidden nao apontou regra — ver acima)" : why)}");
                }
                if (hidden.Count > 200) sb.AppendLine($"  ... e mais {hidden.Count - 200} Tasks escondidas.");
                sb.AppendLine();
                System.IO.File.AppendAllText(FilterLogPath, sb.ToString());
            }
            catch { /* diagnostico nunca derruba o board */ }
        }

        /// <summary>
        /// Onde o usuario pediu para VER as Tasks encerradas que os filtros escondem (estado
        /// Closed oculto ou corte de "Closed dos ultimos N dias"). A chave e Story + PESSOA da
        /// faixa: na visao Pessoa x Task o 👁 revela so as Tasks encerradas DAQUELA pessoa, nao
        /// as da Story inteira. Pessoa vazia = todas (visao Projeto & Story, que nao tem faixa).
        /// E temporario: nao mexe no filtro da tela, nao vai para as preferencias e morre ao
        /// fechar o board.
        /// </summary>
        private readonly HashSet<string> _revealClosed = new(StringComparer.CurrentCultureIgnoreCase);

        private static string RevealKey(int storyId, string person) => storyId + "|" + (person ?? "");

        /// <summary>A Task esta liberada pelo 👁 da Story (pela faixa da pessoa dela ou pelo
        /// revelar geral da visao Projeto &amp; Story)?</summary>
        private bool IsRevealed(TfsImportService.SprintTaskCard t)
        {
            var parent = EffTaskParent(t);
            return _revealClosed.Contains(RevealKey(parent, t.AssignedTo ?? ""))
                || _revealClosed.Contains(RevealKey(parent, ""));
        }

        /// <summary>Botao 👁 do card da Story: mostra/esconde as Tasks encerradas DESTA PESSOA.
        /// So aparece quando ha o que revelar (ou quando ja esta revelado, para dar como desfazer).
        /// personKey vazio = visao sem faixa de pessoa, ai vale para todas as Tasks da Story.</summary>
        private UIElement? BuildRevealClosedButton(int storyId, string personKey)
        {
            if (storyId <= 0) return null;
            var key = RevealKey(storyId, personKey);
            var on = _revealClosed.Contains(key);
            var story = StoryById(storyId);
            if (story == null) return null;
            // So conta as Tasks da faixa (quando ha pessoa). Com o botao ligado elas ja passam
            // nos filtros, entao o teste olha o estado encerrado + o motivo de estarem fora.
            var mine = string.IsNullOrEmpty(personKey)
                ? story.Tasks
                : story.Tasks.Where(t => string.Equals(t.AssignedTo ?? "", personKey,
                    StringComparison.CurrentCultureIgnoreCase)).ToList();
            var hasHidden = mine.Any(t => IsClosedState(EffState(t)) && (on || !PassesFilters(t)));
            if (!hasHidden) return null;

            var btn = new Button
            {
                Content = on ? "🙈" : "👁", FontSize = 11,
                Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(0, 0, 3, 2),
                ToolTip = AppStrings.Get(on ? "Sprint_HideClosedTasksTip" : "Sprint_ShowClosedTasksTip")
            };
            btn.Click += (_, _) =>
            {
                if (!_revealClosed.Remove(key)) _revealClosed.Add(key);
                Render();
            };
            return btn;
        }

        /// <summary>
        /// Trilha das gravacoes de TAG no DevOps (bloqueio/NP), em tfs-write-log.txt na pasta do
        /// usuario. E uma linha por gravacao — volume baixo — e responde "o NX mandou o que?".
        /// </summary>
        private static void AppendTagWriteLog(string line)
        {
            try
            {
                var file = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NXProject.Community", "tfs-write-log.txt");
                System.IO.File.AppendAllText(file,
                    $"{DateTime.Now:dd/MM/yyyy HH:mm:ss} {line}{Environment.NewLine}");
            }
            catch { /* diagnostico nunca derruba a gravacao */ }
        }

        /// <summary>
        /// Recalcula e grava, em segundo plano, o tempo TOTAL de impedimento dos itens que acabaram
        /// de ser desbloqueados. Fica fora do "Atualizar TFS" de proposito: e uma leitura de
        /// historico por item e o desbloqueio ja foi aplicado — falhar aqui so avisa, nao desfaz.
        /// </summary>
        private async Task UpdateBlockDurationsAsync(List<int> ids)
        {
            var bad = new List<string>();
            foreach (var id in ids)
            {
                var (okDur, msgDur) = await BlockService.UpdateTotalDurationAsync(_options, id);
                AppendTagWriteLog($"#{id} duracao de bloqueio: {(okDur ? "OK" : "FALHOU: " + msgDur)}");
                if (!okDur) bad.Add($"#{id}: {msgDur}");
            }
            if (bad.Count == 0) return;
            await Dispatcher.InvokeAsync(() =>
                StatusText.Text = AppStrings.Get("Sprint_BlockDurationFailed", string.Join(" · ", bad)));
        }

        private string? _currentUser;
        // Estado alterado localmente (arrasto), pendente de gravar; e o já gravado com sucesso.
        private readonly Dictionary<int, string> _pending = new();
        private readonly Dictionary<int, string> _applied = new();
        // Chave de ordenação por card (para inserir na posição solta, não no fim).
        private readonly Dictionary<int, double> _order = new();
        // Cards marcados "Doing" (localmente) e os que já têm a tag no TFS.
        private readonly HashSet<int> _doing = new();
        // WIP: Tasks que estão ACIMA do limite configurado (por pessoa) e a contagem de cada pessoa.
        // Recalculado a cada Render(); não bloqueia nada, só marca.
        private readonly HashSet<int> _wipOver = new();
        /// <summary>Quem estava acima do limite de WIP NA CARGA do board. So o que mudou em
        /// relacao a esta foto e gravado: sem isso, qualquer gravacao saia corrigindo a tag de
        /// dezenas de cards antigos e o "N atualizadas no TFS" virava um numero sem sentido.</summary>
        private readonly HashSet<int> _wipBaseline = new();
        private readonly Dictionary<string, int> _wipCount = new(StringComparer.CurrentCultureIgnoreCase);
        private readonly HashSet<int> _appliedDoing = new();
        // "Done" marcado explicitamente no card: permite concluir o andamento SEM fechar a Task
        // (antes o Done só saía quando o estado virava Closed).
        private readonly HashSet<int> _done = new();
        private readonly HashSet<int> _appliedDone = new();
        // Descrição/trâmite editados (HTML), pendentes de gravar no TFS pelo botão Salvar TFS.
        private readonly Dictionary<int, string> _descPending = new();
        private readonly Dictionary<int, string> _tramitePending = new();
        // Responsável (System.AssignedTo) alterado (pendente) e o já gravado (baseline pós-Salvar).
        private readonly Dictionary<int, string> _ownerPending = new();
        private readonly Dictionary<int, string> _ownerApplied = new();
        // Nome/título (System.Title) alterado (pendente) e o já gravado (baseline pós-Salvar).
        private readonly Dictionary<int, string> _titlePending = new();
        private readonly Dictionary<int, string> _titleApplied = new();
        // Anexos do TFS enviados pelo botão do card. A UI só guarda o nome do arquivo; o real
        // fica no Azure DevOps e o id do attachment pode ser usado para download futuro.
        // Anexos enviados NESTA sessao, por Task — todos, nao so o ultimo: antes um segundo envio
        // substituia o primeiro no card ate a recarga, e parecia que so cabia um arquivo.
        private readonly Dictionary<int, List<TfsAttachmentService.TfsAttachmentInfo>> _attachments = new();
        /// <summary>Urls de anexos excluidos nesta sessao: somem do card sem esperar o reload.</summary>
        private readonly HashSet<string> _removedAttachmentUrls = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Anexos marcados para excluir: so vao ao DevOps no "Atualizar TFS".</summary>
        private readonly Dictionary<int, List<TfsAttachmentService.TfsAttachmentInfo>> _attachRemovePending = new();
        // HH estimado (OriginalEstimate) e HH realizado (CompletedWork) alterados, pendentes de gravar.
        private readonly Dictionary<int, double?> _estPending = new();
        private readonly Dictionary<int, double?> _donePending = new();
        /// <summary>
        /// Tasks que o usuario arrastou para uma coluna encerrada NESTA sessao e que ainda nao
        /// foram gravadas no TFS. O campo "HH Realizado" do card aparece por causa desta lista —
        /// nao basta olhar _pending: quem ja estava Closed no DevOps, foi para Active e voltou,
        /// termina sem mudanca de estado pendente e ficava sem o campo para ajustar as horas.
        /// </summary>
        private readonly HashSet<int> _closedDropped = new();
        // Tasks (New) marcadas para excluir; a exclusão no DevOps ocorre no Salvar TFS.
        private readonly HashSet<int> _deletePending = new();
        // Iteração (sprint) da Story alterada (pendente) e a já gravada (baseline pós-Salvar).
        private readonly Dictionary<int, string> _iterPending = new();
        private readonly Dictionary<int, string> _iterApplied = new();
        // Feature (pai) da Story alterada (pendente: novo FeatureId) e a já gravada.
        private readonly Dictionary<int, int> _featurePending = new();
        /// <summary>Task -> nova Story (System.Parent) na fila do "Atualizar TFS".</summary>
        private readonly Dictionary<int, int> _taskParentPending = new();
        private readonly Dictionary<int, int> _taskParentApplied = new();
        /// <summary>
        /// Task marcada para mover (botao direito no card). A Story destino costuma estar longe
        /// na tela — as vezes nem visivel —, entao arrastar nao serve: marca-se aqui e depois
        /// escolhe-se a Story destino pelo menu de contexto dela. Vale so para a sessao; nao e
        /// alteracao pendente ate o "mover para esta Story" ser confirmado.
        /// </summary>
        private readonly List<int> _moveTaskIds = new();

        /// <summary>
        /// Itens recolhidos (Work Item Project, EPIC, Feature ou Story): o card do proprio item
        /// fica, mas tudo abaixo dele sai da tela. Serve para tirar do caminho o que nao esta no
        /// foco do momento. Fica nas preferencias, entao a tela reabre como voce deixou.
        /// </summary>
        private readonly HashSet<int> _collapsed = new();

        /// <summary>Algum ancestral (Project/EPIC/Feature) da linha esta recolhido?</summary>
        private bool IsCollapsed(int id) => id > 0 && _collapsed.Contains(id);

        /// <summary>
        /// Pessoas recolhidas na visao Pessoa &amp; Task. Como a "chave" da faixa e o nome (nao
        /// ha work item por tras), a lista e de texto, separada do <see cref="_collapsed"/>.
        /// </summary>
        private readonly HashSet<string> _collapsedPeople = new(StringComparer.CurrentCultureIgnoreCase);
        private readonly Dictionary<int, int> _featureApplied = new();
        // Data de Início (Data_Inicio) da Story alterada (pendente) e a já gravada.
        private readonly Dictionary<int, DateTime?> _startPending = new();
        /// <summary>Data alvo (Data_Fim) da Task na fila do "Atualizar TFS".</summary>
        private readonly Dictionary<int, DateTime?> _finishPending = new();
        /// <summary>
        /// Datas JA GRAVADAS no DevOps nesta sessao. O card le a data da carga do board, que so e
        /// refeita no reload: sem esta baseline, gravar a data alvo tirava a pendencia e o card
        /// voltava a mostrar o valor velho — parecia que a gravacao nao tinha funcionado.
        /// </summary>
        private readonly Dictionary<int, DateTime?> _finishApplied = new();
        private readonly Dictionary<int, DateTime?> _startApplied = new();
        /// <summary>
        /// Data alvo colocada pelo proprio arrasto, por origem. Cada regra so desfaz a data que
        /// ELA colocou: antes o "saiu de Closed" apagava qualquer data alvo igual a hoje — e a
        /// calculada no arrasto para Active (Task curta, que termina no mesmo dia) sumia na hora.
        /// </summary>
        private readonly HashSet<int> _finishAutoByActive = new();
        private readonly HashSet<int> _finishAutoByClosed = new();
        // Critérios de Aceitação (HTML) pendentes por Story — gravados no "Atualizar TFS".
        private readonly Dictionary<int, string> _acPending = new();
        // Bloqueio alterado (pendente: novo valor) e conjuntos de tags já gravados.
        // O nome da tag é configurável em "Configurar DevOps" (padrão "BLOCK").
        private readonly Dictionary<int, bool> _blockPending = new();
        // Tag "nao planejada" (NP) da Task: alterada no editor, gravada no "Atualizar TFS".
        private readonly Dictionary<int, bool> _unplannedPending = new();
        private readonly Dictionary<int, string> _tagsApplied = new(); // tags após gravar (baseline)
        // Prioridade da Task alterada (pendente) e a já gravada (baseline após Salvar TFS).
        private readonly Dictionary<int, int> _prioPending = new();
        private readonly Dictionary<int, int> _prioApplied = new();
        // Rank (StackRank) efetivo das Stories e os ids com rank pendente de gravar (mover Story).
        private readonly Dictionary<int, double> _storyRank = new();
        private readonly HashSet<int> _storyRankPending = new();
        // Estado da Story alterado (arrasto entre colunas no StoryBoard), pendente de gravar.
        private readonly Dictionary<int, string> _storyStatePending = new();
        private readonly Dictionary<int, string> _storyStateApplied = new();
        // Rank (StackRank) efetivo das Tasks e pendências (mover a Task dentro do grupo de prioridade).
        private readonly Dictionary<int, double> _taskRank = new();
        private readonly HashSet<int> _taskRankPending = new();
        // Lookup dos cards efetivos por id (para saber prioridade/rank ao arrastar).
        private readonly Dictionary<int, TfsImportService.SprintTaskCard> _cardById = new();
        // Novos cards (Story/Task) criados localmente, sem ID do TFS ainda (id temporário negativo).
        private sealed class NewCard { public int TempId; public string Type = ""; public string Title = ""; public int ParentId; public string FeatureTitle = ""; public int FeatureId; public string AssignedTo = ""; public double? Effort; public string Description = ""; public string IterationPath = ""; public DateTime? StartDate; }
        private readonly List<NewCard> _newCards = new();
        private int _nextTempId = -1;
        // Sprints selecionadas (1 = normal; várias = união; vazio = "todas"). O caminho "único"
        // só existe quando exatamente uma sprint específica está ativa (usado p/ criar itens/last).
        private List<string> _sprintPaths = new();
        private string _sprintPath => _sprintPaths.Count == 1 ? _sprintPaths[0] : "";

        // Visao ativa: 0 = Projeto & Story, 1 = Pessoa & Task. Botoes segmentados no lugar da
        // combo (duas opcoes, um clique); o resto do codigo continua lendo um indice.
        private int ViewIndex
        {
            get => ViewPersonBtn.IsChecked == true ? 1 : 0;
            set { if (value == 1) ViewPersonBtn.IsChecked = true; else ViewBoardBtn.IsChecked = true; }
        }

        private static string SprintSettingsPath => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NXProject.Community", "sprintview.json");

        // Preferências persistidas da tela (últimos filtros do usuário).
        private sealed class SprintPrefs
        {
            public string LastSprintPath { get; set; } = "";
            public int? ClosedDays { get; set; }        // só > 0 (0/null = todos)
            public List<string>? Persons { get; set; }   // vazio/null = todas
            public int? View { get; set; }              // 0 = Por Story, 1 = Pessoa & Task
            public bool? OnlySchedule { get; set; }
            public bool? OnlyBlocked { get; set; }    // só Tasks com a tag de bloqueio
            public bool? OnlyUnplanned { get; set; }  // só Tasks com a tag de não planejada
            public bool? OnlyDoneActive { get; set; } // só Tasks Done que ainda não foram encerradas
            public bool? OnlyDoing { get; set; }      // só Tasks em andamento (Doing, não encerradas)
            public bool? OnlyTaskActive { get; set; } // só Tasks com o ESTADO Active no DevOps
            public bool? EditMode { get; set; }
            public List<string>? HiddenStates { get; set; }
            public List<string>? SprintPaths { get; set; }   // >1 = multi-seleção de sprints
            public Dictionary<string, string>? StateColors { get; set; } // estado(lower) -> #RRGGBB
            public bool AutoOpen { get; set; }   // abrir o TaskBoard ao iniciar o NX
            public int? WipLimit { get; set; }   // limite de Tasks em andamento POR PESSOA (0 = sem limite; null = 3)
            public int? PriorityMax { get; set; } // maximo de Priority do template (descoberto via validateOnly)
            public DateTime? PriorityMaxAt { get; set; } // quando foi descoberto (vence: o template pode mudar)
            public bool? FieldCache { get; set; } // ler campos do TFS em cache (null = ligado)
            public bool? CardDocsOnly { get; set; } // no card, listar so PDF/Word/Excel (null = ligado)
            public bool? LastSprintPerPerson { get; set; } // so a ultima sprint (ja iniciada) de cada pessoa
            public bool? ShowStoryNoTask { get; set; }     // Story sem Task no board (null = mostra)
            public bool? ShowFeatureNoStory { get; set; }  // Feature/EPIC sem Story (null = mostra)
            public string? CardDocExtensions { get; set; } // extensoes de "documento", separadas por virgula
            public bool? ShowEpic { get; set; }  // coluna EPIC na visão Pessoa & Task (null = mostra)
            public bool? ShowProjPerson { get; set; } // coluna Projeto na visão Pessoa & Task (null = oculta)
            public List<int>? CollapsedEpics { get; set; } // legado (so EPICs); lido na abertura
            public List<int>? CollapsedNodes { get; set; } // Project/EPIC/Feature/Story recolhidos
            public List<string>? CollapsedPeople { get; set; } // faixas de pessoa recolhidas
            public bool? ShowProjCol { get; set; } // coluna Projeto na visão Projeto & Story (null = mostra)
            public bool? ShowFeatCol { get; set; } // coluna Feature na visão Pessoa & Task (null = mostra)
            public bool? ShowEpicCol { get; set; } // coluna EPIC na visão Projeto & Story (null = mostra)
            public bool WinMaximized { get; set; }
            public double WinLeft { get; set; }
            public double WinTop { get; set; }
            public double WinWidth { get; set; }
            public double WinHeight { get; set; }
        }
        private SprintPrefs _prefs = new();
        private bool _restoringPrefs;   // evita salvar enquanto restaura os controles
        private bool _firstBoardLoad = true; // aplica os filtros salvos só na 1ª carga
        private bool _boardEverLoaded;  // só salva prefs depois da 1ª carga (evita sobrescrever com defaults)

        private static SprintPrefs LoadPrefs()
        {
            try
            {
                var p = SprintSettingsPath;
                if (System.IO.File.Exists(p))
                    return System.Text.Json.JsonSerializer.Deserialize<SprintPrefs>(System.IO.File.ReadAllText(p)) ?? new();
            }
            catch { }
            return new();
        }

        /// <summary>Se o usuário pediu para abrir o TaskBoard automaticamente ao iniciar o NX.</summary>
        public static bool ShouldAutoOpenTaskBoard() => LoadPrefs().AutoOpen;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            if (_prefs.WinMaximized) WindowState = WindowState.Maximized;
        }

        private static string? LoadLastSprintPath() => LoadPrefs().LastSprintPath is { Length: > 0 } s ? s : null;
        private static int? LoadClosedDays() => LoadPrefs().ClosedDays is int d && d > 0 ? d : (int?)null;

        // Persiste o estado atual dos filtros (chamado a cada mudança). ClosedDays só grava se > 0.
        private void SavePrefs()
        {
            if (_restoringPrefs || !_boardEverLoaded) return;
            try
            {
                _prefs.LastSprintPath = _sprintPath ?? "";
                _prefs.SprintPaths = _sprintPaths.Count > 1 ? _sprintPaths.ToList() : null;
                // Geometria da janela (usa RestoreBounds p/ manter o tamanho normal mesmo maximizado).
                _prefs.WinMaximized = WindowState == WindowState.Maximized;
                var b = WindowState == WindowState.Maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
                if (b.Width > 200 && b.Height > 200)
                { _prefs.WinLeft = b.Left; _prefs.WinTop = b.Top; _prefs.WinWidth = b.Width; _prefs.WinHeight = b.Height; }
                _prefs.ClosedDays = _closedDays > 0 ? _closedDays : (int?)null;
                _prefs.Persons = _selectedPeople.Count > 0 ? _selectedPeople.ToList() : null;
                _prefs.CollapsedNodes = _collapsed.Count > 0 ? _collapsed.ToList() : null;
                _prefs.CollapsedPeople = _collapsedPeople.Count > 0 ? _collapsedPeople.ToList() : null;
                _prefs.CollapsedEpics = null;   // substituido por CollapsedNodes
                _prefs.View = ViewIndex;
                _prefs.OnlySchedule = OnlyScheduleCheck.IsChecked == true;
                _prefs.OnlyBlocked = OnlyBlockedCheck.IsChecked == true;
                _prefs.OnlyUnplanned = OnlyUnplannedCheck.IsChecked == true;
                _prefs.OnlyDoneActive = OnlyDoneActiveCheck.IsChecked == true;
                _prefs.OnlyDoing = OnlyDoingCheck.IsChecked == true;
                _prefs.OnlyTaskActive = OnlyTaskActiveCheck.IsChecked == true;
                _prefs.EditMode = EditModeCheck.IsChecked == true;
                _prefs.HiddenStates = _hiddenStates.ToList();
                var p = SprintSettingsPath;
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
                System.IO.File.WriteAllText(p, System.Text.Json.JsonSerializer.Serialize(_prefs));
            }
            catch { }
        }

        private static bool IsClosedState(string s) =>
            s.Equals("Closed", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Done", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Completed", StringComparison.OrdinalIgnoreCase);

        public TfsSprintWindow(IReadOnlySet<int>? scheduleIds = null, Action<int>? openInSchedule = null,
            string? preferredSprint = null, IReadOnlyList<int>? scheduleOrder = null, int openRootId = 0)
        {
            _openRootId = openRootId;
            InitializeComponent();
            _options = TfsConnectionStore.Load("NXProject.Community");
            _scheduleIds = scheduleIds ?? new HashSet<int>();
            // Posição de cada item na ÁRVORE do cronograma aberto — usada para ordenar o filtro
            // na mesma ordem que o usuário vê no cronograma.
            if (scheduleOrder != null)
                for (int i = 0; i < scheduleOrder.Count; i++)
                    _scheduleRank.TryAdd(scheduleOrder[i], i);
            _openInSchedule = openInSchedule;
            _preferredSprint = preferredSprint;
            SearchScopeCombo.Items.Add(AppStrings.Get("Sprint_ScopeBoth"));  // 0
            SearchScopeCombo.Items.Add(AppStrings.Get("Sprint_ScopeTask"));  // 1
            SearchScopeCombo.Items.Add(AppStrings.Get("Sprint_ScopeStory")); // 2
            SearchScopeCombo.Items.Add(AppStrings.Get("Sprint_ScopeLevels")); // 3 = Feature/EPIC/Project
            SearchScopeCombo.Items.Add(AppStrings.Get("Sprint_ScopePerson")); // 4 = responsavel
            SearchScopeCombo.SelectedIndex = 0;
            // Carrega os últimos filtros salvos (aplicados na 1ª carga do board).
            _prefs = LoadPrefs();
            // Maximo de Priority: a descoberta custa ~1,4s em VALIDATEONLY e era refeita a cada
            // abertura (o campo vive na janela, e a janela e nova toda vez). Guardado nas prefs.
            // Vale so no dia em que foi descoberto: a 1a abertura do dia redescobre e o valor se
            // corrige sozinho se o template mudar a faixa.
            if (_prefs.PriorityMax is int prioMax && prioMax > 0
                && _prefs.PriorityMaxAt is DateTime prioAt
                && prioAt.ToLocalTime().Date == DateTime.Today)
                _discoveredPrioMax = prioMax;
            // Visao ja nasce na opcao salva (0 = Projeto & Story, 1 = Pessoa & Task). Antes
            // marcava a 0 aqui e so trocava na 1a carga do board, e o botao "pulava" na tela.
            ViewIndex = _prefs.View is int vw0 && vw0 is 0 or 1 ? vw0 : 0;
            AutoOpenCheck.IsChecked = _prefs.AutoOpen;
            // Cache dos campos do TFS: ligado por padrao (null = ligado).
            CardDocsOnlyCheck.IsChecked = _prefs.CardDocsOnly ?? true;
            LastSprintPerPersonCheck.IsChecked = _prefs.LastSprintPerPerson ?? false;
            ShowStoryNoTaskCheck.IsChecked = _prefs.ShowStoryNoTask ?? true;
            ShowFeatureNoStoryCheck.IsChecked = _prefs.ShowFeatureNoStory ?? true;
            ApplyCardDocExtensions(_prefs.CardDocExtensions);
            CardDocExtsBox.Text = FormatExtensions(_cardDocExtensions);
            CardDocExtsBox.IsEnabled = CardDocsOnlyCheck.IsChecked == true;
            FieldCacheCheck.IsChecked = _prefs.FieldCache ?? true;
            TfsImportService.FieldRefCacheEnabled = FieldCacheCheck.IsChecked == true;
            // Sem a variavel de ambiente, as marcacoes de tempo nem sao montadas.
            TfsImportService.LoadTrace.Enabled = LoadPerfEnabled;
            WipLimitBox.Text = WipLimit().ToString();
            // Restaura geometria/estado da janela do TaskBoard.
            if (_prefs.WinWidth > 200 && _prefs.WinHeight > 200)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = _prefs.WinLeft; Top = _prefs.WinTop; Width = _prefs.WinWidth; Height = _prefs.WinHeight;
            }
            Closing += OnWindowClosing;
            // Restaura a preferência de "dias anteriores" do Closed (0/ausente = padrão 30).
            if (_prefs.ClosedDays is int sd && sd > 0)
            {
                _closedDays = sd;
                ClosedDaysBox.Text = sd.ToString();
            }
            Loaded += async (_, _) => await LoadSprintsAsync();
        }

        private async Task LoadSprintsAsync()
        {
            BeginLoading();
            try
            {
                // Estas duas ficam FORA do cronometro do board, mas pesam na impressao de lentidao:
                // entram na mesma medicao por etapa.
                TfsImportService.LoadTrace.Reset();
                var userWatch = Stopwatch.StartNew();
                _currentUser = await TfsImportService.GetCurrentUserDisplayNameAsync(_options);
                TfsImportService.LoadTrace.Mark("connectionData", userWatch.ElapsedMilliseconds);
                var sprintWatch = Stopwatch.StartNew();
                _sprints = await TfsImportService.ListSprintsAsync(_options);
                TfsImportService.LoadTrace.Mark("iterations", sprintWatch.ElapsedMilliseconds);
                // Lista multi-seleção (estilo do filtro de pessoa): cada sprint com data início–fim.
                PopulateSprintList();
                // Seleção inicial: multi salva → última sprint salva → sprint atual (data)
                // → sugerida do cronograma → última da lista.
                var initial = new List<string>();
                if (_prefs.SprintPaths is { Count: > 1 } saved2
                    && saved2.All(p => _sprints.Any(s => string.Equals(s.Path, p, StringComparison.OrdinalIgnoreCase))))
                    initial = saved2.ToList();
                else
                {
                    string? one = null;
                    var saved = LoadLastSprintPath();
                    if (!string.IsNullOrWhiteSpace(saved) && _sprints.Any(s => string.Equals(s.Path, saved, StringComparison.OrdinalIgnoreCase)))
                        one = saved;
                    one ??= CurrentSprintPath();
                    if (one == null && !string.IsNullOrWhiteSpace(_preferredSprint)
                        && _sprints.Any(s => string.Equals(s.Path, _preferredSprint, StringComparison.OrdinalIgnoreCase)))
                        one = _preferredSprint;
                    one ??= _sprints.LastOrDefault()?.Path;
                    if (!string.IsNullOrEmpty(one)) initial.Add(one);
                }
                StatusText.Text = "";
                ApplySprintChecks(initial);
                await ReloadBoardAsync(initial);
            }
            catch (Exception ex)
            {
                StatusText.Text = "";
                MessageBox.Show(this, AppStrings.Get("Sprint_Error", ex.Message),
                    "NXProject", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { EndLoading(); }
        }

        // Preenche os checkboxes das sprints com o rótulo "Nome (dd/MM/aa–dd/MM/aa)".
        private void PopulateSprintList()
        {
            SprintMultiList.Children.Clear();
            foreach (var sp in _sprints.Where(s => !string.IsNullOrEmpty(s.Path)))
                SprintMultiList.Children.Add(new CheckBox { Content = SprintLabel(sp), Tag = sp.Path,
                    IsChecked = _sprintPaths.Contains(sp.Path), Margin = new Thickness(0, 1, 0, 1) });
        }

        private static string SprintLabel(TfsImportService.SprintInfo s)
        {
            if (s.Start is { } st && s.End is { } en)
                return $"{s.Name}   ({st:dd/MM/yy}–{en:dd/MM/yy})";
            return s.Name;
        }

        private void ApplySprintChecks(List<string> paths)
        {
            foreach (var cb in SprintMultiList.Children.OfType<CheckBox>())
                cb.IsChecked = cb.Tag is string tp && paths.Contains(tp);
            UpdateSprintToggleText();
        }

        private void UpdateSprintToggleText()
        {
            if (_sprintPaths.Count == 0)
                SprintFilterToggle.Content = AppStrings.Get("Sprint_AllSprints");
            else if (_sprintPaths.Count == 1)
            {
                var sp = _sprints.FirstOrDefault(s => string.Equals(s.Path, _sprintPaths[0], StringComparison.OrdinalIgnoreCase));
                SprintFilterToggle.Content = sp != null ? SprintLabel(sp) : _sprintPaths[0];
            }
            else
                SprintFilterToggle.Content = AppStrings.Get("Sprint_MultiN", _sprintPaths.Count.ToString());
        }

        // Caminho da sprint atual pela data (a de menor duração que contém hoje).
        private string? CurrentSprintPath()
        {
            var today = DateTime.Today;
            return _sprints
                .Where(s => !string.IsNullOrEmpty(s.Path) && s.Start is { } st && s.End is { } en && today >= st && today <= en)
                .OrderBy(s => (s.End!.Value - s.Start!.Value).TotalDays)
                .FirstOrDefault()?.Path;
        }

        /// <summary>
        /// Volta TODOS os recortes ao padrao de tela nova: sem pessoa, sem projeto, sem busca,
        /// sem as marcacoes "somente ...", Closed escondido e o corte de dias no default.
        /// Nao mexe na sprint escolhida nem em nada pendente de gravacao — e so filtro.
        /// </summary>
        private void OnResetFiltersClick(object sender, RoutedEventArgs e)
        {
            _selectedPeople.Clear();
            if (_board != null) PopulatePersonFilter(BoardPeople(_board));
            if (PersonSearchBox != null) PersonSearchBox.Text = "";

            _selectedStoryIds.Clear();
            PopulateStoryFilter();

            SearchBox.Text = "";
            SearchScopeCombo.SelectedIndex = 0;

            OnlyScheduleCheck.IsChecked = false;
            OnlyBlockedCheck.IsChecked = false;
            OnlyUnplannedCheck.IsChecked = false;
            OnlyDoneActiveCheck.IsChecked = false;
            OnlyDoingCheck.IsChecked = false;
            OnlyTaskActiveCheck.IsChecked = false;
            // Padrao: os "vazios" aparecem. O limpar sempre volta a mostrar Story sem Task e
            // Feature/EPIC sem Story — esconde-los e uma escolha momentanea, nao o estado normal.
            ShowStoryNoTaskCheck.IsChecked = true;
            ShowFeatureNoStoryCheck.IsChecked = true;
            LastSprintPerPersonCheck.IsChecked = false;   // recorte forte: o limpar sempre desliga

            // Padrao de tela nova: Closed escondido e o corte de dias de volta ao default.
            _hiddenStates.Clear();
            if (_board != null)
                foreach (var st in _board.States.Where(IsClosedState)) _hiddenStates.Add(st);
            PopulateStateFilter();
            _closedDays = DefaultClosedDays;
            ClosedDaysBox.Text = DefaultClosedDays.ToString();

            // O que esta recolhido tambem e recorte de tela: o "limpar" expande tudo de volta.
            // (Fora daqui isso fica gravado e volta do jeito que estava na proxima abertura.)
            _collapsed.Clear();
            _collapsedPeople.Clear();
            // O "Limpar filtros" ja define um estado novo: nao ha mais o que restaurar.
            _restoreFilters = null;
            RestoreFiltersButton.Visibility = Visibility.Collapsed;

            StoryFilterToggle.IsChecked = false;
            RenderBusy();
            SavePrefs();
        }

        // Recarrega do TFS. Se houver mudanças pendentes, pede confirmação (o reload as descarta).
        private async void OnReloadClick(object sender, RoutedEventArgs e)
        {
            if (PendingCount() > 0 &&
                MessageBox.Show(this, AppStrings.Get("Sprint_ReloadConfirm"), "NXProject",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            await ReloadBoardAsync(_sprintPaths.ToList());
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        private bool _closingHandled;

        /// <summary>
        /// Fechando com alteracoes pendentes: pergunta se grava no TFS, se descarta (reverte) ou
        /// se volta ao board. Vale para o botao Fechar e para o X da janela.
        /// </summary>
        private async void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_closingHandled || PendingCount() == 0) { SavePrefs(); return; }

            var answer = MessageBox.Show(this,
                AppStrings.Get("Sprint_CloseSaveQuestion", PendingCount().ToString()), "NXProject",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) { e.Cancel = true; return; }
            if (answer == MessageBoxResult.No) { SavePrefs(); return; }   // reverter = so fechar

            // Gravar: a janela fica aberta durante a gravacao; so fecha se nao sobrou pendencia
            // (falha de permissao, etc. mantem o board aberto para o usuario ver o que faltou).
            e.Cancel = true;
            await UpdateTfsAsync();
            if (PendingCount() == 0)
            {
                _closingHandled = true;
                SavePrefs();
                Close();
            }
        }

        // Ao abrir o popup, reflete a seleção atual (evita "união" acidental com a sprint anterior).
        private void OnSprintFilterOpened(object sender, RoutedEventArgs e) => ApplySprintChecks(_sprintPaths.ToList());

        // Aplica a seleção de sprints (0 marcadas = todas as sprints).
        private async void OnSprintMultiApply(object sender, RoutedEventArgs e)
        {
            var paths = SprintMultiList.Children.OfType<CheckBox>()
                .Where(cb => cb.IsChecked == true && cb.Tag is string).Select(cb => (string)cb.Tag!).ToList();
            SprintFilterToggle.IsChecked = false;
            await ReloadBoardAsync(paths);
            SavePrefs();
        }

        private void OnSprintMultiNone(object sender, RoutedEventArgs e)
        {
            foreach (var cb in SprintMultiList.Children.OfType<CheckBox>()) cb.IsChecked = false;
        }

        // Compat: recarrega uma única sprint (ou "todas" com path vazio).
        private Task ReloadBoardAsync(string path) =>
            ReloadBoardAsync(string.IsNullOrEmpty(path) ? new List<string>() : new List<string> { path });

        /// <summary>
        /// Barra indeterminada + "Carregando..." enquanto a lista de sprints e o board vem do
        /// DevOps, para a abertura nao parecer travada. Contador porque a lista de sprints chama
        /// a carga do board por dentro: a barra so some quando o ultimo carregamento termina.
        /// </summary>
        private int _loadingDepth;

        private void BeginLoading()
        {
            if (_loadingDepth++ == 0)
            {
                SaveProgress.IsIndeterminate = true;
                SaveProgress.Visibility = Visibility.Visible;
            }
            StatusText.Text = AppStrings.Get("Sprint_Loading");
        }

        private void EndLoading()
        {
            if (--_loadingDepth > 0) return;
            _loadingDepth = 0;
            SaveProgress.IsIndeterminate = false;
            SaveProgress.Visibility = Visibility.Collapsed;
            if (StatusText.Text == AppStrings.Get("Sprint_Loading")) StatusText.Text = "";
        }

        // Carrega/recarrega o board (1+ sprints) e reseta o estado local (pendências, filtros).
        private async Task ReloadBoardAsync(List<string> paths)
        {
            BeginLoading();
            try
            {
                _sprintPaths = paths;
                UpdateSprintToggleText();
                var loadWatch = Stopwatch.StartNew();
                _board = await TfsImportService.BuildSprintBoardAsync(_options, paths);
                AppendLoadPerf(
                    $"TaskBoard.Load: {loadWatch.ElapsedMilliseconds} ms ({_board.Stories.Count} stories, "
                    + $"{_board.Stories.Sum(x => x.Tasks.Count)} tasks, {paths.Count} sprint(s))" + Environment.NewLine
                    + "  etapas: " + TfsImportService.LoadTrace.Dump());
                TfsImportService.LoadTrace.Reset();
                var people = BoardPeople(_board);
                // Mantém só as pessoas ainda existentes no board (preserva a seleção múltipla).
                _selectedPeople.RemoveWhere(p => !people.Contains(p, StringComparer.CurrentCultureIgnoreCase));
                PopulatePersonFilter(people);
                _storyById = _board.Stories.Where(s => s.Id > 0).ToDictionary(s => s.Id);
                // Trocar de sprint NAO joga fora o recorte de Projeto: mantem as Stories que
                // continuam existindo na nova selecao de sprints. So quando nao sobra nenhuma
                // (ou nunca houve recorte) e que volta o padrao — as Stories do cronograma aberto.
                var kept = _selectedStoryIds.Where(_storyById.ContainsKey).ToList();
                _selectedStoryIds.Clear();
                if (kept.Count > 0)
                    foreach (var id in kept) _selectedStoryIds.Add(id);
                else
                {
                    // Com projeto aberto: por padrão filtra só as Stories dele (Todo Portfólio desmarcado).
                    var openIds = _board.Stories.Where(s => s.Id > 0 && _scheduleIds.Contains(s.Id)).Select(s => s.Id).ToList();
                    foreach (var oid in openIds) _selectedStoryIds.Add(oid);
                }
                // Na PRIMEIRA carga: restaura os filtros salvos (ou o padrão: esconder Closed).
                // Nos reloads seguintes: PRESERVA a seleção do usuário (só descarta estados que
                // sumiram do board), para não voltar a esconder Closed a cada troca de sprint.
                if (_firstBoardLoad)
                {
                    _restoringPrefs = true;
                    try
                    {
                        _selectedPeople.Clear();
                        if (_prefs.Persons is { Count: > 0 } pl)
                            foreach (var p in pl.Where(x => people.Contains(x, StringComparer.CurrentCultureIgnoreCase)))
                                _selectedPeople.Add(p);
                        else if (MatchCurrentUser() is { } me) // padrão: o usuário atual
                            _selectedPeople.Add(me);
                        PopulatePersonFilter(people);
                        _collapsed.Clear();
                        foreach (var nodeId in (_prefs.CollapsedNodes ?? _prefs.CollapsedEpics) ?? new List<int>())
                            _collapsed.Add(nodeId);
                        _collapsedPeople.Clear();
                        foreach (var who in _prefs.CollapsedPeople ?? new List<string>())
                            _collapsedPeople.Add(who);
                        if (_prefs.View is int vw && vw is 0 or 1) ViewIndex = vw;
                        if (_prefs.OnlySchedule is bool os) OnlyScheduleCheck.IsChecked = os;
                        if (_prefs.OnlyBlocked is bool ob) OnlyBlockedCheck.IsChecked = ob;
                        if (_prefs.OnlyUnplanned is bool ou) OnlyUnplannedCheck.IsChecked = ou;
                        if (_prefs.OnlyDoneActive is bool oda) OnlyDoneActiveCheck.IsChecked = oda;
                        if (_prefs.OnlyDoing is bool odg) OnlyDoingCheck.IsChecked = odg;
                        if (_prefs.OnlyTaskActive is bool ota) OnlyTaskActiveCheck.IsChecked = ota;
                        if (_prefs.EditMode is bool em) EditModeCheck.IsChecked = em;
                        _hiddenStates.Clear();
                        if (_prefs.HiddenStates is { } hs)
                            foreach (var st in hs) _hiddenStates.Add(st); // pode ser vazio (Closed visível)
                        else
                            foreach (var st in _board.States.Where(IsClosedState)) _hiddenStates.Add(st);
                    }
                    finally { _restoringPrefs = false; }
                    _firstBoardLoad = false;
                }
                else
                {
                    _hiddenStates.RemoveWhere(s => !_board.States.Contains(s, StringComparer.OrdinalIgnoreCase));
                }
                _boardEverLoaded = true; // a partir daqui, mudanças de filtro podem ser salvas
                _pending.Clear();
                _applied.Clear();
                _order.Clear();
                _doing.Clear();
                _appliedDoing.Clear();
                _done.Clear();
                _appliedDone.Clear();
                _descPending.Clear();
                _tramitePending.Clear();
                _ownerPending.Clear();
                _ownerApplied.Clear();
                _titlePending.Clear();
                _titleApplied.Clear();
                _estPending.Clear();
                _donePending.Clear();
                _closedDropped.Clear();
                _deletePending.Clear();
                _iterPending.Clear();
                _iterApplied.Clear();
                _blockPending.Clear(); _unplannedPending.Clear();
                _tagsApplied.Clear();
                _featurePending.Clear();
                _featureApplied.Clear();
                _taskParentPending.Clear();
                _taskParentApplied.Clear();
                _moveTaskIds.Clear();
                _startPending.Clear(); _startApplied.Clear();
                _finishPending.Clear(); _finishApplied.Clear();
                _finishAutoByActive.Clear(); _finishAutoByClosed.Clear();
            _acPending.Clear();
                _acPending.Clear();
                _newCards.Clear();
                _prioPending.Clear();
                _prioApplied.Clear();
                _storyRank.Clear();
                _storyRankPending.Clear();
                _storyStatePending.Clear();
                _storyStateApplied.Clear();
                _taskRank.Clear();
                _taskRankPending.Clear();
                double sr = 0;
                foreach (var s in _board.Stories.Where(s => s.Id > 0))
                    _storyRank[s.Id] = double.IsNaN(s.StackRank) ? (1e6 + sr++) : s.StackRank;
                double k = 0;
                foreach (var t in _board.Stories.SelectMany(s => s.Tasks))
                {
                    _order[t.Id] = k++;
                    _taskRank[t.Id] = double.IsNaN(t.StackRank) ? (1e6 + k) : t.StackRank;
                    if (HasTag(t.Tags, "Doing") || HasTag(t.Tags, "Done")) { _doing.Add(t.Id); _appliedDoing.Add(t.Id); }
                    if (HasTag(t.Tags, "Done")) { _done.Add(t.Id); _appliedDone.Add(t.Id); }
                }
                UpdatePendingButton();
                PopulateStoryFilter();
                PopulateStateFilter();
                Render();   // o Render recalcula o WIP; a foto e tirada logo apos ele
                _wipBaseline.Clear();
                foreach (var wid in _wipOver) _wipBaseline.Add(wid);

                // Descobre uma vez por sessão o máximo de Priority aceito pelo template (validateOnly).
                if (_discoveredPrioMax == 0)
                {
                    var sample = _board.Stories.SelectMany(s => s.Tasks).FirstOrDefault(t => t.Id > 0);
                    if (sample != null)
                    {
                        // Roda DEPOIS do cronometro do board: ganha linha propria no log.
                        var prioWatch = Stopwatch.StartNew();
                        _discoveredPrioMax = await TfsImportService.DiscoverTaskPriorityMaxAsync(_options, sample.Id);
                        AppendLoadPerf("TaskBoard.PrioridadeMax: " + prioWatch.ElapsedMilliseconds + " ms");
                        if (_discoveredPrioMax > 0)
                        {
                            // Grava direto: o SavePrefs comum ainda pode estar bloqueado nesta 1a carga.
                            _prefs.PriorityMax = _discoveredPrioMax;
                            _prefs.PriorityMaxAt = DateTime.UtcNow;
                            try
                            {
                                var pp = SprintSettingsPath;
                                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(pp)!);
                                System.IO.File.WriteAllText(pp, System.Text.Json.JsonSerializer.Serialize(_prefs));
                            }
                            catch { }
                        }
                        if (_discoveredPrioMax > 0) Render(); // re-render com a faixa correta
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "";
                MessageBox.Show(this, AppStrings.Get("Sprint_Error", ex.Message),
                    "NXProject", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { EndLoading(); }
        }

        // Cards efetivos (do TFS + novos locais) para renderizar/filtrar.
        private List<(TfsImportService.SprintStoryRow Story, List<TfsImportService.SprintTaskCard> Tasks)> EffectiveStories()
        {
            var list = new List<(TfsImportService.SprintStoryRow, List<TfsImportService.SprintTaskCard>)>();
            if (_board == null) return list;
            // Os dois agrupamentos saem UMA vez, nao por Story: com o board inteiro carregado,
            // varrer todas as Stories (e todos os cards novos) dentro do laco custava caro.
            var movedByTarget = _board.Stories.SelectMany(o => o.Tasks)
                .Where(t => HasLiveParentMove(t.Id))
                .ToLookup(t => _taskParentPending[t.Id]);
            var newTasksByParent = _newCards.Where(n => n.Type == "Task").ToLookup(n => n.ParentId);
            foreach (var s in _board.Stories)
            {
                // Move de Story pendente: a Task ja aparece na Story DESTINO (e sai da origem),
                // mesmo antes de gravar — o selo laranja no card diz que a troca esta na fila.
                var tks = s.Tasks.Where(t => !HasLiveParentMove(t.Id)).ToList();
                tks.AddRange(movedByTarget[s.Id]);
                tks.AddRange(newTasksByParent[s.Id].Select(NewToCard));
                list.Add((s, tks));
            }
            foreach (var ns in _newCards.Where(n => n.Type == "Story"))
            {
                // Leva o responsavel do card novo para a linha: e por ele que a visao
                // Pessoa & Task decide em qual faixa a Story aparece.
                // E herda a hierarquia (ids, titulos e RANKS) de uma Story irma da mesma Feature:
                // sem os ranks dos niveis a Story nova nao tinha onde se encaixar e ia parar no
                // fim da faixa, longe do bloco da Feature onde foi criada.
                var sib = _board?.Stories.FirstOrDefault(x => x.FeatureId == ns.FeatureId && x.Id > 0);
                var row = new TfsImportService.SprintStoryRow(ns.TempId, ns.Title, "New", ns.AssignedTo ?? "", new())
                {
                    FeatureId = ns.FeatureId,
                    FeatureTitle = ns.FeatureTitle,
                    FeatureEpicId = sib?.FeatureEpicId ?? 0,
                    FeatureEpicTitle = sib?.FeatureEpicTitle ?? "",
                    FeatureProjectId = sib?.FeatureProjectId ?? 0,
                    FeatureProjectTitle = sib?.FeatureProjectTitle ?? "",
                    FeatureRank = sib?.FeatureRank ?? double.NaN,
                    FeatureEpicRank = sib?.FeatureEpicRank ?? double.NaN,
                    FeatureProjectRank = sib?.FeatureProjectRank ?? double.NaN
                };
                var tks = newTasksByParent[ns.TempId].Select(NewToCard).ToList();
                list.Add((row, tks));
            }
            return list;
        }

        /// <summary>
        /// Move de Story pendente que ainda VALE. Quando o destino era uma Story nova e o card
        /// dela foi descartado, o destino deixou de existir: sem esta checagem a Task sumia do
        /// board (saia da origem e nao tinha para onde ir) e levava junto a linha da Feature.
        /// </summary>
        private bool HasLiveParentMove(int taskId)
        {
            if (!_taskParentPending.TryGetValue(taskId, out var target)) return false;
            if (target > 0) return true;
            return _newCards.Any(n => n.TempId == target);
        }

        private TfsImportService.SprintTaskCard NewToCard(NewCard n) =>
            new(n.TempId, n.Title, "New", n.AssignedTo ?? "", "", n.ParentId, "", null, 0, 0) { IterationPath = n.IterationPath };

        // Diálogo simples de uma linha (nome do novo card).
        private string? PromptText(string title)
        {
            var win = new Window { Title = title, Width = 460, Height = 150, Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
            var dock = new DockPanel { Margin = new Thickness(12) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Bottom);
            var tb = new TextBox { VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
            var okBtn = new Button { Content = "OK", Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
            okBtn.Click += (_, _) => { win.DialogResult = true; };
            var cancel = new Button { Content = AppStrings.Get("Setup_Close"), Padding = new Thickness(14, 3, 14, 3), IsCancel = true };
            buttons.Children.Add(okBtn); buttons.Children.Add(cancel);
            dock.Children.Add(buttons);
            dock.Children.Add(tb);
            win.Content = dock;
            tb.Loaded += (_, _) => tb.Focus();
            return win.ShowDialog() == true && !string.IsNullOrWhiteSpace(tb.Text) ? tb.Text.Trim() : null;
        }

        /// <summary>
        /// Abre o caminho ate o no informado (ele e os niveis acima). Criar um card dentro de um
        /// nivel RECOLHIDO deixava o card novo invisivel: ele entrava na fila, mas nao aparecia na
        /// tela e parecia que o botao nao tinha funcionado.
        /// </summary>
        private void ExpandTo(int nodeId)
        {
            if (nodeId <= 0) return;
            _collapsed.Remove(nodeId);
            if (_board == null) return;

            if (StoryById(nodeId) is { } story)
            {
                _collapsed.Remove(story.FeatureId);
                _collapsed.Remove(story.FeatureEpicId);
                _collapsed.Remove(story.FeatureProjectId);
                return;
            }
            if (_board.Stories.FirstOrDefault(s => s.FeatureId == nodeId) is { } byFeature)
            {
                _collapsed.Remove(byFeature.FeatureEpicId);
                _collapsed.Remove(byFeature.FeatureProjectId);
                return;
            }
            if (_board.Stories.FirstOrDefault(s => s.FeatureEpicId == nodeId) is { } byEpic)
                _collapsed.Remove(byEpic.FeatureProjectId);
        }

        // Cria um card NOVO vazio (editável no próprio card): Nome, Responsável, HH, Descrição.
        private int AddNewStory(int featureId, string featureTitle, string? assignedTo = null)
        {
            // Na visao Pessoa & Task o botao fica DENTRO da faixa de uma pessoa: a Story nasce
            // para ELA, nao para o dono da Feature (que pode ser outro). Sem pessoa no contexto
            // (visao Projeto & Story), herda o responsavel da Feature. Editavel no proprio card.
            var featOwner = assignedTo != null
                ? (string.Equals(assignedTo, AppStrings.Get("Sprint_NoOwner"), StringComparison.Ordinal) ? "" : assignedTo)
                : EffOwner(featureId,
                    _board?.Stories.FirstOrDefault(x => x.FeatureId == featureId)?.FeatureAssignedTo ?? "");
            var tempId = _nextTempId--;
            _newCards.Add(new NewCard { TempId = tempId, Type = "Story", ParentId = featureId, FeatureId = featureId, FeatureTitle = featureTitle, AssignedTo = featOwner, IterationPath = DefaultNewIterationPath() });
            ExpandTo(featureId);
            if (!string.IsNullOrEmpty(featOwner)) _collapsedPeople.Remove(featOwner);
            UpdatePendingButton(); Render();
            return tempId;
        }

        /// <summary>
        /// Task pendurada DIRETO na Feature: cria a Story que falta ali e marca todas as Tasks
        /// daquela Feature para virarem filhas dela. A Story so nasce no "Atualizar TFS", e a
        /// troca de pai acontece logo depois, ja com o id real.
        /// </summary>
        private void CreateStoryForOrphans(int featureId, string featureTitle, string? personKey,
            IEnumerable<TfsImportService.SprintTaskCard> tasks)
        {
            var tempId = AddNewStory(featureId, featureTitle, personKey);
            foreach (var t in tasks.Where(t => t.Id > 0)) _taskParentPending[t.Id] = tempId;
            UpdatePendingButton();
            Render();
        }

        // "+Feature" no card do EPIC e "+EPIC" no card do Projeto: mesma fila dos demais
        // cards novos — o work item so nasce no DevOps no "Atualizar TFS".
        private void AddNewFeature(int epicId, string epicTitle)
        {
            var tempId = _nextTempId--;
            _newCards.Add(new NewCard { TempId = tempId, Type = "Feature", ParentId = epicId,
                FeatureTitle = epicTitle, IterationPath = DefaultNewIterationPath() });
            ExpandTo(epicId);
            // Feature sem Story cai numa linha propria, que pode ficar no fim do board: rola ate
            // ela, senao o usuario cria e acha que nao aconteceu nada.
            _scrollToNewCard = tempId;
            UpdatePendingButton(); Render(); ScrollToNewCardIfAny();
        }

        private void AddNewEpic(int projectId, string projectTitle)
        {
            var tempId = _nextTempId--;
            _newCards.Add(new NewCard { TempId = tempId, Type = "Epic", ParentId = projectId,
                FeatureTitle = projectTitle, IterationPath = DefaultNewIterationPath() });
            ExpandTo(projectId);
            _scrollToNewCard = tempId;
            UpdatePendingButton(); Render(); ScrollToNewCardIfAny();
        }

        // Botao "+X" dos cards de Projeto/EPIC. So aparece com o pai ja existente no DevOps
        // (id > 0) e no modo edicao, como os demais botoes de criacao do board.
        private UIElement? BuildAddChildButton(int parentId, string parentTitle, string kind)
        {
            if (parentId <= 0 || EditModeCheck.IsChecked != true) return null;
            var btn = new Button
            {
                Content = AppStrings.Get(kind == "Epic" ? "Sprint_AddEpic" : "Sprint_AddFeature"),
                FontSize = 9, Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(0, 4, 0, 0),
                Height = LevelButtonHeight, VerticalAlignment = VerticalAlignment.Center,
                ToolTip = AppStrings.Get(kind == "Epic" ? "Sprint_AddEpicTip" : "Sprint_AddFeatureTip")
            };
            if (kind == "Epic") btn.Click += (_, _) => AddNewEpic(parentId, parentTitle);
            else                btn.Click += (_, _) => AddNewFeature(parentId, parentTitle);
            return Light(btn);
        }

        /// <summary>
        /// 🗑 do card de EPIC: marca o EPIC para excluir no "Atualizar TFS". So habilita quando
        /// ele esta VAZIO no board — sem Feature e sem Story abaixo. Com filho, excluir deixaria
        /// trabalho orfao no DevOps, entao o botao fica apagado e sobra o 🔗 para resolver la.
        /// </summary>
        /// <summary>
        /// 🗑 do card da FEATURE: marca para excluir no "Atualizar TFS". So habilita com a
        /// Feature VAZIA no board — sem Story e sem Task pendurada direto nela (Task marcada
        /// para excluir nao conta). Com filho vivo, excluir deixaria trabalho orfao no DevOps.
        /// </summary>
        /// <summary>
        /// Marca (ou desmarca) o item para excluir. Antes de MARCAR, pergunta ao DevOps se ele tem
        /// filho: o board so enxerga as sprints carregadas, e um EPIC "vazio" na tela pode ter
        /// Features em outras sprints. Desmarcar nao precisa de checagem.
        /// </summary>
        private async Task ToggleDeleteWithChildCheckAsync(int id)
        {
            if (id <= 0) return;
            if (_deletePending.Remove(id))
            {
                UpdatePendingButton(); Render();
                return;
            }

            var n = await TfsImportService.CountChildrenAsync(_options, id);
            if (n > 0)
            {
                MessageBox.Show(this, AppStrings.Get("Sprint_DeleteHasChildrenDevOps", id.ToString(), n.ToString()),
                    "NXProject", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (n < 0)
            {
                MessageBox.Show(this, AppStrings.Get("Sprint_DeleteCheckFailed", id.ToString()),
                    "NXProject", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _deletePending.Add(id);
            UpdatePendingButton(); Render();
        }

        private UIElement? BuildDeleteFeatureButton(int featureId)
        {
            if (featureId <= 0 || EditModeCheck.IsChecked != true) return null;
            var marked = _deletePending.Contains(featureId);
            var hasStory = _board?.Stories.Any(s2 => s2.Id > 0 && s2.FeatureId == featureId) == true;
            var hasTask = _board?.Stories.Where(s2 => s2.OrphanParentId == featureId)
                              .SelectMany(s2 => s2.Tasks)
                              .Any(t2 => t2.Id > 0 && !_deletePending.Contains(t2.Id)) == true;
            // Nao da para excluir: o botao NEM APARECE (em vez de aparecer apagado), para o card
            // so mostrar o que de fato da para fazer ali. Marcada, o ↩ continua visivel p/ desfazer.
            if (!marked && (hasStory || hasTask)) return null;
            var btn = new Button
            {
                Content = marked ? "↩" : "🗑", FontSize = 11,
                Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(0, 4, 3, 0),
                Height = LevelButtonHeight, VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x30, 0x30)),
                ToolTip = marked ? AppStrings.Get("Sprint_UndoDelete")
                    : AppStrings.Get("Sprint_DeleteFeatureTip", featureId.ToString())
            };
            btn.Click += async (_, _) => await ToggleDeleteWithChildCheckAsync(featureId);
            return Light(btn);
        }

        private UIElement? BuildDeleteEpicButton(int epicId)
        {
            if (epicId <= 0 || EditModeCheck.IsChecked != true) return null;
            var marked = _deletePending.Contains(epicId);
            var hasChild = _board?.Stories.Any(s2 => s2.FeatureEpicId == epicId
                               && (s2.FeatureId > 0 || s2.Id > 0)) == true
                           || _board?.LevelItems.Any(li => li.EpicId == epicId) == true;
            // Com filho abaixo, o botao NEM APARECE (ver BuildDeleteFeatureButton).
            if (!marked && hasChild) return null;
            var btn = new Button
            {
                Content = marked ? "↩" : "🗑", FontSize = 11,
                Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(0, 4, 3, 0),
                Height = LevelButtonHeight, VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x30, 0x30)),
                ToolTip = marked ? AppStrings.Get("Sprint_UndoDelete")
                    : AppStrings.Get("Sprint_DeleteEpicTip", epicId.ToString())
            };
            btn.Click += async (_, _) => await ToggleDeleteWithChildCheckAsync(epicId);
            return Light(btn);
        }

        // ✎ dos cards de EPIC/Feature: abre o mesmo editor da Story (nome, responsavel,
        // descricao). Segue a regra do "+": so com o item existente no DevOps e no modo edicao.
        private UIElement? BuildEditLevelButton(int id, string title, string kind)
        {
            if (id <= 0 || EditModeCheck.IsChecked != true) return null;
            var btn = new Button
            {
                Content = "✎", FontSize = 11, Padding = new Thickness(4, 0, 4, 0),
                Height = LevelButtonHeight, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 3, 0),
                ToolTip = AppStrings.Get(kind == "Epic" ? "Sprint_EditEpic" : "Sprint_EditFeature")
            };
            btn.Click += async (_, _) => await EditDescriptionAsync(id, title, "", kind);
            return Light(btn);
        }

        // Junta os botoes do card de Projeto/EPIC numa linha so (✎ editar + ➕ criar filho).
        private UIElement? JoinButtons(params UIElement?[] buttons)
        {
            var list = buttons.Where(b => b != null).ToList();
            if (list.Count == 0) return null;
            if (list.Count == 1) return list[0];
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var b in list) sp.Children.Add(b);
            return sp;
        }

        private void AddNewTask(int storyId, string? assignedTo = null)
        {
            // Já nasce na faixa da pessoa onde foi criado (fica no grupo da Story).
            var who = string.Equals(assignedTo, AppStrings.Get("Sprint_NoOwner"), StringComparison.Ordinal) ? "" : (assignedTo ?? "");
            _newCards.Add(new NewCard { TempId = _nextTempId--, Type = "Task", ParentId = storyId, AssignedTo = who, IterationPath = DefaultNewIterationPath() });
            ExpandTo(storyId);
            // Na visao Pessoa & Task a faixa da pessoa tambem pode estar recolhida.
            if (!string.IsNullOrEmpty(assignedTo)) _collapsedPeople.Remove(assignedTo);
            UpdatePendingButton(); Render();
        }

        // Sprint sugerida p/ novos cards: a única aberta → a atual (se estiver entre as selecionadas)
        // → a 1ª selecionada → a atual → a 1ª sprint real.
        private string DefaultNewIterationPath()
        {
            if (!string.IsNullOrEmpty(_sprintPath)) return _sprintPath;
            var cur = CurrentSprintPath();
            if (cur != null && (_sprintPaths.Count == 0 || _sprintPaths.Contains(cur))) return cur;
            if (_sprintPaths.Count > 0) return _sprintPaths[0];
            return cur ?? _sprints.FirstOrDefault(s => !string.IsNullOrEmpty(s.Path))?.Path ?? "";
        }

        // Sprints oferecidas no card novo: as selecionadas (se houver) ou todas as reais.
        private List<TfsImportService.SprintInfo> NewCardSprintOptions() =>
            (_sprintPaths.Count > 0
                ? _sprints.Where(s => _sprintPaths.Contains(s.Path))
                : _sprints.Where(s => !string.IsNullOrEmpty(s.Path))).ToList();

        /// <summary>
        /// Redesenho de filtro com indicacao de progresso. Render() e sincrono e, com muitos
        /// cards, segura a UI por alguns segundos — sem sinal nenhum a tela parece travada.
        /// Aqui a barra indeterminada aparece, a UI ganha uma volta do dispatcher para pintar,
        /// e so entao o board e remontado. No fim o proprio Render() repoe o texto de contagem.
        /// </summary>
        private bool _renderBusy;

        private bool _renderAgain;

        private async void RenderBusy()
        {
            if (_board == null) return;
            // Pedido novo com um redesenho em curso: nao se perde — roda de novo ao terminar,
            // senao o board ficaria mostrando o filtro anterior.
            if (_renderBusy) { _renderAgain = true; return; }
            _renderBusy = true;
            var filtering = AppStrings.Get("Sprint_Filtering");
            SaveProgress.IsIndeterminate = true;
            SaveProgress.Visibility = Visibility.Visible;
            StatusText.Text = filtering;
            try
            {
                do
                {
                    _renderAgain = false;
                    // Uma volta do dispatcher para a barra ser PINTADA antes do redesenho.
                    // Na prioridade Render, e nao Background: a barra indeterminada anima o
                    // tempo todo e a animacao fica acima de Background — a continuacao podia
                    // nunca rodar, deixando "Aplicando filtro..." preso com a tela liberada.
                    await Dispatcher.InvokeAsync(() => { },
                        System.Windows.Threading.DispatcherPriority.Render);
                    Render();
                } while (_renderAgain);
            }
            finally
            {
                SaveProgress.IsIndeterminate = false;
                SaveProgress.Visibility = Visibility.Collapsed;
                // Rede de seguranca: se o Render nao repos a contagem, nao deixa a mensagem presa.
                if (StatusText.Text == filtering) StatusText.Text = "";
                _renderBusy = false;
            }
        }

        /// <summary>
        /// <summary>
        /// Sprints aceitas de cada pessoa no recorte "ultima sprint". E um CONJUNTO, nao um path:
        /// a mesma sprint aparece com paths diferentes em cada projeto/time, e guardar so um deles
        /// fazia a Story do outro projeto sumir do board.
        /// </summary>
        private readonly Dictionary<string, HashSet<string>> _lastSprintByPerson =
            new(StringComparer.CurrentCultureIgnoreCase);

        /// <summary>Sprints EM ANDAMENTO hoje (Start &lt;= hoje &lt;= Fim). Sempre entram no recorte.</summary>
        private readonly HashSet<string> _runningSprintPaths = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Sprints que ainda nao comecaram (Start &gt; hoje). Nunca entram no recorte.</summary>
        private readonly HashSet<string> _futureSprintPaths = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Recorte ligado? Guardado a parte: os conjuntos acima podem ficar vazios.</summary>
        private bool _lastSprintCutOn;

        /// <summary>
        /// Marcar o recorte exige TODAS as sprints no board: a ultima sprint de alguem pode estar
        /// fora do recorte carregado, e ai a conta sairia errada. Desmarcar nao recarrega — as
        /// sprints ja estao em maos e o board so volta a mostrar tudo.
        /// </summary>
        private async void OnLastSprintPerPersonChanged(object sender, RoutedEventArgs e)
        {
            _prefs.LastSprintPerPerson = LastSprintPerPersonCheck.IsChecked == true;
            SavePrefs();
            if (LastSprintPerPersonCheck.IsChecked == true && _sprintPaths.Count > 0)
            {
                await ReloadBoardAsync(new List<string>());   // todas as sprints
                return;
            }
            RenderBusy();
        }

        /// <summary>
        /// Monta o recorte: a sprint em andamento (para todos) e, para cada pessoa, a sprint mais
        /// recente JA INICIADA em que ela tem item — usada por quem nao tem nada na corrente.
        /// Tudo vira dicionario/HashSet aqui, uma vez por desenho, porque o teste roda por card.
        /// </summary>
        private void RebuildLastSprintByPerson()
        {
            _lastSprintByPerson.Clear();
            _runningSprintPaths.Clear();
            _futureSprintPaths.Clear();
            _lastSprintCutOn = _board != null && LastSprintPerPersonCheck?.IsChecked == true;
            if (!_lastSprintCutOn) return;

            var today = DateTime.Today;
            var rankByPath = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < _sprints.Count; i++)
            {
                var sp = _sprints[i];
                if (string.IsNullOrEmpty(sp.Path)) continue;
                if (sp.Start is DateTime st && st.Date > today) { _futureSprintPaths.Add(sp.Path); continue; }
                // Comecou. Se ainda nao terminou (ou nao tem fim cadastrado), esta em andamento.
                if (sp.Start is DateTime st2 && st2.Date <= today
                    && (sp.End is not DateTime en || en.Date >= today))
                    _runningSprintPaths.Add(sp.Path);
                rankByPath[sp.Path] = sp.Start?.Ticks ?? i;   // sem data: posicao na lista do DevOps
            }

            var bestRank = new Dictionary<string, double>(StringComparer.CurrentCultureIgnoreCase);
            void Consider(string? who, string? path)
            {
                if (string.IsNullOrWhiteSpace(who) || string.IsNullOrWhiteSpace(path)) return;
                if (!rankByPath.TryGetValue(path!, out var rank)) return;
                if (bestRank.TryGetValue(who!, out var cur) && rank < cur) return;
                if (!bestRank.TryGetValue(who!, out cur) || rank > cur)
                {
                    bestRank[who!] = rank;
                    _lastSprintByPerson[who!] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                _lastSprintByPerson[who!].Add(path!);   // empate de data: os dois paths valem
            }

            foreach (var s in _board!.Stories)
            {
                Consider(s.AssignedTo, s.IterationPath);
                foreach (var t in s.Tasks) Consider(t.AssignedTo, t.IterationPath);
            }
            foreach (var it in _board.LevelItems) Consider(it.AssignedTo, it.IterationPath);
        }

        /// <summary>
        /// Sprint que ainda NAO comecou (Start &gt; hoje). Com o recorte ligado ela fica de fora
        /// mesmo quando a Story pai entra: o board mostra o que esta em andamento, nao o planejado.
        /// </summary>
        private bool IsFutureSprint(string? iterationPath)
        {
            if (!_lastSprintCutOn || string.IsNullOrWhiteSpace(iterationPath)) return false;
            return _futureSprintPaths.Contains(iterationPath!);
        }

        /// <summary>
        /// O item passa no recorte? Sem recorte, sempre passa. A sprint EM ANDAMENTO passa para
        /// todo mundo — foi o caso das Stories que sumiam: a pessoa tinha itens na sprint corrente
        /// sob outro path de projeto, e o desempate escolhia so um deles.
        /// </summary>
        private bool LastSprintOk(string? who, string? iterationPath)
        {
            if (!_lastSprintCutOn) return true;
            var path = iterationPath ?? "";
            if (_runningSprintPaths.Contains(path)) return true;
            if (string.IsNullOrWhiteSpace(who)) return true;                  // sem responsavel: fica
            if (!_lastSprintByPerson.TryGetValue(who!, out var accepted)) return true;
            return accepted.Contains(path);
        }

        private void OnFilterChanged(object sender, RoutedEventArgs e)
        {
            // Os dois recortes de "itens vazios" ficam salvos como as demais preferencias de tela.
            if (ShowStoryNoTaskCheck != null) _prefs.ShowStoryNoTask = ShowStoryNoTaskCheck.IsChecked == true;
            if (ShowFeatureNoStoryCheck != null) _prefs.ShowFeatureNoStory = ShowFeatureNoStoryCheck.IsChecked == true;
            // Ao entrar em "Pessoa & Task" sem ninguém marcado, já traz o usuário do NX.
            if (ViewIndex == 1 && _selectedPeople.Count == 0
                && _board != null && MatchCurrentUser() is { } me)
            {
                _selectedPeople.Add(me);
                PopulatePersonFilter(BoardPeople(_board));
            }
            RenderBusy();
            SavePrefs();
        }

        /// <summary>
        /// Pessoas que realmente aparecem no board: responsavel de Story ou de Task. O
        /// People do board vem do AssignedTo de TODO work item da sprint (Feature, EPIC e
        /// Work Item Project inclusive), e esses nomes enchiam o filtro sem ter card algum.
        /// </summary>
        private static List<string> BoardPeople(TfsImportService.SprintBoard b)
        {
            var set = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var s in b.Stories)
            {
                if (!s.IsLevelPlaceholder && !string.IsNullOrWhiteSpace(s.AssignedTo)) set.Add(s.AssignedTo);
                foreach (var t in s.Tasks)
                    if (!string.IsNullOrWhiteSpace(t.AssignedTo)) set.Add(t.AssignedTo);
            }
            return set.ToList();
        }

        // (Re)constrói a lista de checkboxes de pessoas e atualiza o texto do botão.
        private void PopulatePersonFilter(List<string> people)
        {
            PersonFilterList.Children.Clear();
            foreach (var p in people)
            {
                // Nome + 📍. O 📍 e o comando VISIVEL de "ir ate a pessoa no board"; o Ctrl+clique
                // no nome faz o mesmo, para quem ja pegou o atalho. O conteudo do CheckBox virou
                // painel, mas o nome continua no Tag — e por ele que a busca e o filtro trabalham.
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new TextBlock { Text = p, VerticalAlignment = VerticalAlignment.Center });
                var goTo = new TextBlock
                {
                    Text = "📍", Margin = new Thickness(6, 0, 0, 0), FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A)),
                    ToolTip = AppStrings.Get("Sprint_PersonGoToTip")
                };
                // Handled = true impede que o clique no icone marque/desmarque o checkbox.
                goTo.PreviewMouseLeftButtonDown += (s, ev) =>
                {
                    ev.Handled = true;
                    // Sem Ctrl o 📍 ISOLA a pessoa (desmarca as demais); com Ctrl, soma a selecao.
                    var keepOthers = (System.Windows.Input.Keyboard.Modifiers
                                      & System.Windows.Input.ModifierKeys.Control) != 0;
                    GoToPerson(p, keepOthers);
                };
                row.Children.Add(goTo);

                var cb = new CheckBox { Content = row, Tag = p,
                    IsChecked = _selectedPeople.Contains(p), Margin = new Thickness(0, 1, 0, 1),
                    ToolTip = AppStrings.Get("Sprint_PersonCtrlClickTip") };
                // Ctrl+clique NAO marca/desmarca: so leva a lista ate a faixa dessa pessoa.
                cb.PreviewMouseLeftButtonDown += (s, ev) =>
                {
                    if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0) return;
                    ev.Handled = true;
                    GoToPerson(p, keepOthers: true);   // Ctrl segurado = soma a quem ja esta marcado
                };
                PersonFilterList.Children.Add(cb);
            }
            ApplyPersonSearch();
            UpdatePersonToggleText();
        }

        /// <summary>
        /// Ir para a pessoa pelo 📍 (ou Ctrl+clique no nome).
        ///
        /// Sem Ctrl o filtro passa a ser SO ela: as demais sao desmarcadas. E o gesto de "quero
        /// ver o quadro desta pessoa", que era o uso real — antes o board continuava cheio e a
        /// pessoa so aparecia mais acima. Com Ctrl, a selecao que ja existia e mantida e ela e
        /// somada, para comparar duas ou tres pessoas lado a lado.
        /// </summary>
        private void GoToPerson(string person, bool keepOthers = false)
        {
            var boxes = PersonFilterList.Children.OfType<CheckBox>().ToList();
            var box = boxes.FirstOrDefault(c => c.Tag is string t
                && string.Equals(t, person, StringComparison.CurrentCultureIgnoreCase));
            if (box == null) { PersonFilterToggle.IsChecked = false; ScrollToPerson(person); return; }

            if (keepOthers) box.IsChecked = true;
            else foreach (var c in boxes) c.IsChecked = ReferenceEquals(c, box);

            // Mesmo caminho do botao Aplicar: refaz a selecao a partir das caixas e redesenha.
            var before = _selectedPeople.ToList();
            _selectedPeople.Clear();
            foreach (var c in boxes)
                if (c.IsChecked == true && c.Tag is string t) _selectedPeople.Add(t);
            var changed = !before.SequenceEqual(_selectedPeople);
            if (changed) { UpdatePersonToggleText(); SavePrefs(); }

            PersonFilterToggle.IsChecked = false;    // fecha o popup para ver o board
            if (changed) RenderBusy();               // redesenha antes de procurar a faixa
            ScrollToPerson(person);
        }

        /// <summary>
        /// Rola o board ate a faixa da pessoa, deixando-a no TOPO. Nao mexe em filtro nenhum: e so
        /// navegacao, para nao ter que procurar a pessoa descendo a lista.
        /// </summary>
        private void ScrollToPerson(string person)
        {
            // Espera o popup fechar e o layout assentar, senao a posicao vem desatualizada.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_personCellByKey.TryGetValue(person, out var cell) || !cell.IsVisible)
                {
                    // Tres motivos diferentes, cada um com a sua mensagem: a visao nao tem faixa
                    // de pessoa, a pessoa esta fora do filtro, ou esta no filtro mas sem card.
                    StatusText.Text = ViewPersonBtn.IsChecked != true
                        ? AppStrings.Get("Sprint_PersonNotPersonView")
                        : _selectedPeople.Count > 0 && !_selectedPeople.Contains(person)
                            ? AppStrings.Get("Sprint_PersonOutOfFilter", person)
                            : AppStrings.Get("Sprint_PersonNoCards", person);
                    return;
                }
                try
                {
                    var y = cell.TransformToAncestor(BoardHost).Transform(new Point(0, 0)).Y;
                    BoardScroll.ScrollToVerticalOffset(Math.Max(0, y - 4));
                    StatusText.Text = "";
                }
                catch (InvalidOperationException) { /* celula fora da arvore visual: ignora */ }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Busca dentro do filtro de pessoas: esconde (nao remove) os checkboxes que nao casam,
        /// para que a marcacao de quem ficou fora da busca continue valendo no Aplicar.
        /// Quem ja esta marcado nunca some, senao o usuario perderia a selecao de vista.
        /// </summary>
        private void ApplyPersonSearch()
        {
            var q = PersonSearchBox?.Text?.Trim();
            foreach (var cb in PersonFilterList.Children.OfType<CheckBox>())
            {
                var name = cb.Tag as string ?? "";
                cb.Visibility = string.IsNullOrEmpty(q) || cb.IsChecked == true
                    || name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0
                    ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void OnPersonSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
            => ApplyPersonSearch();

        private void UpdatePersonToggleText()
        {
            PersonFilterToggle.Content = _selectedPeople.Count == 0
                ? AppStrings.Get("Sprint_PersonAll")
                : _selectedPeople.Count == 1 ? _selectedPeople.First()
                : AppStrings.Get("Sprint_PersonN", _selectedPeople.Count.ToString());
        }

        private void OnPersonFilterApply(object sender, RoutedEventArgs e)
        {
            _selectedPeople.Clear();
            foreach (var cb in PersonFilterList.Children.OfType<CheckBox>())
                if (cb.IsChecked == true && cb.Tag is string p) _selectedPeople.Add(p);
            PersonFilterToggle.IsChecked = false;
            UpdatePersonToggleText();
            RenderBusy();
            SavePrefs();
        }

        private void OnPersonFilterNone(object sender, RoutedEventArgs e)
        {
            foreach (var cb in PersonFilterList.Children.OfType<CheckBox>()) cb.IsChecked = false;
        }

        private void OnAutoOpenChanged(object sender, RoutedEventArgs e)
        {
            _prefs.AutoOpen = AutoOpenCheck.IsChecked == true;
            SavePrefs();
        }

        // Desmarcar tambem JOGA FORA o que ja estava guardado: quem desliga o cache normalmente
        // quer justamente parar de ver o valor antigo.
        private void OnCardDocsOnlyChanged(object sender, RoutedEventArgs e)
        {
            _prefs.CardDocsOnly = CardDocsOnlyCheck.IsChecked == true;
            CardDocExtsBox.IsEnabled = CardDocsOnlyCheck.IsChecked == true;
            SavePrefs();
            Render();
        }

        private void OnCardDocExtsKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter) OnCardDocExtsChanged(sender, e);
        }

        /// <summary>Grava a lista de extensoes digitada e redesenha. Lista vazia volta ao padrao.</summary>
        private void OnCardDocExtsChanged(object sender, RoutedEventArgs e)
        {
            ApplyCardDocExtensions(CardDocExtsBox.Text);
            // Normaliza o que ficou na caixa: sem ponto, minusculo, sem repetir.
            CardDocExtsBox.Text = FormatExtensions(_cardDocExtensions);
            _prefs.CardDocExtensions = CardDocExtsBox.Text;
            SavePrefs();
            Render();
        }

        private void OnFieldCacheChanged(object sender, RoutedEventArgs e)
        {
            var on = FieldCacheCheck.IsChecked == true;
            TfsImportService.FieldRefCacheEnabled = on;
            if (!on) TfsImportService.InvalidateFieldRefCache(_options);
            _prefs.FieldCache = on;
            SavePrefs();
        }

        // Limite de WIP: aceita 0..99 (0 = sem limite). Valor inválido volta para o que estava.
        private void OnWipLimitChanged(object sender, RoutedEventArgs e)
        {
            if (_restoringPrefs) return;
            if (int.TryParse(WipLimitBox.Text?.Trim(), out var n) && n >= 0 && n <= 99)
            {
                if (n == WipLimit()) return;
                _prefs.WipLimit = n;
                SavePrefs();
                Render();
            }
            else WipLimitBox.Text = WipLimit().ToString();
        }

        private void OnWipLimitKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            OnWipLimitChanged(sender, e);
            e.Handled = true;
        }

        // Casa o usuário autenticado (connectionData) com uma pessoa da sprint.
        private string? MatchCurrentUser()
        {
            if (string.IsNullOrWhiteSpace(_currentUser) || _board == null) return null;
            var me = _currentUser.Trim();
            return _board.People.FirstOrDefault(p =>
                string.Equals(p, me, StringComparison.CurrentCultureIgnoreCase)
                || p.IndexOf(me, StringComparison.CurrentCultureIgnoreCase) >= 0
                || me.IndexOf(p, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }

        // Estado efetivo: pendente (arrasto) → já gravado → original do DevOps.
        private string EffState(TfsImportService.SprintTaskCard t)
            => _pending.TryGetValue(t.Id, out var p) ? p
             : _applied.TryGetValue(t.Id, out var a) ? a : t.State;

        private void PopulateStateFilter()
        {
            StateFilterList.Children.Clear();
            if (_board == null) return;
            foreach (var st in _board.States)
                StateFilterList.Children.Add(new CheckBox
                {
                    Content = st, Tag = st, Margin = new Thickness(2),
                    IsChecked = !_hiddenStates.Contains(st)
                });
        }

        private void OnStateFilterAll(object sender, RoutedEventArgs e)
        {
            _hiddenStates.Clear();
            foreach (var cb in StateFilterList.Children.OfType<CheckBox>()) cb.IsChecked = true;
            RenderBusy();
        }

        private void OnStateFilterApply(object sender, RoutedEventArgs e)
        {
            _hiddenStates.Clear();
            foreach (var cb in StateFilterList.Children.OfType<CheckBox>())
                if (cb.IsChecked != true && cb.Tag is string s) _hiddenStates.Add(s);
            if (int.TryParse(ClosedDaysBox.Text?.Trim(), out var d) && d >= 0)
                _closedDays = d; // 0 = todos (não persiste)
            StateFilterToggle.IsChecked = false;
            RenderBusy();
            SavePrefs(); // grava estados ocultos + ClosedDays (>0)
        }

        // Botões ✎ (descrição) e 💬 (trâmite) para Story/Task — reusam o editor WebView do NX.
        private void AddEditButtons(Panel panel, int id, string title, string currentOwner = "", string kind = "Story", string currentIteration = "")
        {
            if (id <= 0) return;
            // ✎ marca "●" quando há descrição OU responsável/HH/sprint pendente.
            var descDirty = _descPending.ContainsKey(id) || _ownerPending.ContainsKey(id)
                || _titlePending.ContainsKey(id) || _estPending.ContainsKey(id) || _donePending.ContainsKey(id)
                || _iterPending.ContainsKey(id) || _featurePending.ContainsKey(id) || _startPending.ContainsKey(id)
                || _finishPending.ContainsKey(id)
                || _acPending.ContainsKey(id) || _tramitePending.ContainsKey(id);
            var desc = new Button { Content = descDirty ? "✎●" : "✎", FontSize = 11,
                Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(0, 0, 3, 2),
                Foreground = descDirty ? new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)) : Brushes.Black,
                ToolTip = AppStrings.Get("Sprint_EditDesc") };
            desc.Click += async (_, _) => await EditDescriptionAsync(id, title, currentOwner, kind, currentIteration);
            panel.Children.Add(desc);
            // O 💬 (tramite) saiu do card: virou botao dentro da tela de edicao, que abre pelo
            // proprio ✎. Assim a linha de botoes da Story/Task cabe numa linha so.
        }

        // Descrição: abre o editor (WebView) com a descrição atual do DevOps (ou o rascunho pendente).
        /// <summary>
        /// Texto de orientacao quando a Story esta numa sprint e a Task em outra. O Mapa de
        /// Alocacao fecha as horas POR SPRINT: se a Story atravessa sprints, as horas dela nao
        /// caem na sprint em que o trabalho aconteceu. O certo e encerrar a Story na sprint atual
        /// e criar outra na sprint seguinte — a Task pode migrar, a Story fica com o autor dela.
        /// Devolve null quando nao ha divergencia.
        /// </summary>
        private string? SprintAdviceFor(int id, string kind)
        {
            if (id <= 0) return null;
            if (kind == "Story")
            {
                var st = StoryById(id);
                if (st == null || !st.OutOfSprint) return null;
                return AppStrings.Get("Sprint_OutOfSprintAdvice", IterLeaf(st.IterationPath));
            }
            if (kind == "Task")
            {
                if (!_cardById.TryGetValue(id, out var card) || (card.ParentId ?? 0) <= 0) return null;
                var st = StoryById(card.ParentId ?? 0);
                if (st == null || !st.OutOfSprint) return null;
                return AppStrings.Get("Sprint_OutOfSprintAdviceTask", st.Id.ToString(),
                    IterLeaf(st.IterationPath), IterLeaf(EffIter(id, card.IterationPath)));
            }
            return null;
        }

        private async Task EditDescriptionAsync(int id, string title, string currentOwner = "", string kind = "Story", string currentIteration = "")
        {
            // Nome efetivo (pendente > aplicado > título recebido), editável na mesma tela.
            var effName = EffTitle(id, title);
            var pt = new NXProject.Models.ProjectTask { TfsId = id, Name = effName };
            // Todas as leituras do DevOps disparam JUNTAS aqui e sao aguardadas depois, cada uma
            // onde e usada. Antes eram ~6 consultas em sequencia e a edicao demorava a abrir.
            // So consulta o que nao tem valor pendente local.
            var descTask = _descPending.ContainsKey(id) ? null
                : TfsImportService.LoadWorkItemDescriptionHtmlAsync(_options, id);
            var hoursTask = id > 0 && kind != "Feature" ? TfsImportService.GetWorkItemHoursAsync(_options, id) : null;
            var startTask = kind is "Story" or "Task" && id > 0 && !_startPending.ContainsKey(id)
                ? TfsImportService.GetWorkItemStartDateAsync(_options, id) : null;
            var chainTask = kind == "Task" && id > 0 ? TfsImportService.GetParentChainAsync(_options, id) : null;
            var finishTask = kind == "Task" && id > 0 && !_finishPending.ContainsKey(id)
                ? TfsImportService.GetWorkItemFinishDateAsync(_options, id) : null;
            var acTask = kind == "Story" && id > 0 && !_acPending.ContainsKey(id)
                ? TfsImportService.GetWorkItemAcceptanceCriteriaAsync(_options, id) : null;

            pt.Description = _descPending.TryGetValue(id, out var d) ? d
                : (await descTask!) ?? string.Empty;
            // Responsável efetivo (pendente > aplicado > valor do board), editável na mesma tela.
            var owner = _ownerPending.TryGetValue(id, out var op) ? op
                : _ownerApplied.TryGetValue(id, out var oa) ? oa : currentOwner;
            var people = _board?.People.ToList() ?? new List<string>();
            // HH atuais do DevOps (com pendências locais sobrepostas) + estado p/ decidir HH Realizado.
            // Feature: só descrição (sem HH/estado/sprint), então não busca horas.
            double? est = null, done = null; string hState = kind;
            if (id > 0 && kind != "Feature")
            {
                var (e, c, st) = await hoursTask!;
                est = _estPending.TryGetValue(id, out var ep) ? ep : e;
                done = _donePending.TryGetValue(id, out var dp) ? dp : c;
                // Estado EFETIVO (considera arrasto pendente p/ Closed) → libera o HH Realizado.
                hState = _cardById.TryGetValue(id, out var cardH) ? EffState(cardH) : st;
            }
            // Sprint editável em TODOS os níveis: Task, Story, Feature e EPIC. A Feature/EPIC
            // também mora numa sprint no DevOps, e é por ela que o board decide o que carregar —
            // sem poder trocar aqui, o item ficava preso na sprint errada.
            System.Collections.Generic.IReadOnlyList<(string Name, string Path)>? sprints = null;
            var effIter = "";
            if (id > 0)
            {
                // Para Task, a iteração-base vem do próprio card se não veio no parâmetro; para
                // Feature/EPIC, do item de nível carregado do DevOps.
                var baseIterOrig = !string.IsNullOrEmpty(currentIteration) ? currentIteration
                    : (_cardById.TryGetValue(id, out var cIt) ? cIt.IterationPath : LevelIterationOf(id));
                sprints = _sprints.Where(s => !string.IsNullOrEmpty(s.Path)).Select(s => (s.Name, s.Path)).ToList();
                effIter = _iterPending.TryGetValue(id, out var ip) ? ip
                    : _iterApplied.TryGetValue(id, out var ia) ? ia : baseIterOrig;
            }
            // Bloqueio (tag) editável para itens com ID.
            var curTags = kind == "Task"
                ? (_cardById.TryGetValue(id, out var cc) ? cc.Tags : "")
                : (StoryById(id)?.Tags ?? "");
            var curBlocked = id > 0 && EffBlocked(id, curTags);
            // NP so existe na Task (a Story nao leva a tag).
            var curUnplanned = kind == "Task" && id > 0 && EffUnplanned(id, curTags);
            // Estado editável para Story (na visão Pessoa & Task não há coluna de estado da Story).
            System.Collections.Generic.IReadOnlyList<string>? storyStates = null;
            var effStState = "";
            if (kind == "Story" && id > 0 && StoryById(id) is { } srow)
            {
                storyStates = _board?.States?.ToList();
                effStState = EffStoryState(srow);
            }
            else if ((kind == "Feature" || kind == "Epic") && id > 0)
            {
                // Feature/EPIC: mesma lista de estados do board; o valor atual vem do DevOps
                // (ou da troca pendente ainda nao gravada).
                storyStates = _board?.States?.ToList();
                effStState = EffLevelState(id, LevelStateOf(id, kind));
            }
            // Troca de Feature: só para Story em New (evita reparent de itens em andamento).
            System.Collections.Generic.IReadOnlyList<(string Title, int Id)>? features = null;
            var curFeatureId = 0;
            if (kind == "Story" && id > 0 && StoryById(id) is { } fsrow
                && string.Equals(EffStoryState(fsrow), "New", StringComparison.OrdinalIgnoreCase))
            {
                features = _board?.Stories.Where(s => s.FeatureId > 0)
                    .Select(s => (Title: s.FeatureTitle, Id: s.FeatureId))
                    .Distinct().OrderBy(f => f.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
                curFeatureId = _featurePending.TryGetValue(id, out var fp) ? fp
                    : _featureApplied.TryGetValue(id, out var fa) ? fa : fsrow.FeatureId;
            }
            // Data de Início: editável para Story. Valor efetivo (pendente > DevOps).
            var enableStart = kind is "Story" or "Task" && id > 0;
            DateTime? curStart = null;
            if (enableStart)
                curStart = _startPending.TryGetValue(id, out var sp) ? sp
                    : await startTask!;
            // Cadeia de pais da Task direto do DevOps: mostra o id/tipo/nome de cada nivel,
            // porque a Task pode estar ligada a uma Feature ou outro tipo, nao so a uma Story.
            string? parentInfo = null;
            List<(int Id, string Text)>? parentLinks = null;
            if (kind == "Task" && id > 0)
            {
                var chain = await chainTask!;
                parentLinks = chain.Select((pl, i) => (pl.Id,
                    new string(' ', i * 3) + "↑ " + pl.Type + " #" + pl.Id + " — " + pl.Title
                    + (string.IsNullOrWhiteSpace(pl.State) ? "" : " (" + pl.State + ")"))).ToList();
                parentInfo = chain.Count == 0
                    ? AppStrings.Get("Desc_ParentNone")
                    : string.Join(Environment.NewLine, chain.Select((pl, i) =>
                        new string(' ', i * 3) + "\u2191 " + pl.Type + " #" + pl.Id + " \u2014 " + pl.Title
                        + (string.IsNullOrWhiteSpace(pl.State) ? "" : " (" + pl.State + ")")));
            }
            // Data alvo: campo Data_Fim da Task (pendente > valor atual do DevOps).
            var enableFinish = kind == "Task" && id > 0;
            DateTime? curFinish = null;
            if (enableFinish)
                curFinish = _finishPending.TryGetValue(id, out var fp0) ? fp0
                    : await finishTask!;
            // Critérios de Aceitação: campo da Story no DevOps (pendente > valor atual).
            var enableAc = kind == "Story" && id > 0;
            var curAc = "";
            if (enableAc)
                curAc = _acPending.TryGetValue(id, out var acp) ? acp
                    : await acTask!;
            // EPIC e Feature nao tem HH proprio (vem do rollup) nem tag de bloqueio no board:
            // o editor abre nos dois com os mesmos campos.
            var isFeat = kind is "Feature" or "Epic";
            // Contexto so leitura: para a Feature, o EPIC e o Project acima dela; para o EPIC,
            // o Project. Ambos vem de uma Story que esteja abaixo do item.
            string epicTitle = "", projTitle = "";
            if (kind == "Feature" && _board?.Stories.FirstOrDefault(s => s.FeatureId == id) is { } fsr)
            {
                epicTitle = fsr.FeatureEpicTitle;
                projTitle = fsr.FeatureProjectTitle;
            }
            else if (kind == "Epic" && _board?.Stories.FirstOrDefault(s => s.FeatureEpicId == id) is { } esr)
            {
                projTitle = esr.FeatureProjectTitle;
            }
            // Feature: responsável é demandante (não editável aqui) → não exibe o combo de responsável.
            var dlg = new TaskDescriptionEditWindow(pt, isFeat ? null : people, owner, enableNameEdit: id > 0, objectKind: kind,
                enableHours: id > 0 && !isFeat, estimate: est, completed: done, state: hState,
                sprints: sprints, currentIteration: effIter,
                enableBlocked: id > 0 && !isFeat, currentBlocked: curBlocked,
                enableUnplanned: kind == "Task" && id > 0, currentUnplanned: curUnplanned, unplannedTag: UnplannedTag(),
                states: storyStates, currentState: effStState,
                features: features, currentFeatureId: curFeatureId,
                enableStartDate: enableStart, currentStartDate: curStart,
                epicTitle: epicTitle, projectTitle: projTitle,
                enableAcceptance: enableAc, acceptanceHtml: curAc,
                // Mesmas datas do hint do card, aqui so para consulta.
                datesInfo: kind == "Task" && _cardById.TryGetValue(id, out var dcard) ? TaskDatesText(dcard)
                    : StoryById(id) is { } dst ? StoryDatesText(dst) : null,
                // 💬 Tramite: so para itens ja existentes no DevOps.
                onTramite: id > 0 ? () => EditTramite(id, title) : null,
                enableFinishDate: enableFinish, currentFinishDate: curFinish,
                parentInfo: parentInfo, parentLinks: parentLinks, onOpenWorkItem: OpenInDevOps,
                attachments: kind == "Task" && _cardById.TryGetValue(id, out var attCard) ? AttachmentsOf(attCard) : null,
                onOpenAttachment: a => _ = OpenAttachmentAsync(a),
                onRemoveAttachment: a => RemoveAttachmentAsync(id, a)) { Owner = this };
            // Story fora da sprint da Task: aqui e onde a sprint muda, entao e aqui que o aviso
            // tem que estar — trocar a sprint da Story arrasta o HH dela para a outra sprint.
            if (SprintAdviceFor(id, kind) is { } advice) dlg.ShowSprintAdvice(advice);
            if (dlg.ShowDialog() == true)
            {
                _descPending[id] = pt.Description ?? string.Empty;
                if (dlg.IterationChanged)
                {
                    var origIter = !string.IsNullOrEmpty(currentIteration) ? currentIteration
                        : (_cardById.TryGetValue(id, out var c2) ? c2.IterationPath : "");
                    var baseIter = _iterApplied.TryGetValue(id, out var bi) ? bi : origIter;
                    var chosenIter = dlg.SelectedIteration ?? string.Empty;
                    if (string.Equals(chosenIter, (baseIter ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                        _iterPending.Remove(id);
                    else
                        _iterPending[id] = chosenIter;
                }
                if (dlg.HoursChanged)
                {
                    _estPending[id] = dlg.EstimatedHours;
                    // HH Realizado agora e sempre editavel; so vai para a fila quando o valor
                    // mudou de fato (mexer so no Estimado nao regrava o Realizado no DevOps).
                    if ((dlg.CompletedHours ?? -1) != (done ?? -1) || _donePending.ContainsKey(id))
                        _donePending[id] = dlg.CompletedHours;
                }
                if (dlg.NameChanged)
                {
                    var baseName = _titleApplied.TryGetValue(id, out var bn) ? bn : title;
                    var newName = dlg.EditedName ?? string.Empty;
                    if (string.Equals(newName.Trim(), (baseName ?? "").Trim(), StringComparison.Ordinal))
                        _titlePending.Remove(id);
                    else
                        _titlePending[id] = newName;
                }
                if (dlg.BlockedChanged)
                {
                    var baseBlocked = HasTag(EffTags(id, curTags), BlockedTag());
                    if (dlg.Blocked == baseBlocked) _blockPending.Remove(id); else _blockPending[id] = dlg.Blocked;
                }
                if (dlg.UnplannedChanged)
                {
                    var baseNp = HasTag(EffTags(id, curTags), UnplannedTag());
                    if (dlg.Unplanned == baseNp) _unplannedPending.Remove(id); else _unplannedPending[id] = dlg.Unplanned;
                }
                if (dlg.StateWasChanged && StoryById(id) is { } srow2)
                {
                    var baseState = _storyStateApplied.TryGetValue(id, out var bs) ? bs : srow2.State;
                    var chosen = dlg.SelectedState ?? string.Empty;
                    if (SameState(chosen, baseState)) _storyStatePending.Remove(id);
                    else _storyStatePending[id] = chosen;
                }
                else if (dlg.StateWasChanged && (kind == "Feature" || kind == "Epic"))
                {
                    // Feature/EPIC: entra na mesma fila (SetWorkItemStateAsync e generico por id).
                    var baseState = _storyStateApplied.TryGetValue(id, out var bs2) ? bs2 : LevelStateOf(id, kind);
                    var chosen = dlg.SelectedState ?? string.Empty;
                    if (SameState(chosen, baseState)) _storyStatePending.Remove(id);
                    else _storyStatePending[id] = chosen;
                }
                if (dlg.FeatureChanged && StoryById(id) is { } srow3)
                {
                    var baseFeat = _featureApplied.TryGetValue(id, out var bf) ? bf : srow3.FeatureId;
                    if (dlg.SelectedFeatureId == baseFeat) _featurePending.Remove(id);
                    else _featurePending[id] = dlg.SelectedFeatureId;
                }
                if (dlg.StartDateChanged) _startPending[id] = dlg.SelectedStartDate;
                if (dlg.FinishDateChanged)
                {
                    _finishPending[id] = dlg.SelectedFinishDate;
                    _finishAutoByActive.Remove(id); _finishAutoByClosed.Remove(id);
                }
                if (dlg.AcceptanceChanged) _acPending[id] = dlg.AcceptanceHtml;
                if (dlg.OwnerChanged)
                {
                    var baseline = _ownerApplied.TryGetValue(id, out var b) ? b : currentOwner;
                    var chosen = dlg.SelectedOwner ?? string.Empty;
                    if (string.Equals(chosen.Trim(), (baseline ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                        _ownerPending.Remove(id);
                    else
                        _ownerPending[id] = chosen;
                }
                UpdatePendingButton();
                Render();
            }
        }

        // Trâmite: tela com HISTÓRICO dos comentários + editor rico (com imagem). Vale p/ Story e Task.
        // O novo trâmite fica pendente e é gravado como comentário no Salvar TFS.
        private void EditTramite(int id, string title)
        {
            var draft = _tramitePending.TryGetValue(id, out var t) ? t : string.Empty;
            var dlg = new TramiteWindow(id, AppStrings.Get("Sprint_EditTramite") + " — " + title, draft) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                // Tramite so com print TEM conteudo: antes o texto plano vinha vazio e o comentario
                // era jogado fora aqui mesmo, sem nunca chegar no DevOps e sem aviso nenhum.
                if (!TfsImportService.HasCommentContent(dlg.NewComment))
                    _tramitePending.Remove(id);
                else
                    _tramitePending[id] = dlg.NewComment;
                UpdatePendingButton();
                Render();
            }
        }

        private static bool HasTag(string tags, string tag) =>
            !string.IsNullOrEmpty(tags) && tags.Split(';').Any(x => x.Trim().Equals(tag, StringComparison.OrdinalIgnoreCase));

        // Diferença de marcações Doing pendentes (marcadas − já gravadas) + mudanças de estado.
        /// <summary>
        /// Sinal do HTML para conferir o que o DevOps REALMENTE gravou: texto plano + quantas
        /// imagens. O servidor reescreve o HTML (normaliza tags), entao comparar o HTML inteiro
        /// daria alarme falso; o que importa e nao ter perdido texto nem imagem.
        /// </summary>
        private static (string Text, int Images) HtmlSignal(string? html)
        {
            var text = TfsImportService.ToPlainTextPublic(html ?? "");
            var imgs = System.Text.RegularExpressions.Regex.Matches(
                html ?? "", "<img", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;
            return (System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim(), imgs);
        }

        /// <summary>
        /// Le a descricao de volta depois de gravar. O DevOps higieniza o HTML e pode DESCARTAR a
        /// imagem colada (data:) devolvendo sucesso assim mesmo — sem conferir, sumia calada.
        /// Devolve null quando esta tudo la, ou a mensagem para a lista de falhas.
        /// </summary>
        private async Task<string?> VerifySavedDescriptionAsync(int id, string sentHtml)
        {
            try
            {
                var saved = await TfsImportService.LoadWorkItemDescriptionHtmlAsync(_options, id);
                var sent = HtmlSignal(sentHtml);
                var got = HtmlSignal(saved);
                if (got.Images < sent.Images)
                    return $"#{id} (descr): o texto foi gravado, mas o DevOps descartou "
                         + $"{sent.Images - got.Images} imagem(ns). Imagem colada na descricao precisa ir como anexo.";
                if (!string.Equals(sent.Text, got.Text, StringComparison.Ordinal))
                    return $"#{id} (descr): o que ficou gravado no DevOps ficou diferente do enviado.";
                return null;
            }
            catch (Exception ex)
            {
                return $"#{id} (descr): gravou, mas nao deu para conferir depois: {ex.Message}";
            }
        }

        private int PendingCount()
        {
            var doingDiff = _doing.Except(_appliedDoing).Count() + _appliedDoing.Except(_doing).Count();
            doingDiff += _done.Except(_appliedDone).Count() + _appliedDone.Except(_done).Count();
            return _pending.Count + doingDiff + _descPending.Count + _tramitePending.Count + _newCards.Count + _prioPending.Count + _storyRankPending.Count + _taskRankPending.Count + _storyStatePending.Count + _ownerPending.Count + _titlePending.Count + _estPending.Count + _donePending.Count + _deletePending.Count + _iterPending.Count + _blockPending.Count + _unplannedPending.Count + _featurePending.Count + _startPending.Count + _acPending.Count + _taskParentPending.Count + _finishPending.Count
                + _attachRemovePending.Sum(kv => kv.Value.Count);
        }

        private void UpdatePendingButton()
        {
            var n = PendingCount();
            UpdateTfsButton.IsEnabled = n > 0;
            RevertButton.IsEnabled = n > 0;
            UpdateTfsButton.Content = n > 0
                ? AppStrings.Get("Sprint_UpdateTfsN", n.ToString())
                : AppStrings.Get("Sprint_UpdateTfs");
        }

        // Reverte as mudanças pendentes (estado + Doing) antes de gravar no TFS.
        private void OnRevertClick(object sender, RoutedEventArgs e)
        {
            _pending.Clear();
            _descPending.Clear();
            _tramitePending.Clear();
            _ownerPending.Clear();
            _titlePending.Clear();
            _estPending.Clear();
            _donePending.Clear();
            _closedDropped.Clear();
            _deletePending.Clear();
            _iterPending.Clear();
            _blockPending.Clear(); _unplannedPending.Clear();
            _featurePending.Clear();
            _taskParentPending.Clear();
            _moveTaskIds.Clear();
            _startPending.Clear();
            _finishPending.Clear(); _finishAutoByActive.Clear(); _finishAutoByClosed.Clear();
            // O "Reverter" desfaz PENDENCIAS; o que ja foi gravado continua valendo na tela.

            // Anexo marcado para excluir volta a aparecer: nada foi ao DevOps ainda.
            _attachRemovePending.Clear(); _removedAttachmentUrls.Clear();
            _newCards.Clear();
            _prioPending.Clear();
            _storyRankPending.Clear();
            _storyStatePending.Clear();
            _taskRankPending.Clear();
            // re-semeia os ranks (Story e Task) a partir do TFS
            if (_board != null)
            {
                _storyRank.Clear();
                double sr = 0;
                foreach (var s in _board.Stories.Where(s => s.Id > 0))
                    _storyRank[s.Id] = double.IsNaN(s.StackRank) ? (1e6 + sr++) : s.StackRank;
                _taskRank.Clear();
                double tr = 0;
                foreach (var t in _board.Stories.SelectMany(s => s.Tasks))
                    _taskRank[t.Id] = double.IsNaN(t.StackRank) ? (1e6 + tr++) : t.StackRank;
            }
            _doing.Clear();
            foreach (var id in _appliedDoing) _doing.Add(id);
            _done.Clear();
            foreach (var id in _appliedDone) _done.Add(id);
            UpdatePendingButton();
            Render();
        }

        // Grava no TFS os estados pendentes (arrastados). O DevOps aplica a permissão: 403 =
        // sem escrita (não é responsável, não está no grupo, ou o token não permite).
        private async void OnUpdateTfsClick(object sender, RoutedEventArgs e) => await UpdateTfsAsync();

        private async Task UpdateTfsAsync()
        {
            if (PendingCount() == 0) return;
            UpdateTfsButton.IsEnabled = false;
            var ok = 0;
            // Contado a parte: a tag de WIP e ajustada sozinha em varios cards quando um card
            // entra ou sai, e o total virava um numero grande sem explicacao ("15 atualizadas").
            var wipOk = 0;
            var fails = new List<string>();
            // Barra de progresso + texto da etapa durante a gravação.
            var total = PendingCount();
            SaveProgress.Maximum = Math.Max(1, total);
            SaveProgress.Value = 0;
            SaveProgress.Visibility = Visibility.Visible;
            void Phase(string key)
            {
                StatusText.Text = AppStrings.Get(key);
                SaveProgress.Value = Math.Min(SaveProgress.Maximum, ok + fails.Count);
            }
            try
            {
            var reload = false;

            // 1) Mudanças de estado (arrasto). Guarda os que mudaram para reavaliar a tag (Doing→Done).
            Phase("Sprint_PhState");
            var stateChanged = _pending.Keys.ToHashSet();
            foreach (var kv in _pending.ToList())
            {
                // Fechar (Closed) exige HH Realizado preenchido (> 0). Bloqueia e pede para informar.
                if (IsClosedState(kv.Value))
                {
                    double? completed = _donePending.TryGetValue(kv.Key, out var dv) ? dv : null;
                    if (!(completed > 0))
                    {
                        var (_, c, _) = await TfsImportService.GetWorkItemHoursAsync(_options, kv.Key);
                        completed = _donePending.TryGetValue(kv.Key, out var dv2) ? dv2 : c;
                    }
                    if (!(completed > 0))
                    {
                        fails.Add(AppStrings.Get("Sprint_ClosedNeedsHH", kv.Key.ToString()));
                        stateChanged.Remove(kv.Key);
                        continue; // não fecha sem HH Realizado
                    }
                }
                var (success, msg) = await TfsImportService.SetWorkItemStateAsync(_options, kv.Key, kv.Value);
                if (success) { _applied[kv.Key] = kv.Value; _pending.Remove(kv.Key); ok++; }
                else { fails.Add($"#{kv.Key}: {msg}"); stateChanged.Remove(kv.Key); }
            }

            // 2) Tags de andamento. Marcado + (novo OU mudou de estado) → grava Doing/Done conforme
            //    o estado atual (Closed vira Done). Desmarcado que já tinha a tag → remove.
            string? EffCard(int id) => _board?.Stories.SelectMany(s => s.Tasks)
                .FirstOrDefault(t => t.Id == id) is { } c ? EffState(c) : null;
            foreach (var id in _doing.ToList())
            {
                var isNew = !_appliedDoing.Contains(id);
                var doneChanged = _done.Contains(id) != _appliedDone.Contains(id);
                if (!isNew && !doneChanged && !stateChanged.Contains(id)) continue; // nada a gravar
                // Done quando marcado no card OU quando a Task foi encerrada.
                var tag = _done.Contains(id) || (EffCard(id) is { } st && IsClosedState(st)) ? "Done" : "Doing";
                var (success, msg) = await TfsImportService.SetDoingTagAsync(_options, id, tag);
                if (success)
                {
                    _appliedDoing.Add(id);
                    if (_done.Contains(id)) _appliedDone.Add(id); else _appliedDone.Remove(id);
                    if (isNew || doneChanged) ok++;
                }
                else fails.Add($"#{id} (tag): {msg}");
            }
            foreach (var id in _appliedDoing.Except(_doing).ToList())
            {
                var (success, msg) = await TfsImportService.SetDoingTagAsync(_options, id, null);
                if (success) { _appliedDoing.Remove(id); _appliedDone.Remove(id); _done.Remove(id); ok++; }
                else fails.Add($"#{id} (tag): {msg}");
            }

            // 3) Descrição (grava direto no TFS; 403 = sem permissão).
            Phase("Sprint_PhFields");
            foreach (var kv in _descPending.ToList())
            {
                // Descricao que estava la antes: serve para achar imagem que o usuario TIROU do
                // texto e cujo anexo ficaria orfao na Task.
                var oldHtml = "";
                try { oldHtml = await TfsImportService.LoadWorkItemDescriptionHtmlAsync(_options, kv.Key); }
                catch { /* sem a anterior, so nao da para limpar orfao */ }
                // Imagem colada vai embutida como "data:" e o DevOps DESCARTA esse src ao gravar.
                // Sobe cada uma como anexo e deixa a tag apontando para a URL, igual a tela dele.
                var html = kv.Value;
                try
                {
                    html = await TfsAttachmentService.InlineImagesToAttachmentsAsync(
                        _options, kv.Key, html, TfsAttachmentService.InlineImageDescriptionPrefix);
                }
                catch (Exception ex) { fails.Add($"#{kv.Key} (imagem da descricao): {ex.Message}"); }
                var (success, msg) = await TfsImportService.SetWorkItemDescriptionAsync(_options, kv.Key, html);
                if (!success) { fails.Add($"#{kv.Key} (descr): {msg}"); continue; }
                // Gravou: sai da fila e conta como ok. Mas confere o que chegou do outro lado —
                // se o DevOps comeu a imagem, o usuario fica sabendo em vez de descobrir depois.
                _descPending.Remove(kv.Key);
                ok++;
                if (await VerifySavedDescriptionAsync(kv.Key, html) is { } warn) fails.Add(warn);

                // Imagem tirada da descricao: apaga o anexo que ficou sem uso (e so ele).
                try
                {
                    var used = (await TfsImportService.GetWorkItemCommentsAsync(_options, kv.Key)).Select(c => c.Html);
                    var gone = await TfsAttachmentService.CleanupUnusedDescriptionImagesAsync(
                        _options, kv.Key, oldHtml, html, used);
                    foreach (var g in gone) _removedAttachmentUrls.Add(g.Url);
                }
                catch { /* limpeza de orfao e conveniencia: nunca derruba a gravacao */ }
            }

            // 3a) Exclusao de anexo: ate 3 tentativas. Se falhar, o anexo VOLTA para o card — sumir
            // da tela com o arquivo ainda no DevOps foi exatamente o que enganou antes.
            foreach (var kv in _attachRemovePending.ToList())
            {
                foreach (var att in kv.Value.ToList())
                {
                    Exception? error = null;
                    for (var attempt = 1; attempt <= 3; attempt++)
                    {
                        try
                        {
                            await TfsAttachmentService.RemoveAttachmentAsync(_options, kv.Key, att.Url);
                            error = null;
                            break;
                        }
                        catch (Exception ex)
                        {
                            error = ex;
                            if (attempt < 3) await Task.Delay(400);
                        }
                    }
                    kv.Value.Remove(att);
                    if (error == null)
                    {
                        if (_attachments.TryGetValue(kv.Key, out var sent))
                            sent.RemoveAll(a => string.Equals(a.Url, att.Url, StringComparison.OrdinalIgnoreCase));
                        ok++;
                    }
                    else
                    {
                        _removedAttachmentUrls.Remove(att.Url);
                        fails.Add($"#{kv.Key} (anexo {att.Name}): {error.Message}");
                    }
                }
                _attachRemovePending.Remove(kv.Key);
            }

            // 3b) Responsável (System.AssignedTo). 403 = sem permissão.
            foreach (var kv in _ownerPending.ToList())
            {
                var (success, msg) = await TfsImportService.SetWorkItemAssignedToAsync(_options, kv.Key, kv.Value);
                if (success) { _ownerApplied[kv.Key] = kv.Value; _ownerPending.Remove(kv.Key); ok++; }
                else fails.Add($"#{kv.Key} (responsável): {msg}");
            }

            // 3c) Nome/título (System.Title). 403 = sem permissão.
            foreach (var kv in _titlePending.ToList())
            {
                var (success, msg) = await TfsImportService.SetWorkItemTitleAsync(_options, kv.Key, kv.Value);
                if (success) { _titleApplied[kv.Key] = kv.Value; _titlePending.Remove(kv.Key); ok++; }
                else fails.Add($"#{kv.Key} (nome): {msg}");
            }

            // 3d) HH estimado (OriginalEstimate) e HH realizado (CompletedWork). 403 = sem permissão.
            foreach (var wid in _estPending.Keys.Union(_donePending.Keys).ToList())
            {
                double? eh = _estPending.TryGetValue(wid, out var ev) ? ev : null;
                double? ch = _donePending.TryGetValue(wid, out var cv) ? cv : null;
                var (success, msg) = await TfsImportService.SetWorkItemHoursAsync(_options, wid, eh, ch);
                if (success) { _estPending.Remove(wid); _donePending.Remove(wid); _closedDropped.Remove(wid); ok++; }
                else fails.Add($"#{wid} (HH): {msg}");
            }

            // 3f) Sprint/iteração da Story (System.IterationPath). 403 = sem permissão.
            foreach (var kv in _iterPending.ToList())
            {
                var (success, msg) = await TfsImportService.SetWorkItemIterationPathAsync(_options, kv.Key, kv.Value);
                if (success) { _iterApplied[kv.Key] = kv.Value; _iterPending.Remove(kv.Key); ok++; }
                else fails.Add($"#{kv.Key} (sprint): {msg}");
            }

            Phase("Sprint_PhFields");
            // 3i) Data de Início (Data_Inicio) da Story. 403 = sem permissão.
            foreach (var kv in _startPending.ToList())
            {
                var (success, msg) = await TfsImportService.SetWorkItemStartDateAsync(_options, kv.Key, kv.Value);
                if (success) { _startApplied[kv.Key] = kv.Value; _startPending.Remove(kv.Key); ok++; }
                else fails.Add($"#{kv.Key} (data início): {msg}");
            }
            // 3i2) Data alvo (Data_Fim) da Task.
            foreach (var kv in _finishPending.ToList())
            {
                var (success, msg) = await TfsImportService.SetWorkItemFinishDateAsync(_options, kv.Key, kv.Value);
                if (success) { _finishApplied[kv.Key] = kv.Value; _finishPending.Remove(kv.Key); ok++; }
                else fails.Add($"#{kv.Key} (data alvo): {msg}");
            }

            // 3j) Critérios de Aceitação da Story. 403 = sem permissão.
            foreach (var kv in _acPending.ToList())
            {
                var (success, msg) = await TfsImportService.SetWorkItemAcceptanceCriteriaAsync(_options, kv.Key, kv.Value);
                if (success) { _acPending.Remove(kv.Key); ok++; }
                else fails.Add($"#{kv.Key} (critérios de aceitação): {msg}");
            }

            // 3g2) Story (pai) da Task: mesma troca de System.Parent usada na Feature da Story.
            foreach (var kv in _taskParentPending.ToList())
            {
                // Alvo negativo = Story nova, que ainda nao existe no DevOps. Essas ficam para
                // depois do bloco de criacao, quando o id temporario ja virou id real.
                if (kv.Value < 0) continue;
                var (success, msg) = await TfsImportService.SetWorkItemParentAsync(_options, kv.Key, kv.Value);
                if (success) { _taskParentApplied[kv.Key] = kv.Value; _taskParentPending.Remove(kv.Key); ok++; reload = true; }
                else fails.Add($"#{kv.Key} (story): {msg}");
            }

            // 3h) Feature (pai) da Story. 403 = sem permissão.
            foreach (var kv in _featurePending.ToList())
            {
                var (success, msg) = await TfsImportService.SetWorkItemParentAsync(_options, kv.Key, kv.Value);
                if (success) { _featureApplied[kv.Key] = kv.Value; _featurePending.Remove(kv.Key); ok++; reload = true; }
                else fails.Add($"#{kv.Key} (feature): {msg}");
            }

            // 3g) Bloqueio: tag + tramite + conferencia, tudo no BlockService (mesmo caminho da
            // sincronizacao e da grade do tech lead). 403 = sem permissão.
            var blockDurationIds = new List<int>();
            foreach (var kv in _blockPending.ToList())
            {
                var cur = _cardById.TryGetValue(kv.Key, out var cc2) ? cc2.Tags : (StoryById(kv.Key)?.Tags ?? "");
                var note = AppStrings.Get(kv.Value ? "Sprint_BlockHistory" : "Sprint_UnblockHistory",
                    BlockedTag(), string.IsNullOrWhiteSpace(_currentUser) ? "NXProject" : _currentUser!);
                var res = await BlockService.SetBlockedAsync(
                    _options, kv.Key, kv.Value, EffTags(kv.Key, cur), _currentUser, note);
                // Trilha da gravacao da tag: sem ela nao da para saber se o NX mandou o valor certo,
                // se o DevOps recusou, ou se a tag voltou na releitura do board.
                AppendTagWriteLog($"#{kv.Key} bloqueio={kv.Value} tag='{BlockedTag()}'"
                    + $" | origem='{cur}' | efetivas='{EffTags(kv.Key, cur)}' | enviado='{res.Tags}'"
                    + $" | resultado={(res.Ok ? "OK" : "FALHOU: " + res.Message)}");
                if (res.Ok)
                {
                    _tagsApplied[kv.Key] = res.Tags; _blockPending.Remove(kv.Key); ok++;
                    // Campo de duracao (opcional): ao BLOQUEAR marca 1 na hora — e barato e o card
                    // ja mostra o icone. Ao DESBLOQUEAR o total vem do historico, o que custa uma
                    // leitura por item: sai depois, sem segurar a tela.
                    if (BlockService.DurationEnabled(_options))
                    {
                        if (kv.Value) await BlockService.MarkBlockedAsync(_options, kv.Key);
                        else blockDurationIds.Add(kv.Key);
                    }
                }
                else fails.Add($"#{kv.Key} (bloqueio): {res.Message}");
            }
            // Dispara o calculo do total DEPOIS de tudo: o board volta a responder na hora.
            if (blockDurationIds.Count > 0) _ = UpdateBlockDurationsAsync(blockDurationIds);

            // 3h) Tag "nao planejada" (NP), preservando as demais tags.
            foreach (var kv in _unplannedPending.ToList())
            {
                var cur = _cardById.TryGetValue(kv.Key, out var cc3) ? cc3.Tags : (StoryById(kv.Key)?.Tags ?? "");
                var newTags = TfsImportService.ToggleTag(EffTags(kv.Key, cur), UnplannedTag(), kv.Value);
                var (success, msg) = await TfsImportService.SetWorkItemTagsAsync(_options, kv.Key, newTags);
                if (success) { _tagsApplied[kv.Key] = newTags; _unplannedPending.Remove(kv.Key); ok++; }
                else fails.Add($"#{kv.Key} (NP): {msg}");
            }

            // 3i) Tag de WIP: marca no DevOps as Tasks que estão acima do limite da pessoa e
            //     tira das que voltaram para dentro. Só grava o que realmente mudou.
            var wipTag = WipTag();
            foreach (var card in _cardById.Values.Where(c => c.Id > 0).ToList())
            {
                var want = _wipOver.Contains(card.Id);
                // Nada mudou para este card desde a carga: nao e trabalho desta gravacao.
                if (want == _wipBaseline.Contains(card.Id)) continue;
                if (HasTag(EffTags(card.Id, card.Tags), wipTag) == want) continue;
                var (success, msg) = await TfsImportService.SetSingleTagAsync(_options, card.Id, wipTag, want);
                if (success)
                {
                    _tagsApplied[card.Id] = TfsImportService.ToggleTag(EffTags(card.Id, card.Tags), wipTag, want);
                    if (want) _wipBaseline.Add(card.Id); else _wipBaseline.Remove(card.Id);
                    ok++; wipOk++;
                }
                else fails.Add($"#{card.Id} (WIP): {msg}");
            }

            // 3e) Exclusão de Tasks marcadas (só New). Move para a lixeira do DevOps.
            foreach (var did in _deletePending.ToList())
            {
                var (success, msg) = await TfsImportService.TryDeleteWorkItemAsync(_options, did);
                if (success)
                {
                    _deletePending.Remove(did);
                    _pending.Remove(did); _descPending.Remove(did); _tramitePending.Remove(did);
                    _ownerPending.Remove(did); _titlePending.Remove(did); _prioPending.Remove(did);
                    _estPending.Remove(did); _donePending.Remove(did); _closedDropped.Remove(did); _taskRankPending.Remove(did);
                    ok++; reload = true;
                }
                else fails.Add($"#{did} (excluir): {msg}");
            }

            // 4) Trâmite (comentário/discussão; aceita HTML com imagem).
            Phase("Sprint_PhTramite");
            foreach (var kv in _tramitePending.ToList())
            {
                try
                {
                    // Mesmo tratamento da descricao: imagem colada vira anexo antes de postar.
                    var tramiteHtml = kv.Value;
                    try
                    {
                        tramiteHtml = await TfsAttachmentService.InlineImagesToAttachmentsAsync(
                            _options, kv.Key, tramiteHtml, TfsAttachmentService.InlineImageTramitePrefix);
                    }
                    catch (Exception ex) { fails.Add($"#{kv.Key} (imagem do tramite): {ex.Message}"); }
                    var posted = await TfsImportService.AddWorkItemCommentIfChangedAsync(_options, kv.Key, tramiteHtml);
                    if (posted)
                    {
                        _tramitePending.Remove(kv.Key);
                        ok++;
                        // Mesma conferencia da descricao: o comentario aceita imagem, mas se o
                        // servidor descartar alguma, o usuario precisa saber na hora.
                        var last = await TfsImportService.GetLastWorkItemCommentAsync(_options, kv.Key);
                        var sent = HtmlSignal(tramiteHtml);
                        var got = HtmlSignal(last);
                        if (got.Images < sent.Images)
                            fails.Add($"#{kv.Key} (tramite): o comentario foi gravado, mas o DevOps descartou "
                                      + $"{sent.Images - got.Images} imagem(ns).");
                    }
                    else fails.Add($"#{kv.Key} (trâmite): sem permissão ou sem alteração");
                }
                catch (Exception ex) { fails.Add($"#{kv.Key} (trâmite): {ex.Message}"); }
            }

            // 4b) Prioridade da Task (atributo, dentro da faixa configurada).
            foreach (var kv in _prioPending.ToList())
            {
                var (success, msg) = await TfsImportService.SetWorkItemPriorityAsync(_options, kv.Key, kv.Value);
                if (success) { _prioApplied[kv.Key] = kv.Value; _prioPending.Remove(kv.Key); ok++; } else fails.Add($"#{kv.Key} (prio): {msg}");
            }

            // 4c) Rank (StackRank) das Stories movidas.
            foreach (var id in _storyRankPending.ToList())
            {
                var (success, msg) = await TfsImportService.SetWorkItemStackRankAsync(_options, id, _storyRank.TryGetValue(id, out var r) ? r : 0);
                if (success) { _storyRankPending.Remove(id); ok++; } else fails.Add($"#{id} (rank): {msg}");
            }

            // 4d) Rank (StackRank) das Tasks reordenadas dentro do grupo de prioridade.
            foreach (var id in _taskRankPending.ToList())
            {
                var (success, msg) = await TfsImportService.SetWorkItemStackRankAsync(_options, id, _taskRank.TryGetValue(id, out var r) ? r : 0);
                if (success) { _taskRankPending.Remove(id); ok++; } else fails.Add($"#{id} (rank): {msg}");
            }

            // 4e) Estado das Stories movidas no StoryBoard.
            foreach (var kv in _storyStatePending.ToList())
            {
                var (success, msg) = await TfsImportService.SetWorkItemStateAsync(_options, kv.Key, kv.Value);
                if (success) { _storyStateApplied[kv.Key] = kv.Value; _storyStatePending.Remove(kv.Key); ok++; }
                else fails.Add($"#{kv.Key} (estado Story): {msg}");
            }

            Phase("Sprint_PhNew");
            // 5) Novos cards: cria Stories (recebem ID) e depois Tasks (usando o ID da story-pai).
            // Em "Todas as sprints" não há iteração-alvo: não permite criar itens novos.
            var tempToReal = new Dictionary<int, int>();
            // Iteração-alvo de cada card: a escolhida no card, senão a sprint única aberta.
            string IterOf(NewCard n) => !string.IsNullOrWhiteSpace(n.IterationPath) ? n.IterationPath : _sprintPath;
            // Ordem importa: EPIC -> Feature -> Story -> Task, para o filho encontrar o id real
            // do pai criado na mesma gravacao (tempToReal).
            foreach (var ne in _newCards.Where(n => n.Type is "Epic" or "Feature").ToList())
            {
                var kind = ne.Type == "Epic" ? "Epic" : "Feature";
                if (string.IsNullOrWhiteSpace(ne.Title))
                { fails.Add(AppStrings.Get("Sprint_NewIncomplete")); continue; }

                var parentId = ne.ParentId < 0 ? (tempToReal.TryGetValue(ne.ParentId, out var pr) ? pr : 0) : ne.ParentId;
                if (parentId <= 0) { fails.Add($"{kind} '{ne.Title}': {AppStrings.Get("Sprint_NewNoParent")}"); continue; }

                bool dupEf = _newCards.Any(o => o != ne && o.Type == ne.Type && o.ParentId == ne.ParentId
                    && o.Title.Trim().Equals(ne.Title.Trim(), StringComparison.CurrentCultureIgnoreCase));
                if (dupEf) { fails.Add($"{kind} '{ne.Title}': {AppStrings.Get("Sprint_DupName")}"); continue; }

                // Sem HH: o esforco de Feature/EPIC vem do rollup dos filhos no DevOps.
                var (eid, emsg) = await TfsImportService.CreateChildWorkItemAsync(_options, kind, ne.Title.Trim(), parentId,
                    string.IsNullOrWhiteSpace(IterOf(ne)) ? null : IterOf(ne),
                    string.IsNullOrWhiteSpace(ne.Description) ? null : TfsImportService.PlainTextToSimpleHtml(ne.Description),
                    string.IsNullOrWhiteSpace(ne.AssignedTo) ? null : ne.AssignedTo);
                if (eid > 0) { tempToReal[ne.TempId] = eid; _newCards.Remove(ne); ok++; reload = true; }
                else fails.Add($"{kind} '{ne.Title}': {emsg}");
            }

            foreach (var ns in _newCards.Where(n => n.Type == "Story").ToList())
            {
                if (string.IsNullOrWhiteSpace(ns.Title) || !(ns.Effort is > 0) || string.IsNullOrWhiteSpace(ns.AssignedTo))
                { fails.Add(AppStrings.Get("Sprint_NewIncomplete")); continue; }
                if (string.IsNullOrWhiteSpace(IterOf(ns))) { fails.Add(AppStrings.Get("Sprint_NewNeedsSprint")); continue; }
                bool dup = (_board?.Stories.Any(s => s.FeatureId == ns.FeatureId && s.Title.Trim().Equals(ns.Title.Trim(), StringComparison.CurrentCultureIgnoreCase)) ?? false)
                    || _newCards.Any(o => o != ns && o.Type == "Story" && o.FeatureId == ns.FeatureId && o.Title.Trim().Equals(ns.Title.Trim(), StringComparison.CurrentCultureIgnoreCase));
                if (dup) { fails.Add($"Story '{ns.Title}': {AppStrings.Get("Sprint_DupName")}"); continue; }
                var storyParent = ns.ParentId < 0 ? (tempToReal.TryGetValue(ns.ParentId, out var sp2) ? sp2 : 0) : ns.ParentId;
                if (storyParent <= 0) { fails.Add($"Story '{ns.Title}': {AppStrings.Get("Sprint_NewNoParent")}"); continue; }
                var (nid, msg) = await TfsImportService.CreateChildWorkItemAsync(_options, "User Story", ns.Title.Trim(), storyParent, IterOf(ns),
                    string.IsNullOrWhiteSpace(ns.Description) ? null : TfsImportService.PlainTextToSimpleHtml(ns.Description),
                    string.IsNullOrWhiteSpace(ns.AssignedTo) ? null : ns.AssignedTo, ns.Effort, ns.StartDate);
                if (nid > 0)
                {
                    tempToReal[ns.TempId] = nid; _newCards.Remove(ns); ok++; reload = true;
                    // Fica no topo da Feature onde foi criada, inclusive depois do reload.
                    _storyRank[nid] = await RankNewOnTopAsync(nid,
                        (_board?.Stories.Where(s => s.FeatureId == ns.FeatureId && s.Id > 0)
                                       .Select(s => StoryRankOf(s.Id)) ?? Enumerable.Empty<double>()));
                    // O filtro de Projeto guarda IDs de Story. A Story recem-criada nao esta
                    // nessa lista e sumia do board depois de gravar (so voltava ao reaplicar o
                    // filtro). Entra aqui, para continuar visivel onde foi criada.
                    if (_selectedStoryIds.Count > 0) _selectedStoryIds.Add(nid);
                }
                else fails.Add($"Story '{ns.Title}': {msg}");
            }
            foreach (var nt in _newCards.Where(n => n.Type == "Task").ToList())
            {
                if (string.IsNullOrWhiteSpace(nt.Title) || !(nt.Effort is > 0) || string.IsNullOrWhiteSpace(nt.AssignedTo))
                { fails.Add(AppStrings.Get("Sprint_NewIncomplete")); continue; }
                if (string.IsNullOrWhiteSpace(IterOf(nt))) { fails.Add(AppStrings.Get("Sprint_NewNeedsSprint")); continue; }
                var parent = nt.ParentId < 0 ? (tempToReal.TryGetValue(nt.ParentId, out var r) ? r : 0) : nt.ParentId;
                if (parent <= 0) { fails.Add($"Task '{nt.Title}': story-pai não criada"); continue; }
                bool dup = (_board?.Stories.FirstOrDefault(s => s.Id == parent)?.Tasks.Any(t => t.Title.Trim().Equals(nt.Title.Trim(), StringComparison.CurrentCultureIgnoreCase)) ?? false)
                    || _newCards.Any(o => o != nt && o.Type == "Task" && o.ParentId == nt.ParentId && o.Title.Trim().Equals(nt.Title.Trim(), StringComparison.CurrentCultureIgnoreCase));
                if (dup) { fails.Add($"Task '{nt.Title}': {AppStrings.Get("Sprint_DupName")}"); continue; }
                var (nid, msg) = await TfsImportService.CreateChildWorkItemAsync(_options, "Task", nt.Title.Trim(), parent, IterOf(nt),
                    string.IsNullOrWhiteSpace(nt.Description) ? null : TfsImportService.PlainTextToSimpleHtml(nt.Description),
                    string.IsNullOrWhiteSpace(nt.AssignedTo) ? null : nt.AssignedTo, nt.Effort);
                if (nid > 0)
                {
                    _newCards.Remove(nt); ok++; reload = true;
                    // Mesma ideia da Story: no topo da Story onde foi criada.
                    _taskRank[nid] = await RankNewOnTopAsync(nid,
                        (_board?.Stories.FirstOrDefault(s => s.Id == parent)?.Tasks
                                .Where(t => t.Id > 0).Select(EffTaskRank) ?? Enumerable.Empty<double>()));
                }
                else fails.Add($"Task '{nt.Title}': {msg}");
            }

            // 5b) Tasks que esperavam uma Story NOVA (criada agora pelo card "criar Story aqui"):
            // troca o id temporario pelo real e so entao muda o pai no DevOps.
            foreach (var kv in _taskParentPending.Where(k => k.Value < 0).ToList())
                if (tempToReal.TryGetValue(kv.Value, out var realStoryId))
                    _taskParentPending[kv.Key] = realStoryId;
            foreach (var kv in _taskParentPending.Where(k => k.Value > 0).ToList())
            {
                var (success, msg) = await TfsImportService.SetWorkItemParentAsync(_options, kv.Key, kv.Value);
                if (success) { _taskParentApplied[kv.Key] = kv.Value; _taskParentPending.Remove(kv.Key); ok++; reload = true; }
                else fails.Add($"#{kv.Key} (story): {msg}");
            }

            Phase("Sprint_PhReload");
            SaveProgress.Value = SaveProgress.Maximum;
            if (reload)
                await ReloadBoardAsync(_sprintPaths.ToList()); // recarrega a seleção atual (não "todas")
            UpdatePendingButton();
            Render();
            if (fails.Count == 0)
                MessageBox.Show(this, AppStrings.Get("Sprint_UpdateDone", ok.ToString())
                        + (wipOk > 0 ? Environment.NewLine + Environment.NewLine
                                       + AppStrings.Get("Sprint_UpdateWipNote", wipOk.ToString()) : ""),
                    "NXProject", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                MessageBox.Show(this, AppStrings.Get("Sprint_UpdatePartial",
                        ok.ToString(), fails.Count.ToString(), string.Join("\n", fails)),
                    "NXProject", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                SaveProgress.Visibility = Visibility.Collapsed;
                StatusText.Text = "";
                UpdatePendingButton(); // reabilita o botão conforme pendências restantes
            }
        }

        // Filtros de recorte (pessoa, cronograma, story) — não mexem nas colunas.
        private bool PassesBaseFilters(TfsImportService.SprintTaskCard t)
        {
            // "Somente Story do cronograma": o recorte é pela STORY pai estar no cronograma
            // aberto. As Tasks continuam vindo do TFS (o cronograma normalmente para na Story,
            // então filtrar pelo id da própria Task esvaziava o board).
            if (OnlyScheduleCheck.IsChecked == true
                && !(EffTaskParent(t) is int sp0 && _scheduleIds.Contains(sp0))) return false;
            // Recortes por tag da Task (bloqueada / não planejada). Usam o valor EFETIVO das
            // tags, então respeitam alterações ainda na fila do "Atualizar TFS".
            // Story bloqueada bloqueia as filhas na prática (é como o cronograma trata: o BLOCK
            // da Story tem prioridade sobre o da Task), então as Tasks dela entram no filtro.
            if (OnlyBlockedCheck.IsChecked == true
                && !EffBlocked(t.Id, t.Tags)
                && !(StoryById(EffTaskParent(t)) is { } bs && EffBlocked(bs.Id, bs.Tags)))
                return false;
            if (OnlyUnplannedCheck.IsChecked == true && !EffUnplanned(t.Id, t.Tags)) return false;
            // Em andamento: Doing e ainda não encerrada. Marcada Done já saiu do "fazendo",
            // e encerrada vira Done por definição — os dois casos ficam de fora.
            if (OnlyDoingCheck.IsChecked == true
                && !(_doing.Contains(t.Id) && !_done.Contains(t.Id) && !IsClosedState(EffState(t)))) return false;
            // Done com o estado ainda aberto: trabalho concluído que falta encerrar no DevOps.
            if (OnlyDoneActiveCheck.IsChecked == true
                && !(_done.Contains(t.Id) && !IsClosedState(EffState(t)))) return false;
            // Recorte pelo ESTADO da Task (o do DevOps, não a marcação Doing): só Active.
            if (OnlyTaskActiveCheck.IsChecked == true
                && TfsImportService.NormalizeTaskState(EffState(t)) != "Active") return false;
            if (_selectedPeople.Count > 0 && !_selectedPeople.Contains(t.AssignedTo ?? "")) return false;
            // Recorte "ultima sprint da pessoa": quem manda e a STORY. Se a Story entra, as
            // Tasks dela vem junto, mesmo as de sprint anterior — Task nao se le sozinha, se le
            // dentro da entrega. Duas excecoes: sprint FUTURA nunca entra (o recorte e do que ja
            // comecou), e Task orfa (sem Story no board) vale pela propria sprint.
            if (IsFutureSprint(t.IterationPath)) return false;
            if (StoryById(EffTaskParent(t)) is { } parentStory)
            {
                if (!LastSprintOk(EffOwner(parentStory.Id, parentStory.AssignedTo), parentStory.IterationPath))
                    return false;
            }
            else if (!LastSprintOk(t.AssignedTo, t.IterationPath)) return false;
            if (_selectedStoryIds.Count > 0 && !_selectedStoryIds.Contains(EffTaskParent(t))) return false;
            // Busca ao vivo com escopo (Ambos / Task / Story).
            var q = SearchQuery();
            if (!string.IsNullOrEmpty(q))
            {
                var scope = SearchScope();
                var storyRow = _storyById.TryGetValue(EffTaskParent(t), out var st) ? st : null;
                bool Has(string? s) => !string.IsNullOrEmpty(s) && s.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0;
                bool taskMatch = Has(t.Title) || Has(t.AssignedTo) || t.Id.ToString().Contains(q);
                bool storyMatch = Has(storyRow?.Title);
                // A Task herda o casamento pelos niveis acima da Story dela.
                bool levelMatch = storyRow != null && LevelMatches(storyRow, q);
                bool personMatch = Has(t.AssignedTo) || Has(storyRow?.AssignedTo);
                bool match = scope switch
                {
                    1 => taskMatch, 2 => storyMatch, 3 => levelMatch, 4 => personMatch,
                    _ => taskMatch || storyMatch || levelMatch
                };
                if (!match) return false;
            }
            return true;
        }

        // 0 = Tudo, 1 = Task, 2 = Story, 3 = niveis acima (Feature / EPIC / Work Item Project),
        // 4 = pessoa (responsavel da Task ou da Story) — atalho para quando o filtro de pessoa
        // esta com varios marcados e o que se quer e so isolar um deles.
        private int SearchScope() => SearchScopeCombo?.SelectedIndex is int i && i >= 0 ? i : 0;

        /// <summary>
        /// A Story casa com a busca pelos niveis ACIMA dela: Feature, EPIC e Work Item Project
        /// (nome ou id). Assim da para achar "tudo do projeto X" ou "tudo da feature Y" sem
        /// precisar abrir o filtro em arvore.
        /// </summary>
        private static bool LevelMatches(TfsImportService.SprintStoryRow s, string q)
        {
            bool Has(string? v) => !string.IsNullOrEmpty(v)
                && v.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0;
            bool IsId(int id) => id > 0 && id.ToString().Contains(q);
            return Has(s.FeatureTitle) || IsId(s.FeatureId)
                || Has(s.FeatureEpicTitle) || IsId(s.FeatureEpicId)
                || Has(s.FeatureProjectTitle) || IsId(s.FeatureProjectId);
        }

        /// <summary>
        /// O resumo por estado so interessa parado no topo; durante a rolagem ele so ocupava
        /// altura que faz falta para os cards. Entao ele some ao rolar e volta ao topo.
        /// A histerese (some acima de 24px, volta em 2px) evita o vai-e-vem: esconder o resumo
        /// aumenta a area visivel e poderia zerar o offset, reexibindo o resumo em loop.
        /// </summary>
        private void OnBoardScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // Mudanca de altura da area visivel e EFEITO do proprio esconder/mostrar o resumo,
            // nao rolagem do usuario: reagir a ela e o que realimentava o vai-e-vem.
            if (e.ViewportHeightChange != 0 || e.ExtentHeightChange != 0) return;
            // Arrastando a barra: o polegar e reposicionado pelo mouse a cada layout, entao
            // trocar a altura agora faz o offset pular e o resumo piscar. Decide ao soltar.
            if (System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed) return;
            UpdateSummaryVisibility();
        }

        private void OnBoardScrollMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => UpdateSummaryVisibility();

        /// <summary>"Ver Resumo": liga/desliga o resumo por estado. Vale so para esta sessao,
        /// de proposito: o board sempre abre com o resumo, que e o panorama da sprint.</summary>
        private void OnShowSummaryChanged(object sender, RoutedEventArgs e)
        {
            if (SummaryBox == null) return;
            // Religado: volta a aparecer ja, se a lista estiver no topo (a regra de sempre).
            SummaryBox.Visibility = Visibility.Collapsed;
            UpdateSummaryVisibility();
        }

        private void UpdateSummaryVisibility()
        {
            if (SummaryBox == null || BoardScroll == null) return;
            // "Ver Resumo" desmarcado: o resumo nunca aparece, nem com a lista no topo.
            if (ShowSummaryCheck?.IsChecked != true)
            {
                if (SummaryBox.Visibility != Visibility.Collapsed) SummaryBox.Visibility = Visibility.Collapsed;
                return;
            }
            var off = BoardScroll.VerticalOffset;
            if (off > 60 && SummaryBox.Visibility == Visibility.Visible)
                SummaryBox.Visibility = Visibility.Collapsed;
            else if (off <= 0 && SummaryBox.Visibility != Visibility.Visible)
                SummaryBox.Visibility = Visibility.Visible;
        }

        /// <summary>Minimo de caracteres para a busca valer. Com 1 ou 2 letras quase tudo casa
        /// e o board inteiro era redesenhado a cada tecla, dando sensacao de travamento.</summary>
        private const int SearchMinChars = 3;

        /// <summary>Texto da busca ja validado: vazio (= sem busca) enquanto nao houver
        /// <see cref="SearchMinChars"/> caracteres.</summary>
        private string SearchQuery()
        {
            var q = SearchBox?.Text?.Trim() ?? "";
            return q.Length >= SearchMinChars ? q : "";
        }

        // Redesenho da busca sai por um timer: digitando rapido, so o ultimo toque redesenha.
        private System.Windows.Threading.DispatcherTimer? _searchTimer;

        private void OnSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_board == null) return;
            UpdateSearchHint();
            _searchTimer ??= CreateSearchTimer();
            _searchTimer.Stop();
            _searchTimer.Start();
        }

        private System.Windows.Threading.DispatcherTimer CreateSearchTimer()
        {
            var t = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            t.Tick += (_, _) => { t.Stop(); RenderBusy(); };
            return t;
        }

        /// <summary>Deixa claro que a busca ainda nao comecou (menos de 3 caracteres).</summary>
        private void UpdateSearchHint()
        {
            if (SearchBox == null) return;
            var len = (SearchBox.Text ?? "").Trim().Length;
            SearchBox.ToolTip = len > 0 && len < SearchMinChars
                ? AppStrings.Get("Sprint_SearchMinChars", SearchMinChars.ToString())
                : null;
            SearchBox.BorderBrush = len > 0 && len < SearchMinChars
                ? new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00))
                : SystemColors.ControlDarkBrush;
        }

        // Filtros que escondem CARDS por estado (a coluna continua no board). Cards pendentes
        // (recém-arrastados) sempre aparecem, mesmo num estado escondido/Closed.
        /// <summary>
        /// Faz a Task aparecer no board AFROUXANDO os filtros que a escondem, em vez de furar o
        /// recorte: o board continua coerente com o que esta na tela. Mostra antes o que sera
        /// alterado e so mexe com a confirmacao do usuario. Devolve se ela passou a aparecer (a
        /// grade usa isso para se fechar) e a mensagem para a linha de status.
        /// </summary>
        private async Task<(bool Shown, string Message)> ShowTaskOnBoardAsync(int taskId, string? iterationPath)
        {
            // Foto do estado ANTES de qualquer mexida — inclui as SPRINTS carregadas, porque
            // trazer outra sprint tambem muda o que o board mostra. So a PRIMEIRA e guardada:
            // varios "mostrar no board" seguidos devem voltar para onde o usuario estava.
            CaptureBoardViewSnapshot();
            var card = _board?.Stories.SelectMany(s => s.Tasks).FirstOrDefault(t => t.Id == taskId);

            // Fora da(s) sprint(s) carregada(s): nenhum filtro de tela resolve — o card nem foi
            // trazido do DevOps. Ai a saida e marcar a sprint dela e recarregar.
            if (card == null)
            {
                var sprint = _sprints.FirstOrDefault(sp =>
                    !string.IsNullOrWhiteSpace(iterationPath)
                    && string.Equals(sp.Path, iterationPath, StringComparison.OrdinalIgnoreCase));
                if (sprint == null)
                    return (false, ShowInfo(AppStrings.Get("Sprint_ForceNotLoaded", taskId.ToString())));

                var askSprint = AppStrings.Get("Sprint_AdjustSprintAsk", taskId.ToString(), sprint.Name);
                if (MessageBox.Show(this, askSprint, "NXProject", MessageBoxButton.YesNo,
                        MessageBoxImage.Question, MessageBoxResult.Yes) != MessageBoxResult.Yes)
                    return (false, AppStrings.Get("Sprint_AdjustCancelled"));

                var paths = _sprintPaths.ToList();
                if (!paths.Contains(sprint.Path)) paths.Add(sprint.Path);
                ApplySprintChecks(paths);
                await ReloadBoardAsync(paths);
                RestoreFiltersButton.Visibility = Visibility.Visible;   // ha o que restaurar
                card = _board?.Stories.SelectMany(s => s.Tasks).FirstOrDefault(t => t.Id == taskId);
                if (card == null)
                    return (false, ShowInfo(AppStrings.Get("Sprint_ForceNotLoaded", taskId.ToString())));
            }

            var why = WhyHidden(card);
            if (string.IsNullOrEmpty(why))
            {
                var okMsg = AppStrings.Get("Sprint_ForceAlreadyVisible", taskId.ToString());
                StatusText.Text = okMsg;
                return (true, okMsg);
            }

            // Lista o que sera afrouxado, na linguagem dos proprios filtros da tela.
            var plan = BuildFilterRelaxPlan(card);
            var ask = AppStrings.Get("Sprint_AdjustAsk", taskId.ToString(), why,
                string.Join(Environment.NewLine, plan.Select(p => "  • " + p.Label)));
            if (MessageBox.Show(this, ask, "NXProject", MessageBoxButton.YesNo,
                    MessageBoxImage.Question, MessageBoxResult.Yes) != MessageBoxResult.Yes)
                return (false, AppStrings.Get("Sprint_AdjustCancelled"));

            foreach (var step in plan) step.Apply();
            // NAO chama SavePrefs: este afrouxamento e para OLHAR uma Task, nao a preferencia
            // de trabalho do usuario. Fechando e reabrindo o board, os filtros salvos voltam —
            // e, sem fechar, o botao "Restaurar filtros" devolve o estado anterior.
            RestoreFiltersButton.Visibility = Visibility.Visible;
            RenderBusy();
            var done = AppStrings.Get("Sprint_AdjustDone", taskId.ToString(),
                string.Join(" · ", plan.Select(p => p.Label)));
            // Tambem na linha de status do board: o botao amarelo e o aviso se reforcam.
            StatusText.Text = done;
            return (true, done);
        }

        /// <summary>Como voltar ao estado anterior ao primeiro "Mostrar no board". Null = nada a restaurar.</summary>
        private Func<Task>? _restoreFilters;

        /// <summary>
        /// Guarda filtros e sprints como estao agora. Chamada a cada "Mostrar no board", mas so a
        /// PRIMEIRA vale: o restaurar tem que devolver o ponto de partida, nao o passo anterior.
        /// </summary>
        private void CaptureBoardViewSnapshot()
        {
            if (_restoreFilters != null) return;

            var wasSchedule = OnlyScheduleCheck.IsChecked;
            var wasBlocked = OnlyBlockedCheck.IsChecked;
            var wasUnplanned = OnlyUnplannedCheck.IsChecked;
            var wasDoing = OnlyDoingCheck.IsChecked;
            var wasDoneActive = OnlyDoneActiveCheck.IsChecked;
            var wasTaskActive = OnlyTaskActiveCheck.IsChecked;
            var wasPeople = _selectedPeople.ToList();
            var wasStories = _selectedStoryIds.ToList();
            var wasHidden = _hiddenStates.ToList();
            var wasDays = _closedDays;
            var wasSearch = SearchBox.Text;
            var wasSprints = _sprintPaths.ToList();

            _restoreFilters = async () =>
            {
                OnlyScheduleCheck.IsChecked = wasSchedule;
                OnlyBlockedCheck.IsChecked = wasBlocked;
                OnlyUnplannedCheck.IsChecked = wasUnplanned;
                OnlyDoingCheck.IsChecked = wasDoing;
                OnlyDoneActiveCheck.IsChecked = wasDoneActive;
                OnlyTaskActiveCheck.IsChecked = wasTaskActive;
                _selectedPeople.Clear();
                foreach (var p in wasPeople) _selectedPeople.Add(p);
                _selectedStoryIds.Clear();
                foreach (var i in wasStories) _selectedStoryIds.Add(i);
                _hiddenStates.Clear();
                foreach (var st in wasHidden) _hiddenStates.Add(st);
                _closedDays = wasDays;
                ClosedDaysBox.Text = wasDays.ToString();
                SearchBox.Text = wasSearch;
                PopulateStateFilter();
                // Sprint so volta recarregando o board — e so quando de fato mudou.
                if (!wasSprints.OrderBy(x => x).SequenceEqual(_sprintPaths.OrderBy(x => x)))
                {
                    ApplySprintChecks(wasSprints);
                    await ReloadBoardAsync(wasSprints);
                }
            };
        }

        /// <summary>Botao "Aplicar" da configuracao: fecha o popup e redesenha o board, o mesmo
        /// gesto dos filtros de Projeto/Sprint/Pessoa. As opcoes ja valem ao serem marcadas.</summary>
        private void OnConfigApplyClick(object sender, RoutedEventArgs e)
        {
            ConfigToggle.IsChecked = false;
            SavePrefs();
            Render();
        }

        private async void OnRestoreFiltersClick(object sender, RoutedEventArgs e)
        {
            if (_restoreFilters is { } restore)
            {
                _restoreFilters = null;
                await restore();
            }
            RestoreFiltersButton.Visibility = Visibility.Collapsed;
            StatusText.Text = AppStrings.Get("Sprint_RestoreFiltersDone");
            RenderBusy();
        }

        private string ShowInfo(string msg)
        {
            MessageBox.Show(this, msg, "NXProject", MessageBoxButton.OK, MessageBoxImage.Information);
            return msg;
        }

        /// <summary>
        /// O que precisa ser afrouxado para esta Task aparecer: um passo por filtro que a barra,
        /// cada um com o rotulo que o usuario ve e a acao correspondente. Nada e aplicado aqui.
        /// </summary>
        private List<(string Label, Action Apply)> BuildFilterRelaxPlan(TfsImportService.SprintTaskCard t)
        {
            var plan = new List<(string Label, Action Apply)>();
            void Add(string label, Action apply) => plan.Add((label, apply));

            if (OnlyScheduleCheck.IsChecked == true
                && !(EffTaskParent(t) is int sp8 && _scheduleIds.Contains(sp8)))
                Add(AppStrings.Get("Sprint_AdjustUncheck", AppStrings.Get("Sprint_OnlyScheduleStory")),
                    () => OnlyScheduleCheck.IsChecked = false);
            if (OnlyBlockedCheck.IsChecked == true && !EffBlocked(t.Id, t.Tags))
                Add(AppStrings.Get("Sprint_AdjustUncheck", AppStrings.Get("Sprint_OnlyBlocked")),
                    () => OnlyBlockedCheck.IsChecked = false);
            if (OnlyUnplannedCheck.IsChecked == true && !EffUnplanned(t.Id, t.Tags))
                Add(AppStrings.Get("Sprint_AdjustUncheck", AppStrings.Get("Sprint_OnlyUnplanned")),
                    () => OnlyUnplannedCheck.IsChecked = false);
            if (OnlyDoingCheck.IsChecked == true && !_doing.Contains(t.Id))
                Add(AppStrings.Get("Sprint_AdjustUncheck", AppStrings.Get("Sprint_OnlyDoing")),
                    () => OnlyDoingCheck.IsChecked = false);
            if (OnlyDoneActiveCheck.IsChecked == true && !_done.Contains(t.Id))
                Add(AppStrings.Get("Sprint_AdjustUncheck", AppStrings.Get("Sprint_OnlyDoneActive")),
                    () => OnlyDoneActiveCheck.IsChecked = false);
            if (OnlyTaskActiveCheck.IsChecked == true
                && TfsImportService.NormalizeTaskState(EffState(t)) != "Active")
                Add(AppStrings.Get("Sprint_AdjustUncheck", AppStrings.Get("Sprint_OnlyTaskActive")),
                    () => OnlyTaskActiveCheck.IsChecked = false);

            var owner = t.AssignedTo ?? "";
            if (_selectedPeople.Count > 0 && !_selectedPeople.Contains(owner))
                Add(AppStrings.Get("Sprint_AdjustAddPerson",
                        string.IsNullOrWhiteSpace(owner) ? AppStrings.Get("Sprint_NoOwner") : owner),
                    () => _selectedPeople.Add(owner));

            var parentId = EffTaskParent(t);
            if (_selectedStoryIds.Count > 0 && !_selectedStoryIds.Contains(parentId))
                Add(AppStrings.Get("Sprint_AdjustAddStory", parentId.ToString()),
                    () => _selectedStoryIds.Add(parentId));

            var state = EffState(t);
            if (_hiddenStates.Contains(state))
                Add(AppStrings.Get("Sprint_AdjustShowState", state), () =>
                {
                    _hiddenStates.Remove(state);
                    PopulateStateFilter();
                });

            if (_closedDays > 0 && IsClosedState(state) && t.ClosedDate is DateTime cdp
                && cdp.Date < DateTime.Today.AddDays(-_closedDays))
            {
                var days = (int)Math.Ceiling((DateTime.Today - cdp.Date).TotalDays) + 1;
                Add(AppStrings.Get("Sprint_AdjustClosedDays", _closedDays.ToString(), days.ToString()), () =>
                {
                    _closedDays = days;
                    ClosedDaysBox.Text = days.ToString();
                });
            }

            if (!string.IsNullOrEmpty(SearchQuery()))
                Add(AppStrings.Get("Sprint_AdjustClearSearch"), () => SearchBox.Text = "");

            return plan;
        }

        /// <summary>Quais recortes estao escondendo esta Task. Vazio = nenhum (ela ja aparecia).</summary>
        private string WhyHidden(TfsImportService.SprintTaskCard t)
        {
            var r = new List<string>();
            if (OnlyScheduleCheck.IsChecked == true
                && !(EffTaskParent(t) is int sp9 && _scheduleIds.Contains(sp9))) r.Add(AppStrings.Get("Sprint_WhyOnlySchedule"));
            if (OnlyBlockedCheck.IsChecked == true && !EffBlocked(t.Id, t.Tags)) r.Add(AppStrings.Get("Sprint_WhyOnlyBlocked"));
            if (OnlyUnplannedCheck.IsChecked == true && !EffUnplanned(t.Id, t.Tags)) r.Add(AppStrings.Get("Sprint_WhyOnlyUnplanned"));
            if (OnlyDoingCheck.IsChecked == true && !_doing.Contains(t.Id)) r.Add(AppStrings.Get("Sprint_WhyOnlyDoing"));
            if (OnlyDoneActiveCheck.IsChecked == true && !_done.Contains(t.Id)) r.Add(AppStrings.Get("Sprint_WhyOnlyDoneActive"));
            if (OnlyTaskActiveCheck.IsChecked == true
                && TfsImportService.NormalizeTaskState(EffState(t)) != "Active") r.Add(AppStrings.Get("Sprint_WhyOnlyActive"));
            if (_selectedPeople.Count > 0 && !_selectedPeople.Contains(t.AssignedTo ?? "")) r.Add(AppStrings.Get("Sprint_WhyPerson"));
            if (_selectedStoryIds.Count > 0 && !_selectedStoryIds.Contains(EffTaskParent(t))) r.Add(AppStrings.Get("Sprint_WhyProject"));
            if (_hiddenStates.Contains(EffState(t))) r.Add(AppStrings.Get("Sprint_WhyState", EffState(t)));
            if (_closedDays > 0 && IsClosedState(EffState(t)) && t.ClosedDate is DateTime cdw
                && cdw.Date < DateTime.Today.AddDays(-_closedDays)) r.Add(AppStrings.Get("Sprint_WhyClosedDays", _closedDays.ToString()));
            if (!string.IsNullOrEmpty(SearchQuery())) r.Add(AppStrings.Get("Sprint_WhySearch"));
            return string.Join(" · ", r);
        }

        private bool PassesFilters(TfsImportService.SprintTaskCard t)
        {
            if (t.Id < 0) return true; // card novo (local) sempre visível até salvar
            if (!PassesBaseFilters(t)) return false;
            if (_pending.ContainsKey(t.Id)) return true;
            var eff = EffState(t);
            // Story com "mostrar encerradas" ligado: as Tasks dela furam o recorte por ESTADO e o
            // corte de dias. Os demais filtros (pessoa, Story, busca, tags) continuam valendo.
            if (IsClosedState(eff) && IsRevealed(t)) return true;
            if (_hiddenStates.Contains(eff)) return false;
            if (_closedDays > 0 && IsClosedState(eff)
                && t.ClosedDate is DateTime cd && cd.Date < DateTime.Today.AddDays(-_closedDays))
                return false;
            return true;
        }

        /// <summary>
        /// Mesmo recorte por ESTADO do card de Task (estados ocultos e o corte de "Closed dos
        /// ultimos N dias"), aplicado ao estado da propria Story. Vale para as Stories sem Task
        /// da visao Pessoa x Task: sem isto uma Story encerrada ha meses reaparecia so porque as
        /// Tasks dela tinham sido escondidas pelo corte de dias.
        /// </summary>
        private bool StoryStatePasses(TfsImportService.SprintStoryRow st)
        {
            if (st.Id <= 0) return true;
            if (_storyStatePending.ContainsKey(st.Id)) return true; // mudanca pendente sempre aparece
            var eff = EffStoryState(st);
            if (_hiddenStates.Contains(eff)) return false;
            if (_closedDays > 0 && IsClosedState(eff)
                && st.ClosedDate is DateTime cd && cd.Date < DateTime.Today.AddDays(-_closedDays))
                return false;
            return true;
        }

        // Popup de filtro: raiz "Projeto Aberto" (Stories do cronograma aberto) — só se houver — e
        // "Todo Portfólio" (o restante). Em cada raiz: Features → Stories, com "sem Feature" à parte.
        /// <summary>
        /// Arvore do filtro na hierarquia do DevOps: Work Item Project → EPIC → Feature → Story.
        /// Cada nivel abre no "▸" e tem checkbox proprio, que marca/desmarca tudo abaixo.
        ///
        /// Os Projects listados sao SO os do Portfolio do NX (nao todos os do Team Project); o
        /// que estiver fora dele cai num no "Outros", para nenhuma Story do board ficar sem
        /// como ser filtrada. O item raiz do cronograma aberto vem marcado, expandido e com
        /// "(Aberto no NX)" no rotulo.
        /// </summary>
        private void PopulateStoryFilter()
        {
            StoryFilterList.Children.Clear();
            _filterGroups.Clear();
            if (_board == null) return;

            var stories = _board.Stories.Where(s => s.Id > 0 && !s.IsLevelPlaceholder).ToList();

            // Feature/EPIC que NAO tem Story nenhuma nao apareciam na arvore — e, sem estar aqui,
            // sumiam do board assim que qualquer filtro era aplicado, sem o usuario ter como
            // marca-los. Entram como uma folha propria (o Tag e o id do proprio work item), sob o
            // EPIC/Projeto a que pertencem.
            var coveredFeat = stories.Where(x => x.FeatureId > 0).Select(x => x.FeatureId).ToHashSet();
            var coveredEpic = stories.Where(x => x.FeatureEpicId > 0).Select(x => x.FeatureEpicId).ToHashSet();
            foreach (var it in _board.LevelItems)
            {
                if (it.Id <= 0) continue;
                if (it.Kind == "Feature" && coveredFeat.Contains(it.Id)) continue;
                if (it.Kind == "Epic" && coveredEpic.Contains(it.Id)) continue;
                if (it.Kind != "Feature" && it.Kind != "Epic") continue;   // Project e so no raiz

                var row = new TfsImportService.SprintStoryRow(it.Id, it.Title, it.State, it.AssignedTo, new())
                {
                    IterationPath = it.IterationPath, Tags = it.Tags,
                    FeatureEpicId = it.Kind == "Epic" ? it.Id : it.EpicId,
                    FeatureEpicTitle = it.Kind == "Epic" ? it.Title : it.EpicTitle,
                    FeatureProjectId = it.ProjectId, FeatureProjectTitle = it.ProjectTitle
                };
                if (it.Kind == "Feature") { row.FeatureId = it.Id; row.FeatureTitle = it.Title; }
                stories.Add(row);
            }

            if (stories.Count == 0) return;

            var portfolio = LoadPortfolioProjects();
            // Sem filtro salvo, marca so o cronograma aberto (quando ele aparece na arvore);
            // sem cronograma aberto, tudo marcado — o padrao de "sem recorte".
            var hasSaved = _selectedStoryIds.Count > 0;
            var openInTree = _openRootId > 0 && stories.Any(s =>
                s.FeatureProjectId == _openRootId || s.FeatureEpicId == _openRootId || s.FeatureId == _openRootId);

            bool StoryChecked(TfsImportService.SprintStoryRow s)
            {
                if (hasSaved) return _selectedStoryIds.Contains(s.Id);
                if (!openInTree) return true;
                return s.FeatureProjectId == _openRootId || s.FeatureEpicId == _openRootId
                    || s.FeatureId == _openRootId || s.Id == _openRootId;
            }

            // Raiz do Portfolio a que a Story pertence. A coluna do cadastro e "Root Work Item":
            // essa raiz NAO e necessariamente do tipo Project — pode ser um Epic ou uma Feature.
            // Por isso procuramos o id cadastrado em qualquer nivel da ancestralidade, do topo
            // para baixo; so o que nao casar com nenhuma raiz vai para o no "Outros".
            int PortfolioRootOf(TfsImportService.SprintStoryRow s)
            {
                if (portfolio.ContainsKey(s.FeatureProjectId)) return s.FeatureProjectId;
                if (portfolio.ContainsKey(s.FeatureEpicId)) return s.FeatureEpicId;
                if (portfolio.ContainsKey(s.FeatureId)) return s.FeatureId;
                return portfolio.ContainsKey(s.Id) ? s.Id : 0;
            }

            // A arvore lista TODO o Portfolio do NX, na ordem em que esta cadastrado — inclusive
            // o projeto que nao tem Story nesta sprint (entra vazio, com o checkbox desligado).
            // Assim o filtro espelha o cadastro e nao "some" projeto por causa da sprint aberta.
            // O no "Outros" (id 0) vai ao fim e so aparece quando ha Story fora do Portfolio.
            var byRoot = stories.GroupBy(PortfolioRootOf)
                                .ToDictionary(g => g.Key, g => g.ToList());
            var roots = portfolio.Keys.ToList();
            if (byRoot.ContainsKey(0)) roots.Add(0);

            foreach (var projId in roots)
            {
                var pg = byRoot.TryGetValue(projId, out var found)
                    ? found : new List<TfsImportService.SprintStoryRow>();
                // O nome vem do cadastro do Portfolio: e ele que o usuario reconhece, e a raiz
                // pode nem ser um work item do tipo Project (nesse caso nao ha titulo de Project).
                var projTitle = projId == 0
                    ? AppStrings.Get("Sprint_FilterOtherProjects")
                    : portfolio.GetValueOrDefault(projId, $"#{projId}");

                var epicElements = new List<UIElement>();
                var projLeaves = new List<CheckBox>();

                foreach (var eg in pg
                             .GroupBy(s => s.FeatureEpicId)
                             .OrderBy(g => g.Key == 0 ? 1 : 0)
                             .ThenBy(g => g.Min(FilterOrderOf)))
                {
                    var featElements = new List<UIElement>();
                    var epicLeaves = new List<CheckBox>();

                    foreach (var fg in eg
                                 .GroupBy(s => s.FeatureId)
                                 .OrderBy(g => g.Key == 0 ? 1 : 0)
                                 .ThenBy(g => g.Min(FilterOrderOf)))
                    {
                        var storyBoxes = fg
                            .OrderBy(FilterOrderOf).ThenBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase)
                            .Select(s => new CheckBox
                            {
                                Content = s.Title, Tag = s.Id, Margin = new Thickness(2),
                                IsChecked = StoryChecked(s)
                            }).ToList();
                        foreach (var b in storyBoxes) b.Click += (_, _) => RefreshFilterGroupStates();

                        var featTitle = fg.Key == 0
                            ? AppStrings.Get("Sprint_NoFeature")
                            : (fg.Select(s => s.FeatureTitle).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))
                               ?? AppStrings.Get("Sprint_NoFeature"));
                        featElements.Add(BuildFilterBranch("📦 " + LevelLabel(fg.Key, featTitle), fg.Key,
                            storyBoxes.Cast<UIElement>().ToList(), storyBoxes));
                        epicLeaves.AddRange(storyBoxes);
                    }

                    var epicTitle = eg.Key == 0
                        ? AppStrings.Get("Sprint_FilterNoEpic")
                        : (eg.Select(s => s.FeatureEpicTitle).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))
                           ?? AppStrings.Get("Sprint_FilterNoEpic"));
                    epicElements.Add(BuildFilterBranch("🏔 " + LevelLabel(eg.Key, epicTitle), eg.Key,
                        featElements, epicLeaves));
                    projLeaves.AddRange(epicLeaves);
                }

                StoryFilterList.Children.Add(BuildFilterBranch("🗂 " + LevelLabel(projId, projTitle), projId,
                    epicElements, projLeaves));
            }

            RefreshFilterGroupStates();
        }

        /// <summary>Rotulo do no: o do cronograma aberto ganha "(Aberto no NX)" no fim.</summary>
        private string LevelLabel(int id, string title) =>
            id > 0 && id == _openRootId ? $"{title} {AppStrings.Get("Sprint_FilterOpenInNx")}" : title;

        /// <summary>Projects do Portfolio do NX (id do work item raiz → nome). O filtro so
        /// mostra esses; o resto vai para o no "Outros".</summary>
        private Dictionary<int, string> LoadPortfolioProjects()
        {
            var map = new Dictionary<int, string>();
            try
            {
                foreach (var p in DevOpsProjectListService.Load(_options.DevOpsProjectListPath))
                    if (p.RootWorkItemId > 0) map[p.RootWorkItemId] = p.Name;
            }
            catch { /* sem portfolio configurado: tudo cai em "Outros" */ }
            return map;
        }

        /// <summary>Um no da arvore: Expander (o "▸") + checkbox que marca/desmarca tudo abaixo.</summary>
        private FrameworkElement BuildFilterBranch(string text, int id, List<UIElement> children,
            List<CheckBox> leaves)
        {
            // IsThreeState fica FALSE: o clique so alterna marcado/desmarcado. O estado parcial
            // (null) e aplicado por codigo em RefreshFilterGroupStates, para o pai mostrar que
            // parte dos filhos esta marcada.
            var box = new CheckBox
            {
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0),
                // No sem Story (projeto do Portfolio fora desta sprint): nada para marcar.
                IsEnabled = leaves.Count > 0
            };
            box.Click += (_, _) =>
            {
                var target = box.IsChecked == true;
                foreach (var leaf in leaves) leaf.IsChecked = target;
                RefreshFilterGroupStates();
            };

            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(box);
            header.Children.Add(new TextBlock
            {
                Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
                // SO o no do cronograma aberto fica em negrito azul. Antes todo Project vinha em
                // negrito (bold), e isso se confundia com o destaque do "(Aberto no NX)".
                FontWeight = id > 0 && id == _openRootId ? FontWeights.Bold : FontWeights.Normal,
                Foreground = id > 0 && id == _openRootId
                    ? new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A)) : Brushes.Black
            });

            var panel = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
            foreach (var c in children) panel.Children.Add(c);

            _filterGroups.Add((box, leaves));
            return new Expander
            {
                Style = (Style)FindResource("FilterBranch"),
                Header = header, Content = panel, Margin = new Thickness(0, 1, 0, 1),
                // O no do cronograma aberto ja nasce aberto; os demais, fechados.
                IsExpanded = id > 0 && id == _openRootId
            };
        }

        /// <summary>Recalcula marcado / desmarcado / parcial de cada no, das folhas para a raiz.</summary>
        private void RefreshFilterGroupStates()
        {
            foreach (var (parent, leaves) in _filterGroups)
            {
                if (leaves.Count == 0) { parent.IsChecked = false; continue; }
                var checkedCount = leaves.Count(l => l.IsChecked == true);
                parent.IsChecked = checkedCount == 0 ? false
                    : checkedCount == leaves.Count ? true : (bool?)null;
            }
        }

        /// <summary>Chave de ordenação do filtro: posição no cronograma aberto quando o item está
        /// lá; senão o rank do backlog do TFS. Assim a lista segue a ordem que o usuário conhece.</summary>
        private double FilterOrderOf(TfsImportService.SprintStoryRow s) =>
            _scheduleRank.TryGetValue(s.Id, out var pos) ? pos : double.MaxValue / 2 + s.StackRank;

        private void AddFilterRoot(string label, List<TfsImportService.SprintStoryRow> stories)
        {
            var rootPanel = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
            var root = new CheckBox { Content = label, FontWeight = FontWeights.Bold, Margin = new Thickness(2) };
            // Marca/desmarca TUDO abaixo (Features + Stories), não só as Stories.
            root.Click += (_, _) => { foreach (var cb in AllCheckBoxesIn(rootPanel)) cb.IsChecked = root.IsChecked; };
            StoryFilterList.Children.Add(root);
            StoryFilterList.Children.Add(rootPanel);

            // Ordem: a MESMA do cronograma aberto quando o item está lá; senão o rank do backlog
            // do TFS (StackRank). Antes era alfabético, que não casava com nenhuma das duas.
            // Features (sem-Feature por último), pela posição da 1ª Story de cada uma.
            foreach (var fg in stories
                         .GroupBy(s => string.IsNullOrWhiteSpace(s.FeatureTitle) ? "" : s.FeatureTitle)
                         .OrderBy(g => g.Key == "" ? 1 : 0)
                         .ThenBy(g => g.Min(FilterOrderOf))
                         .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                var featTitle = fg.Key == "" ? AppStrings.Get("Sprint_NoFeature") : fg.Key;
                var container = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
                var childPanel = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
                foreach (var s in fg.OrderBy(FilterOrderOf).ThenBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase))
                    childPanel.Children.Add(new CheckBox
                    {
                        Content = s.Title, Tag = s.Id, Margin = new Thickness(2),
                        IsChecked = _selectedStoryIds.Count == 0 || _selectedStoryIds.Contains(s.Id)
                    });
                var feat = new CheckBox { Content = "📦 " + featTitle, FontWeight = FontWeights.Bold,
                    IsChecked = childPanel.Children.OfType<CheckBox>().All(cb => cb.IsChecked == true) };
                feat.Click += (_, _) => { foreach (var cb in childPanel.Children.OfType<CheckBox>()) cb.IsChecked = feat.IsChecked; };
                container.Children.Add(feat);
                container.Children.Add(childPanel);
                rootPanel.Children.Add(container);
            }
            root.IsChecked = StoryCheckBoxesIn(rootPanel).All(cb => cb.IsChecked == true);
        }

        // Todas as checkboxes de Story (Tag = id) abaixo de um elemento. Percorre a ARVORE
        // LOGICA porque a arvore do filtro usa Expander (Header/Content), que nao e Panel.
        private static List<CheckBox> StoryCheckBoxesIn(DependencyObject root)
        {
            var list = new List<CheckBox>();
            void Walk(object? node)
            {
                if (node is CheckBox cb) { if (cb.Tag is int) list.Add(cb); return; }
                if (node is DependencyObject dobj)
                    foreach (var child in LogicalTreeHelper.GetChildren(dobj))
                        Walk(child);
            }
            Walk(root);
            return list;
        }

        private IEnumerable<CheckBox> AllStoryCheckBoxes() => StoryCheckBoxesIn(StoryFilterList);

        // Todas as checkboxes (nos + Stories) abaixo de um elemento, recursivo.
        private static List<CheckBox> AllCheckBoxesIn(Panel root)
        {
            var list = new List<CheckBox>();
            void Walk(Panel p)
            {
                foreach (var child in p.Children)
                {
                    if (child is CheckBox cb) list.Add(cb);
                    if (child is Panel sub) Walk(sub);
                }
            }
            Walk(root);
            return list;
        }

        private void OnStoryFilterAll(object sender, RoutedEventArgs e)
        {
            _selectedStoryIds.Clear();
            foreach (var cb in AllStoryCheckBoxes()) cb.IsChecked = true;
            // NAO reconstruir a arvore aqui: o rebuild reaplica a regra de "sem filtro salvo", que
            // marca apenas o cronograma aberto — e o "Todas" acabava desmarcando o resto. Basta
            // recalcular o estado dos nos (marcado / parcial) a partir das folhas ja marcadas.
            RefreshFilterGroupStates();
            RenderBusy();
        }

        /// <summary>
        /// Desmarca tudo na arvore SEM aplicar: e o ponto de partida para escolher poucos itens
        /// (o caminho contrario do "Todas"). Nada muda no board ate clicar em Aplicar — la, se
        /// continuar tudo desmarcado, o filtro sai de cena e o board volta a mostrar tudo.
        /// </summary>
        private void OnStoryFilterNone(object sender, RoutedEventArgs e)
        {
            foreach (var cb in AllStoryCheckBoxes()) cb.IsChecked = false;
            RefreshFilterGroupStates();
        }

        private void OnStoryFilterApply(object sender, RoutedEventArgs e)
        {
            var all = AllStoryCheckBoxes().ToList();
            var checkedIds = all.Where(cb => cb.IsChecked == true).Select(cb => (int)cb.Tag!).ToList();
            _selectedStoryIds.Clear();
            // Todas ou nenhuma marcada → sem filtro (mostra todas).
            if (checkedIds.Count > 0 && checkedIds.Count < all.Count)
                foreach (var id in checkedIds) _selectedStoryIds.Add(id);
            StoryFilterToggle.IsChecked = false;
            RenderBusy();
        }

        private void Render()
        {
            SummaryHost.Items.Clear();
            BoardHost.Children.Clear();
            _personCellByKey.Clear();
            _newCardBorderById.Clear();
            RebuildStoryRowCache();
            RebuildLastSprintByPerson();
            HeaderHost.Children.Clear();
            if (_board == null) return;

            // Tasks visíveis após filtros (inclui os cards novos locais).
            var eff = EffectiveStories();
            _cardById.Clear();
            foreach (var c in eff.SelectMany(x => x.Tasks)) _cardById[c.Id] = c;
            DumpFilterLog();   // diagnostico temporario: precisa do _cardById ja preenchido
            // WIP é calculado sobre TUDO que está carregado (não sobre o que passou nos filtros):
            // esconder cards não pode fazer o limite de uma pessoa parecer menor do que é.
            RecomputeWip(eff.SelectMany(x => x.Tasks));
            var visibleByStory = eff
                .Select(x => (Story: x.Story, Tasks: x.Tasks.Where(PassesFilters).ToList()))
                .ToList();
            var allVisible = visibleByStory.SelectMany(x => x.Tasks).ToList();
            var states = _board.States; // as colunas de estado aparecem SEMPRE (alvo de arrasto)
            // Resumo conta todos os cards (só recorte pessoa/story/cronograma), inclusive Closed.
            var summarySet = eff.SelectMany(x => x.Tasks).Where(t => t.Id < 0 || PassesBaseFilters(t)).ToList();

            // ── Resumo por estado (contagem + barra) — usa o estado efetivo (arrasto/gravado) ──
            var maxCount = states.Select(st => summarySet.Count(t => SameState(EffState(t), st))).DefaultIfEmpty(0).Max();
            foreach (var st in states)
            {
                var c = summarySet.Count(t => SameState(EffState(t), st));
                var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                var lbl = new TextBlock { Text = st, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
                Grid.SetColumn(lbl, 0);
                var bar = new Border
                {
                    Height = 12, HorizontalAlignment = HorizontalAlignment.Left,
                    Background = StateBrush(st), CornerRadius = new CornerRadius(2),
                    Width = maxCount > 0 ? Math.Max(2, 300.0 * c / maxCount) : 2
                };
                Grid.SetColumn(bar, 1);
                var num = new TextBlock { Text = c.ToString(), FontSize = 11, FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(num, 2);
                row.Children.Add(lbl); row.Children.Add(bar); row.Children.Add(num);
                SummaryHost.Items.Add(row);
            }

            if (ViewIndex == 1)
            {
                // ── TaskBoard: Pessoa | Story | estados (agrupa por pessoa e story) ──
                RenderPersonBoard(allVisible, states);
            }
            else
            {
                // ── Por Story: Feature | Story | estados (agrupa as Stories por Feature/entrega) ──
                RenderStoryBoard(visibleByStory, states);
            }

            StatusText.Text = AppStrings.Get("Sprint_TaskCount",
                allVisible.Count.ToString(), _scheduleIds.Count == 0 ? "0"
                    : allVisible.Count(t => _scheduleIds.Contains(t.Id)).ToString());
            FilterSummary.Text = BuildFilterSummary();
        }

        // Linha-resumo dos filtros aplicados (mostrada ao abrir a tela e a cada Render).
        private string BuildFilterSummary()
        {
            var parts = new List<string>();
            if (_selectedPeople.Count == 1)
                parts.Add(AppStrings.Get("Sprint_FSPerson", _selectedPeople.First()));
            else if (_selectedPeople.Count > 1)
                parts.Add(AppStrings.Get("Sprint_FSPerson", AppStrings.Get("Sprint_PersonN", _selectedPeople.Count.ToString())));
            if (_selectedStoryIds.Count > 0)
                parts.Add(AppStrings.Get("Sprint_FSStories", _selectedStoryIds.Count.ToString()));
            if (_hiddenStates.Count > 0)
                parts.Add(AppStrings.Get("Sprint_FSHidden", _hiddenStates.Count.ToString()));
            parts.Add(_closedDays > 0
                ? AppStrings.Get("Sprint_FSClosedDays", _closedDays.ToString())
                : AppStrings.Get("Sprint_FSClosedAll"));
            if (OnlyScheduleCheck.IsChecked == true)
                parts.Add(AppStrings.Get("Sprint_FSOnlyScheduleStory"));
            if (OnlyBlockedCheck.IsChecked == true)
                parts.Add(AppStrings.Get("Sprint_FSOnlyBlocked"));
            if (OnlyUnplannedCheck.IsChecked == true)
                parts.Add(AppStrings.Get("Sprint_FSOnlyUnplanned"));
            if (OnlyDoneActiveCheck.IsChecked == true)
                parts.Add(AppStrings.Get("Sprint_FSOnlyDoneActive"));
            if (OnlyDoingCheck.IsChecked == true)
                parts.Add(AppStrings.Get("Sprint_FSOnlyDoing"));
            if (OnlyTaskActiveCheck.IsChecked == true)
                parts.Add(AppStrings.Get("Sprint_FSOnlyTaskActive"));
            if (!string.IsNullOrWhiteSpace(SearchBox.Text))
                parts.Add(AppStrings.Get("Sprint_FSSearch", SearchBox.Text.Trim()));
            return AppStrings.Get("Sprint_FSPrefix", string.Join(" · ", parts));
        }

        // Sub-visão "Por Task": lista plana das tasks (útil ao filtrar por pessoa), cada uma
        // com a conexão ↑ para a Story pai. Ordena por Story e depois por estado.
        private void RenderByTask(List<TfsImportService.SprintTaskCard> tasks)
        {
            var storyById = _board!.Stories.Where(s => s.Id > 0).ToDictionary(s => s.Id);
            string StoryTitle(int? pid) => pid is int p && storyById.TryGetValue(p, out var st)
                ? st.Title : AppStrings.Get("Sprint_NoStory");

            foreach (var t in tasks
                         .OrderBy(t => StoryTitle(t.ParentId), StringComparer.CurrentCultureIgnoreCase)
                         .ThenBy(t => t.State, StringComparer.CurrentCultureIgnoreCase))
            {
                var inSched = _scheduleIds.Contains(t.Id);
                var border = new Border
                {
                    Background = Brushes.White,
                    BorderBrush = new SolidColorBrush(inSched ? Color.FromRgb(0x2B, 0x57, 0x9A) : Color.FromRgb(0xD0, 0xD7, 0xE0)),
                    BorderThickness = new Thickness(inSched ? 2 : 1),
                    CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(8, 5, 8, 5)
                };
                var sp = new StackPanel();

                var head = new StackPanel { Orientation = Orientation.Horizontal };
                head.Children.Add(new TextBlock { Text = "📋 ", VerticalAlignment = VerticalAlignment.Center });
                head.Children.Add(new TextBlock { Text = EffTitle(t.Id, t.Title), FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
                    Foreground = _titlePending.ContainsKey(t.Id) ? new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)) : Brushes.Black });
                head.Children.Add(new Border
                {
                    Background = StateBrush(t.State), CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(6, 1, 6, 1),
                    Child = new TextBlock { Text = t.State, Foreground = Brushes.White, FontSize = 10 }
                });
                sp.Children.Add(head);

                var meta = "#" + t.Id
                    + (string.IsNullOrWhiteSpace(t.AssignedTo) ? "" : "  ·  " + t.AssignedTo)
                    + (string.IsNullOrWhiteSpace(t.Effort) ? "" : "  ·  " + t.Effort + "h");
                sp.Children.Add(new TextBlock { Text = meta, Foreground = Brushes.Gray, FontSize = 11 });

                // Conexão ↑ para a Story pai (clicável: abre a Story no DevOps).
                var up = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
                up.Children.Add(new TextBlock { Text = "↑ " + AppStrings.Get("Sprint_UpStory") + " ",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A)), VerticalAlignment = VerticalAlignment.Center });
                if (t.ParentId is int pid)
                {
                    var link = new Button
                    {
                        Content = StoryTitle(t.ParentId),
                        Foreground = new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A)),
                        Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                        Cursor = System.Windows.Input.Cursors.Hand, Padding = new Thickness(0),
                        HorizontalContentAlignment = HorizontalAlignment.Left
                    };
                    link.Click += (_, _) => OpenInDevOps(pid);
                    up.Children.Add(link);
                    if (_scheduleIds.Contains(pid) && _openInSchedule != null)
                    {
                        var s = new Button { Content = "📅", FontSize = 11, Padding = new Thickness(4, 0, 4, 0),
                            Margin = new Thickness(6, 0, 0, 0), ToolTip = AppStrings.Get("Query_OpenInSchedule") };
                        s.Click += (_, _) => _openInSchedule!(pid);
                        up.Children.Add(s);
                    }
                }
                else
                {
                    up.Children.Add(new TextBlock { Text = AppStrings.Get("Sprint_NoStory"), Foreground = Brushes.Gray });
                }
                sp.Children.Add(up);

                var actions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0), Cursor = System.Windows.Input.Cursors.Arrow };
                var open = new Button { Content = "🔗", FontSize = 11, Padding = new Thickness(4, 0, 4, 0), ToolTip = AppStrings.Get("Sprint_OpenDevOps") };
                open.Click += (_, _) => OpenInDevOps(t.Id);
                actions.Children.Add(open);
                if (inSched && _openInSchedule != null)
                {
                    var sc = new Button { Content = "📅", FontSize = 11, Padding = new Thickness(4, 0, 4, 0),
                        Margin = new Thickness(4, 0, 0, 0), ToolTip = AppStrings.Get("Query_OpenInSchedule") };
                    sc.Click += (_, _) => _openInSchedule!(t.Id);
                    actions.Children.Add(sc);
                }
                sp.Children.Add(actions);

                border.Child = sp;
                BoardHost.Children.Add(border);
            }
        }

        // Uma linha do board: coluna 0 = Story; demais = estados com cards.
        private FrameworkElement BuildBoardRow(TfsImportService.SprintStoryRow? story,
            List<TfsImportService.SprintTaskCard>? tasks, List<string> states, bool header,
            string firstColHeaderKey = "Sprint_ColStory", bool showStory = false)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, header ? 2 : 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            foreach (var _ in states)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });

            // Coluna 0
            if (header)
            {
                var h0 = new TextBlock { Text = AppStrings.Get(firstColHeaderKey), FontWeight = FontWeights.Bold, FontSize = 12,
                    Margin = new Thickness(4, 2, 4, 2) };
                Grid.SetColumn(h0, 0); grid.Children.Add(h0);
                for (int i = 0; i < states.Count; i++)
                {
                    var hs = new Border { Background = StateBrush(states[i]), CornerRadius = new CornerRadius(3),
                        Margin = new Thickness(3, 0, 3, 0), Padding = new Thickness(6, 2, 6, 2) };
                    hs.Child = new TextBlock { Text = states[i], Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 11 };
                    Grid.SetColumn(hs, i + 1); grid.Children.Add(hs);
                }
                return grid;
            }

            var storyBorder = new Border { Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xF7)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD7, 0xE0)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3), Margin = new Thickness(3), Padding = new Thickness(6) };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = EffTitle(story!.Id, story.Title), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
                Foreground = _titlePending.ContainsKey(story.Id) ? new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)) : Brushes.Black });
            if (story.Id > 0)
            {
                sp.Children.Add(new TextBlock { Text = $"#{story.Id}  ·  {story.State}", FontSize = 10, Foreground = Brushes.Gray });
                var sowner = EffOwner(story.Id, story.AssignedTo);
                sp.Children.Add(new TextBlock {
                    Text = "👤 " + (string.IsNullOrWhiteSpace(sowner) ? AppStrings.Get("Sprint_NoOwner") : sowner),
                    FontSize = 10, TextWrapping = TextWrapping.Wrap,
                    Foreground = _ownerPending.ContainsKey(story.Id) ? new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)) : Brushes.DimGray });
                var stActions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0), Cursor = System.Windows.Input.Cursors.Arrow };
                AddEditButtons(stActions, story.Id, story.Title, story.AssignedTo, "Story", story.IterationPath); // ✎/💬 da Story
                sp.Children.Add(stActions);
            }
            storyBorder.Child = sp;
            Grid.SetColumn(storyBorder, 0); grid.Children.Add(storyBorder);

            for (int i = 0; i < states.Count; i++)
            {
                var el = BuildStateCell(states[i], tasks!, showStory);
                Grid.SetColumn(el, i + 1); grid.Children.Add(el);
            }
            return grid;
        }

        // Célula de um estado: pilha de cards (ordenada); no modo edição vira alvo de soltura.
        private FrameworkElement BuildStateCell(string state, IEnumerable<TfsImportService.SprintTaskCard> tasks, bool showStory)
        {
            var cell = new StackPanel { Margin = new Thickness(2) };
            foreach (var t in tasks.Where(t => SameState(EffState(t), state))
                         // Card novo primeiro (-1): ele ainda nao tem prioridade e cairia no 99,
                         // ou seja, no fim da coluna — justamente onde nao se ve.
                         .OrderBy(t => t.Id < 0 ? -1 : EffPrio(t) > 0 ? EffPrio(t) : 99)
                         .ThenBy(EffTaskRank))                              // depois o rank (StackRank) dentro do grupo
                cell.Children.Add(BuildCard(t, showStory
                    ? (_storyById.TryGetValue(EffTaskParent(t), out var st) ? st.Title : null)
                    : null));
            if (EditModeCheck.IsChecked != true) return cell;
            var host = new Border
            {
                MinHeight = 44, Background = new SolidColorBrush(Color.FromRgb(0xF6, 0xF8, 0xFB)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE1, 0xE7, 0xEF)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                Margin = new Thickness(1), Padding = new Thickness(2), AllowDrop = true, Tag = state, Child = cell
            };
            host.Drop += OnCardDrop;
            host.DragOver += (s, ev) => { ev.Effects = DragDropEffects.Move; ev.Handled = true; };
            return host;
        }

        // TaskBoard por Pessoa com coluna de Story: Pessoa | Story | estados. Agrupa os cards
        // por (pessoa → story) para visualizar melhor.
        // Mostra o Work Item "Project" no card da Feature só quando há mais de um projeto no board.
        private bool MultiProjectBoard() =>
            (_board?.Stories ?? Enumerable.Empty<TfsImportService.SprintStoryRow>())
                .Select(s => s.FeatureProjectTitle).Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.CurrentCultureIgnoreCase).Count() > 1;

        // Botão padrão "abrir no DevOps" usado nos cards de rótulo (EPIC/Projeto).
        // Altura fixa dos botoes da fileira de acoes do card de Projeto/EPIC (🔗 ✎ ➕):
        // sem isso cada um assume a altura do proprio conteudo e a linha fica desalinhada.
        private const double LevelButtonHeight = 20;

        // Botoes dos cards de Projeto/EPIC/Feature com fundo claro: o Button padrao do WPF
        // vem cinza e pesa sobre esses cards, que sao so um titulo. Story/Task ficam no padrao.
        private static readonly Brush LevelButtonBg = new SolidColorBrush(Color.FromRgb(0xEE, 0xF0, 0xF3));
        private static readonly Brush LevelButtonBorder = new SolidColorBrush(Color.FromRgb(0xC8, 0xCF, 0xD8));
        private static Button Light(Button b)
        {
            b.Background = LevelButtonBg;
            b.BorderBrush = LevelButtonBorder;
            b.BorderThickness = new Thickness(1);
            return b;
        }

        private Button OpenDevOpsButton(int id)
        {
            var b = new Button { Content = "🔗", FontSize = 11, Padding = new Thickness(4, 0, 4, 0),
                Height = LevelButtonHeight, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 3, 0), HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = System.Windows.Input.Cursors.Arrow, ToolTip = AppStrings.Get("Sprint_OpenDevOps") };
            b.Click += (_, _) => OpenInDevOps(id);
            return Light(b);
        }

        // Conteúdo do card do EPIC: título + (opcional) botão de abrir no DevOps.
        private UIElement BuildLabelBody(string icon, string text, Color c, int id, UIElement? extra = null, string state = "")
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = icon + " " + text, TextWrapping = TextWrapping.Wrap, FontSize = 11,
                FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(c)
            });
            if (LevelStateLine(id, state, 10) is { } stLine) sp.Children.Add(stLine);
            if (id > 0 || extra != null)
            {
                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                if (id > 0) actions.Children.Add(OpenDevOpsButton(id));
                if (extra != null) actions.Children.Add(extra);
                sp.Children.Add(actions);
            }
            return sp;
        }

        // Card só de rótulo (colunas Work Item "Project" e EPIC da visão "Por Story").
        // `strong` deixa o texto maior/negrito (EPIC); senão fica discreto (Project).
        private UIElement BuildLabelCard(string icon, string text, bool strong, int id = 0, UIElement? extra = null, string state = "")
        {
            if (string.IsNullOrWhiteSpace(text)) return new TextBlock();
            var c = (StateBrush(FeatureColorKey) as SolidColorBrush)?.Color ?? FactoryStateColor(FeatureColorKey);
            byte Mix(byte v, double f) => (byte)(v + (255 - v) * f);
            // Project fica sem borda (só o texto); EPIC mantém o card com borda.
            if (!strong)
            {
                var plain = new StackPanel { Margin = new Thickness(4, 6, 4, 2), VerticalAlignment = VerticalAlignment.Top };
                plain.Children.Add(new TextBlock
                {
                    Text = icon + " " + text, TextWrapping = TextWrapping.Wrap, FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(Mix(c.R, .45), Mix(c.G, .45), Mix(c.B, .45)))
                });
                if (LevelStateLine(id, state, 9) is { } stLine) plain.Children.Add(stLine);
                if (id > 0 || extra != null)
                {
                    var actions = new StackPanel { Orientation = Orientation.Horizontal };
                    if (id > 0) actions.Children.Add(OpenDevOpsButton(id));
                    if (extra != null) actions.Children.Add(extra);
                    plain.Children.Add(actions);
                }
                return plain;
            }
            return new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(Mix(c.R, .70), Mix(c.G, .70), Mix(c.B, .70))),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                Margin = new Thickness(3), Padding = new Thickness(6),
                VerticalAlignment = VerticalAlignment.Top,
                Child = BuildLabelBody(icon, text, c, id, extra, state)
            };
        }

        // Card da Feature — mesmo visual nas visões "Por Story" e "Pessoa & Task".
        // `extra` recebe um botão adicional da visão (ex.: "+Story" na visão Por Story).
        /// <summary>
        /// Linha "🗓 sprint" EM VERMELHO para Story/Feature que nao esta na sprint carregada: ela so
        /// veio para o board porque uma Task dela esta aqui. O vermelho e o aviso de que a hierarquia
        /// precisa de ajuste (a Task e o pai estao em sprints diferentes).
        /// </summary>
        private static TextBlock? OutOfSprintLine(string iterationPath)
        {
            var leaf = IterLeaf(iterationPath);
            if (string.IsNullOrWhiteSpace(leaf)) return null;
            return new TextBlock
            {
                Text = "🗓 " + leaf + " ⚠", FontSize = 10, FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x30, 0x30)),
                ToolTip = AppStrings.Get("Sprint_OutOfSprintTip", leaf)
            };
        }

        private Border BuildFeatureCard(int featId, string featTitle, string featOwner,
            string featEpic, string featProj, bool multiProject, UIElement? extra = null, string featState = "",
            string outOfSprintIter = "")
        {
            // Cor configurável na paleta (chave "feature").
            var featColor = (StateBrush(FeatureColorKey) as SolidColorBrush)?.Color ?? FactoryStateColor(FeatureColorKey);
            var featBrush = new SolidColorBrush(featColor);
            byte MixF(byte v) => (byte)(v + (255 - v) * 0.55); // tom claro da cor p/ projeto
            var featLight = new SolidColorBrush(Color.FromRgb(MixF(featColor.R), MixF(featColor.G), MixF(featColor.B)));
            var featSp = new StackPanel();
            // Projeto (Work Item "Project") só quando há mais de um projeto no board.
            if (multiProject && !string.IsNullOrWhiteSpace(featProj))
                featSp.Children.Add(new TextBlock { Text = "🗂 " + featProj, FontSize = 9,
                    Foreground = featLight, TextWrapping = TextWrapping.Wrap });
            // EPIC pai — destacado (negrito/cor forte) para diferenciar do Project.
            if (!string.IsNullOrWhiteSpace(featEpic))
                featSp.Children.Add(new TextBlock { Text = "🏔 " + featEpic, FontSize = 10,
                    FontWeight = FontWeights.SemiBold, Foreground = featBrush, TextWrapping = TextWrapping.Wrap });
            featSp.Children.Add(new TextBlock { Text = "📦 " + featTitle, FontSize = 11, FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap, Foreground = featBrush });
            if (LevelStateLine(featId, featState, 10) is { } featStLine) featSp.Children.Add(featStLine);
            if (OutOfSprintLine(outOfSprintIter) is { } featOut) featSp.Children.Add(featOut);
            if (!string.IsNullOrWhiteSpace(featOwner))
                featSp.Children.Add(new TextBlock { Text = "👤 " + featOwner, FontSize = 10,
                    Margin = new Thickness(0, 2, 0, 0), Foreground = featBrush, TextWrapping = TextWrapping.Wrap });
            if (featId > 0)
            {
                var fid = featId; var ftit = featTitle;
                var featBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0),
                    Cursor = System.Windows.Input.Cursors.Arrow };
                var featDesc = new Button { Content = "✎", FontSize = 11, Padding = new Thickness(4, 0, 4, 0),
                    Margin = new Thickness(0, 0, 4, 0), ToolTip = AppStrings.Get("Sprint_EditFeature") };
                featDesc.Click += async (_, _) => await EditDescriptionAsync(fid, ftit, "", "Feature");
                featBtns.Children.Add(Light(featDesc));
                var featOpen = new Button { Content = "🔗", FontSize = 11, Padding = new Thickness(4, 0, 4, 0),
                    ToolTip = AppStrings.Get("Sprint_OpenDevOps") };
                featOpen.Click += (_, _) => OpenInDevOps(fid);
                featBtns.Children.Add(Light(featOpen));
                if (extra != null) featBtns.Children.Add(extra);
                featSp.Children.Add(featBtns);
            }
            byte Bg(byte v) => (byte)(v + (255 - v) * 0.92); // fundo bem claro da cor
            byte Bd(byte v) => (byte)(v + (255 - v) * 0.70); // borda clara da cor
            return new Border { Background = new SolidColorBrush(Color.FromRgb(Bg(featColor.R), Bg(featColor.G), Bg(featColor.B))),
                BorderBrush = new SolidColorBrush(Color.FromRgb(Bd(featColor.R), Bd(featColor.G), Bd(featColor.B))),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                Margin = new Thickness(3), Padding = new Thickness(6), Child = featSp };
        }

        /// <summary>Checkbox que liga/desliga uma coluna opcional do board. Fica no cabeçalho
        /// da coluna seguinte e a escolha é salva nas preferências.</summary>
        private CheckBox MakeColToggle(string label, bool current, string hint, Action<bool> apply)
        {
            var chk = new CheckBox
            {
                Content = label, IsChecked = current, VerticalAlignment = VerticalAlignment.Center,
                FontSize = 10, Margin = new Thickness(6, 2, 0, 2), ToolTip = hint
            };
            chk.Click += (_, _) => { apply(chk.IsChecked == true); SavePrefs(); Render(); };
            return chk;
        }

        private void RenderPersonBoard(List<TfsImportService.SprintTaskCard> allVisible, List<string> states)
        {
            // Colunas EPIC e Feature são opcionais: cada checkbox fica no cabeçalho da coluna
            // SEGUINTE (EPIC no da Feature, Feature no da Story). Desligada, a informação volta
            // para dentro do card seguinte, em vez de sumir.
            var showEpic = _prefs.ShowEpic ?? true;
            var showFeat = _prefs.ShowFeatCol ?? true;
            // Coluna Projeto: padrao OCULTA (quem tem um projeto so nao precisa dela; o Projeto
            // ja aparece dentro do card quando ha varios). O checkbox fica no cabecalho do EPIC,
            // como na visao Projeto & Story.
            var showProj = _prefs.ShowProjPerson ?? false;
            // Colunas: # | Pessoa | [Projeto] | [EPIC] | [Feature] | Story | estados.
            var cols = new List<GridLength> { new(RowNumberWidth), new(150) };
            if (showProj) cols.Add(new(150));
            if (showEpic) cols.Add(new(160));
            if (showFeat) cols.Add(new(160));
            cols.Add(new(210));
            var cProj = 2;                                   // só usado quando showProj
            var cEpic = showProj ? 3 : 2;                    // só usado quando showEpic
            var cFeat = (showProj ? 1 : 0) + (showEpic ? 1 : 0) + 2;   // só usado quando showFeat
            var cStory = (showProj ? 1 : 0) + (showEpic ? 1 : 0) + (showFeat ? 1 : 0) + 2;
            var cState0 = cStory + 1;              // 1ª coluna de estado
            foreach (var _ in states) cols.Add(new GridLength(210));

            var multiProject = MultiProjectBoard();

            // Faixa: nesta visão os cards das colunas de estado são TASKS.
            AddCardsBandRow(cols, cState0, AppStrings.Get("Sprint_CardsAreTasks"));
            var head = MakeRowGrid(cols);
            AddCell(head, 0, MakeHeader("#"));
            var personHead = new StackPanel { Orientation = Orientation.Horizontal };
            personHead.Children.Add(MakeHeader(AppStrings.Get("Sprint_ColPerson")));
            personHead.Children.Add(new TextBlock
            {
                Text = "ⓘ", FontSize = 11, Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A)),
                Cursor = System.Windows.Input.Cursors.Help,
                ToolTip = AppStrings.Get("Sprint_PersonOrderHelp")
            });
            AddCell(head, 1, personHead);
            if (showProj) AddCell(head, cProj, MakeHeader(AppStrings.Get("Sprint_ColProject")));
            // Checkbox da coluna Projeto: fica no cabeçalho da coluna SEGUINTE que estiver
            // visível (EPIC → Feature → Story), o mesmo padrão dos outros toggles.
            CheckBox ProjToggle() => MakeColToggle(AppStrings.Get("Sprint_ColProject"), showProj,
                AppStrings.Get("Sprint_ShowProjHint"), v => _prefs.ShowProjPerson = v);

            if (showEpic)
            {
                var epicHead = new StackPanel { Orientation = Orientation.Horizontal };
                epicHead.Children.Add(MakeHeader(AppStrings.Get("Sprint_ColEpic")));
                epicHead.Children.Add(ProjToggle());
                AddCell(head, cEpic, epicHead);
            }
            // Cabeçalho da Feature + checkbox que liga/desliga a coluna do EPIC.
            if (showFeat)
            {
                var featHead = new StackPanel { Orientation = Orientation.Horizontal };
                featHead.Children.Add(MakeHeader(AppStrings.Get("Sprint_ColFeature")));
                featHead.Children.Add(MakeColToggle(AppStrings.Get("Sprint_ColEpic"), showEpic,
                    AppStrings.Get("Sprint_ShowEpicHint"), v => _prefs.ShowEpic = v));
                if (!showEpic) featHead.Children.Add(ProjToggle());
                AddCell(head, cFeat, featHead);
            }
            // Cabeçalho da Story + checkbox que liga/desliga a coluna da Feature.
            var storyHead = new StackPanel { Orientation = Orientation.Horizontal };
            storyHead.Children.Add(MakeHeader(AppStrings.Get("Sprint_ColStory")));
            storyHead.Children.Add(MakeColToggle(AppStrings.Get("Sprint_ColFeature"), showFeat,
                AppStrings.Get("Sprint_ShowFeatHint"), v => _prefs.ShowFeatCol = v));
            // Com a coluna Feature desligada, o checkbox do EPIC migra para cá (não há
            // cabeçalho de Feature para hospedá-lo).
            if (!showFeat)
                storyHead.Children.Add(MakeColToggle(AppStrings.Get("Sprint_ColEpic"), showEpic,
                    AppStrings.Get("Sprint_ShowEpicHint"), v => _prefs.ShowEpic = v));
            if (!showEpic && !showFeat) storyHead.Children.Add(ProjToggle());
            AddCell(head, cStory, storyHead);
            for (int i = 0; i < states.Count; i++) AddCell(head, i + cState0, MakeStateHeader(states[i]));
            HeaderHost.Children.Add(head);
            HeaderHost.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0xC8, 0xD0, 0xD8)) });

            var rowNo = 0;
            var noOwner = AppStrings.Get("Sprint_NoOwner");
            var tasksByPerson = allVisible
                .GroupBy(t => string.IsNullOrWhiteSpace(t.AssignedTo) ? noOwner : t.AssignedTo)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.CurrentCultureIgnoreCase);

            // Stories SEM Task visivel: o dono acompanha a entrega sem detalhar em Task. Elas nao
            // entram no agrupamento por Task (que continua como esta) — sao acrescentadas no FIM
            // da faixa da pessoa, pelo responsavel da STORY.
            var storiesNoTask = ShowStoryNoTaskCheck?.IsChecked != true
                ? new List<TfsImportService.SprintStoryRow>()
                : EffectiveStories()
                .Where(x => !x.Story.IsLevelPlaceholder && x.Story.Id != 0
                            && !x.Tasks.Any(PassesFilters) && StoryPasses(x.Story)
                            && StoryStatePasses(x.Story))
                .Select(x => x.Story)
                .ToList();
            // De quem e a faixa da Story sem Task visivel. Normalmente do responsavel dela. Mas
            // com FILTRO DE PESSOA ativo a Story pode ter entrado no board por causa da Task de
            // uma pessoa filtrada (StoryPasses aceita por responsavel OU por Task) e a Task ter
            // sido escondida pelo corte de estado/dias — ai a Story ia para a faixa do dono, que
            // nem esta no filtro, e sumia de quem trabalhou nela. Nesse caso ela vai para a
            // pessoa FILTRADA que tem Task nela.
            string NoTaskPersonKey(TfsImportService.SprintStoryRow st)
            {
                var owner = EffOwner(st.Id, st.AssignedTo);
                if (_selectedPeople.Count == 0 || (!string.IsNullOrWhiteSpace(owner) && _selectedPeople.Contains(owner)))
                    return string.IsNullOrWhiteSpace(owner) ? noOwner : owner;
                var helper = st.Tasks.Select(t => t.AssignedTo ?? "")
                    .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a) && _selectedPeople.Contains(a));
                if (!string.IsNullOrWhiteSpace(helper)) return helper;
                return string.IsNullOrWhiteSpace(owner) ? noOwner : owner;
            }
            var noTaskByPerson = storiesNoTask
                .GroupBy(NoTaskPersonKey, StringComparer.CurrentCultureIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderBy(st => StoryRankOf(st.Id))
                                                .ThenBy(st => st.Title, StringComparer.CurrentCultureIgnoreCase).ToList(),
                              StringComparer.CurrentCultureIgnoreCase);

            foreach (var personKey in tasksByPerson.Keys.Concat(noTaskByPerson.Keys)
                         .Distinct(StringComparer.CurrentCultureIgnoreCase)
                         .OrderBy(k => k, StringComparer.CurrentCultureIgnoreCase))
            {
                var pg = tasksByPerson.TryGetValue(personKey, out var pl)
                    ? pl : new List<TfsImportService.SprintTaskCard>();
                var firstRow = true;
                // Pessoa recolhida: sobra a linha dela (com o botao para expandir) e nada mais.
                if (_collapsedPeople.Contains(personKey))
                {
                    var prow = MakeRowGrid(cols);
                    AddCell(prow, 0, MakeRowNumber(++rowNo));
                    AddCell(prow, 1, MakePersonCell(personKey));
                    AddCell(prow, cStory, BuildCollapsedHint(pg.Count));
                    BoardHost.Children.Add(prow);
                    BoardHost.Children.Add(new Border
                    {
                        Height = 3, Background = new SolidColorBrush(Color.FromRgb(0x8C, 0x9E, 0xB5)),
                        Margin = new Thickness(0, 8, 0, 8)
                    });
                    continue;
                }
                // Agrupa pelo ID do pai, nao pelo titulo: Tasks penduradas direto em Features
                // DIFERENTES caiam todas num unico grupo "(sem Story)", sem dar para saber de que
                // entrega era cada uma.
                int GroupStoryId(IGrouping<int, TfsImportService.SprintTaskCard> g) => g.Key;
                TfsImportService.SprintStoryRow? GroupStory(IGrouping<int, TfsImportService.SprintTaskCard> g) =>
                    _storyById.TryGetValue(g.Key, out var st) ? st
                    : _board?.Stories.FirstOrDefault(r => r.OrphanParentId == g.Key);
                // Ordem dentro da pessoa, SEMPRE pela hierarquia do backlog do DevOps:
                //   Pessoa → rank do Project → rank do EPIC → rank da Feature → rank da Story.
                // A prioridade da Task NAO entra aqui: ela ordena as Tasks DENTRO da Story, na
                // celula de estado (BuildStateCell). Antes a prioridade vinha primeiro e repartia
                // os blocos de Feature/Story pela pessoa. Sem rank (o DevOps nao devolveu), o item
                // vai para o fim do seu nivel; o titulo so desempata.
                static double RankOrLast(double r) => double.IsNaN(r) ? 1e9 : r;
                var shownCollapsed = new HashSet<int>();   // EPIC recolhido ja anunciado nesta pessoa
                var entries = new List<(double ProjRank, string Proj, double EpicRank, string Epic,
                    double FeatRank, string Feat, double Rank,
                    string Title, IGrouping<int, TfsImportService.SprintTaskCard>? Tasks,
                    TfsImportService.SprintStoryRow? Solo)>();
                foreach (var g in pg.GroupBy(EffTaskParent))
                    entries.Add((RankOrLast(GroupStory(g)?.FeatureProjectRank ?? double.NaN),
                        GroupStory(g)?.FeatureProjectTitle ?? "",
                        RankOrLast(GroupStory(g)?.FeatureEpicRank ?? double.NaN),
                        GroupStory(g)?.FeatureEpicTitle ?? "",
                        RankOrLast(GroupStory(g)?.FeatureRank ?? double.NaN),
                        GroupStory(g)?.FeatureTitle ?? "",
                        StoryRankOf(GroupStoryId(g)),
                        _storyById.TryGetValue(g.Key, out var gst) ? gst.Title : AppStrings.Get("Sprint_NoStory"),
                        g, null));
                // Story sem Task entra na MESMA lista e usa a MESMA chave: ela aparece no lugar
                // dela na hierarquia, nao amontoada no fim.
                if (noTaskByPerson.TryGetValue(personKey, out var soloStories))
                    foreach (var st in soloStories)
                        entries.Add((RankOrLast(st.FeatureProjectRank), st.FeatureProjectTitle ?? "",
                            RankOrLast(st.FeatureEpicRank), st.FeatureEpicTitle ?? "",
                            RankOrLast(st.FeatureRank), st.FeatureTitle ?? "",
                            StoryRankOf(st.Id), st.Title, null, st));

                foreach (var entry in entries
                             .OrderBy(e => e.ProjRank)
                             .ThenBy(e => e.Proj, StringComparer.CurrentCultureIgnoreCase)
                             .ThenBy(e => e.EpicRank)
                             .ThenBy(e => e.Epic, StringComparer.CurrentCultureIgnoreCase)
                             .ThenBy(e => e.FeatRank)
                             .ThenBy(e => e.Feat, StringComparer.CurrentCultureIgnoreCase)
                             .ThenBy(e => e.Rank)
                             .ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase))
                {
                    // Recolhido (Project, EPIC ou Feature): mostra so a linha do nivel recolhido,
                    // uma vez por pessoa, e pula o que vem abaixo dele.
                    var entryStory = entry.Solo ?? (entry.Tasks is { } eg && _storyById.TryGetValue(
                        eg.Select(EffTaskParent).FirstOrDefault(i2 => i2 > 0), out var est) ? est : null);
                    var entryProjId = entryStory?.FeatureProjectId ?? 0;
                    var entryEpicId = entryStory?.FeatureEpicId ?? 0;
                    var entryFeatId = entryStory?.FeatureId ?? 0;
                    var stopAt = IsCollapsed(entryProjId) ? entryProjId
                               : IsCollapsed(entryEpicId) ? entryEpicId
                               : IsCollapsed(entryFeatId) ? entryFeatId : 0;
                    if (stopAt > 0)
                    {
                        if (!shownCollapsed.Add(stopAt)) continue;
                        var crow = MakeRowGrid(cols);
                        AddCell(crow, 0, MakeRowNumber(++rowNo));
                        if (firstRow) { AddCell(crow, 1, MakePersonCell(personKey)); firstRow = false; }
                        if (showProj && entryProjId > 0)
                            AddCell(crow, cProj, BuildLabelCard("🗂", entry.Proj, strong: false, id: entryProjId,
                                extra: BuildCollapseButton(entryProjId)));
                        if (showEpic && stopAt != entryProjId && entryEpicId > 0)
                            AddCell(crow, cEpic, BuildLabelCard("🏔", entry.Epic, strong: true, id: entryEpicId,
                                extra: BuildCollapseButton(entryEpicId)));
                        if (showFeat && stopAt == entryFeatId && entryFeatId > 0)
                            AddCell(crow, cFeat, BuildFeatureCard(entryFeatId, entry.Feat, "", "", "", false,
                                extra: BuildCollapseButton(entryFeatId)));
                        AddCell(crow, cStory, BuildCollapsedHint(0));
                        BoardHost.Children.Add(crow);
                        continue;
                    }
                    var row = MakeRowGrid(cols);
                    AddCell(row, 0, MakeRowNumber(++rowNo));
                    if (firstRow) AddCell(row, 1, MakePersonCell(personKey));
                    if (entry.Solo is { } st2)
                    {
                        firstRow = false;
                        AddPersonNoTaskCells(row, st2, personKey, cols, cProj, cEpic, cFeat, cStory,
                            showProj, showEpic, showFeat, multiProject);
                        BoardHost.Children.Add(row);
                        continue;
                    }
                    var sg = entry.Tasks!;
                    var tks = sg.ToList();
                    var storySp = new StackPanel();
                    var storyId = sg.Key;
                    var sgTitle = StoryById(storyId)?.Title ?? AppStrings.Get("Sprint_NoStory");
                    // O "pai" nao e uma Story (Task pendurada direto na Feature): em vez de um card
                    // de Story que nao existe, oferece criar a Story que falta ali.
                    var orphanFeatureId = StoryById(storyId) == null ? storyId : 0;
                    var sOwnerOrig = storyId > 0 ? (StoryById(storyId)?.AssignedTo ?? string.Empty) : string.Empty;
                    var sOwner = EffOwner(storyId, sOwnerOrig);
                    // "Ajudante": a pessoa desta faixa tem task na Story mas NÃO é a responsável dela.
                    var isHelper = storyId > 0 && !string.IsNullOrWhiteSpace(sOwner)
                        && !string.Equals(sOwner.Trim(), personKey.Trim(), StringComparison.OrdinalIgnoreCase);
                    storySp.Children.Add(new TextBlock { Text = EffTitle(storyId, sgTitle), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
                        Foreground = _titlePending.ContainsKey(storyId) ? new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)) : Brushes.Black });
                    if (orphanFeatureId > 0)
                    {
                        // Nao existe Story: um card vazio convida a criar a que falta, e as
                        // Tasks desta Feature passam para ela na gravacao.
                        var createSt = new Button
                        {
                            Content = AppStrings.Get("Sprint_CreateStoryHere"), FontSize = 10,
                            Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 4, 0, 0),
                            HorizontalAlignment = HorizontalAlignment.Left,
                            ToolTip = AppStrings.Get("Sprint_CreateStoryHereTip"),
                            IsEnabled = EditModeCheck.IsChecked == true
                        };
                        var orphanFeat = orphanFeatureId;
                        var orphanFeatTitle = entry.Feat;
                        var orphanTasks = tks.ToList();
                        createSt.Click += (_, _) => CreateStoryForOrphans(orphanFeat, orphanFeatTitle, personKey, orphanTasks);

                        // Abre no DevOps o item em que as Tasks estao penduradas — que NAO e uma
                        // Story (normalmente a Feature). Sem isto nao havia como chegar nele daqui.
                        var openOrphan = new Button
                        {
                            Content = "🔗", FontSize = 11, Padding = new Thickness(3, 0, 3, 0),
                            Margin = new Thickness(0, 4, 3, 0), HorizontalAlignment = HorizontalAlignment.Left,
                            ToolTip = AppStrings.Get("Sprint_OpenParentDevOps", orphanFeat.ToString())
                        };
                        openOrphan.Click += (_, _) => OpenInDevOps(orphanFeat);
                        // Excluir a Feature: so quando ela NAO tem Story e nenhuma Task solta
                        // sobrando. Com filho vivo, excluir deixaria trabalho orfao no DevOps —
                        // ai o botao fica desabilitado e sobra o 🔗 para abrir e resolver la.
                        var orphanRow = new StackPanel { Orientation = Orientation.Horizontal };
                        orphanRow.Children.Add(openOrphan);
                        // Mesmo botao (e mesma regra) do card da Feature: so aparece se der para excluir.
                        if (BuildDeleteFeatureButton(orphanFeat) is { } delFeat) orphanRow.Children.Add(delFeat);
                        orphanRow.Children.Add(createSt);
                        storySp.Children.Add(orphanRow);
                    }
                    else if (storyId > 0)
                    {
                        // Estado da Story (#id · estado). Laranja quando há mudança de estado pendente.
                        var stRow = StoryById(storyId);
                        storySp.Children.Add(stRow != null
                            ? BuildStoryStateEditor(stRow)
                            : new TextBlock { Text = $"#{storyId}", FontSize = 10, Foreground = Brushes.Gray });
                        storySp.Children.Add(new TextBlock {
                            Text = "👤 " + (string.IsNullOrWhiteSpace(sOwner) ? AppStrings.Get("Sprint_NoOwner") : sOwner),
                            FontSize = 10, TextWrapping = TextWrapping.Wrap,
                            Foreground = _ownerPending.ContainsKey(storyId) ? new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)) : Brushes.DimGray });
                        if (isHelper)
                            storySp.Children.Add(new TextBlock { Text = AppStrings.Get("Sprint_Helping"),
                                FontSize = 10, FontStyle = FontStyles.Italic, Foreground = new SolidColorBrush(Color.FromRgb(0xB2, 0x6A, 0x00)) });
                        // Sprint da Story quando há mais de uma sprint no board.
                        var sIter = IterLeaf(EffIter(storyId, StoryById(storyId)?.IterationPath ?? ""));
                        if (stRow?.OutOfSprint == true && OutOfSprintLine(stRow.IterationPath) is { } sOut)
                            storySp.Children.Add(sOut);
                        else if (_sprintPaths.Count != 1 && !string.IsNullOrEmpty(sIter))
                            storySp.Children.Add(new TextBlock { Text = "🗓 " + sIter, FontSize = 10, TextWrapping = TextWrapping.Wrap,
                                Foreground = _iterPending.ContainsKey(storyId) ? new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)) : Brushes.DimGray });
                        var stActions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0), Cursor = System.Windows.Input.Cursors.Arrow };
                        // O recolher da Story fica junto dos botoes do card (metrica menor que
                        // a dos cards de nivel, onde ele acompanha o ✎ e o ➕).
                        if (BuildCollapseButton(storyId, compact: true) is { } stColl) stActions.Children.Add(stColl);
                        AddEditButtons(stActions, storyId, sgTitle, sOwnerOrig, "Story", StoryById(storyId)?.IterationPath ?? "");
                        // Abrir a Story no DevOps.
                        var openSt = new Button { Content = "🔗", FontSize = 11,
                            Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(0, 0, 3, 2),
                            ToolTip = AppStrings.Get("Sprint_OpenDevOps") };
                        var openStId = storyId;
                        openSt.Click += (_, _) => OpenInDevOps(openStId);
                        stActions.Children.Add(openSt);
                        stActions.Children.Add(BuildStoryTasksButton(openStId, EffTitle(openStId, sgTitle)));
                        var addTask = new Button { Content = AppStrings.Get("Sprint_AddTask"), FontSize = 9,
                            Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(0, 0, 3, 2) };
                        addTask.Click += (_, _) => AddNewTask(storyId, personKey); // já nasce na faixa da pessoa
                        stActions.Children.Add(addTask);
                        // Bloquear/desbloquear a Story — último botão, igual à visão Projeto & Story.
                        stActions.Children.Add(BuildBlockButton(storyId, StoryById(storyId)?.Tags ?? "", isStory: true));
                        // 👁 (mostrar encerradas desta pessoa) e o ULTIMO botao do card.
                        if (BuildRevealClosedButton(openStId, personKey) is { } revPerson) stActions.Children.Add(revPerson);
                        storySp.Children.Add(stActions);
                    }
                    // Destaque laranja quando a Story tem alteração pendente (rank/responsável/nome).
                    var storyPend = storyId > 0 && (_storyRankPending.Contains(storyId) || _ownerPending.ContainsKey(storyId)
                        || _titlePending.ContainsKey(storyId) || _blockPending.ContainsKey(storyId));
                    // Colaborador: cor customizável (paleta "Story Colaborador"); dono: azul-claro normal.
                    var storyBorder = new Border {
                        Background = isHelper ? StateBrush(HelperColorKey) : new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xF7)),
                        BorderBrush = new SolidColorBrush(storyPend ? Color.FromRgb(0xE0, 0x8A, 0x00) : isHelper ? Color.FromRgb(0xE6, 0xC9, 0x8A) : Color.FromRgb(0xD0, 0xD7, 0xE0)),
                        BorderThickness = new Thickness(storyPend ? 2 : 1),
                        CornerRadius = new CornerRadius(3), Margin = new Thickness(3), Padding = new Thickness(6),
                        Child = storySp, Tag = storyId };
                    // Story bloqueada: borda vermelha, igual à visão Projeto & Story.
                    if (storyId > 0 && !storyPend && EffBlocked(storyId, StoryById(storyId)?.Tags ?? ""))
                    {
                        storyBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x30, 0x30));
                        storyBorder.BorderThickness = new Thickness(2);
                    }
                    AttachStoryMoveTargetMenu(storyBorder, storyId);
                    // Hint do card: datas (criacao/estado) + Critérios de Aceitação da Story.
                    if (StoryById(storyId) is { } stDates) AppendTip(storyBorder, StoryDatesText(stDates));
                    var acHint = _acPending.TryGetValue(storyId, out var acp2) ? acp2
                        : (StoryById(storyId)?.AcceptanceCriteria ?? "");
                    var acText = TfsImportService.ToPlainTextPublic(acHint);
                    if (!string.IsNullOrWhiteSpace(acText))
                        AppendTip(storyBorder, AppStrings.Get("Desc_Acceptance") + Environment.NewLine + acText);
                    // Modo edição: arrastar a Story p/ outra pessoa troca o Responsável — só quando esta
                    // pessoa É a responsável (ajudante não move a Story de outro).
                    if (EditModeCheck.IsChecked == true && storyId > 0 && !isHelper)
                    {
                        var personKey2 = personKey;
                        storyBorder.Cursor = System.Windows.Input.Cursors.SizeAll;
                        storyBorder.PreviewMouseMove += (s, ev) =>
                        {
                            if (ev.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
                            if (IsInteractive(ev.OriginalSource as DependencyObject)) return;
                            DragDrop.DoDragDrop(storyBorder, "STORYOWNER:" + storyId, DragDropEffects.Move);
                        };
                        storyBorder.AllowDrop = true;
                        storyBorder.Drop += (s, ev) => OnStoryOwnerDrop(ev, personKey2, storyId);
                        storyBorder.DragOver += (s, ev) => { ev.Effects = DragDropEffects.Move; ev.Handled = true; };
                    }
                    // Coluna Feature (📦) — card só com borda; botão 📄 abre a descrição da Feature.
                    var featTitle = storyId > 0 ? (StoryById(storyId)?.FeatureTitle ?? "") : "";
                    var featId = storyId > 0 ? (StoryById(storyId)?.FeatureId ?? 0) : 0;
                    var featOwner = storyId > 0 ? (StoryById(storyId)?.FeatureAssignedTo ?? "") : "";
                    var featEpic = storyId > 0 ? (StoryById(storyId)?.FeatureEpicTitle ?? "") : "";
                    var featProj = storyId > 0 ? (StoryById(storyId)?.FeatureProjectTitle ?? "") : "";
                    var featState = storyId > 0 ? (StoryById(storyId)?.FeatureState ?? "") : "";
                    // Feature em outra sprint (veio junto com a Task): sprint dela sai em vermelho.
                    var featOutIter = storyId > 0 && StoryById(storyId)?.FeatureOutOfSprint == true
                        ? (StoryById(storyId)?.FeatureIterationPath ?? "") : "";
                    // Coluna Feature desligada: a Feature (e o EPIC/Projeto, se também estiverem
                    // desligados) entram como linhas no TOPO do card da Story.
                    if (!showFeat)
                    {
                        var fBrush = new SolidColorBrush(
                            (StateBrush(FeatureColorKey) as SolidColorBrush)?.Color ?? FactoryStateColor(FeatureColorKey));
                        var inlinePos = 0;
                        void InlineLine(string icon, string text, bool strong)
                        {
                            if (string.IsNullOrWhiteSpace(text)) return;
                            storySp.Children.Insert(inlinePos++, new TextBlock
                            {
                                Text = icon + " " + text, FontSize = strong ? 10 : 9,
                                FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal,
                                Foreground = fBrush, Opacity = strong ? 1.0 : 0.75,
                                TextWrapping = TextWrapping.Wrap
                            });
                        }
                        if (!showEpic && !showProj && multiProject) InlineLine("🗂", featProj, false);
                        if (!showEpic) InlineLine("🏔", featEpic, false);
                        InlineLine("📦", featTitle, true);
                    }
                    // Coluna Projeto (opcional): card proprio, como na visao Projeto & Story.
                    if (showProj)
                        AddCell(row, cProj, BuildLabelCard("🗂", featProj, strong: false,
                            id: StoryById(storyId)?.FeatureProjectId ?? 0,
                            extra: BuildCollapseButton(StoryById(storyId)?.FeatureProjectId ?? 0),
                            state: StoryById(storyId)?.FeatureProjectState ?? ""));
                    // Coluna EPIC (opcional): card do EPIC com o Projeto (sem borda) acima quando há vários.
                    if (showEpic)
                    {
                        var epicSp = new StackPanel();
                        if (!showProj && multiProject && !string.IsNullOrWhiteSpace(featProj))
                            epicSp.Children.Add(BuildLabelCard("🗂", featProj, strong: false, id: StoryById(storyId)?.FeatureProjectId ?? 0,
                                state: StoryById(storyId)?.FeatureProjectState ?? ""));
                        if (!string.IsNullOrWhiteSpace(featEpic))
                            epicSp.Children.Add(BuildLabelCard("🏔", featEpic, strong: true, id: StoryById(storyId)?.FeatureEpicId ?? 0,
                                extra: BuildCollapseButton(StoryById(storyId)?.FeatureEpicId ?? 0),
                                state: StoryById(storyId)?.FeatureEpicState ?? ""));
                        AddCell(row, cEpic, epicSp);
                    }
                    if (showFeat)
                    {
                        // Card da Feature: com a coluna EPIC ligada ele não repete o EPIC; desligada,
                        // o EPIC/Project voltam para dentro do card (linha no nome da Feature).
                        // O "+Story" vem junto, como na visao Projeto & Story: nasce sob esta
                        // Feature e ja com a pessoa da faixa como responsavel.
                        UIElement? addStoryBtn = null;
                        if (featId > 0)
                        {
                            var b = new Button { Content = AppStrings.Get("Sprint_AddStory"), FontSize = 10,
                                Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(4, 0, 0, 0) };
                            var fId = featId; var fName = featTitle;
                            b.Click += (_, _) => AddNewStory(fId, fName, personKey);
                            addStoryBtn = Light(b);
                        }
                        var featExtra = JoinButtons(BuildCollapseButton(featId), addStoryBtn,
                            BuildDeleteFeatureButton(featId));
                        UIElement featCell = string.IsNullOrWhiteSpace(featTitle) ? new TextBlock()
                            : showEpic ? BuildFeatureCard(featId, featTitle, featOwner, "", "", false, extra: featExtra, featState: featState,
                                outOfSprintIter: featOutIter)
                            : BuildFeatureCard(featId, featTitle, featOwner, featEpic,
                                showProj ? "" : featProj, multiProject && !showProj, extra: featExtra, featState: featState,
                                outOfSprintIter: featOutIter);
                        AddCell(row, cFeat, featCell);
                    }
                    AddCell(row, cStory, storyBorder);
                    // Story recolhida: o card dela fica, as Tasks somem das colunas de estado.
                    var storyCollapsed = IsCollapsed(storyId);
                    for (int i = 0; i < states.Count; i++)
                        AddCell(row, i + cState0, storyCollapsed
                            ? (i == 0 ? BuildCollapsedHint(tks.Count) : new TextBlock())
                            : BuildStateCell(states[i], tks, showStory: false));
                    BoardHost.Children.Add(row);
                    firstRow = false;
                }
                // Separador entre pessoas: barra grossa e escura, bem diferente da linha fina
                // que separa as Stories da MESMA pessoa — na rolagem as duas se confundiam e nao
                // dava para ver onde os cards de uma pessoa terminam e os da outra comecam.
                BoardHost.Children.Add(new Border
                {
                    Height = 3, Background = new SolidColorBrush(Color.FromRgb(0x8C, 0x9E, 0xB5)),
                    Margin = new Thickness(0, 8, 0, 8)
                });
            }
        }

        /// <summary>
        /// Linhas de data da Story, iguais nas duas visoes: quando foi criada e desde quando
        /// esta no estado atual (ou quando foi encerrada). Sao campos do work item — nao ha
        /// leitura de historico. Com mudanca de estado pendente a data do estado sai de cena,
        /// porque ainda vale para o estado antigo.
        /// </summary>
        private string StoryDatesText(TfsImportService.SprintStoryRow st)
        {
            if (st.Id <= 0) return "";
            var parts = new List<string>();
            if (st.CreatedDate is { } created)
                parts.Add(AppStrings.Get("Sprint_CreatedOn", created.ToString("dd/MM/yyyy")));
            if (!_storyStatePending.ContainsKey(st.Id))
            {
                var closed = IsClosedState(EffStoryState(st));
                var when = closed ? (st.ClosedDate ?? st.StateChangeDate) : st.StateChangeDate;
                if (when is { } dt)
                    parts.Add(AppStrings.Get(closed ? "Sprint_ClosedOn" : "Sprint_StateSince", dt.ToString("dd/MM/yyyy")));
            }
            return string.Join(Environment.NewLine, parts);
        }

        /// <summary>Datas da Task para o hint: criacao e desde quando esta no estado atual
        /// (ou quando encerrou). Com arrasto pendente a data do estado sai, porque ainda e a
        /// do estado antigo.</summary>
        private string TaskDatesText(TfsImportService.SprintTaskCard t)
        {
            if (t.Id <= 0) return "";
            var parts = new List<string>();
            if (t.CreatedDate is { } created)
                parts.Add(AppStrings.Get("Sprint_CreatedOn", created.ToString("dd/MM/yyyy")));
            if (!_pending.ContainsKey(t.Id))
            {
                var closed = IsClosedState(EffState(t));
                var when = closed ? (t.ClosedDate ?? t.StateChangeDate) : t.StateChangeDate;
                if (when is { } dt)
                    parts.Add(AppStrings.Get(closed ? "Sprint_ClosedOn" : "Sprint_StateSince", dt.ToString("dd/MM/yyyy")));
            }
            return string.Join(Environment.NewLine, parts);
        }

        /// <summary>Acrescenta um bloco ao hint do card, preservando o que ja existe nele.</summary>
        private static void AppendTip(FrameworkElement el, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var prev = el.ToolTip switch
            {
                TextBlock tb => tb.Text,
                string str => str,
                _ => ""
            };
            el.ToolTip = new TextBlock
            {
                Text = string.IsNullOrEmpty(prev) ? text : prev + Environment.NewLine + Environment.NewLine + text,
                TextWrapping = TextWrapping.Wrap, MaxWidth = 420
            };
        }

        /// <summary>
        /// Menu do botao direito no card da Task: marca (ou desmarca) a Task para mover de Story.
        /// A troca so acontece quando a Story destino for escolhida no menu dela.
        /// </summary>
        /// <summary>
        /// Acrescenta "Auditoria de BLOCK" ao menu do botao direito do card. E consulta pura: le o
        /// historico no DevOps na hora, nao grava nada e nao mexe no item.
        /// </summary>
        private void AttachBlockAuditMenu(FrameworkElement card, int id)
        {
            if (id <= 0) return;
            var menu = card.ContextMenu ??= new ContextMenu();
            if (menu.Items.Count > 0) menu.Items.Add(new Separator());
            var item = new MenuItem
            {
                Header = AppStrings.Get("Block_MenuItem"),
                ToolTip = AppStrings.Get("Block_MenuItemTip")
            };
            item.Click += async (_, _) => await ShowBlockAuditAsync(id);
            menu.Items.Add(item);
        }

        /// <summary>
        /// Atalho para a Auditoria de BLOCK no card da Task. So existe quando o campo opcional de
        /// duracao esta habilitado E o item ja acumulou impedimento (> 0) — assim o icone marca de
        /// longe quem tem historico de bloqueio, sem custar leitura de historico por card.
        /// </summary>
        private UIElement? BuildBlockAuditButton(TfsImportService.SprintTaskCard t)
        {
            if (t.Id <= 0 || !BlockService.DurationEnabled(_options)) return null;
            if (t.BlockDurationHours is not double h || h <= 0) return null;

            var btn = new Button
            {
                Content = "⏱", FontSize = 11,
                Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(0, 0, 3, 2),
                Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x30, 0x30)),
                ToolTip = AppStrings.Get("Sprint_BlockAuditShortcut", h.ToString("0"))
            };
            btn.Click += async (_, _) => await ShowBlockAuditAsync(t.Id);
            return btn;
        }

        /// <summary>Le a auditoria de bloqueio no DevOps e abre a tela. Sempre online, sob demanda.</summary>
        private async Task ShowBlockAuditAsync(int id)
        {
            try
            {
                StatusText.Text = AppStrings.Get("Block_Loading", id.ToString());
                var audit = await TfsImportService.LoadBlockAuditAsync(_options, id);
                StatusText.Text = "";
                if (audit == null)
                {
                    MessageBox.Show(this, AppStrings.Get("Block_NoHistory", id.ToString()),
                        "NXProject", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                new TfsBlockAuditWindow(audit, OpenInDevOps,
                    () => TfsImportService.LoadBlockAuditAsync(_options, id)) { Owner = this }.ShowDialog();
            }
            catch (Exception ex)
            {
                StatusText.Text = "";
                MessageBox.Show(this, AppStrings.Get("Block_Failed", id.ToString(), ex.Message),
                    "NXProject", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void AttachTaskMoveMenu(FrameworkElement card, TfsImportService.SprintTaskCard t)
        {
            if (t.Id <= 0) return;   // card novo ainda nao existe no DevOps
            var menu = card.ContextMenu ??= new ContextMenu();
            // Selecao MULTIPLA: cada Task entra ou sai do conjunto; o destino e uma Story so.
            if (_moveTaskIds.Contains(t.Id))
            {
                var unmark = new MenuItem { Header = AppStrings.Get("Sprint_MoveUnmark") };
                unmark.Click += (_, _) => { _moveTaskIds.Remove(t.Id); MoveSelectionChanged(); };
                menu.Items.Add(unmark);
            }
            else
            {
                var mark = new MenuItem
                {
                    Header = _moveTaskIds.Count == 0
                        ? AppStrings.Get("Sprint_MoveMark")
                        : AppStrings.Get("Sprint_MoveMarkMore", _moveTaskIds.Count.ToString())
                };
                mark.Click += (_, _) => { _moveTaskIds.Add(t.Id); MoveSelectionChanged(); };
                menu.Items.Add(mark);
            }
            if (_moveTaskIds.Count > 0)
            {
                var cancel = new MenuItem
                {
                    Header = AppStrings.Get("Sprint_MoveCancelAll", _moveTaskIds.Count.ToString())
                };
                cancel.Click += (_, _) => { _moveTaskIds.Clear(); MoveSelectionChanged(); };
                menu.Items.Add(cancel);
            }
            if (_taskParentPending.ContainsKey(t.Id))
            {
                var undo = new MenuItem { Header = AppStrings.Get("Sprint_MoveUndo") };
                undo.Click += (_, _) => { _taskParentPending.Remove(t.Id); UpdatePendingButton(); Render(); };
                menu.Items.Add(undo);
            }
            AttachBlockAuditMenu(card, t.Id);
        }

        /// <summary>
        /// Menu do botao direito no card da Story: destino do "mover Task". So aparece com uma
        /// Task marcada, e nao deixa mover para a Story onde ela ja esta.
        /// </summary>
        private void AttachStoryMoveTargetMenu(FrameworkElement card, int storyId)
        {
            if (storyId <= 0) return;
            // A auditoria de bloqueio vale para toda Story; o destino do "mover Task" so quando ha
            // uma Task marcada — por isso ela entra antes das saidas abaixo.
            AttachBlockAuditMenu(card, storyId);
            if (_moveTaskIds.Count == 0) return;
            // So as que ainda NAO estao nesta Story: marcar uma Task e clicar na propria Story
            // dela nao pode virar movimento.
            var movable = _moveTaskIds
                .Where(id => _cardById.TryGetValue(id, out var c) && EffTaskParent(c) != storyId)
                .ToList();
            if (movable.Count == 0) return;

            var menu = card.ContextMenu ??= new ContextMenu();
            var apply = new MenuItem
            {
                Header = movable.Count == 1 && _cardById.TryGetValue(movable[0], out var only)
                    ? AppStrings.Get("Sprint_MoveApply", "#" + movable[0], EffTitle(movable[0], only.Title))
                    : AppStrings.Get("Sprint_MoveApplyMany", movable.Count.ToString())
            };
            apply.Click += (_, _) =>
            {
                foreach (var taskId in movable)
                {
                    if (!_cardById.TryGetValue(taskId, out var moving)) continue;
                    // Voltar para o pai original cancela a pendencia em vez de gravar o mesmo valor.
                    var baseParent = _taskParentApplied.TryGetValue(taskId, out var ap) ? ap : (moving.ParentId ?? 0);
                    if (storyId == baseParent) _taskParentPending.Remove(taskId);
                    else _taskParentPending[taskId] = storyId;
                }
                _moveTaskIds.Clear();
                UpdatePendingButton();
                MoveSelectionChanged();
            };
            // O "mover para ca" vai no TOPO: e a acao do momento, a auditoria e consulta.
            menu.Items.Insert(0, apply);
        }

        /// <summary>
        /// Redesenha o board e atualiza o contador da selecao de "mover Task" na barra de status —
        /// com varias Tasks marcadas, espalhadas pelo board, e ali que se ve quantas sao.
        /// </summary>
        private void MoveSelectionChanged()
        {
            Render();
            StatusText.Text = _moveTaskIds.Count > 0
                ? AppStrings.Get("Sprint_MoveSelectionCount", _moveTaskIds.Count.ToString())
                : "";
        }

        /// <summary>
        /// Sprint de um Feature/EPIC/Project, lida do que o board carregou: primeiro o item de
        /// nivel, depois a ancestralidade das Stories (que traz a iteracao da Feature).
        /// </summary>
        private string LevelIterationOf(int id)
        {
            if (id <= 0 || _board == null) return "";
            var lvl = _board.LevelItems.FirstOrDefault(i => i.Id == id);
            if (lvl != null && !string.IsNullOrEmpty(lvl.IterationPath)) return lvl.IterationPath;
            var byFeature = _board.Stories.FirstOrDefault(s => s.FeatureId == id
                && !string.IsNullOrEmpty(s.FeatureIterationPath));
            return byFeature?.FeatureIterationPath ?? "";
        }

        /// <summary>Story pai efetiva da Task: pendente -> ja gravada -> a do DevOps.</summary>
        private int EffTaskParent(TfsImportService.SprintTaskCard t) =>
            _taskParentPending.TryGetValue(t.Id, out var p) ? p
            : _taskParentApplied.TryGetValue(t.Id, out var a) ? a : (t.ParentId ?? 0);

        /// <summary>
        /// Botao que recolhe/expande tudo abaixo do item (vale para Work Item Project, EPIC,
        /// Feature e Story). Recolhido, sobra so a linha do proprio item — e a forma de focar
        /// no que interessa sem mexer em filtro nenhum.
        /// </summary>
        private UIElement? BuildCollapseButton(int epicId, bool compact = false)
        {
            if (epicId <= 0) return null;
            var collapsed = _collapsed.Contains(epicId);
            var btn = new Button
            {
                Content = new TextBlock { Text = collapsed ? "▸" : "▾", FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center },
                // Mesma altura, alinhamento e margem do ✎ e do ➕ (BuildEditLevelButton /
                // BuildAddChildButton): os tres ficam na mesma linha do card do EPIC.
                Padding = new Thickness(3, 0, 3, 0),
                Margin = compact ? new Thickness(0, 0, 3, 2) : new Thickness(0, 4, 3, 0),
                Height = LevelButtonHeight, VerticalAlignment = VerticalAlignment.Center,
                ToolTip = AppStrings.Get(collapsed ? "Sprint_EpicExpandTip" : "Sprint_EpicCollapseTip")
            };
            btn.Click += (_, _) =>
            {
                if (!_collapsed.Remove(epicId)) _collapsed.Add(epicId);
                SavePrefs();
                RenderBusy();
            };
            return Light(btn);
        }

        /// <summary>Linha enxuta de um EPIC recolhido: so o aviso de que ha conteudo escondido.</summary>
        private UIElement BuildCollapsedHint(int hidden) => new TextBlock
        {
            Text = hidden > 0 ? AppStrings.Get("Sprint_EpicCollapsed") + " (" + hidden + ")"
                : AppStrings.Get("Sprint_EpicCollapsed"),
            FontSize = 10, FontStyle = FontStyles.Italic, Margin = new Thickness(4, 4, 4, 2),
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x88, 0x92)), TextWrapping = TextWrapping.Wrap
        };

        /// <summary>Horas de trabalho consideradas por dia para projetar a data alvo.</summary>
        private const double TargetHoursPerDay = 8;

        /// <summary>
        /// Data em que terminam <paramref name="hours"/> horas de trabalho comecando em
        /// <paramref name="start"/> (o proprio dia conta como o 1o), pulando sabado e domingo.
        /// Ate 8h termina no mesmo dia; 16h no dia util seguinte; e assim por diante.
        /// </summary>
        private static DateTime AddWorkHours(DateTime start, double hours)
        {
            var d = start.Date;
            while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(1);
            var days = Math.Max(1, (int)Math.Ceiling(hours / TargetHoursPerDay));
            for (var i = 1; i < days; i++)
            {
                d = d.AddDays(1);
                while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(1);
            }
            return d;
        }

        /// <summary>Celula da pessoa: nome, selo de WIP e alvo de arrasto para trocar o
        /// responsavel da Story. Usada tanto na 1a linha com Task quanto nas Stories sem Task.</summary>
        /// <summary>Celula do nome de cada pessoa no desenho atual — e o ponto de chegada do
        /// Ctrl+clique no filtro de pessoas. Refeito a cada Render.</summary>
        private readonly Dictionary<string, FrameworkElement> _personCellByKey =
            new(StringComparer.CurrentCultureIgnoreCase);

        private UIElement MakePersonCell(string personKey)
        {
            var personCell = new TextBlock { Text = personKey, FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 2, 4, 2) };
            // So a PRIMEIRA celula de cada pessoa conta: e o topo da faixa dela.
            _personCellByKey.TryAdd(personKey, personCell);
            if (EditModeCheck.IsChecked == true)
            {
                personCell.AllowDrop = true;
                personCell.Drop += (s, ev) => OnStoryOwnerDrop(ev, personKey);
                personCell.DragOver += (s, ev) => { ev.Effects = DragDropEffects.Move; ev.Handled = true; };
            }
            // Recolher a faixa inteira da pessoa: mesmo botao dos niveis, so que a chave e o nome.
            var collapsed = _collapsedPeople.Contains(personKey);
            var toggle = new Button
            {
                Content = new TextBlock { Text = collapsed ? "▸" : "▾", FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center },
                Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(0, 0, 4, 0),
                Height = LevelButtonHeight, VerticalAlignment = VerticalAlignment.Center,
                ToolTip = AppStrings.Get(collapsed ? "Sprint_EpicExpandTip" : "Sprint_EpicCollapseTip")
            };
            toggle.Click += (_, _) =>
            {
                if (!_collapsedPeople.Remove(personKey)) _collapsedPeople.Add(personKey);
                SavePrefs();
                RenderBusy();
            };
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(Light(toggle));
            head.Children.Add(personCell);
            var personPanel = new StackPanel();
            personPanel.Children.Add(head);
            // Selo "em andamento / limite" da pessoa — o WIP é dela, não do projeto.
            var wipN = WipCountOf(personKey);
            var wipMax = WipLimit();
            if (wipMax > 0 && wipN > 0)
                personPanel.Children.Add(new TextBlock
                {
                    Text = AppStrings.Get("Sprint_WipCount", wipN.ToString(), wipMax.ToString()),
                    FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(4, 0, 4, 2),
                    ToolTip = AppStrings.Get("Sprint_WipCountTip", wipMax.ToString()),
                    Foreground = new SolidColorBrush(wipN > wipMax
                        ? Color.FromRgb(0xC0, 0x30, 0x30) : Color.FromRgb(0x5A, 0x7A, 0x5A))
                });
            return personPanel;
        }

        /// <summary>
        /// Celulas (Projeto / EPIC / Feature / Story) da linha de uma Story SEM Task na visao
        /// Pessoa x Task. As colunas de estado ficam vazias — e o proprio sinal de "sem Task".
        /// </summary>
        private void AddPersonNoTaskCells(Grid row, TfsImportService.SprintStoryRow st, string personKey,
            List<GridLength> cols, int cProj, int cEpic, int cFeat, int cStory,
            bool showProj, bool showEpic, bool showFeat, bool multiProject)
        {
            if (showProj)
                AddCell(row, cProj, BuildLabelCard("🗂", st.FeatureProjectTitle, strong: false,
                    id: st.FeatureProjectId, extra: BuildCollapseButton(st.FeatureProjectId),
                    state: st.FeatureProjectState));
            if (showEpic)
            {
                var epicSp2 = new StackPanel();
                if (!showProj && multiProject && !string.IsNullOrWhiteSpace(st.FeatureProjectTitle))
                    epicSp2.Children.Add(BuildLabelCard("🗂", st.FeatureProjectTitle, strong: false,
                        id: st.FeatureProjectId, state: st.FeatureProjectState));
                if (!string.IsNullOrWhiteSpace(st.FeatureEpicTitle))
                    epicSp2.Children.Add(BuildLabelCard("🏔", st.FeatureEpicTitle, strong: true,
                        id: st.FeatureEpicId, extra: BuildCollapseButton(st.FeatureEpicId),
                        state: st.FeatureEpicState));
                AddCell(row, cEpic, epicSp2);
            }
            if (showFeat)
            {
                UIElement? addSt = null;
                if (st.FeatureId > 0)
                {
                    var b2 = new Button { Content = AppStrings.Get("Sprint_AddStory"), FontSize = 10,
                        Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(4, 0, 0, 0) };
                    var fId2 = st.FeatureId; var fName2 = st.FeatureTitle;
                    b2.Click += (_, _) => AddNewStory(fId2, fName2, personKey);
                    addSt = Light(b2);
                }
                var featExtra2 = JoinButtons(BuildCollapseButton(st.FeatureId), addSt,
                    BuildDeleteFeatureButton(st.FeatureId));
                var featOut2 = st.FeatureOutOfSprint ? st.FeatureIterationPath : "";
                AddCell(row, cFeat, string.IsNullOrWhiteSpace(st.FeatureTitle) ? new TextBlock()
                    : showEpic ? BuildFeatureCard(st.FeatureId, st.FeatureTitle, st.FeatureAssignedTo, "", "", false, extra: featExtra2, featState: st.FeatureState,
                        outOfSprintIter: featOut2)
                    : BuildFeatureCard(st.FeatureId, st.FeatureTitle, st.FeatureAssignedTo, st.FeatureEpicTitle,
                        showProj ? "" : st.FeatureProjectTitle, multiProject && !showProj, extra: featExtra2, featState: st.FeatureState,
                        outOfSprintIter: featOut2));
            }
            AddCell(row, cStory, BuildPersonStoryCardNoTask(st, personKey));
        }

        /// <summary>
        /// Card da Story na visao Pessoa x Task quando ela NAO tem Task visivel. Traz os mesmos
        /// dados e botoes do card normal (estado, responsavel, sprint, editar, DevOps, grade de
        /// Tasks, +Task e bloquear) e um selo dizendo que ainda nao ha Task.
        /// </summary>
        private Border BuildPersonStoryCardNoTask(TfsImportService.SprintStoryRow st, string personKey)
        {
            // Story NOVA (id temporario, ainda sem work item): vai o card EDITAVEL, o mesmo da
            // visao Projeto & Story. Antes caia neste card de consulta — sem campos para
            // preencher nome/responsavel/HH e com botoes apontando para um id que nao existe.
            if (st.Id < 0 && _newCards.FirstOrDefault(x => x.TempId == st.Id) is { } ncNew)
                return BuildNewCardBorder(ncNew);

            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = EffTitle(st.Id, st.Title), FontWeight = FontWeights.Normal, TextWrapping = TextWrapping.Wrap,
                Foreground = _titlePending.ContainsKey(st.Id)
                    ? new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00))
                    : new SolidColorBrush(Color.FromRgb(0x55, 0x5C, 0x66))
            });
            sp.Children.Add(BuildStoryStateEditor(st));
            var owner = EffOwner(st.Id, st.AssignedTo);
            sp.Children.Add(new TextBlock
            {
                Text = "👤 " + (string.IsNullOrWhiteSpace(owner) ? AppStrings.Get("Sprint_NoOwner") : owner),
                FontSize = 10, TextWrapping = TextWrapping.Wrap,
                Foreground = _ownerPending.ContainsKey(st.Id)
                    ? new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)) : Brushes.DimGray
            });
            // A Story pode estar na faixa de quem TEM Task nela, e nao do responsavel (filtro de
            // pessoa + Task escondida pelo corte de estado/dias). Deixa claro que e colaboracao.
            if (!string.IsNullOrWhiteSpace(owner) && !string.Equals(owner, personKey, StringComparison.CurrentCultureIgnoreCase))
                sp.Children.Add(new TextBlock
                {
                    Text = AppStrings.Get("Sprint_Helping"), FontSize = 10, FontStyle = FontStyles.Italic,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xB2, 0x6A, 0x00))
                });
            sp.Children.Add(new TextBlock
            {
                Text = AppStrings.Get("Sprint_StoryNoTask"), FontSize = 10, FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x88, 0x92))
            });
            var sIter = IterLeaf(EffIter(st.Id, st.IterationPath ?? ""));
            if (_sprintPaths.Count != 1 && !string.IsNullOrEmpty(sIter))
                sp.Children.Add(new TextBlock { Text = "🗓 " + sIter, FontSize = 10, TextWrapping = TextWrapping.Wrap,
                    Foreground = _iterPending.ContainsKey(st.Id)
                        ? new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)) : Brushes.DimGray });

            var actions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0),
                Cursor = System.Windows.Input.Cursors.Arrow };
            AddEditButtons(actions, st.Id, st.Title, st.AssignedTo, "Story", st.IterationPath ?? "");
            var open = new Button { Content = "🔗", FontSize = 11, Padding = new Thickness(3, 0, 3, 0),
                Margin = new Thickness(0, 0, 3, 2), ToolTip = AppStrings.Get("Sprint_OpenDevOps") };
            open.Click += (_, _) => OpenInDevOps(st.Id);
            actions.Children.Add(open);
            actions.Children.Add(BuildStoryTasksButton(st.Id, EffTitle(st.Id, st.Title)));
            var addTask = new Button { Content = AppStrings.Get("Sprint_AddTask"), FontSize = 9,
                Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(0, 0, 3, 2) };
            addTask.Click += (_, _) => AddNewTask(st.Id, personKey);
            actions.Children.Add(addTask);
            // Excluir: so quando a Story esta SEM TASK DE VERDADE — nenhuma no DevOps, ou
            // todas ja marcadas para excluir. Task apenas escondida por filtro (Closed, por
            // exemplo) NAO libera o botao: o item continua existindo no DevOps.
            var stDel = _deletePending.Contains(st.Id);
            var stNoTasks = st.Tasks.Count == 0 || st.Tasks.All(t => _deletePending.Contains(t.Id));
            if (string.Equals(EffStoryState(st), "New", StringComparison.OrdinalIgnoreCase) && stNoTasks)
            {
                var delNoTask = new Button { Content = stDel ? "↩" : "🗑", FontSize = 11,
                    Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(0, 0, 3, 2),
                    Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x30, 0x30)),
                    ToolTip = AppStrings.Get(stDel ? "Sprint_UndoDelete" : "Sprint_DeleteTask") };
                delNoTask.Click += async (_, _) => await ToggleDeleteWithChildCheckAsync(st.Id);
                actions.Children.Add(delNoTask);
            }
            actions.Children.Add(BuildBlockButton(st.Id, st.Tags ?? "", isStory: true));
            // 👁 (mostrar encerradas desta pessoa) e o ULTIMO botao do card.
            if (BuildRevealClosedButton(st.Id, personKey) is { } revNoTask) actions.Children.Add(revNoTask);
            sp.Children.Add(actions);

            var pend = _storyRankPending.Contains(st.Id) || _ownerPending.ContainsKey(st.Id)
                || _titlePending.ContainsKey(st.Id) || _blockPending.ContainsKey(st.Id)
                || _deletePending.Contains(st.Id);
            // Cor de MENOR destaque que o card de Story com Task: a linha existe para
            // acompanhamento, nao deve competir visualmente com quem tem trabalho em andamento.
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF8, 0xFA)),
                BorderBrush = new SolidColorBrush(pend ? Color.FromRgb(0xE0, 0x8A, 0x00) : Color.FromRgb(0xE2, 0xE6, 0xEB)),
                BorderThickness = new Thickness(pend ? 2 : 1),
                CornerRadius = new CornerRadius(3), Margin = new Thickness(3), Padding = new Thickness(6),
                Opacity = pend ? 1.0 : 0.85,
                Child = sp, Tag = st.Id
            };
            if (!pend && EffBlocked(st.Id, st.Tags ?? ""))
            {
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x30, 0x30));
                border.BorderThickness = new Thickness(2);
            }
            AppendTip(border, StoryDatesText(st));
            AttachStoryMoveTargetMenu(border, st.Id);
            // Modo edicao: arrastar para outra pessoa troca o Responsavel, igual ao card normal.
            if (EditModeCheck.IsChecked == true)
            {
                border.Cursor = System.Windows.Input.Cursors.SizeAll;
                border.PreviewMouseMove += (s2, ev) =>
                {
                    if (ev.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
                    if (IsInteractive(ev.OriginalSource as DependencyObject)) return;
                    DragDrop.DoDragDrop(border, "STORYOWNER:" + st.Id, DragDropEffects.Move);
                };
                border.AllowDrop = true;
                border.Drop += (s2, ev) => OnStoryOwnerDrop(ev, personKey, st.Id);
                border.DragOver += (s2, ev) => { ev.Effects = DragDropEffects.Move; ev.Handled = true; };
            }
            return border;
        }

        // Soltou uma Story na visão Pessoa & Task:
        //  • na MESMA pessoa, sobre outra Story → reordena (troca o StackRank);
        //  • em OUTRA pessoa → troca o Responsável da Story.
        private void OnStoryOwnerDrop(DragEventArgs e, string targetPerson, int targetStoryId = 0)
        {
            if (!e.Data.GetDataPresent(DataFormats.StringFormat)) return;
            var payload = e.Data.GetData(DataFormats.StringFormat) as string;
            if (payload == null || !payload.StartsWith("STORYOWNER:")) return;
            if (!int.TryParse(payload.Substring("STORYOWNER:".Length), out var id) || id <= 0) return;
            var story = StoryById(id);
            if (story == null) return;
            var owner = EffOwner(id, story.AssignedTo);
            var samegroup = string.Equals((targetPerson ?? "").Trim(), (owner ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
            if (samegroup)
            {
                // Mesma pessoa: reordena trocando o rank com a Story-alvo.
                if (targetStoryId > 0 && targetStoryId != id)
                {
                    var a = StoryRankOf(id); var b = StoryRankOf(targetStoryId);
                    _storyRank[id] = b; _storyRank[targetStoryId] = a;
                    _storyRankPending.Add(id); _storyRankPending.Add(targetStoryId);
                }
            }
            else
            {
                var baseline = _ownerApplied.TryGetValue(id, out var ap) ? ap : story.AssignedTo;
                if (string.Equals((targetPerson ?? "").Trim(), (baseline ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                    _ownerPending.Remove(id);
                else
                    _ownerPending[id] = targetPerson ?? string.Empty;
            }
            UpdatePendingButton();
            Render();
        }

        // StoryBoard: a STORY é o card, posicionada na coluna do seu estado, agrupada por Feature.
        private void RenderStoryBoard(List<(TfsImportService.SprintStoryRow Story, List<TfsImportService.SprintTaskCard> Tasks)> visibleByStory,
            List<string> states)
        {
            var stories = EffectiveStories().Select(x => x.Story).Where(StoryPasses).ToList();
            var storyStates = TfsImportService.OrderTaskboardStates(states.Concat(stories.Select(EffStoryState)));
            if (storyStates.Count == 0) storyStates = states;
            // Feature/EPIC/Project que estao NA SPRINT sem Story sua visivel: ocupam a coluna do
            // seu tipo como linha propria (placeholder), nunca como card de estado.
            stories.AddRange(BuildLevelPlaceholders(stories));

            // Colunas Projeto e EPIC são opcionais: cada checkbox fica no cabeçalho da coluna
            // SEGUINTE (Projeto no do EPIC, EPIC no da Feature). Desligada, a informação volta
            // para dentro do card seguinte.
            var showProjCol = _prefs.ShowProjCol ?? true;
            var showEpicCol = _prefs.ShowEpicCol ?? true;
            // Colunas: [Projeto] | [EPIC] | Feature | estados.
            var cols = new List<GridLength> { new(RowNumberWidth) };
            if (showProjCol) cols.Add(new(150));
            if (showEpicCol) cols.Add(new(170));
            cols.Add(new(180));
            var cProjCol = 1;                                 // só usado quando showProjCol
            var cEpic = 1 + (showProjCol ? 1 : 0);            // só usado quando showEpicCol
            var cFeat = 1 + (showProjCol ? 1 : 0) + (showEpicCol ? 1 : 0);
            var cState0 = cFeat + 1;
            foreach (var _ in storyStates) cols.Add(new GridLength(240));

            // Faixa: nesta visão os cards das colunas de estado são STORIES.
            AddCardsBandRow(cols, cState0, AppStrings.Get("Sprint_CardsAreStories"));
            var head = MakeRowGrid(cols);
            AddCell(head, 0, MakeHeader("#"));
            if (showProjCol) AddCell(head, cProjCol, MakeHeader(AppStrings.Get("Sprint_ColProject")));
            // Cabeçalho do EPIC + checkbox que liga/desliga a coluna do Projeto.
            if (showEpicCol)
            {
                var epicHead = new StackPanel { Orientation = Orientation.Horizontal };
                epicHead.Children.Add(MakeHeader(AppStrings.Get("Sprint_ColEpic")));
                epicHead.Children.Add(MakeColToggle(AppStrings.Get("Sprint_ColProject"), showProjCol,
                    AppStrings.Get("Sprint_ShowProjHint"), v => _prefs.ShowProjCol = v));
                AddCell(head, cEpic, epicHead);
            }
            // Cabeçalho da Feature + checkbox que liga/desliga a coluna do EPIC.
            var featHead2 = new StackPanel { Orientation = Orientation.Horizontal };
            featHead2.Children.Add(MakeHeader(AppStrings.Get("Sprint_ColFeature")));
            featHead2.Children.Add(MakeColToggle(AppStrings.Get("Sprint_ColEpic"), showEpicCol,
                AppStrings.Get("Sprint_ShowEpicColHint"), v => _prefs.ShowEpicCol = v));
            // Sem a coluna EPIC, o checkbox do Projeto migra para cá.
            if (!showEpicCol)
                featHead2.Children.Add(MakeColToggle(AppStrings.Get("Sprint_ColProject"), showProjCol,
                    AppStrings.Get("Sprint_ShowProjHint"), v => _prefs.ShowProjCol = v));
            AddCell(head, cFeat, featHead2);
            for (int i = 0; i < storyStates.Count; i++) AddCell(head, i + cState0, MakeStateHeader(storyStates[i]));
            HeaderHost.Children.Add(head);
            HeaderHost.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0xC8, 0xD0, 0xD8)) });

            // Cards de Project/EPIC aparecem só na 1ª Feature de cada um (não repetem).
            var lastProj = (string?)null;
            var lastEpic = (string?)null;
            var lastShownProjCollapsed = (string?)null;   // Project recolhido ja anunciado
            // Cards novos de EPIC/Feature ja renderizados (saem junto do bloco do pai).
            var newShown = new HashSet<int>();
            var rowNo = 0;
            static string FirstText(IEnumerable<TfsImportService.SprintStoryRow> g, Func<TfsImportService.SprintStoryRow, string?> pick)
                => g.Select(pick).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? "";
            foreach (var fg in stories
                         .GroupBy(s => s.FeatureId > 0 ? $"F{s.FeatureId}" : $"E{s.FeatureEpicId}|P{s.FeatureProjectId}")
                         .OrderBy(g => FirstText(g, s => s.FeatureProjectTitle), StringComparer.CurrentCultureIgnoreCase)
                         .ThenBy(g => FirstText(g, s => s.FeatureEpicTitle), StringComparer.CurrentCultureIgnoreCase)
                         .ThenBy(g => FirstText(g, s => s.FeatureTitle), StringComparer.CurrentCultureIgnoreCase))
            {
                var featureId = fg.Select(s => s.FeatureId).FirstOrDefault(id => id > 0);
                var featName = FirstText(fg, s => s.FeatureTitle);
                if (string.IsNullOrWhiteSpace(featName)) featName = AppStrings.Get("Sprint_NoFeature");
                var groupStories = fg.OrderBy(s => StoryRankOf(s.Id))
                    .ThenBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
                var realIds = groupStories.Where(s => s.Id > 0).Select(s => s.Id).ToList();

                var row = MakeRowGrid(cols);
                AddCell(row, 0, MakeRowNumber(++rowNo));
                // Card da Feature igual ao da visão Pessoa & Task, com o "+Story" junto dos botões.
                var featRow = fg.FirstOrDefault(s => s.FeatureEpicId > 0 || s.FeatureProjectId > 0)
                              ?? fg.FirstOrDefault(s => s.FeatureId > 0) ?? fg.First();
                UIElement addStory = null!;
                if (featureId > 0)
                {
                    var btn = new Button { Content = AppStrings.Get("Sprint_AddStory"), FontSize = 10,
                        Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(4, 0, 0, 0) };
                    btn.Click += (_, _) => AddNewStory(featureId, featName);
                    addStory = Light(btn);
                }
                // Colunas Project e EPIC (só na 1ª Feature de cada um, para não repetir).
                var projTitle = featRow?.FeatureProjectTitle ?? "";
                var epicTitle = featRow?.FeatureEpicTitle ?? "";
                var isNewBlock = epicTitle != lastEpic || projTitle != lastProj;
                if (showProjCol)
                    AddCell(row, cProjCol, projTitle == lastProj ? new TextBlock()
                        : BuildLabelCard("🗂", projTitle, strong: false, id: featRow?.FeatureProjectId ?? 0,
                            extra: JoinButtons(
                                BuildCollapseButton(featRow?.FeatureProjectId ?? 0),
                                BuildAddChildButton(featRow?.FeatureProjectId ?? 0, projTitle, "Epic")),
                            state: featRow?.FeatureProjectState ?? ""));
                // Sem a coluna Projeto, ele aparece como linha discreta acima do EPIC.
                if (showEpicCol)
                {
                    var epicSp = new StackPanel();
                    if (isNewBlock)
                    {
                        if (!showProjCol && !string.IsNullOrWhiteSpace(projTitle))
                            epicSp.Children.Add(BuildLabelCard("🗂", projTitle, strong: false, id: featRow?.FeatureProjectId ?? 0,
                                extra: JoinButtons(
                                    BuildCollapseButton(featRow?.FeatureProjectId ?? 0),
                                    BuildAddChildButton(featRow?.FeatureProjectId ?? 0, projTitle, "Epic")),
                                state: featRow?.FeatureProjectState ?? ""));
                        epicSp.Children.Add(BuildLabelCard("🏔", epicTitle, strong: true, id: featRow?.FeatureEpicId ?? 0,
                            extra: JoinButtons(
                                BuildCollapseButton(featRow?.FeatureEpicId ?? 0),
                                BuildEditLevelButton(featRow?.FeatureEpicId ?? 0, epicTitle, "Epic"),
                                BuildAddChildButton(featRow?.FeatureEpicId ?? 0, epicTitle, "Feature"),
                                BuildDeleteEpicButton(featRow?.FeatureEpicId ?? 0)),
                            state: featRow?.FeatureEpicState ?? ""));
                    }
                    AddCell(row, cEpic, epicSp);
                }
                lastProj = projTitle; lastEpic = epicTitle;
                // Recolhido: a linha para no nivel recolhido (o card dele fica, com o botao
                // para expandir) e tudo abaixo sai. Project recolhe EPIC/Feature/Story; EPIC
                // recolhe Feature/Story; Feature recolhe so as Storys.
                var projIdRow = featRow?.FeatureProjectId ?? 0;
                var epicIdRow = featRow?.FeatureEpicId ?? 0;
                var storyCount = groupStories.Count(s2 => !s2.IsLevelPlaceholder);
                if (IsCollapsed(projIdRow))
                {
                    // Uma linha por Project: so o card dele; nem EPIC nem Feature aparecem.
                    if (projTitle != lastShownProjCollapsed)
                    {
                        if (showEpicCol) AddCell(row, cEpic, new TextBlock());
                        AddCell(row, cFeat, BuildCollapsedHint(0));
                        BoardHost.Children.Add(row);
                        BoardHost.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0xD5, 0xDD, 0xD7)), Margin = new Thickness(0, 2, 0, 4) });
                        lastShownProjCollapsed = projTitle;
                    }
                    else rowNo--;
                    continue;
                }
                if (IsCollapsed(epicIdRow))
                {
                    if (isNewBlock)
                    {
                        AddCell(row, cFeat, BuildCollapsedHint(storyCount));
                        BoardHost.Children.Add(row);
                        BoardHost.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0xD5, 0xDD, 0xD7)), Margin = new Thickness(0, 2, 0, 4) });
                    }
                    else rowNo--;   // linha descartada: nao consome numeracao
                    continue;
                }
                // Card da Feature. Com a coluna EPIC desligada, o EPIC (e o Projeto, se a coluna
                // dele também estiver desligada) voltam para dentro do card.
                var onlyLevel = groupStories.All(s => s.IsLevelPlaceholder);
                if (featureId <= 0 && onlyLevel)
                    AddCell(row, cFeat, new TextBlock());   // EPIC/Projeto da sprint sem Feature: fica em branco
                else
                {
                    var featExtra = JoinButtons(BuildCollapseButton(featureId), addStory,
                        BuildDeleteFeatureButton(featureId));
                    AddCell(row, cFeat, showEpicCol
                        ? BuildFeatureCard(featureId, featName, featRow?.FeatureAssignedTo ?? "", "", "", false, featExtra,
                            featState: featRow?.FeatureState ?? "")
                        : BuildFeatureCard(featureId, featName, featRow?.FeatureAssignedTo ?? "",
                            epicTitle, projTitle, !showProjCol, featExtra, featState: featRow?.FeatureState ?? ""));
                }

                var featCollapsed = IsCollapsed(featureId);
                for (int i = 0; i < storyStates.Count; i++)
                {
                    var st = storyStates[i];
                    var cell = new StackPanel { Margin = new Thickness(2) };
                    if (!featCollapsed)
                        foreach (var s in groupStories.Where(s => !s.IsLevelPlaceholder && SameState(EffStoryState(s), st)))
                            cell.Children.Add(BuildStoryCard(s, realIds));
                    if (featCollapsed && i == 0) cell.Children.Add(BuildCollapsedHint(storyCount));
                    if (EditModeCheck.IsChecked == true)
                    {
                        var host = new Border
                        {
                            MinHeight = 44, Background = new SolidColorBrush(Color.FromRgb(0xF6, 0xF8, 0xFB)),
                            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE1, 0xE7, 0xEF)), BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(3), Margin = new Thickness(1), Padding = new Thickness(2),
                            AllowDrop = true, Tag = st, Child = cell
                        };
                        host.Drop += OnStoryDrop;
                        host.DragOver += (s, ev) => { ev.Effects = DragDropEffects.Move; ev.Handled = true; };
                        AddCell(row, i + cState0, host);
                    }
                    else AddCell(row, i + cState0, cell);
                }
                BoardHost.Children.Add(row);
                BoardHost.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0xD5, 0xDD, 0xD7)), Margin = new Thickness(0, 2, 0, 4) });

                // O card recem-criado sai logo abaixo do bloco do pai que o originou.
                var here = new HashSet<int>();
                if (featRow?.FeatureEpicId is int ei && ei > 0) here.Add(ei);
                if (featRow?.FeatureProjectId is int pi && pi > 0) here.Add(pi);
                if (here.Count > 0)
                    RenderNewEpicAndFeatureRows(cols, showProjCol, showEpicCol, cEpic, cFeat, here, newShown);
            }

            // Sobra: card novo cujo pai nao apareceu em nenhum bloco (filtro escondeu o bloco).
            RenderNewEpicAndFeatureRows(cols, showProjCol, showEpicCol, cEpic, cFeat, null, newShown);
        }

        // Feature/EPIC/Project atribuidos a sprint que NAO tem Story sua visivel no board viram
        // uma linha sintetica na coluna do seu tipo — assim o item aparece onde pertence, sem
        // virar card de estado. Quem ja aparece por causa de uma Story (ou de um item de nivel
        // abaixo) nao e duplicado. Respeita pessoa, busca e "somente Story do cronograma"; com
        // filtro de Story ativo nao entra (o recorte e de Stories).
        private List<TfsImportService.SprintStoryRow> BuildLevelPlaceholders(List<TfsImportService.SprintStoryRow> visible)
        {
            var list = new List<TfsImportService.SprintStoryRow>();
            if (_board == null || _board.LevelItems.Count == 0) return list;
            if (ShowFeatureNoStoryCheck?.IsChecked != true) return list;
            // Com filtro ativo, o item de nivel entra se ELE estiver marcado na arvore (a Feature
            // sem Story tem checkbox proprio la). Antes a linha era simplesmente suprimida, e a
            // Feature sumia do board sem o usuario ter como pedi-la de volta.
            bool FilterOk(int id) => _selectedStoryIds.Count == 0 || _selectedStoryIds.Contains(id);

            var q = SearchQuery();
            bool PersonOk(string who) => _selectedPeople.Count == 0 || _selectedPeople.Contains(who ?? "");
            // O card de nivel (Feature/EPIC/Project solto na sprint) casa pelo proprio nome ou id.
            // Com o escopo restrito a Task ou Story ele sai do board durante a busca.
            bool SearchOk(string title, int id)
            {
                if (string.IsNullOrEmpty(q)) return true;
                if (SearchScope() is 1 or 2 or 4) return false;
                return title.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0
                    || id.ToString().Contains(q);
            }
            bool SchedOk(int id) => OnlyScheduleCheck.IsChecked != true || _scheduleIds.Contains(id);

            var items = _board.LevelItems;
            var featCovered = visible.Where(s => s.FeatureId > 0).Select(s => s.FeatureId).ToHashSet();
            var epicCovered = visible.Where(s => s.FeatureEpicId > 0).Select(s => s.FeatureEpicId)
                .Concat(items.Where(i => i.Kind == "Feature" && i.EpicId > 0).Select(i => i.EpicId)).ToHashSet();
            var projCovered = visible.Where(s => s.FeatureProjectId > 0).Select(s => s.FeatureProjectId)
                .Concat(items.Where(i => i.Kind != "Project" && i.ProjectId > 0).Select(i => i.ProjectId)).ToHashSet();

            foreach (var it in items)
            {
                var covered = it.Kind switch
                {
                    "Feature" => featCovered.Contains(it.Id),
                    "Epic"    => epicCovered.Contains(it.Id),
                    _         => projCovered.Contains(it.Id)
                };
                if (covered || !FilterOk(it.Id) || !LastSprintOk(it.AssignedTo, it.IterationPath)
                    || !PersonOk(it.AssignedTo)
                    || !SearchOk(it.Title, it.Id) || !SchedOk(it.Id)) continue;

                // Id negativo e fora da faixa dos cards novos (que comecam em -1): nunca colide.
                var row = new TfsImportService.SprintStoryRow(-(1_000_000 + it.Id), it.Title, it.State, it.AssignedTo, new())
                {
                    IsLevelPlaceholder = true, IterationPath = it.IterationPath, Tags = it.Tags
                };
                switch (it.Kind)
                {
                    case "Feature":
                        row.FeatureId = it.Id; row.FeatureTitle = it.Title; row.FeatureAssignedTo = it.AssignedTo;
                        row.FeatureState = it.State; row.FeatureEpicState = it.EpicState; row.FeatureProjectState = it.ProjectState;
                        row.FeatureEpicId = it.EpicId; row.FeatureEpicTitle = it.EpicTitle;
                        row.FeatureProjectId = it.ProjectId; row.FeatureProjectTitle = it.ProjectTitle;
                        break;
                    case "Epic":
                        row.FeatureEpicId = it.Id; row.FeatureEpicTitle = it.Title;
                        row.FeatureEpicState = it.State; row.FeatureProjectState = it.ProjectState;
                        row.FeatureProjectId = it.ProjectId; row.FeatureProjectTitle = it.ProjectTitle;
                        break;
                    default:
                        row.FeatureProjectId = it.Id; row.FeatureProjectTitle = it.Title;
                        row.FeatureProjectState = it.State;
                        break;
                }
                list.Add(row);
            }
            return list;
        }

        // EPICs e Features criados pelo "+EPIC"/"+Feature" ainda nao existem no DevOps e nao
        // tem Story para entrar na grade normal: ganham uma linha propria na coluna do seu
        // nivel, logo abaixo do bloco do pai (como a Story nova aparece dentro do grupo dela),
        // ate serem gravados no "Atualizar TFS". `parentIds` limita o que sai agora; null =
        // o que sobrou (pai fora do recorte visivel), renderizado no fim.
        private void RenderNewEpicAndFeatureRows(List<GridLength> cols, bool showProjCol, bool showEpicCol,
            int cEpic, int cFeat, HashSet<int>? parentIds, HashSet<int> alreadyShown)
        {
            var pend = _newCards
                .Where(n => (n.Type == "Epic" || n.Type == "Feature") && !alreadyShown.Contains(n.TempId))
                .Where(n => parentIds == null || parentIds.Contains(n.ParentId))
                .ToList();
            if (pend.Count == 0) return;

            foreach (var nc in pend)
            {
                alreadyShown.Add(nc.TempId);
                var row = MakeRowGrid(cols);
                AddCell(row, 0, new TextBlock());   // linha de card novo: sem numero
                // Onde o pai aparece, para o usuario saber sob quem o item vai nascer.
                var parent = new TextBlock
                {
                    Text = (nc.Type == "Epic" ? "🗂 " : "🏔 ") + nc.FeatureTitle,
                    FontSize = 10, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray,
                    Margin = new Thickness(4, 6, 4, 2)
                };

                if (nc.Type == "Epic")
                {
                    // EPIC novo: pai (Projeto) na coluna do Projeto, card na coluna do EPIC.
                    if (showProjCol) AddCell(row, 0, parent);
                    AddCell(row, showEpicCol ? cEpic : cFeat, BuildNewCardBorder(nc));
                }
                else
                {
                    // Feature nova: pai (EPIC) na coluna do EPIC, card na coluna da Feature.
                    if (showEpicCol) AddCell(row, cEpic, parent);
                    AddCell(row, cFeat, BuildNewCardBorder(nc));
                }

                BoardHost.Children.Add(row);
                BoardHost.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0xD5, 0xDD, 0xD7)), Margin = new Thickness(0, 2, 0, 4) });
            }
        }

        // Estado efetivo de Feature/EPIC/Project: a fila e a mesma da Story (_storyStatePending /
        // _storyStateApplied), porque o id do work item e unico e a gravacao e generica.
        private string EffLevelState(int id, string original) =>
            _storyStatePending.TryGetValue(id, out var p) ? p
            : _storyStateApplied.TryGetValue(id, out var a) ? a : original ?? "";

        // Estado do DevOps de um Feature/EPIC pelo id, buscado nas linhas/itens do board.
        private string LevelStateOf(int id, string kind)
        {
            if (_board == null || id <= 0) return "";
            if (kind == "Feature")
                return _board.Stories.FirstOrDefault(r => r.FeatureId == id)?.FeatureState
                    ?? _board.LevelItems.FirstOrDefault(i => i.Id == id)?.State ?? "";
            return _board.Stories.FirstOrDefault(r => r.FeatureEpicId == id)?.FeatureEpicState
                ?? _board.LevelItems.FirstOrDefault(i => i.Id == id)?.State
                ?? _board.LevelItems.FirstOrDefault(i => i.EpicId == id)?.EpicState ?? "";
        }

        // Linha "estado" dos cards de Feature/EPIC/Project, na cor do estado (laranja quando
        // ha troca pendente). null quando o DevOps nao informou estado.
        private UIElement? LevelStateLine(int id, string state, double fontSize)
        {
            var eff = EffLevelState(id, state);
            if (string.IsNullOrWhiteSpace(eff)) return null;
            var pending = _storyStatePending.ContainsKey(id);
            return new TextBlock
            {
                Text = eff, FontSize = fontSize, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 1, 0, 0),
                Foreground = pending ? new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)) : StateBrush(eff)
            };
        }

        private string EffStoryState(TfsImportService.SprintStoryRow s) =>
            _storyStatePending.TryGetValue(s.Id, out var p) ? p
            : _storyStateApplied.TryGetValue(s.Id, out var a) ? a : s.State;

        // Responsável efetivo de um work item (pendente > aplicado > valor original).
        /// <summary>Data alvo efetiva: pendente > gravada nesta sessao > o que veio na carga.</summary>
        private DateTime? EffFinishDate(TfsImportService.SprintTaskCard t) =>
            _finishPending.TryGetValue(t.Id, out var p) ? p
            : _finishApplied.TryGetValue(t.Id, out var a) ? a : t.FinishDate;

        private string EffOwner(int id, string original) =>
            _ownerPending.TryGetValue(id, out var p) ? p
            : _ownerApplied.TryGetValue(id, out var a) ? a : original;

        // Tags/estado de bloqueio efetivos (pendente > tags aplicadas > tags originais).
        private string EffTags(int id, string original) =>
            _tagsApplied.TryGetValue(id, out var a) ? a : (original ?? "");
        private bool EffBlocked(int id, string originalTags) =>
            _blockPending.TryGetValue(id, out var b) ? b : HasTag(EffTags(id, originalTags), BlockedTag());
        // Tag NP efetiva (pendente > tags aplicadas > tags originais), como o bloqueio.
        private bool EffUnplanned(int id, string originalTags) =>
            _unplannedPending.TryGetValue(id, out var u) ? u : HasTag(EffTags(id, originalTags), UnplannedTag());
        // Alterna o bloqueio (pendente) comparando com a baseline (tags gravadas/originais).
        private void ToggleBlockPending(int id, string originalTags)
        {
            var baseBlocked = HasTag(EffTags(id, originalTags), BlockedTag());
            var target = !EffBlocked(id, originalTags);
            if (target == baseBlocked) _blockPending.Remove(id); else _blockPending[id] = target;
            UpdatePendingButton(); Render();
        }

        // Iteração (sprint) efetiva da Story (pendente > aplicado > valor original).
        private string EffIter(int id, string original) =>
            _iterPending.TryGetValue(id, out var p) ? p
            : _iterApplied.TryGetValue(id, out var a) ? a : original;

        // Último segmento do IterationPath (nome curto da sprint).
        private static string IterLeaf(string path) =>
            string.IsNullOrEmpty(path) ? "" : path.Split('\\', '/').LastOrDefault() ?? path;

        // Nome/título efetivo de um work item (pendente > aplicado > valor original).
        private string EffTitle(int id, string original) =>
            id <= 0 ? original
            : _titlePending.TryGetValue(id, out var p) ? p
            : _titleApplied.TryGetValue(id, out var a) ? a : original;

        private bool StoryPasses(TfsImportService.SprintStoryRow s)
        {
            if (s.Id < 0) return true; // Story nova sempre visível
            if (OnlyScheduleCheck.IsChecked == true && !_scheduleIds.Contains(s.Id)) return false;
            if (_selectedStoryIds.Count > 0 && !_selectedStoryIds.Contains(s.Id)) return false;
            // So a sprint da STORY decide: Story de sprint antiga sai do board levando as Tasks
            // dela junto, mesmo que alguma esteja na sprint nova.
            if (IsFutureSprint(s.IterationPath)) return false;
            if (!LastSprintOk(EffOwner(s.Id, s.AssignedTo), s.IterationPath)) return false;
            // Filtro de pessoa também na visão Por Story: mostra a Story se o responsável dela
            // ou alguma task sua for de uma das pessoas selecionadas.
            if (_selectedPeople.Count > 0)
            {
                var ownerMatch = !string.IsNullOrWhiteSpace(EffOwner(s.Id, s.AssignedTo))
                                 && _selectedPeople.Contains(EffOwner(s.Id, s.AssignedTo));
                var taskMatch = s.Tasks.Any(t => _selectedPeople.Contains(t.AssignedTo ?? ""));
                if (!ownerMatch && !taskMatch) return false;
            }
            var q = SearchQuery();
            if (!string.IsNullOrEmpty(q))
            {
                var scope = SearchScope();
                bool storyMatch = s.Title.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 || s.Id.ToString().Contains(q);
                bool taskMatch = s.Tasks.Any(t => t.Title.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0
                    || (!string.IsNullOrEmpty(t.AssignedTo) && t.AssignedTo.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0));
                bool levelMatch = LevelMatches(s, q);
                bool personMatch =
                    (EffOwner(s.Id, s.AssignedTo) ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0
                    || s.Tasks.Any(t => !string.IsNullOrEmpty(t.AssignedTo)
                        && t.AssignedTo.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0);
                return scope switch
                {
                    1 => taskMatch, 2 => storyMatch, 3 => levelMatch, 4 => personMatch,
                    _ => storyMatch || taskMatch || levelMatch
                };
            }
            return true;
        }

        /// <summary>
        /// Indice id -> Story do desenho atual. Antes StoryById remontava EffectiveStories() a
        /// cada chamada, e ela e chamada por card, varias vezes: com todas as pessoas e todas as
        /// sprints carregadas isso virava trabalho quadratico e o board demorava a abrir.
        /// </summary>
        private readonly Dictionary<int, TfsImportService.SprintStoryRow> _storyRowCache = new();
        private TfsImportService.SprintBoard? _storyRowCacheBoard;
        private int _storyRowCacheNewCount = -1;

        /// <summary>Refaz o indice. Chamado no inicio do desenho e quando o board/cards novos mudam.</summary>
        private void RebuildStoryRowCache()
        {
            _storyRowCache.Clear();
            foreach (var x in EffectiveStories())
                if (!_storyRowCache.ContainsKey(x.Story.Id)) _storyRowCache[x.Story.Id] = x.Story;
            _storyRowCacheBoard = _board;
            _storyRowCacheNewCount = _newCards.Count;
        }

        private TfsImportService.SprintStoryRow? StoryById(int id)
        {
            if (!ReferenceEquals(_storyRowCacheBoard, _board) || _storyRowCacheNewCount != _newCards.Count)
                RebuildStoryRowCache();
            return _storyRowCache.TryGetValue(id, out var row) ? row : null;
        }

        /// <summary>Borders dos cards novos no desenho atual, por TempId — usados para rolar ate
        /// o card recem-criado, que pode nascer no fim do board (pai sem Story visivel).</summary>
        private readonly Dictionary<int, FrameworkElement> _newCardBorderById = new();

        /// <summary>Card novo a mostrar assim que o board terminar de desenhar.</summary>
        private int _scrollToNewCard;

        /// <summary>Leva a rolagem ate o card novo, se ele ainda estiver no board.</summary>
        private void ScrollToNewCardIfAny()
        {
            if (_scrollToNewCard == 0) return;
            var id = _scrollToNewCard;
            _scrollToNewCard = 0;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_newCardBorderById.TryGetValue(id, out var el) || !el.IsVisible) return;
                try
                {
                    var y = el.TransformToAncestor(BoardHost).Transform(new Point(0, 0)).Y;
                    BoardScroll.ScrollToVerticalOffset(Math.Max(0, y - 60));
                }
                catch (InvalidOperationException) { /* fora da arvore visual: ignora */ }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // Card NOVO editável no próprio card: Nome, Responsável, HH Estimado e Descrição.
        private Border BuildNewCardBorder(NewCard nc)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xE7, 0xF6, 0xE7)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x10)), BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(6)
            };
            _newCardBorderById[nc.TempId] = border;
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = "🆕 " + AppStrings.Get(nc.Type switch
                {
                    "Story" => "Sprint_NewStory",
                    "Feature" => "Sprint_NewFeature",
                    "Epic" => "Sprint_NewEpic",
                    _ => "Sprint_NewTask"
                }),
                FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x10)), FontSize = 11
            });

            sp.Children.Add(new TextBlock { Text = AppStrings.Get("Sprint_FldName"), FontSize = 10, Foreground = Brushes.Gray });
            var name = new TextBox { Text = nc.Title, FontSize = 12, Margin = new Thickness(0, 0, 0, 3) };
            name.TextChanged += (_, _) => nc.Title = name.Text;
            sp.Children.Add(name);

            sp.Children.Add(new TextBlock { Text = AppStrings.Get("Sprint_FldOwner"), FontSize = 10, Foreground = Brushes.Gray });
            var person = new ComboBox { IsEditable = true, FontSize = 11, Margin = new Thickness(0, 0, 0, 3), Text = nc.AssignedTo };
            if (_board != null) foreach (var p in _board.People) person.Items.Add(p);
            // Trocar a pessoa move o card para a faixa dela. O re-render é ADIADO (Dispatcher) para
            // não reconstruir a árvore no meio do evento do ComboBox — senão a 1ª troca não reagrupa.
            void RegroupDeferred() => Dispatcher.BeginInvoke(new Action(Render), System.Windows.Threading.DispatcherPriority.Background);
            person.SelectionChanged += (_, _) =>
            {
                var chosen = person.SelectedItem as string;
                if (string.IsNullOrEmpty(chosen) || string.Equals(chosen, nc.AssignedTo, StringComparison.CurrentCultureIgnoreCase)) return;
                nc.AssignedTo = chosen; UpdatePendingButton(); RegroupDeferred();
            };
            person.LostFocus += (_, _) => { if (nc.AssignedTo != person.Text) { nc.AssignedTo = person.Text; RegroupDeferred(); } };
            sp.Children.Add(person);

            // HH so faz sentido em Story/Task: o esforco de Feature e EPIC vem do rollup dos filhos.
            if (nc.Type is "Story" or "Task")
            {
                var hhRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
                hhRow.Children.Add(new TextBlock { Text = AppStrings.Get("Sprint_FldHH"), FontSize = 10, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
                var hh = new TextBox { Width = 56, FontSize = 11, Text = nc.Effort?.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture) ?? "" };
                hh.TextChanged += (_, _) => nc.Effort = double.TryParse(hh.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.CurrentCulture, out var v) ? v : (double?)null;
                hhRow.Children.Add(hh);
                sp.Children.Add(hhRow);
            }

            // Sprint-alvo: só aparece quando há mais de uma opção (várias sprints/"Todas" selecionadas).
            var sprintOpts = NewCardSprintOptions();
            if (sprintOpts.Count > 1 || (sprintOpts.Count == 1 && nc.Type is "Feature" or "Epic"))
            {
                if (string.IsNullOrEmpty(nc.IterationPath) || sprintOpts.All(s => s.Path != nc.IterationPath))
                    nc.IterationPath = DefaultNewIterationPath();
                sp.Children.Add(new TextBlock { Text = AppStrings.Get("Sprint_FldSprint"), FontSize = 10, Foreground = Brushes.Gray });
                var spCombo = new ComboBox { FontSize = 11, Margin = new Thickness(0, 0, 0, 3),
                    ItemsSource = sprintOpts, DisplayMemberPath = nameof(TfsImportService.SprintInfo.Name) };
                spCombo.SelectedItem = sprintOpts.FirstOrDefault(s => s.Path == nc.IterationPath) ?? sprintOpts[0];
                spCombo.SelectionChanged += (_, _) => { if (spCombo.SelectedItem is TfsImportService.SprintInfo si) nc.IterationPath = si.Path; };
                sp.Children.Add(spCombo);
            }

            // Data de Início (só Story): opcional, gravada no Data_Inicio ao criar.
            if (nc.Type == "Story")
            {
                var dtRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
                dtRow.Children.Add(new TextBlock { Text = AppStrings.Get("Sprint_FldStart"), FontSize = 10, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
                var dp = new DatePicker { SelectedDate = nc.StartDate, FontSize = 11, Width = 118 };
                dp.SelectedDateChanged += (_, _) => nc.StartDate = dp.SelectedDate;
                dtRow.Children.Add(dp);
                sp.Children.Add(dtRow);
            }

            sp.Children.Add(new TextBlock { Text = AppStrings.Get("Sprint_FldDesc"), FontSize = 10, Foreground = Brushes.Gray });
            var desc = new TextBox { Text = nc.Description, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 46, FontSize = 11, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            desc.TextChanged += (_, _) => nc.Description = desc.Text;
            sp.Children.Add(desc);

            var rm = new Button { Content = AppStrings.Get("Sprint_RemoveNew"), FontSize = 10, Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            rm.Click += (_, _) =>
            {
                _newCards.Remove(nc);
                // Tasks marcadas para virar filhas DESTE card novo voltam para onde estavam.
                foreach (var kv in _taskParentPending.Where(k => k.Value == nc.TempId).ToList())
                    _taskParentPending.Remove(kv.Key);
                UpdatePendingButton(); Render();
            };
            sp.Children.Add(rm);

            border.Child = sp;
            return border;
        }

        /// <summary>
        /// Botão de bloquear/desbloquear (tag "Blocked"), com o selo padrão do NX: ⛔ para Story
        /// e 🔴 para Task. Livre mostra só o ícone; bloqueado mostra o selo completo com "BLOCK".
        /// Entra na fila do "Atualizar TFS".
        /// </summary>
        /// <summary>
        /// Cadeado DESENHADO (vetor), nao emoji: o WPF nao suporta fonte colorida, entao 🔒 sempre
        /// saia como contorno vazado e sem aceitar cor. Aqui o corpo e preenchido e o arco e
        /// tracejado na mesma cor, entao o icone fica cheio e acompanha a cor do estado.
        /// </summary>
        private static UIElement BuildPadlockIcon(Color color, bool closed, double size)
        {
            var brush = new SolidColorBrush(color);
            var canvas = new Canvas { Width = 14, Height = 15 };
            // Arco (haste): fechado desce nos dois lados; aberto so no lado esquerdo, aberto p/ cima.
            var shackle = new System.Windows.Shapes.Path
            {
                Stroke = brush, StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Data = Geometry.Parse(closed
                    ? "M 3.6,7 L 3.6,4.6 A 3.4,3.4 0 0 1 10.4,4.6 L 10.4,7"
                    : "M 3.6,7 L 3.6,4.6 A 3.4,3.4 0 0 1 10.4,4.6")
            };
            // Corpo: retangulo arredondado CHEIO + furo da fechadura vazado na cor do fundo.
            var body = new System.Windows.Shapes.Rectangle
            {
                Width = 12, Height = 8.5, RadiusX = 1.8, RadiusY = 1.8, Fill = brush
            };
            Canvas.SetLeft(body, 1); Canvas.SetTop(body, 6.5);
            var hole = new System.Windows.Shapes.Ellipse
            {
                Width = 2.6, Height = 2.6, Fill = Brushes.White, Opacity = 0.9
            };
            Canvas.SetLeft(hole, 5.7); Canvas.SetTop(hole, 9.4);
            canvas.Children.Add(shackle);
            canvas.Children.Add(body);
            canvas.Children.Add(hole);
            return new Viewbox { Width = size, Height = size * 15 / 14, Child = canvas };
        }

        private Button BuildBlockButton(int id, string tags, bool isStory)
        {
            var blocked = EffBlocked(id, tags);
            // No card o bloqueio e um CADEADO (fechado/aberto), nao o rotulo "⛔ BLOCK" do
            // cronograma: ali o texto cabe na coluna, aqui ele espremia a linha de botoes.
            // As chaves Grid_Blocked* seguem valendo para a grade e o Gantt.
            // Bloqueado e VERMELHO nos dois niveis (Story e Task): o bloqueio significa a mesma
            // coisa nos dois, e o amarelo da Task nao chamava tanto quanto o vermelho.
            var bg = Color.FromRgb(0xFD, 0xE7, 0xE9);
            var bd = Color.FromRgb(0xD1, 0x34, 0x38);
            var fg = Color.FromRgb(0xC0, 0x30, 0x30);
            var btn = new Button
            {
                Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(0, 0, 3, 2),
                ToolTip = AppStrings.Get(blocked ? "Sprint_Unblock" : "Sprint_Block"),
                Background = new SolidColorBrush(blocked ? bg : Color.FromRgb(0xF2, 0xF2, 0xF2)),
                BorderBrush = new SolidColorBrush(blocked ? bd : Color.FromRgb(0xC8, 0xC8, 0xC8)),
                // Bloqueado sai um ponto maior: e um alerta, nao mais um botao.
                Content = BuildPadlockIcon(blocked ? fg : Color.FromRgb(0x8A, 0x8A, 0x8A),
                                           blocked, blocked ? 13 : 11),
                Opacity = blocked ? 1.0 : 0.55
            };
            btn.Click += (_, _) => ToggleBlockPending(id, tags);
            return btn;
        }

        // 📋 Abre a lista completa de atividades da Story, buscada no DevOps na hora. Diferente do
        // 📅 (que leva a atividade ao cronograma aberto), aqui NAO ha cronograma envolvido: serve
        // para ver todas as Tasks da Story — inclusive de outras pessoas e as que nao foram
        // importadas — mesmo com a Story fora do cronograma.
        private Button BuildStoryTasksButton(int storyId, string storyTitle)
        {
            // "☰ Tasks": icone de lista + texto, no padrao do "➕ Story" — o 📋 sozinho nao dizia o que abria.
            var btn = new Button { Content = "☰ Tasks", FontSize = 9, Padding = new Thickness(3, 0, 3, 0),
                Margin = new Thickness(0, 0, 3, 2), ToolTip = AppStrings.Get("Sprint_StoryTasks") };
            btn.Click += (_, _) =>
            {
                var win = TfsOnlineChildTasksWindow.FromTaskBoard(storyId, storyTitle);
                win.OnShowOnBoard = ShowTaskOnBoardAsync;
                win.Owner = this;
                win.ShowDialog();
            };
            return btn;
        }

        // Linha "#id · estado" da Story. Quando o estado nao bate com o das Tasks (Story em New
        // com Task ja iniciada, ou Story em Active sem nenhuma Task em Active), o estado aparece
        // destacado com ⚠ e o motivo no ToolTip — e so um aviso, nao bloqueia nada.
        /// <summary>
        /// Estado da Story editavel direto no card (visao Pessoa &amp; Task): "#id ·" seguido de um
        /// combo com os estados do board, sem fundo proprio (fica na cor do card). Mantem as cores
        /// e o aviso da linha somente leitura: laranja com troca pendente, vermelho com o ⚠ de
        /// estado incoerente com as Tasks (motivo no hint). A troca entra na fila do "Atualizar TFS".
        /// </summary>
        private UIElement BuildStoryStateEditor(TfsImportService.SprintStoryRow story)
        {
            var line = BuildStoryStateLine(story);   // reaproveita cor, negrito e o hint do ⚠
            var states = _board?.States?.ToList() ?? new List<string>();
            var current = EffStoryState(story);
            if (story.Id <= 0 || states.Count == 0) return line;
            if (!states.Any(x => SameState(x, current))) states.Add(current);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Cursor = System.Windows.Input.Cursors.Arrow,
                ToolTip = line.ToolTip };
            var hasAlert = line.Text.Contains("⚠");
            row.Children.Add(new TextBlock
            {
                Text = $"#{story.Id}  ·  " + (hasAlert ? "⚠ " : ""),
                FontSize = 10, VerticalAlignment = VerticalAlignment.Center,
                Foreground = line.Foreground, FontWeight = line.FontWeight
            });
            var combo = new ComboBox
            {
                FontSize = 10, Height = 20, MinWidth = 70, Padding = new Thickness(4, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xCF, 0xD8)),
                Foreground = line.Foreground, FontWeight = line.FontWeight,
                ToolTip = line.ToolTip ?? AppStrings.Get("Desc_State")
            };
            foreach (var st in states) combo.Items.Add(st);
            combo.SelectedItem = states.First(x => SameState(x, current));
            // handler so depois do valor inicial, para nao disparar na montagem do card
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is not string chosen) return;
                var baseline = _storyStateApplied.TryGetValue(story.Id, out var a) ? a : story.State;
                if (SameState(baseline, chosen)) _storyStatePending.Remove(story.Id);
                else _storyStatePending[story.Id] = chosen;
                UpdatePendingButton();
                // Redesenha depois que o combo fecha (recriar o card com o dropdown aberto trava o foco).
                Dispatcher.BeginInvoke(new Action(RenderBusy), System.Windows.Threading.DispatcherPriority.Background);
            };
            row.Children.Add(combo);
            return row;
        }

        private TextBlock BuildStoryStateLine(TfsImportService.SprintStoryRow story)
        {
            var state = EffStoryState(story);
            var alert = TfsImportService.CheckStoryStateAlert(state, story.Tasks.Select(t => EffState(t)));
            var tb = new TextBlock { Text = $"#{story.Id}  ·  {state}", FontSize = 10,
                Foreground = _storyStatePending.ContainsKey(story.Id)
                    ? new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)) : Brushes.Gray };
            if (alert == TfsImportService.StoryStateAlert.None) return tb;

            tb.Text = $"#{story.Id}  ·  ⚠ {state}";
            tb.FontWeight = FontWeights.Bold;
            tb.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x30, 0x30));
            // O motivo vem com as Tasks envolvidas: a que disparou o alerta pode estar fora da
            // tela (ex.: Closed escondida pelo filtro de encerradas), e o ⚠ e da Story x Tasks,
            // nao da Story x Feature.
            var sb = new System.Text.StringBuilder(AppStrings.Get(alert == TfsImportService.StoryStateAlert.NewWithStartedTask
                ? "Sprint_StoryAlertNewWithTask"
                : "Sprint_StoryAlertActiveNoTask"));
            var culprits = story.Tasks
                .Where(t => !SameState(EffState(t), "Removed"))
                .Where(t => alert == TfsImportService.StoryStateAlert.NewWithStartedTask
                    ? !SameState(EffState(t), "New")
                    : true)
                .ToList();
            if (culprits.Count > 0)
            {
                sb.Append(nl2()).Append(nl2()).Append(AppStrings.Get(
                    alert == TfsImportService.StoryStateAlert.NewWithStartedTask
                        ? "Sprint_StoryAlertTasksStarted" : "Sprint_StoryAlertTasksAll"));
                foreach (var t in culprits.Take(8))
                    sb.Append(nl2()).Append($"  • #{t.Id} {EffTitle(t.Id, t.Title)} — {EffState(t)}");
                if (culprits.Count > 8) sb.Append(nl2()).Append($"  … +{culprits.Count - 8}");
            }
            tb.ToolTip = sb.ToString();
            return tb;

            static string nl2() => Environment.NewLine;
        }

        private Border BuildStoryCard(TfsImportService.SprintStoryRow story, List<int> realIds)
        {
            if (story.Id < 0 && _newCards.FirstOrDefault(n => n.TempId == story.Id) is { } ncs)
                return BuildNewCardBorder(ncs);
            var isNew = story.Id < 0;
            var pend = _storyStatePending.ContainsKey(story.Id) || _storyRankPending.Contains(story.Id)
                || _ownerPending.ContainsKey(story.Id) || _titlePending.ContainsKey(story.Id) || _iterPending.ContainsKey(story.Id)
                || _blockPending.ContainsKey(story.Id) || _featurePending.ContainsKey(story.Id) || _startPending.ContainsKey(story.Id);

            var storyBlocked = story.Id > 0 && EffBlocked(story.Id, story.Tags);
            var storyToDelete = _deletePending.Contains(story.Id);
            var border = new Border
            {
                Background = storyToDelete ? new SolidColorBrush(Color.FromRgb(0xFB, 0xE3, 0xE3))
                    : isNew ? new SolidColorBrush(Color.FromRgb(0xE7, 0xF6, 0xE7)) : pend ? new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xD6)) : StateTintBrush(EffStoryState(story)),
                BorderBrush = new SolidColorBrush(storyToDelete ? Color.FromRgb(0xC0, 0x30, 0x30) : isNew ? Color.FromRgb(0x10, 0x7C, 0x10) : pend ? Color.FromRgb(0xE0, 0x8A, 0x00) : Color.FromRgb(0xD0, 0xD7, 0xE0)),
                BorderThickness = new Thickness(storyToDelete || isNew || pend ? 2 : 1),
                CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(6), Tag = story.Id
            };
            if (storyBlocked && !pend && !storyToDelete) { border.BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x30, 0x30)); border.BorderThickness = new Thickness(2); }
            AttachStoryMoveTargetMenu(border, story.Id);
            AppendTip(border, StoryDatesText(story));
            var sp = new StackPanel();
            var storyTitleTb = new TextBlock { Text = (isNew ? "🆕 " : "") + EffTitle(story.Id, story.Title), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
                Foreground = storyToDelete ? new SolidColorBrush(Color.FromRgb(0xC0, 0x30, 0x30)) : _titlePending.ContainsKey(story.Id) ? new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)) : Brushes.Black };
            if (storyToDelete) storyTitleTb.TextDecorations = TextDecorations.Strikethrough;
            sp.Children.Add(storyTitleTb);
            if (story.Id > 0)
            {
                sp.Children.Add(BuildStoryStateLine(story));
                // Responsável da Story (👤). Fica laranja quando há troca pendente.
                var owner = EffOwner(story.Id, story.AssignedTo);
                var ownerDirty = _ownerPending.ContainsKey(story.Id);
                sp.Children.Add(new TextBlock {
                    Text = "👤 " + (string.IsNullOrWhiteSpace(owner) ? AppStrings.Get("Sprint_NoOwner") : owner),
                    FontSize = 10, TextWrapping = TextWrapping.Wrap,
                    Foreground = ownerDirty ? new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)) : Brushes.DimGray });
                // HH Estimado (e Realizado quando encerrada).
                if (BuildHoursLine(story.Id, story.EstimateHours, story.CompletedHours, EffStoryState(story)) is { } hhStory)
                    sp.Children.Add(hhStory);
                // Sprint da Story (🗓). Laranja quando há troca pendente.
                var iterLeaf = IterLeaf(EffIter(story.Id, story.IterationPath));
                if (story.OutOfSprint && OutOfSprintLine(story.IterationPath) is { } stOutLine)
                    sp.Children.Add(stOutLine);
                else if (!string.IsNullOrEmpty(iterLeaf))
                    sp.Children.Add(new TextBlock { Text = "🗓 " + iterLeaf, FontSize = 10, TextWrapping = TextWrapping.Wrap,
                        Foreground = _iterPending.ContainsKey(story.Id) ? new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)) : Brushes.DimGray });
                // StoryBoard = nível Story: aqui NÃO cria Task (isso é na visão Pessoa & Task).
                var actions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0), Cursor = System.Windows.Input.Cursors.Arrow };
                AddEditButtons(actions, story.Id, story.Title, story.AssignedTo, "Story", story.IterationPath);
                var openStory = new Button { Content = "🔗", FontSize = 11, Padding = new Thickness(4, 0, 4, 0),
                    Margin = new Thickness(0, 0, 4, 0), ToolTip = AppStrings.Get("Sprint_OpenDevOps") };
                openStory.Click += (_, _) => OpenInDevOps(story.Id);
                actions.Children.Add(openStory);
                actions.Children.Add(BuildStoryTasksButton(story.Id, EffTitle(story.Id, story.Title)));
                // Excluir Story: só quando está em New e SEM Tasks (ou todas as Tasks em New).
                var tasksAllNew = story.Tasks.Count == 0 || story.Tasks.All(t => SameState(EffState(t), "New"));
                if (string.Equals(EffStoryState(story), "New", StringComparison.OrdinalIgnoreCase) && tasksAllNew)
                {
                    var delSt = new Button { Content = storyToDelete ? "↩" : "🗑", FontSize = 11, Padding = new Thickness(4, 0, 4, 0),
                        Margin = new Thickness(0, 0, 4, 0), Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x30, 0x30)),
                        ToolTip = AppStrings.Get(storyToDelete ? "Sprint_UndoDelete" : "Sprint_DeleteTask") };
                    delSt.Click += async (_, _) => await ToggleDeleteWithChildCheckAsync(story.Id);
                    actions.Children.Add(delSt);
                }
                // Bloquear/desbloquear a Story (tag "Blocked") — último botão, como na Task.
                actions.Children.Add(BuildBlockButton(story.Id, story.Tags, isStory: true));
                // 👁 por ultimo. Projeto & Story nao tem faixa de pessoa: vale para a Story inteira.
                if (BuildRevealClosedButton(story.Id, "") is { } revStory) actions.Children.Add(revStory);
                sp.Children.Add(actions);
            }
            else
                sp.Children.Add(new TextBlock { Text = AppStrings.Get("Sprint_New"), FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x10)) });
            border.Child = sp;

            // Arrastar a Story entre colunas muda o ESTADO da Story (só existentes, no modo edição).
            if (EditModeCheck.IsChecked == true && story.Id > 0)
            {
                border.Cursor = System.Windows.Input.Cursors.SizeAll;
                border.PreviewMouseMove += (s, ev) =>
                {
                    if (ev.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
                    if (IsInteractive(ev.OriginalSource as DependencyObject)) return;
                    DragDrop.DoDragDrop(border, story.Id, DragDropEffects.Move);
                };
            }
            return border;
        }

        // Arrasta a caixa da Story: na MESMA coluna reordena o rank (StackRank); em OUTRA muda o estado.
        private void OnStoryDrop(object sender, DragEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not string newState) return;
            if (!e.Data.GetDataPresent(typeof(int))) return;
            var id = (int)e.Data.GetData(typeof(int))!;
            var story = StoryById(id);
            if (story == null || id <= 0) return;
            var cell = (fe as Border)?.Child as StackPanel;

            if (SameState(EffStoryState(story), newState))
            {
                // Mesma coluna → reordena o rank pela posição solta (entre as Stories da coluna).
                if (cell != null) ReorderStoryRankInCell(cell, id, e.GetPosition(cell).Y);
            }
            else
            {
                var baseline = _storyStateApplied.TryGetValue(id, out var a) ? a : story.State;
                if (SameState(baseline, newState)) _storyStatePending.Remove(id);
                else _storyStatePending[id] = newState;
            }
            UpdatePendingButton();
            Render();
        }

        private void ReorderStoryRankInCell(StackPanel cell, int id, double y)
        {
            var others = cell.Children.OfType<Border>()
                .Where(b => b.Tag is int bid && bid != id && bid > 0)
                .Select(b => (Rank: StoryRankOf((int)b.Tag!),
                              Center: b.TranslatePoint(new Point(0, b.ActualHeight / 2), cell).Y))
                .OrderBy(x => x.Center).ToList();
            if (others.Count == 0) return;
            var insertAt = others.Count(x => x.Center < y);
            double newRank;
            if (insertAt == 0) newRank = others[0].Rank - 1;
            else if (insertAt >= others.Count) newRank = others[^1].Rank + 1;
            else newRank = (others[insertAt - 1].Rank + others[insertAt].Rank) / 2.0;
            _storyRank[id] = newRank;
            _storyRankPending.Add(id);
        }

        // Card NOVO (id temporario negativo) vem no TOPO do grupo dele: com muitos cards, nascer
        // no fim da lista era o mesmo que nascer invisivel.
        /// <summary>
        /// Grava o item RECEM-CRIADO no topo do grupo dele tambem no DevOps (StackRank abaixo do
        /// menor dos irmaos). Enquanto pendente o card ja aparece em cima; sem isto, ao gravar ele
        /// nascia sem rank, ia para o fim da lista e "pulava" da posicao onde estava — confuso.
        /// Se a gravacao do rank falhar, o card so perde a posicao: nao e erro de criacao.
        /// </summary>
        private async Task<double> RankNewOnTopAsync(int newId, IEnumerable<double> siblingRanks)
        {
            var ranks = siblingRanks.Where(r => !double.IsNaN(r) && !double.IsInfinity(r) && r < double.MaxValue).ToList();
            var top = (ranks.Count > 0 ? ranks.Min() : 0) - 10;
            try { await TfsImportService.SetWorkItemStackRankAsync(_options, newId, top); }
            catch { /* posicao e conveniencia: nunca derruba a criacao */ }
            return top;
        }

        private double StoryRankOf(int id) => id < 0 ? double.NegativeInfinity
            : _storyRank.TryGetValue(id, out var r) ? r : double.MaxValue;

        private double EffTaskRank(TfsImportService.SprintTaskCard t) =>
            t.Id < 0 ? double.NegativeInfinity
            : _taskRank.TryGetValue(t.Id, out var r) ? r
            : _order.TryGetValue(t.Id, out var o) ? o : t.Id;

        // Move a Story trocando o StackRank com a vizinha do grupo (▲/▼). Só reordena dentro do grupo.
        private void MoveStoryRank(List<int> orderedIds, int id, int dir)
        {
            var idx = orderedIds.IndexOf(id);
            var nb = idx + dir;
            if (idx < 0 || nb < 0 || nb >= orderedIds.Count) return;
            var other = orderedIds[nb];
            var a = StoryRankOf(id);
            var b = StoryRankOf(other);
            _storyRank[id] = b;
            _storyRank[other] = a;
            _storyRankPending.Add(id);
            _storyRankPending.Add(other);
            UpdatePendingButton();
            Render();
        }

        private static Grid MakeRowGrid(List<GridLength> cols)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            foreach (var c in cols) g.ColumnDefinitions.Add(new ColumnDefinition { Width = c });
            return g;
        }
        private static void AddCell(Grid g, int col, UIElement el) { Grid.SetColumn(el, col); g.Children.Add(el); }

        /// <summary>Largura da coluna "#" (numero sequencial da linha), a 1a de cada visao.</summary>
        private const double RowNumberWidth = 30;

        /// <summary>Numero da linha: ancora visual para nao se perder ao rolar o board.</summary>
        private static TextBlock MakeRowNumber(int n) => new()
        {
            Text = n.ToString(), FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xAD)),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 6, 0)
        };
        private static TextBlock MakeHeader(string text) => new()
        { Text = text, FontWeight = FontWeights.Bold, FontSize = 12, Margin = new Thickness(4, 2, 4, 2) };
        private Border MakeStateHeader(string state) => new()
        {
            Background = StateBrush(state), CornerRadius = new CornerRadius(3),
            Margin = new Thickness(3, 0, 3, 0), Padding = new Thickness(6, 2, 6, 2),
            Child = new TextBlock { Text = state, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 11 }
        };

        // Faixa acima das colunas de estado dizendo QUAL objeto são os cards daquelas colunas
        // (Story na visão "Por Story", Task na visão "Pessoa & Task").
        private void AddCardsBandRow(List<GridLength> cols, int firstStateCol, string text)
        {
            var band = MakeRowGrid(cols);
            var label = new TextBlock
            {
                Text = text, FontSize = 10, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x6A, 0x76)),
                Margin = new Thickness(4, 2, 4, 2)
            };
            Grid.SetColumnSpan(label, cols.Count - firstStateCol);
            AddCell(band, firstStateCol, label);
            HeaderHost.Children.Add(band);
        }

        /// <summary>Linha de HH do card: Estimado sempre; Realizado só quando o item está
        /// encerrado. Considera as edições pendentes (fila do "Atualizar TFS").</summary>
        private TextBlock? BuildHoursLine(int id, double? estimate, double? completed, string effState)
        {
            var est = _estPending.TryGetValue(id, out var ep) ? ep : estimate;
            var done = _donePending.TryGetValue(id, out var dp) ? dp : completed;
            var dirty = _estPending.ContainsKey(id) || _donePending.ContainsKey(id);
            var closed = IsClosedState(effState);
            var txt = est.HasValue ? AppStrings.Get("Sprint_HHEst", est.Value.ToString("0.##")) : "";
            if (closed && done.HasValue)
                txt += (txt.Length > 0 ? "  ·  " : "") + AppStrings.Get("Sprint_HHDone", done.Value.ToString("0.##"));
            if (txt.Length == 0) return null;
            return new TextBlock
            {
                Text = txt, FontSize = 10,
                Foreground = dirty ? new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)) : Brushes.DimGray
            };
        }

        /// <summary>Nome da tag de "não planejada" (configurável; padrão "NP").</summary>
        private string UnplannedTag() =>
            string.IsNullOrWhiteSpace(_options.UnplannedTagName) ? "NP" : _options.UnplannedTagName.Trim();

        /// <summary>Nome da tag de "acima do WIP" (configurável; padrão "WIP").</summary>
        private string WipTag() =>
            string.IsNullOrWhiteSpace(_options.WipTagName) ? "WIP" : _options.WipTagName.Trim();

        /// <summary>Nome da tag de bloqueio no DevOps (configurável; padrão "BLOCK").</summary>
        private string BlockedTag() =>
            string.IsNullOrWhiteSpace(_options.BlockedTagName) ? "BLOCK" : _options.BlockedTagName.Trim();

        /// <summary>Limite de Tasks em andamento por pessoa. 0 = sem limite; padrão 3.</summary>
        private int WipLimit() => _prefs.WipLimit is int n && n >= 0 ? n : 3;

        /// <summary>Estado que conta como "em andamento" para o WIP: tudo que já saiu de New e
        /// ainda não foi encerrado (Active e quaisquer estados intermediários do processo).</summary>
        private static bool IsWipState(string s) =>
            !IsClosedState(s) && !SameState(s, "New") && !SameState(s, "Removed");

        /// <summary>Recalcula o WIP por PESSOA (não por projeto: o limite protege a capacidade de
        /// quem executa, e uma pessoa costuma atender vários projetos). Marca como "acima do WIP"
        /// as Tasks que passam do limite, olhando da menor para a maior prioridade — as últimas a
        /// entrar na fila da pessoa são as que estouram.</summary>
        private void RecomputeWip(IEnumerable<TfsImportService.SprintTaskCard> allTasks)
        {
            _wipOver.Clear();
            _wipCount.Clear();
            var limit = WipLimit();
            foreach (var g in allTasks
                         .Where(t => IsWipState(EffState(t)))
                         .GroupBy(t => (EffOwner(t.Id, t.AssignedTo) ?? "").Trim(),
                                  StringComparer.CurrentCultureIgnoreCase))
            {
                if (g.Key.Length == 0) continue;   // sem responsável não há WIP de pessoa
                _wipCount[g.Key] = g.Count();
                if (limit <= 0) continue;
                foreach (var t in g.OrderBy(t => EffPrio(t) > 0 ? EffPrio(t) : 99)
                                   .ThenBy(EffTaskRank)
                                   .Skip(limit))
                    _wipOver.Add(t.Id);
            }
        }

        /// <summary>Quantidade de Tasks em andamento da pessoa (para o selo "n/limite").</summary>
        private int WipCountOf(string person) =>
            _wipCount.TryGetValue((person ?? "").Trim(), out var n) ? n : 0;

        private async Task UploadAttachmentForCardAsync(int taskId)
        {
            if (taskId <= 0)
            {
                MessageBox.Show(this, "Só é possível anexar arquivos em Tasks já enviadas ao Azure DevOps.", "Anexo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new OpenFileDialog
            {
                Title = "Selecionar arquivo(s) para anexar à Task",
                Filter = "Todos os arquivos|*.*",
                Multiselect = true
            };

            if (dlg.ShowDialog(this) != true)
                return;

            // Um arquivo por vez: se um falhar, os outros seguem, e no fim a mensagem diz
            // exatamente quais entraram e quais nao.
            var sentNames = new List<string>();
            var failed = new List<string>();
            if (!_attachments.TryGetValue(taskId, out var list))
                _attachments[taskId] = list = new List<TfsAttachmentService.TfsAttachmentInfo>();
            foreach (var file in dlg.FileNames)
            {
                try
                {
                    StatusText.Text = $"Enviando anexo {sentNames.Count + failed.Count + 1} de {dlg.FileNames.Length}: {System.IO.Path.GetFileName(file)}…";
                    var info = await TfsAttachmentService.UploadAttachmentAsync(_options, taskId, file);
                    list.Add(info);
                    sentNames.Add(info.Name);
                }
                catch (Exception ex)
                {
                    failed.Add($"{System.IO.Path.GetFileName(file)}: {ex.Message}");
                }
            }
            StatusText.Text = sentNames.Count == 0 ? "" :
                sentNames.Count == 1 ? $"Anexo enviado: {sentNames[0]}"
                                     : $"{sentNames.Count} anexos enviados: {string.Join(", ", sentNames)}";
            Render();
            if (failed.Count > 0)
                MessageBox.Show(this,
                    $"Falha ao anexar {failed.Count} arquivo(s) no Azure DevOps:\n\n{string.Join("\n", failed)}",
                    "Anexo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>
        /// Tipos que abrem direto apos o download. Sao documentos/imagens que o Windows abre no
        /// visualizador padrao sem executar nada. Executaveis, scripts e extensoes desconhecidas
        /// ficam de fora: para esses o anexo so e salvo onde o usuario escolher.
        /// </summary>
        private static readonly HashSet<string> SafeOpenExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".txt", ".csv",
            ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx"
        };

        /// <summary>Anexos da Task: os da carga do DevOps + o enviado agora pelo 📎 (sem repetir).</summary>
        private List<TfsAttachmentService.TfsAttachmentInfo> AttachmentsOf(TfsImportService.SprintTaskCard t)
        {
            // Imagem colada na descricao/tramite tambem vira anexo no DevOps, mas nao e "arquivo
            // anexado": fica fora da lista do card para nao poluir (e para ninguem excluir sem
            // querer a imagem que o texto usa).
            var list = t.Attachments
                .Where(a => !string.IsNullOrWhiteSpace(a.Name)
                            && !TfsAttachmentService.IsInlineImageAttachment(a.Name)
                            && !_removedAttachmentUrls.Contains(a.Url)).ToList();
            if (_attachments.TryGetValue(t.Id, out var justSent))
                foreach (var sent in justSent)
                    if (!_removedAttachmentUrls.Contains(sent.Url)
                        && !list.Any(a => string.Equals(a.Url, sent.Url, StringComparison.OrdinalIgnoreCase)))
                        list.Add(sent);
            return list;
        }

        /// <summary>
        /// Marca o anexo para exclusao. Diferente do ENVIO, que vai direto, a exclusao so acontece
        /// no "Atualizar TFS" e ate la o "Reverter" desfaz. Devolve true para a tela que chamou
        /// tirar o item da lista.
        /// </summary>
        private Task<bool> RemoveAttachmentAsync(int taskId, TfsAttachmentService.TfsAttachmentInfo attachment)
        {
            var question = "Excluir o anexo \u201C" + attachment.Name + "\u201D da Task #" + taskId + "?"
                + Environment.NewLine + Environment.NewLine
                + "O anexo sai do card agora, mas s\u00F3 \u00E9 exclu\u00EDdo no DevOps quando voc\u00EA clicar em "
                + "\u201CAtualizar TFS\u201D \u2014 at\u00E9 l\u00E1 o \u201CReverter\u201D desfaz. "
                + "Depois de gravado, para recuperar o arquivo precisa ser anexado de novo.";
            // Padrao "Nao": Enter por engano nao exclui.
            if (MessageBox.Show(this, question, "Excluir anexo", MessageBoxButton.YesNo,
                    MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return Task.FromResult(false);

            if (!_attachRemovePending.TryGetValue(taskId, out var list))
                _attachRemovePending[taskId] = list = new List<TfsAttachmentService.TfsAttachmentInfo>();
            if (!list.Any(a => string.Equals(a.Url, attachment.Url, StringComparison.OrdinalIgnoreCase)))
                list.Add(attachment);
            _removedAttachmentUrls.Add(attachment.Url);
            StatusText.Text = "Anexo marcado para excluir: " + attachment.Name;
            UpdatePendingButton();
            Render();
            return Task.FromResult(true);
        }

        /// <summary>Ate este numero os anexos aparecem no card; acima, so um resumo (lista na edicao).</summary>
        // Um anexo so no card: o resto fica na edicao, atras do link "+N". Card enxuto.
        private const int MaxAttachmentsOnCard = 1;

        /// <summary>Extensoes de "documento" quando a lista configurada esta vazia.</summary>
        private static readonly string[] DefaultCardDocExtensions = { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".png" };

        /// <summary>
        /// Anexo de documento: o que vem na frente no card. Configuravel no ⚙ como lista separada
        /// por virgula ("pdf, docx, xlsx" — com ou sem ponto).
        /// </summary>
        private HashSet<string> _cardDocExtensions = new(DefaultCardDocExtensions, StringComparer.OrdinalIgnoreCase);

        private void ApplyCardDocExtensions(string? text)
        {
            var list = (text ?? "")
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().TrimStart('*').TrimStart('.'))
                .Where(x => x.Length > 0)
                .Select(x => "." + x.ToLowerInvariant())
                .Distinct()
                .ToList();
            // Lista vazia (ou so lixo) volta ao padrao — "documento nenhum" nao e uma escolha util.
            _cardDocExtensions = new HashSet<string>(list.Count > 0 ? list : DefaultCardDocExtensions,
                StringComparer.OrdinalIgnoreCase);
        }

        private static string FormatExtensions(IEnumerable<string> exts) =>
            string.Join(", ", exts.Select(x => x.TrimStart('.')));

        /// <summary>Anexos que o CARD lista pelo nome. Com a opcao ligada (padrao), so documento —
        /// o resto vira contador, e a lista inteira continua na edicao.</summary>
        /// <summary>
        /// Ordem dos anexos para o card, que mostra so o PRIMEIRO. Com a opcao "so documentos"
        /// ligada, PDF/Word/Excel vem na frente — mas nada e escondido: sem documento, o card mostra
        /// o primeiro anexo que houver (uma imagem, por exemplo), em vez de ficar sem nome nenhum.
        /// </summary>
        private List<TfsAttachmentService.TfsAttachmentInfo> CardListed(
            List<TfsAttachmentService.TfsAttachmentInfo> all)
        {
            if (CardDocsOnlyCheck?.IsChecked != true) return all;
            return all.OrderBy(a => _cardDocExtensions.Contains(System.IO.Path.GetExtension(a.Name)) ? 0 : 1)
                      .ToList();
        }

        private async Task OpenAttachmentAsync(TfsAttachmentService.TfsAttachmentInfo attachment)
        {
            var ext = System.IO.Path.GetExtension(attachment.Name);
            var openDirect = SafeOpenExtensions.Contains(ext);
            string destination;
            if (openDirect)
            {
                // Pasta temporaria por anexo: dois anexos com o mesmo nome nao se sobrescrevem.
                var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NXProject", "attachments",
                    string.IsNullOrWhiteSpace(attachment.Id) ? Guid.NewGuid().ToString("N") : attachment.Id);
                destination = System.IO.Path.Combine(dir, attachment.Name);
            }
            else
            {
                var save = new SaveFileDialog
                {
                    Title = "Salvar anexo do Azure DevOps",
                    FileName = attachment.Name,
                    Filter = "Todos os arquivos|*.*"
                };
                if (save.ShowDialog(this) != true) return;
                destination = save.FileName;
            }

            try
            {
                StatusText.Text = $"Baixando anexo: {attachment.Name}...";
                await TfsAttachmentService.DownloadAttachmentAsync(_options, attachment.Url, attachment.Name, destination);
                if (openDirect)
                {
                    Process.Start(new ProcessStartInfo(destination) { UseShellExecute = true });
                    StatusText.Text = $"Anexo aberto: {attachment.Name}";
                }
                else
                    StatusText.Text = $"Anexo salvo em: {destination}";
            }
            catch (Exception ex)
            {
                StatusText.Text = "";
                MessageBox.Show(this, "Falha ao baixar o anexo do Azure DevOps:" + Environment.NewLine + Environment.NewLine + ex.Message, "Anexo",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private Border BuildCard(TfsImportService.SprintTaskCard t, string? storyTitle = null)
        {
            if (t.Id < 0 && _newCards.FirstOrDefault(n => n.TempId == t.Id) is { } nct)
                return BuildNewCardBorder(nct);
            var inSched = _scheduleIds.Contains(t.Id);
            var isPending = _pending.ContainsKey(t.Id) || _taskRankPending.Contains(t.Id);
            var isNew = t.Id < 0; // card novo (sem ID do TFS) → destaque verde até salvar
            var toDelete = _deletePending.Contains(t.Id); // marcada p/ excluir no Salvar TFS
            var border = new Border
            {
                Background = toDelete ? new SolidColorBrush(Color.FromRgb(0xFB, 0xE3, 0xE3))
                    : isNew ? new SolidColorBrush(Color.FromRgb(0xE7, 0xF6, 0xE7))
                    : isPending ? new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xD6)) : StateTintBrush(EffState(t)),
                BorderBrush = new SolidColorBrush(toDelete ? Color.FromRgb(0xC0, 0x30, 0x30)
                    : isNew ? Color.FromRgb(0x10, 0x7C, 0x10)
                    : isPending ? Color.FromRgb(0xE0, 0x8A, 0x00)
                    : inSched ? Color.FromRgb(0x2B, 0x57, 0x9A) : Color.FromRgb(0xCF, 0xD8, 0xE3)),
                BorderThickness = new Thickness(toDelete || isNew || isPending || inSched ? 2 : 1),
                CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(6),
                Tag = t.Id
            };
            // Bloqueada: apenas borda vermelha (sem chip escuro), exceto se marcada p/ excluir.
            var blocked = !isNew && EffBlocked(t.Id, t.Tags);
            if (blocked && !toDelete)
            {
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x30, 0x30));
                border.BorderThickness = new Thickness(2);
            }
            var sp = new StackPanel();
            var titleLine = new DockPanel { LastChildFill = true };
            void DockLeft(UIElement el) { DockPanel.SetDock(el, Dock.Left); titleLine.Children.Add(el); }
            if (isNew)
                DockLeft(new TextBlock { Text = "🆕 ", VerticalAlignment = VerticalAlignment.Center });
            // Task NÃO PLANEJADA: tag configurável no DevOps (padrão "NP"). Selo em destaque.
            var npTag = UnplannedTag();
            if (EffUnplanned(t.Id, t.Tags))
                DockLeft(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xE1, 0x8A)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0x7A, 0x00)),
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = AppStrings.Get("Sprint_UnplannedTip"),
                    Child = new TextBlock
                    {
                        Text = npTag, FontSize = 10, FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x52, 0x00))
                    }
                });
            // Acima do limite de WIP da pessoa: só alerta (o arrasto para Active continua livre).
            // A tag vai para o DevOps na gravação.
            if (!isNew && _wipOver.Contains(t.Id))
                DockLeft(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0xD5)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x30, 0x30)),
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = AppStrings.Get("Sprint_WipOverTip", WipLimit().ToString()),
                    Child = new TextBlock
                    {
                        Text = "⚠ " + WipTag(), FontSize = 10, FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x8C, 0x1C, 0x1C))
                    }
                });
            // Prioridade EDITÁVEL via ComboBox (picklist P{min}..P{max}, igual ao TFS). A mudança
            // entra na fila do Salvar TFS. (Só para Task existente.) Ela é montada aqui, mas
            // entra na LINHA DE BOTÕES do rodapé — assim o nome da Task fica com a largura
            // inteira do card.
            ComboBox? prioCombo = null;
            if (!isNew)
            {
                var eff = EffPrio(t);
                var (pmin, pmax) = PriorityRange();
                var prioPend = _prioPending.ContainsKey(t.Id);
                var combo = new ComboBox
                {
                    Width = 40, FontSize = 10, FontWeight = FontWeights.Bold, Height = 20,
                    Padding = new Thickness(2, 0, 0, 0),
                    Cursor = System.Windows.Input.Cursors.Arrow,
                    Margin = new Thickness(0, 0, 3, 2), VerticalAlignment = VerticalAlignment.Center,
                    Background = eff > 0 ? PriorityBrush(eff) : new SolidColorBrush(Color.FromRgb(0xB0, 0xB8, 0xC0)),
                    BorderBrush = prioPend ? new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)) : Brushes.Gray,
                    BorderThickness = new Thickness(prioPend ? 2 : 1),
                    ToolTip = AppStrings.Get("Sprint_PriorityTip")
                };
                for (int v = pmin; v <= pmax; v++)
                    combo.Items.Add(new ComboBoxItem { Content = "P" + v, Tag = v });
                combo.SelectedIndex = eff >= pmin && eff <= pmax ? eff - pmin : -1;
                // handler só depois de definir o índice inicial (evita disparar na montagem)
                combo.SelectionChanged += (s, _) =>
                {
                    if (combo.SelectedItem is not ComboBoxItem ci || ci.Tag is not int vv) return;
                    if (vv == t.Priority) _prioPending.Remove(t.Id); else _prioPending[t.Id] = vv;
                    UpdatePendingButton();
                    Render();
                };
                prioCombo = combo;
            }
            if (isPending)
                DockLeft(new TextBlock { Text = "● ", Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)),
                    FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = AppStrings.Get("Sprint_UpdateTfs") });
            var fullTitle = EffTitle(t.Id, t.Title);
            var titleTb = new TextBlock
            {
                Text = fullTitle, TextWrapping = TextWrapping.Wrap, FontSize = 12, FontWeight = FontWeights.SemiBold,
                // Ate 3 linhas; passando disso corta com reticencias e o hint mostra o nome inteiro.
                MaxHeight = 50, TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = fullTitle,
                Foreground = toDelete ? new SolidColorBrush(Color.FromRgb(0xC0, 0x30, 0x30))
                    : _titlePending.ContainsKey(t.Id) ? new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)) : Brushes.Black
            };
            if (toDelete) titleTb.TextDecorations = TextDecorations.Strikethrough; // marcada p/ excluir
            titleLine.Children.Add(titleTb);
            sp.Children.Add(titleLine);
            // Só o id no corpo: na visao Pessoa & Task o responsavel ja e a faixa, e na visao
            // Projeto & Story ele cabe no hint — o card fica mais curto e o board mais legivel.
            var line = new TextBlock { FontSize = 10, Foreground = Brushes.Gray };
            line.Text = isNew ? AppStrings.Get("Sprint_New") : $"#{t.Id}";
            sp.Children.Add(line);

            // Anexos da Task: os que vieram do DevOps na carga + o enviado agora pelo 📎 (que so
            // chega na lista do DevOps no proximo reload). Sem repetir o mesmo arquivo.
            var allAttachments = AttachmentsOf(t);
            var cardAttachments = CardListed(allAttachments);
            // Card mostra UM anexo (o primeiro listavel). Todo o resto — outros documentos e o que
            // nao se lista no card, como imagem ou zip — vira um "+N", que abre a edicao com a
            // lista completa.
            cardAttachments = cardAttachments.Take(MaxAttachmentsOnCard).ToList();
            var moreCount = allAttachments.Count - cardAttachments.Count;
            foreach (var attachment in cardAttachments)
            {
                // Nome clicavel: baixa do DevOps e abre (tipos seguros) ou so salva (o resto).
                var attachLink = new TextBlock
                {
                    Text = $"📎 {attachment.Name}",
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    TextDecorations = TextDecorations.Underline,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x4E, 0x79)),
                    Margin = new Thickness(0, 2, 0, 0),
                    ToolTip = "Clique para baixar do Azure DevOps"
                };
                attachLink.MouseLeftButtonUp += async (_, ev) =>
                {
                    ev.Handled = true;   // nao deixa o clique virar arrasto/selecao do card
                    await OpenAttachmentAsync(attachment);
                };
                var attMenu = new ContextMenu();
                var attOpen = new MenuItem { Header = "Abrir / baixar" };
                attOpen.Click += async (_, _) => await OpenAttachmentAsync(attachment);
                var attDel = new MenuItem { Header = "Excluir anexo do TFS" };
                attDel.Click += async (_, _) => await RemoveAttachmentAsync(t.Id, attachment);
                attMenu.Items.Add(attOpen);
                attMenu.Items.Add(attDel);
                attachLink.ContextMenu = attMenu;
                attachLink.ToolTip = "Clique para baixar do Azure DevOps \u00B7 bot\u00E3o direito para excluir";
                sp.Children.Add(attachLink);
            }
            if (moreCount > 0)
            {
                // Logo abaixo do anexo mostrado: "+N" com os nomes no hint, e o clique abre a edicao.
                var more = new TextBlock
                {
                    Text = cardAttachments.Count == 0
                        ? $"📎 {moreCount} anexo(s) — ver na edição"
                        : $"📎 +{moreCount} — ver todos na edição",
                    FontSize = 10, TextDecorations = TextDecorations.Underline,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x4E, 0x79)),
                    Margin = new Thickness(0, 2, 0, 0),
                    ToolTip = string.Join(Environment.NewLine, allAttachments.Select(a => a.Name))
                };
                more.MouseLeftButtonUp += async (_, ev) =>
                {
                    ev.Handled = true;
                    await EditDescriptionAsync(t.Id, t.Title, t.AssignedTo ?? "", "Task", t.IterationPath);
                };
                sp.Children.Add(more);
            }

            // HH Estimado (e Realizado quando encerrada).
            if (!isNew && BuildHoursLine(t.Id, t.EstimateHours, t.CompletedHours, EffState(t)) is { } hhTask)
                sp.Children.Add(hhTask);
            // Data alvo editavel direto no card enquanto a Task esta Active: e quando ela e
            // acompanhada de perto. A mudanca vai para a mesma fila do editor (Data_Fim).
            if (!isNew && TfsImportService.NormalizeTaskState(EffState(t)) == "Active")
            {
                var finishPend = _finishPending.TryGetValue(t.Id, out var fpv);
                var finishBase = _finishApplied.TryGetValue(t.Id, out var fav) ? fav : t.FinishDate;
                // Vencida: a data alvo ja passou e a Task continua Active.
                var effFinish = finishPend ? fpv : finishBase;
                var overdue = effFinish is DateTime ef && ef.Date < DateTime.Today;
                var red = new SolidColorBrush(Color.FromRgb(0xC0, 0x30, 0x30));
                var targetRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0),
                    Cursor = System.Windows.Input.Cursors.Arrow };
                targetRow.Children.Add(new TextBlock
                {
                    Text = AppStrings.Get("Desc_TargetDate"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 4, 0),
                    Foreground = overdue ? red
                        : finishPend ? new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)) : Brushes.DimGray,
                    FontWeight = overdue ? FontWeights.SemiBold : FontWeights.Normal
                });
                var targetPicker = new DatePicker
                {
                    SelectedDate = effFinish, FontSize = 10, Width = 108,
                    VerticalAlignment = VerticalAlignment.Center,
                    BorderBrush = overdue ? red
                        : finishPend ? new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)) : null,
                    Foreground = overdue ? red : Brushes.Black,
                    ToolTip = overdue ? AppStrings.Get("Sprint_TargetOverdue") : null
                };
                targetPicker.SelectedDateChanged += (_, _) =>
                {
                    var picked = targetPicker.SelectedDate?.Date;
                    // Voltou ao valor que esta no DevOps (inclusive o gravado agora): nada a fazer.
                    if (picked == finishBase?.Date) _finishPending.Remove(t.Id);
                    else _finishPending[t.Id] = picked;
                    _finishAutoByActive.Remove(t.Id); _finishAutoByClosed.Remove(t.Id);
                    UpdatePendingButton();
                };
                // Sem fundo branco: a data fica na cor do card. O DatePicker pinta o fundo no
                // DatePickerTextBox interno, entao o transparente precisa ir no estilo dele tambem.
                targetPicker.Background = Brushes.Transparent;
                var tbStyle = new Style(typeof(System.Windows.Controls.Primitives.DatePickerTextBox));
                tbStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
                targetPicker.Resources.Add(typeof(System.Windows.Controls.Primitives.DatePickerTextBox), tbStyle);
                targetRow.Children.Add(targetPicker);
                sp.Children.Add(targetRow);
            }
            // Sprint da Task: mostra quando há mais de uma sprint no board (várias/"Todas").
            var tIter = IterLeaf(EffIter(t.Id, t.IterationPath));
            if (_sprintPaths.Count != 1 && !string.IsNullOrEmpty(tIter))
                sp.Children.Add(new TextBlock { Text = "🗓 " + tIter, FontSize = 10, TextWrapping = TextWrapping.Wrap,
                    Foreground = _iterPending.ContainsKey(t.Id) ? new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)) : Brushes.DimGray });
            if (!string.IsNullOrWhiteSpace(storyTitle))
                sp.Children.Add(new TextBlock
                {
                    Text = "📖 " + storyTitle, FontSize = 10, TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A)), Margin = new Thickness(0, 2, 0, 0)
                });

            if (isNew)
            {
                // Card novo: sem ID/ações do TFS ainda; será criado no Salvar TFS.
                border.Child = sp;
                return border;
            }

            // Ao mover para Closed (arrasto pendente), aparece um campo HH Realizado direto no card,
            // para não precisar abrir o editor. O valor entra na fila (CompletedWork).
            if ((_pending.TryGetValue(t.Id, out var pendState) && IsClosedState(pendState))
                || _closedDropped.Contains(t.Id))
            {
                var hhRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0),
                    Cursor = System.Windows.Input.Cursors.Arrow };
                hhRow.Children.Add(new TextBlock { Text = AppStrings.Get("Desc_DoneHours"), FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x30, 0x30)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
                var hhBox = new TextBox { Width = 56, FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                    // Sem valor na fila, mostra o que ja esta no DevOps (pode ser > 0): o usuario
                    // edita em cima do numero atual em vez de digitar tudo de novo.
                    Text = _donePending.TryGetValue(t.Id, out var dpv)
                        ? (dpv.HasValue ? dpv.Value.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture) : "")
                        : (t.CompletedHours is > 0 ? t.CompletedHours.Value.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture) : "") };
                hhBox.TextChanged += (_, _) =>
                {
                    var txt = (hhBox.Text ?? "").Trim().Replace(',', '.');
                    if (string.IsNullOrEmpty(txt)) _donePending.Remove(t.Id);
                    else if (double.TryParse(txt, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0)
                        _donePending[t.Id] = v;
                    UpdatePendingButton();
                };
                hhRow.Children.Add(hhBox);
                sp.Children.Add(hhRow);
            }

            // Marca "Doing" (o que está atuando agora): chip azul; se o card estiver Closed vira "Done".
            var isDoing = _doing.Contains(t.Id);
            var closed = IsClosedState(EffState(t));
            // "Done" vale quando marcado no card OU quando a Task está encerrada.
            var isDone = _done.Contains(t.Id) || closed;
            if (isDoing)
            {
                border.BorderBrush = new SolidColorBrush(isDone ? Color.FromRgb(0x10, 0x7C, 0x10) : Color.FromRgb(0x00, 0x78, 0xD4));
                border.BorderThickness = new Thickness(2);
                var chip = new Border
                {
                    Background = new SolidColorBrush(isDone ? Color.FromRgb(0x10, 0x7C, 0x10) : Color.FromRgb(0x00, 0x78, 0xD4)),
                    CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 2, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new TextBlock { Text = "🔵 " + AppStrings.Get(isDone ? "Sprint_Done" : "Sprint_Doing"),
                        Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.SemiBold }
                };
                sp.Children.Add(chip);
            }

            var actions = new WrapPanel { Margin = new Thickness(0, 4, 0, 0), Cursor = System.Windows.Input.Cursors.Arrow };
            if (prioCombo != null) actions.Children.Add(prioCombo);
            var open = new Button { Content = "🔗", FontSize = 11, Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(0, 0, 3, 2), ToolTip = AppStrings.Get("Sprint_OpenDevOps") };
            open.Click += (_, _) => OpenInDevOps(t.Id);
            actions.Children.Add(open);
            // Botão Doing: marcar só faz sentido no que já começou — em New não há andamento,
            // e em Closed o trabalho terminou. Tirar continua liberado em qualquer estado.
            var isNewState = SameState(EffState(t), "New");
            if (isDoing || (!closed && !isNewState))
            {
                // Só o sinal (+/-) fica maior; o texto "Doing" mantém a fonte padrão.
                // Remover a marcação: o rótulo acompanha o que o card exibe — encerrada mostra
                // "Done", então o botão precisa dizer "-Done" e não "-Doing".
                var doingLabel = !isDoing ? AppStrings.Get("Sprint_MarkDoing")
                    : isDone ? AppStrings.Get("Sprint_RemoveDone")
                    : AppStrings.Get("Sprint_RemoveDoing");
                // So o icone: com o texto ("+Doing"/"-Doing") a linha de botoes nao cabia na
                // largura da coluna e quebrava em duas. O rotulo completo fica no hint.
                var doingBtn = new Button
                {
                    Content = new TextBlock { Text = isDoing ? "⏹" : "▶", FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center },
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(0, 0, 3, 2),
                    ToolTip = doingLabel
                };
                doingBtn.Click += (_, _) =>
                {
                    if (_doing.Contains(t.Id)) _doing.Remove(t.Id); else _doing.Add(t.Id);
                    UpdatePendingButton();
                    Render();
                };
                actions.Children.Add(doingBtn);
            }
            // Concluir o andamento SEM fechar a Task: card marcado Doing e ainda aberto.
            // Encerrada já vira Done sozinha, então o botão não aparece nesse caso.
            if (isDoing && !closed)
            {
                var markedDone = _done.Contains(t.Id);
                // Alterna entre Doing e Done (o "-Done" de remover fica no botão ao lado).
                var doneLabel = markedDone ? AppStrings.Get("Sprint_BackToDoing") : AppStrings.Get("Sprint_MarkDone");
                var doneBtn = new Button
                {
                    Content = new TextBlock { Text = markedDone ? "↺" : "✔", FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center },
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(0, 0, 3, 2),
                    ToolTip = doneLabel + " - " + AppStrings.Get(markedDone ? "Sprint_BackToDoingTip" : "Sprint_MarkDoneTip")
                };
                doneBtn.Click += (_, _) =>
                {
                    if (!_done.Remove(t.Id)) _done.Add(t.Id);
                    UpdatePendingButton();
                    Render();
                };
                actions.Children.Add(doneBtn);
            }
            if (inSched && _openInSchedule != null)
            {
                var sched = new Button { Content = "📅", FontSize = 11, Padding = new Thickness(4, 0, 4, 0),
                    Margin = new Thickness(0, 0, 3, 2), ToolTip = AppStrings.Get("Query_OpenInSchedule") };
                sched.Click += (_, _) => _openInSchedule!(t.Id);
                actions.Children.Add(sched);
            }

            var attachBtn = new Button
            {
                Content = "📎",
                FontSize = 11,
                Padding = new Thickness(4, 0, 4, 0),
                Margin = new Thickness(0, 0, 3, 2),
                ToolTip = "Anexar arquivo no Azure DevOps"
            };
            attachBtn.Click += async (_, _) => await UploadAttachmentForCardAsync(t.Id);
            actions.Children.Add(attachBtn);

            // Bloquear/desbloquear (tag "Blocked"): entra na fila do Salvar TFS.
            AddEditButtons(actions, t.Id, t.Title, t.AssignedTo, "Task"); // ✎ descrição e 💬 trâmite da Task
            // Bloquear/desbloquear é o ÚLTIMO botão do card (tag "Blocked").
            actions.Children.Add(BuildBlockButton(t.Id, t.Tags, isStory: false));
            // Atalho da Auditoria de BLOCK: so aparece quando o campo de duracao diz que ESTA Task
            // ja foi impedida (> 0). Sem o campo configurado, a auditoria fica so no botao direito.
            if (BuildBlockAuditButton(t) is { } auditBtn) actions.Children.Add(auditBtn);
            // Excluir: só Tasks reais no estado New (evita apagar itens já em andamento/encerrados).
            // Marca para excluir (pendente); a exclusão no DevOps ocorre no Salvar TFS.
            if (t.Id > 0 && string.Equals(EffState(t), "New", StringComparison.OrdinalIgnoreCase))
            {
                var marked = _deletePending.Contains(t.Id);
                var del = new Button { Content = marked ? "↩" : "🗑", FontSize = 11, Padding = new Thickness(4, 0, 4, 0),
                    Margin = new Thickness(4, 0, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x30, 0x30)),
                    ToolTip = AppStrings.Get(marked ? "Sprint_UndoDelete" : "Sprint_DeleteTask") };
                del.Click += (_, _) =>
                {
                    if (!_deletePending.Remove(t.Id)) _deletePending.Add(t.Id);
                    UpdatePendingButton(); Render();
                };
                actions.Children.Add(del);
            }
            sp.Children.Add(actions);
            border.Child = sp;

            // Datas (criacao / estado) ficam SO no hint: no corpo do card elas somavam duas
            // linhas em cada card e atrapalhavam a leitura do board.
            if (!isNew)
            {
                if (!string.IsNullOrWhiteSpace(t.AssignedTo))
                    AppendTip(border, "👤 " + t.AssignedTo);
                AppendTip(border, TaskDatesText(t));
            }
            // Mover de Story: marcada (aguardando destino) ou ja com destino na fila.
            AttachTaskMoveMenu(border, t);
            if (_moveTaskIds.IndexOf(t.Id) is var movePos and >= 0)
            {
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(0x7A, 0x3D, 0xB8));
                border.BorderThickness = new Thickness(2);
                sp.Children.Insert(0, new TextBlock
                {
                    Text = AppStrings.Get("Sprint_MoveMarked", (movePos + 1).ToString(), _moveTaskIds.Count.ToString()),
                    FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x3D, 0xB8)), TextWrapping = TextWrapping.Wrap
                });
            }
            else if (_taskParentPending.ContainsKey(t.Id))
            {
                // O card ja aparece na Story DESTINO; o selo diz de onde ele veio e que a
                // troca ainda esta na fila do "Atualizar TFS".
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00));
                border.BorderThickness = new Thickness(2);
                var from = t.ParentId ?? 0;
                var src = from > 0 ? StoryById(from) : null;
                sp.Children.Insert(0, new TextBlock
                {
                    Text = AppStrings.Get("Sprint_MoveTo", from > 0 ? "#" + from : "",
                        src == null ? "" : EffTitle(from, src.Title)),
                    FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)), TextWrapping = TextWrapping.Wrap
                });
            }

            // No modo edição, o card pode ser arrastado para outra coluna de estado.
            if (EditModeCheck.IsChecked == true)
            {
                border.Cursor = System.Windows.Input.Cursors.SizeAll;
                border.PreviewMouseMove += (s, ev) =>
                {
                    if (ev.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
                    // Não iniciar arrasto quando o mouse está sobre um controle do card (combo/botão),
                    // senão o clique (abrir a prioridade, editar, etc.) é engolido pelo drag.
                    if (IsInteractive(ev.OriginalSource as DependencyObject)) return;
                    DragDrop.DoDragDrop(border, t.Id, DragDropEffects.Move);
                };
            }
            return border;
        }

        // Soltou um card numa coluna de estado: marca a mudança como pendente (grava com "Atualizar TFS").
        private async void OnCardDrop(object sender, DragEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not string newState) return;
            if (!e.Data.GetDataPresent(typeof(int))) return;
            var id = (int)e.Data.GetData(typeof(int))!;
            if (!_cardById.TryGetValue(id, out var dragged)) return;
            var cell = (fe as Border)?.Child as StackPanel;
            var y = cell != null ? e.GetPosition(cell).Y : 0;

            if (SameState(EffState(dragged), newState))
            {
                // Mesma coluna → reordena o RANK (StackRank) DENTRO do grupo de prioridade.
                if (id > 0 && cell != null) ReorderTaskRankInColumn(cell, id, y);
            }
            else
            {
                // Outra coluna → muda de estado (a prioridade/rank não muda pelo arrasto).
                var baseline = _applied.TryGetValue(id, out var a) ? a : dragged.State;
                if (SameState(baseline, newState)) _pending.Remove(id);
                else _pending[id] = newState;

                // Soltou em Active: registra a data de inicio (Data_Inicio) como hoje. So quando a
                // Task ainda nao tem inicio no DevOps — uma data ja planejada nao e sobrescrita.
                if (id > 0 && TfsImportService.NormalizeTaskState(newState) == "Active"
                    && !_startPending.ContainsKey(id)
                    && await TfsImportService.GetWorkItemStartDateAsync(_options, id) == null)
                    _startPending[id] = DateTime.Today;
                // Soltou em Active sem data alvo: calcula a partir da ativacao (hoje) somando o
                // HH Estimado em dias uteis (8h/dia, sem sabado e domingo).
                if (id > 0 && TfsImportService.NormalizeTaskState(newState) == "Active"
                    && (!_finishPending.ContainsKey(id) || _finishAutoByClosed.Contains(id))
                    && dragged.FinishDate == null)
                {
                    var hh = _estPending.TryGetValue(id, out var ep) ? ep : dragged.EstimateHours;
                    _finishPending[id] = AddWorkHours(DateTime.Today, hh ?? 0);
                    _finishAutoByClosed.Remove(id);
                    _finishAutoByActive.Add(id);
                }
                else if (id > 0 && TfsImportService.NormalizeTaskState(newState) == "New"
                         && _finishAutoByActive.Remove(id))
                    _finishPending.Remove(id);
                // Voltou de Active sem gravar: desfaz o inicio que o arrasto tinha colocado.
                else if (id > 0 && TfsImportService.NormalizeTaskState(newState) == "New"
                    && _startPending.TryGetValue(id, out var autoStart) && autoStart == DateTime.Today)
                    _startPending.Remove(id);

                // Soltou em coluna encerrada: a data alvo (Data_Fim) vira hoje quando esta vazia ou
                // e posterior a hoje — a Task terminou antes do previsto. Data alvo ja no passado
                // (terminou atrasada) fica como esta, para nao apagar o registro do atraso.
                if (id > 0 && IsClosedState(newState))
                {
                    var curFinish = _finishPending.TryGetValue(id, out var pf) ? pf
                        : await TfsImportService.GetWorkItemFinishDateAsync(_options, id);
                    if (curFinish == null || curFinish.Value.Date > DateTime.Today)
                    {
                        _finishPending[id] = DateTime.Today;
                        _finishAutoByActive.Remove(id);
                        _finishAutoByClosed.Add(id);
                    }
                }
                // Tirou de Closed sem gravar: desfaz so a data alvo que o arrasto para Closed colocou.
                else if (id > 0 && _finishAutoByClosed.Remove(id)
                         && TfsImportService.NormalizeTaskState(newState) != "Active")
                    _finishPending.Remove(id);

                // Soltou numa coluna encerrada: o campo de HH Realizado passa a aparecer no card
                // ate a gravacao no TFS, mesmo que o estado em si nao tenha mudado.
                if (id > 0 && IsClosedState(newState)) _closedDropped.Add(id);
                else _closedDropped.Remove(id);

                // Ao fechar (Closed) e SEM HH Realizado (nem pendente nem no DevOps), sugere o HH
                // Estimado como padrão no campo do card.
                if (id > 0 && IsClosedState(newState) && !_donePending.ContainsKey(id))
                {
                    var (est, comp, _) = await TfsImportService.GetWorkItemHoursAsync(_options, id);
                    if (!(comp > 0))
                    {
                        double? def = est is > 0 ? est
                            : (double.TryParse(dragged.Effort, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.CurrentCulture, out var eh) && eh > 0 ? eh : (double?)null);
                        if (def is > 0) _donePending[id] = def;
                    }
                }
            }
            RepositionOrder(cell, id, y);
            UpdatePendingButton();
            Render();
        }

        // Reordena o rank (StackRank) do card arrastado apenas entre os da MESMA prioridade na coluna.
        private void ReorderTaskRankInColumn(StackPanel cell, int id, double y)
        {
            if (!_cardById.TryGetValue(id, out var dragged)) return;
            var p = EffPrio(dragged);
            var samePrio = cell.Children.OfType<Border>()
                .Where(b => b.Tag is int bid && bid != id && bid > 0
                            && _cardById.TryGetValue(bid, out var c) && EffPrio(c) == p)
                .Select(b => (Rank: EffTaskRank(_cardById[(int)b.Tag!]),
                              Center: b.TranslatePoint(new Point(0, b.ActualHeight / 2), cell).Y))
                .OrderBy(x => x.Center).ToList();
            if (samePrio.Count == 0) return; // sozinho na prioridade → nada a reordenar
            var insertAt = samePrio.Count(x => x.Center < y);
            double newRank;
            if (insertAt == 0) newRank = samePrio[0].Rank - 1;
            else if (insertAt >= samePrio.Count) newRank = samePrio[^1].Rank + 1;
            else newRank = (samePrio[insertAt - 1].Rank + samePrio[insertAt].Rank) / 2.0;
            _taskRank[id] = newRank;
            _taskRankPending.Add(id);
        }

        // Ordem visual (fallback dentro da célula) pela posição solta.
        private void RepositionOrder(StackPanel? cell, int id, double y)
        {
            if (cell == null) return;
            var others = cell.Children.OfType<Border>()
                .Where(b => b.Tag is int bid && bid != id)
                .Select(b => (Key: _order.TryGetValue((int)b.Tag!, out var o) ? o : (double)(int)b.Tag!,
                              Center: b.TranslatePoint(new Point(0, b.ActualHeight / 2), cell).Y))
                .OrderBy(x => x.Center).ToList();
            var insertAt = others.Count(x => x.Center < y);
            _order[id] = others.Count == 0 ? 0
                : insertAt == 0 ? others[0].Key - 1
                : insertAt >= others.Count ? others[^1].Key + 1
                : (others[insertAt - 1].Key + others[insertAt].Key) / 2.0;
        }

        private int EffPrio(TfsImportService.SprintTaskCard t) =>
            _prioPending.TryGetValue(t.Id, out var p) ? p
            : _prioApplied.TryGetValue(t.Id, out var a) ? a : t.Priority;

        // Faixa de prioridade. O campo Priority é Integer e o DevOps NÃO expõe allowedValues (nem
        // no campo, nem no processo, nem em rules — só setDefaultValue). O formulário do DevOps usa
        // o padrão 1–9; então usamos 1–9 por padrão (ou a faixa de Configurar DevOps, se habilitada),
        // AMPLIADA pelas prioridades já em uso no board.
        private (int Min, int Max) PriorityRange()
        {
            // Base: classe central (config + máximo descoberto no template). Ampliada pelo observado.
            var baseRange = TaskPriorityRange.FromOptions(_options, _discoveredPrioMax);
            var observed = _board?.Stories.SelectMany(s => s.Tasks).Select(t => t.Priority).Where(p => p > 0).ToList()
                           ?? new List<int>();
            var min = observed.Count > 0 ? Math.Min(baseRange.Min, observed.Min()) : baseRange.Min;
            var max = observed.Count > 0 ? Math.Max(baseRange.Max, observed.Max()) : baseRange.Max;
            return (Math.Max(1, min), Math.Max(min, max));
        }

        private static Brush PriorityBrush(int p) => p switch
        {
            1 => new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B)), // P1 vermelho
            2 => new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)), // P2 laranja
            3 => new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A)), // P3 azul
            _ => new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A))  // P4+ cinza
        };

        // Verdadeiro se o elemento (ou um ancestral) é um controle interativo — para não confundir
        // clique em combo/botão com início de arrasto do card.
        private static bool IsInteractive(DependencyObject? o)
        {
            while (o != null)
            {
                if (o is System.Windows.Controls.Primitives.ButtonBase || o is ComboBox || o is System.Windows.Controls.Primitives.Selector)
                    return true;
                o = o is System.Windows.Media.Visual || o is System.Windows.Media.Media3D.Visual3D
                    ? System.Windows.Media.VisualTreeHelper.GetParent(o)
                    : System.Windows.LogicalTreeHelper.GetParent(o);
            }
            return false;
        }

        private static bool SameState(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        // Cor do estado: usa a cor customizada (prefs) se houver; senão a paleta padrão.
        private Brush StateBrush(string state)
        {
            if (_prefs.StateColors != null && _prefs.StateColors.TryGetValue(state.ToLowerInvariant(), out var hex)
                && TryParseColor(hex, out var c))
                return new SolidColorBrush(c);
            return new SolidColorBrush(DefaultStateColor(state));
        }

        // Cor padrão do estado (de fábrica). A customização do usuário fica nas prefs do board.
        private static Color DefaultStateColor(string state) => FactoryStateColor(state);

        // Chave da cor do "Story Colaborador" (pessoa ajuda na task, mas não é responsável da Story).
        private const string HelperColorKey = "story-colaborador";
        // Chave da cor do card de Feature (visão Pessoa & Task).
        private const string FeatureColorKey = "feature";

        private static Color FactoryStateColor(string state) => state.ToLowerInvariant() switch
        {
            HelperColorKey => Color.FromRgb(0xFD, 0xF3, 0xE0), // âmbar bem claro
            FeatureColorKey => Color.FromRgb(0x6A, 0x1B, 0x9A), // roxo
            "new" or "to do" or "approved" => Color.FromRgb(0x6B, 0x7A, 0x8A),
            // Active = cor de DESTAQUE (accent forte); é o estado que precisa de atenção.
            "active" or "committed" or "in progress" or "doing" or "open" => Color.FromRgb(0x00, 0x78, 0xD4),
            "resolved" => Color.FromRgb(0xB2, 0x6A, 0x00),
            // Closed = concluído, cor suave (verde acinzentado) para não roubar o destaque.
            "done" or "closed" or "completed" => Color.FromRgb(0x8A, 0xA5, 0x95),
            _ => Color.FromRgb(0x8A, 0x8A, 0x8A)
        };

        // Fundo claro do card tingido pela cor do estado (mistura com branco).
        private Brush StateTintBrush(string state)
        {
            var c = (StateBrush(state) as SolidColorBrush)?.Color ?? DefaultStateColor(state);
            byte Mix(byte v) => (byte)(v + (255 - v) * 0.82); // ~18% da cor sobre branco
            return new SolidColorBrush(Color.FromRgb(Mix(c.R), Mix(c.G), Mix(c.B)));
        }

        private static bool TryParseColor(string? hex, out Color color)
        {
            color = Colors.Gray;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            try { color = (Color)ColorConverter.ConvertFromString(hex.Trim()); return true; }
            catch { return false; }
        }

        // Abre o editor de cores por estado; ao confirmar, salva nas prefs e re-renderiza.
        private void OnColorsClick(object sender, RoutedEventArgs e)
        {
            var states = _board?.States?.ToList() ?? new List<string>();
            if (states.Count == 0) return;
            var dlg = new StateColorsWindow(states, _prefs.StateColors, DefaultStateColor,
                extraRows: new[] { (AppStrings.Get("Colors_StoryHelp"), HelperColorKey),
                                   (AppStrings.Get("Colors_Feature"), FeatureColorKey) }) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _prefs.StateColors = dlg.Result.Count > 0 ? dlg.Result : null;
                SavePrefs();
                Render();
            }
        }

        private void OpenInDevOps(int id)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_options.OrganizationUrl) || string.IsNullOrWhiteSpace(_options.TeamProject)) return;
                var url = $"{_options.OrganizationUrl.TrimEnd('/')}/{Uri.EscapeDataString(_options.TeamProject.Trim())}/_workitems/edit/{id}";
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }
    }
}
