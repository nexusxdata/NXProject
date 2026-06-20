using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace NXProject.Views
{
    public partial class FeaturesHelpWindow : Window
    {
        private readonly List<(string Title, string Subtitle, List<(string Head, string Body)> Sections, string? Tip)> _topics;

        public FeaturesHelpWindow()
        {
            InitializeComponent();
            _topics = BuildTopics();
            TopicList.SelectedIndex = 0;
        }

        private void OnTopicChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TopicList.SelectedIndex < 0 || TopicList.SelectedIndex >= _topics.Count) return;
            RenderTopic(_topics[TopicList.SelectedIndex]);
        }

        private void RenderTopic((string Title, string Subtitle, List<(string Head, string Body)> Sections, string? Tip) topic)
        {
            ContentPanel.Children.Clear();

            // Título
            ContentPanel.Children.Add(new TextBlock
            {
                Text = topic.Title,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(43, 87, 154)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });

            // Subtítulo
            if (!string.IsNullOrWhiteSpace(topic.Subtitle))
                ContentPanel.Children.Add(new TextBlock
                {
                    Text = topic.Subtitle,
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromRgb(80, 90, 110)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 18)
                });

            // Seções
            foreach (var (head, body) in topic.Sections)
            {
                ContentPanel.Children.Add(new TextBlock
                {
                    Text = head,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(43, 87, 154)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 14, 0, 6)
                });

                // Corpo: linhas iniciadas com "• " viram bullets; demais são parágrafo normal
                foreach (var line in body.Split('\n'))
                {
                    var trimmed = line.TrimStart();
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;

                    bool isBullet = trimmed.StartsWith("•");
                    var tb = new TextBlock
                    {
                        Text = trimmed,
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromRgb(40, 45, 55)),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = isBullet
                            ? new Thickness(16, 2, 0, 2)
                            : new Thickness(0, 2, 0, 4)
                    };
                    ContentPanel.Children.Add(tb);
                }
            }

            // Dica final
            if (!string.IsNullOrWhiteSpace(topic.Tip))
            {
                var tipBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(255, 247, 232)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(232, 211, 154)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(14, 10, 14, 10),
                    Margin = new Thickness(0, 20, 0, 0)
                };
                tipBorder.Child = new TextBlock
                {
                    Text = "💡 " + topic.Tip,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.FromRgb(60, 50, 20))
                };
                ContentPanel.Children.Add(tipBorder);
            }
        }

        private static List<(string, string, List<(string, string)>, string?)> BuildTopics() => new()
        {
            (
                "Visão Geral",
                "O NXProject é um gerenciador de projetos de TI que une o rigor técnico do Azure DevOps com a visão de cronograma que gestores e líderes precisam para tomar decisões.",
                new()
                {
                    ("Filosofia de planejamento",
                     "O NXProject planeja até o nível de Story, permitindo que o Desenvolvedor tenha liberdade para detalhar e criar as tarefas durante a execução.\n\n" +
                     "Inspirado no conceito matemático de grau de liberdade — utilizado para modelar sistemas complexos — o NXProject aplica o mesmo princípio ao planejamento: estrutura a complexidade da tecnologia sem engessar o processo de desenvolvimento.\n\n" +
                     "Assim como em um sistema físico onde os graus de liberdade definem o espaço de movimento possível, o NXProject define os limites (datas, recursos, dependências) e preserva o espaço necessário para que o time técnico navegue com autonomia dentro deles."),
                    ("O que o NXProject faz",
                     "O NXProject importa a hierarquia do Azure DevOps (Project → Epic → Feature → Story) e transforma esses dados em um cronograma com datas, dependências, alocação de recursos e Gantt.\n" +
                     "A equipe técnica continua no Azure DevOps como sempre. O NXProject é uma camada de leitura e planejamento sobre esses dados.\n" +
                     "O objetivo da Nexus Xdata é transparência: deixar claro por que cada data, duração, percentual e alerta aparece no cronograma."),
                    ("Quem usa e para quê",
                     "• Gerente de Projeto: cronograma integrado ao backlog, alertas de atraso, visão de dependências.\n" +
                     "• Scrum Master / RTE: capacidade por sprint, conflito de alocação, impacto de mudanças de data.\n" +
                     "• Tech Lead: visão de Features e Stories com predecessoras e estimativas em horas.\n" +
                     "• PMO: exportação para MS Project / Excel, visão consolidada do projeto."),
                    ("Arquivo de projeto (.nxp)",
                     "O cronograma é salvo em um arquivo .nxp que pode ser compartilhado. Ele armazena todas as tarefas, datas, dependências, recursos, configurações de sprint e o vínculo com o Azure DevOps.")
                },
                "Use Arquivo → Importar → TFS / Azure DevOps para criar o cronograma a partir do seu backlog existente."
            ),
            (
                "Cronograma",
                "A grade de tarefas é onde você visualiza e edita a estrutura do projeto: hierarquia, datas, duração, recursos, percentual de conclusão e dependências.",
                new()
                {
                    ("Hierarquia de tarefas",
                     "O projeto é organizado em níveis: Feature → Story → Task ou qualquer agrupamento que faça sentido. Tarefas filhas são indentadas abaixo da tarefa pai.\n" +
                     "• Use Editar → Criar Subtarefa para indentar uma tarefa.\n" +
                     "• Use Editar → Promover Tarefa para subir um nível.\n" +
                     "• Tarefas agrupamento (com filhos) calculam datas e duração automaticamente a partir dos filhos."),
                    ("Duração e datas",
                     "• Coluna Dur.(h): informe em horas (ex: 8) ou em dias úteis com d (ex: 2d = 2 dias úteis).\n" +
                     "• A data Fim é calculada automaticamente: Início + Dur.(h) respeitando o calendário de trabalho.\n" +
                     "• Para fixar a data de Início, digite a data no campo — ela fica marcada com 📌.\n" +
                     "• Para fixar a data de Fim, informe a data no campo Fim ou arraste a borda direita da barra no Gantt com o botão direito do mouse (na barra já selecionada).\n" +
                     "• Para remover fixação de Início, digite 0 no campo Início."),
                    ("Percentual de conclusão",
                     "• O campo % Compl. registra o avanço da tarefa (0 a 100).\n" +
                     "• Na grade, percentuais baixos usam texto escuro sobre o fundo claro; percentuais maiores usam texto branco sobre a área preenchida.\n" +
                     "• Tarefas agrupamento calculam o percentual como média ponderada das horas dos filhos.\n" +
                     "• Se a data Fim estiver no passado e o percentual for menor que 100, o sistema alerta automaticamente no Health Check."),
                    ("Criação de Atividade",
                     "Ao adicionar uma nova atividade (botão + ou Editar → Adicionar Atividade):\n" +
                     "• O Tipo, o Recurso e a Sprint são copiados automaticamente da atividade selecionada no momento do clique.\n" +
                     "• O ID DevOps é definido como 0, indicando que a atividade será criada no Azure DevOps na próxima sincronização (Export → Sincronizar).\n" +
                     "• Atividades com Tipo = 'No DevOps' nunca são enviadas ao Azure DevOps — servem apenas para controle local no cronograma.\n" +
                     "• Atividades sem Tipo definido são automaticamente classificadas como 'No DevOps' para evitar criação acidental no DevOps."),
                    ("Atualização de Atividade no DevOps",
                     "• Atividades com ID DevOps > 0 são atualizadas no Azure DevOps ao executar Export → Sincronizar.\n" +
                     "• Atividades com ID DevOps = 0 (e Tipo diferente de 'No DevOps') são criadas como novos work items no Azure DevOps, e o ID retornado é gravado no cronograma.\n" +
                     "• Atividades com Tipo 'No DevOps' são ignoradas pela sincronização, mesmo que tenham ID = 0.\n" +
                     "• No Import: se um work item do Azure DevOps tiver o mesmo nome que uma atividade 'No DevOps' local, o NXProject vincula automaticamente a atividade local ao item importado, atualizando seu Tipo para o tipo do DevOps."),
                    ("Bloqueio (tag BLOCK)",
                     "O NXProject diferencia dois tipos de bloqueio visíveis na coluna Nome:\n" +
                     "• ⛔ BLOCK (vermelho) — a própria Story/atividade tem a tag 'Block'. Quando ambos existem, apenas este ícone é exibido.\n" +
                     "• 🔴 BLOCK (amarelo) — bloqueio herdado de uma Task filha no DevOps que tem a tag 'Block'.\n\n" +
                     "Para adicionar ou retirar o Block da Story, clique com o botão direito no nome da atividade e use o menu 'Adicionar/Retirar Block da Story'.\n\n" +
                     "Sincronização da tag Block:\n" +
                     "• Se a Story no NXProject tem Block e o DevOps não tem → a tag é adicionada no DevOps ao sincronizar.\n" +
                     "• Se a Story no NXProject não tem Block e o DevOps tem → a tag é removida do DevOps ao sincronizar.\n\n" +
                     "Na importação, o NXProject lê a tag Block tanto da própria Story (registrada nas tags da atividade) quanto das Tasks filhas (refletida como bloqueio herdado).")
                },
                "Informe Início e Dur.(h) — o Fim é calculado pelo calendário. Para dependências, use a coluna Pred."
            ),
            (
                "Datas da Atividade",
                "As datas de uma atividade são calculadas a partir do Início, da duração em horas, do calendário de trabalho, do percentual de conclusão e das regras de cascata. Em linha com o objetivo de transparência da Nexus Xdata, esta seção explicita as regras usadas pelo cronograma.",
                new()
                {
                    ("Início, duração e fim",
                     "• Início é a data em que a atividade começa no cronograma.\n" +
                     "• Dur.(h) é a duração total de trabalho: HH Atual + HH Restante.\n" +
                     "• Fim é calculado por Início + Dur.(h), respeitando dias úteis, feriados e horas úteis por dia.\n" +
                     "• A data mostrada na coluna Fim é a data de término visível para o usuário; internamente o cálculo usa o limite final do período de trabalho."),
                    ("Início fixado",
                     "• Ao digitar uma data no campo Início, o Início fica fixado e aparece com o ícone de fixação.\n" +
                     "• Uma atividade com Início fixado não é recuada automaticamente por cascata de recurso ou predecessora virtual.\n" +
                     "• Para remover a fixação do Início, digite 0 no campo Início.\n" +
                     "• Se o Início fixado estiver no futuro e a atividade for marcada como 100%, o Fim fica igual ao Início fixado, para evitar Fim antes do Início."),
                    ("Fim fixado",
                     "• Ao editar a coluna Fim ou arrastar a borda direita da barra no Gantt com o botão direito, o Fim fica fixado.\n" +
                     "• Com Fim fixado, alterações de duração ou percentual não recalculam automaticamente a data Fim.\n" +
                     "• Use a fixação de Fim para registrar uma data negociada que pode ser diferente da duração calculada por horas e alocação.\n" +
                     "• Se houver diferença entre duração negociada e duração calculada, o Gantt pode indicar conflito visual."),
                    ("Percentual 0%",
                     "• Ao voltar % Compl. para 0%, o NXProject considera que nenhum trabalho foi realizado.\n" +
                     "• HH Atual fica igual a 0.\n" +
                     "• HH Restante volta para HH Original.\n" +
                     "• A data Fim é recalculada por Início + HH Restante, desde que o Fim não esteja fixado.\n" +
                     "• A cascata pode reposicionar atividades seguintes do mesmo recurso, mas não deve usar Features ou agrupadores como referência de fila."),
                    ("Percentual 100%",
                     "• Ao marcar % Compl. como 100%, o NXProject considera a atividade encerrada.\n" +
                     "• HH Atual recebe a duração total da atividade.\n" +
                     "• HH Restante fica igual a 0.\n" +
                     "• O Fim calculado é Início + duração total. Se esse Fim cair no futuro, o Fim é limitado a hoje, pois não é possível encerrar uma atividade no futuro.\n" +
                     "• Exceção: se o Início estiver fixado em uma data futura, o Fim fica igual ao Início fixado."),
                    ("Cascata por predecessoras e recurso",
                     "• Predecessoras explícitas movem a atividade para o próximo dia útil após o fim visível da predecessora.\n" +
                     "• A cascata usa o padrão de ordenação topológica: uma atividade dependente só é recalculada depois que suas predecessoras já foram processadas.\n" +
                     "• A predecessora virtual organiza atividades do mesmo recurso, mesmo pai e mesmo nível, para evitar sobreposição de trabalho.\n" +
                     "• A referência da predecessora virtual deve ser outra atividade folha, como Story/Task, nunca uma Feature, Epic ou agrupador.\n" +
                     "• Agrupadores continuam sendo recalculados para refletir datas, duração e percentual dos filhos.")
                },
                "Regra prática: edite Início e Dur.(h) para planejar; use % Compl. para registrar progresso. Fixações são exceções conscientes ao cálculo automático."
            ),
            (
                "Gráfico Gantt",
                "O Gantt exibe as barras de cada atividade no tempo, com marcos, setas de dependência, sprints e a linha de hoje.",
                new()
                {
                    ("Navegação e zoom",
                     "• Use o botão de zoom na toolbar para alternar entre Dia, Semana, Sprint, Mês, Trimestre e Semestre.\n" +
                     "• Role horizontalmente para navegar no tempo.\n" +
                     "• Ative o botão de lupa na toolbar e mova o mouse sobre o Gantt para analisar datas, barras e dependências de perto.\n" +
                     "• A linha vermelha vertical indica a data de hoje."),
                    ("Visões de cabeçalho por dia",
                     "O botão de calendário (📅) na toolbar cicla entre três modos:\n" +
                     "• Off: cabeçalho padrão por sprint e mês.\n" +
                     "• Dia 1: destaca segunda-feira com número do dia, quarta e sexta em azul mais vivo.\n" +
                     "• Dia 2: exibe o dígito da unidade de cada dia. Os dias 10, 20 e 30 ficam destacados em azul, laranja e verde respectivamente — facilitando a leitura de datas sem sobrecarregar o cabeçalho."),
                    ("Arrastar barras",
                     "• Botão esquerdo + arrastar: move a data de Início da atividade (somente para atividades que ainda não iniciaram).\n" +
                     "• Botão direito + arrastar (na barra já selecionada): ajusta a data de Fim sem alterar a estimativa de horas. Ao soltar, a data Fim fica fixada (📌).\n" +
                     "• Atividades dependentes se deslocam automaticamente ao mover uma predecessora."),
                    ("Barras e cores",
                     "• Barra azul claro: atividade normal.\n" +
                     "• Barra laranja: atividade selecionada.\n" +
                     "• Faixa escura central: percentual de conclusão, no estilo MS Project.\n" +
                     "• Linha escura discreta na base: HH Atual proporcional ao total de HH Atual + HH Restante.\n" +
                     "• Losango dourado: marco (milestone).\n" +
                     "• Barra cinza-azulada clara: agrupamento (Feature/Epic).\n" +
                     "• Bordas ou realces em vermelho indicam conflito, atraso ou duração negociada diferente da calculada.")
                },
                "Clique em uma barra para selecionar a tarefa na grade. As setas de dependência mostram o caminho crítico visualmente."
            ),
            (
                "Predecessoras",
                "Predecessoras definem que uma atividade só pode iniciar após o término de outra, criando a cadeia de dependências do projeto.",
                new()
                {
                    ("Como cadastrar",
                     "Clique no campo Pred. da atividade que depende de outra. Uma janela de seleção abre com todas as atividades de último nível disponíveis.\n" +
                     "• Use a busca para localizar pelo nome ou código.\n" +
                     "• Marque uma ou mais atividades com o checkbox.\n" +
                     "• O painel superior mostra as predecessoras já marcadas antes de confirmar."),
                    ("Predecessoras fora da lista",
                     "Quando uma atividade importada do DevOps tem predecessoras que apontam para itens fora do escopo importado, elas aparecem em amarelo no seletor com o rótulo 'fora da lista filtrada'.\n" +
                     "• Cada predecessora externa pode ser removida individualmente pelo botão ✕ Remover.\n" +
                     "• Predecessoras dentro da lista são marcadas normalmente por checkbox."),
                    ("Efeito no cronograma",
                     "Ao mover uma atividade no Gantt, todas as atividades que dependem dela (direto ou indiretamente) são deslocadas automaticamente pelo mesmo número de dias.")
                },
                "Para encadear atividades em sequência de uma vez, selecione várias e use Editar → Encadear Atividades."
            ),
            (
                "Recursos",
                "Recursos são as pessoas alocadas nas atividades. O NXProject importa responsáveis do Azure DevOps e permite gerenciar a carga de trabalho por pessoa.",
                new()
                {
                    ("Cadastrar recursos",
                     "Acesse Exibir → Pessoas para gerenciar a lista de recursos do projeto. Cada pessoa pode ter nome e e-mail.\n" +
                     "Ao importar do Azure DevOps, o campo System.AssignedTo é importado automaticamente como recurso."),
                    ("Alocação de recursos",
                     "Exibir → Alocação de Recursos mostra a carga de trabalho por pessoa em cada período (sprint ou semana), permitindo identificar sobrecargas antes que virem problemas.\n" +
                     "• Células vermelhas indicam sobrecarga (mais de 100% da capacidade diária).\n" +
                     "• Células verdes indicam capacidade disponível."),
                    ("Filtro por recurso",
                     "O botão 👤 na toolbar permite filtrar o Gantt e a grade para mostrar somente as atividades de uma pessoa específica — útil em reuniões individuais de acompanhamento.")
                },
                "Use o filtro de recurso na toolbar para ver somente as atividades de uma pessoa durante a reunião de status."
            ),
            (
                "Sprints",
                "O NXProject suporta sprints do Azure DevOps e permite configurar sprints locais para organizar o cronograma em iterações.",
                new()
                {
                    ("Configurar sprints",
                     "Exibir → Sprint define o número da primeira sprint, duração em dias e modo de numeração (sequencial, par ou ímpar).\n" +
                     "Se o projeto foi importado do Azure DevOps, as sprints são lidas de System.IterationPath e criadas automaticamente."),
                    ("Associar atividades",
                     "A coluna Sprint na grade permite mover Stories e Features entre sprints. Ao alterar a sprint, a data de Início é recalculada para o início daquela sprint.\n" +
                     "• Para remover a associação com sprint e usar data fixa, basta informar uma data no campo Início."),
                    ("Visão no Gantt",
                     "O Gantt exibe as sprints no cabeçalho inferior, com numeração e cores alternadas. A visão de zoom Sprint ou Semana deixa as iterações mais visíveis.")
                },
                "A coluna Sprint é especialmente útil para replanejar — mova Stories entre sprints e veja o impacto no cronograma imediatamente."
            ),
            (
                "Azure DevOps",
                "A integração com o Azure DevOps é o coração do NXProject: o backlog técnico vira cronograma gerenciável sem mudar o fluxo da equipe.",
                new()
                {
                    ("Importar o projeto",
                     "Arquivo → Importar → TFS / Azure DevOps abre a tela de importação. Informe:\n" +
                     "• URL da organização (ex: https://dev.azure.com/suaorg)\n" +
                     "• Nome do projeto (Team Project)\n" +
                     "• Personal Access Token (PAT) com permissão de leitura em Work Items\n" +
                     "• ID do work item raiz (tipo Project) — ou selecione da lista de projetos cadastrada"),
                    ("O que é importado",
                     "• Hierarquia Project → Epic → Feature → Story via links Child.\n" +
                     "• Estimativas: campo HH Estimado → duração em horas.\n" +
                     "• Datas: Data_Inicio e Data_Fim quando preenchidas no DevOps.\n" +
                     "• Responsável: System.AssignedTo → recurso do projeto.\n" +
                     "• Sprint: System.IterationPath → sprint do NXProject.\n" +
                     "• Ordem: Microsoft.VSTS.Common.StackRank.\n" +
                     "• Bloqueios: Tasks com tag Block marcam a Story como bloqueada."),
                    ("Log de importação",
                     "Ao final da importação, se houver avisos, uma janela de log é exibida com:\n" +
                     "• Stories cujo state foi corrigido automaticamente (ex: Closed com Tasks abertas → Active).\n" +
                     "• Predecessoras fora do escopo importado, com identificação se é Story ou outro tipo.\n" +
                     "• Filtros de Info / Aviso / Erro para facilitar a revisão."),
                    ("Abrir work item no DevOps",
                     "Na janela de Vínculo DevOps (clique no ID da tarefa na grade), o botão Abrir no DevOps ↗ abre o work item diretamente no browser. A janela também exibe as Tasks filhas vinculadas com ID, nome e estado.")
                },
                "Os nomes de campos (HH Estimado, Data_Inicio, Data_Fim) podem ser personalizados na área Campos (avançado) da tela de importação."
            ),
            (
                "Lista de Projetos",
                "A lista de projetos DevOps é um arquivo compartilhado entre a equipe com os projetos disponíveis para importação.",
                new()
                {
                    ("Para que serve",
                     "Em vez de cada pessoa lembrar o ID do work item raiz, você mantém um arquivo JSON com os projetos cadastrados (Nome + ID). Todos da equipe apontam para o mesmo arquivo.\n" +
                     "Acesse em Exibir → Projetos DevOps (lista)..."),
                    ("Gerenciar a lista",
                     "• Clique em Abrir / Criar para carregar ou criar um arquivo de lista.\n" +
                     "• Use os botões Adicionar, Editar e Excluir para manter os projetos.\n" +
                     "• O caminho do arquivo fica salvo nas configurações do usuário e recarregado automaticamente."),
                    ("Usar na importação",
                     "Na tela de importação (Arquivo → Importar → TFS / Azure DevOps), um ComboBox exibe os projetos da lista. Selecione o projeto e o campo de ID raiz é preenchido automaticamente.\n" +
                     "Use o botão ⚙ Gerenciar Lista... para abrir o CRUD diretamente pela tela de importação."),
                    ("Banner no cronograma",
                     "Após importar, o nome do projeto vinculado aparece em um banner azul claro no topo do cronograma, facilitando a identificação visual de qual projeto está aberto.")
                },
                "Salve o arquivo de lista em um diretório compartilhado (rede, OneDrive, SharePoint) para que toda a equipe use a mesma lista de projetos."
            ),
            (
                "Sincronização",
                "A sincronização envia de volta para o Azure DevOps as alterações feitas no cronograma: datas, horas, estado, sprint, tags e predecessoras.",
                new()
                {
                    ("Como sincronizar",
                     "Arquivo → Exportar → Sincronizar TFS / Azure DevOps... abre a tela de sincronização. Use as mesmas credenciais da importação.\n" +
                     "O processo compara o estado atual do cronograma com o DevOps e envia somente o que mudou."),
                    ("O que é sincronizado",
                     "• Título e descrição da Story/Feature.\n" +
                     "• Horas estimadas (HH Estimado).\n" +
                     "• Datas de início e fim (Data_Inicio, Data_Fim).\n" +
                     "• Estado (New, Active, Resolved, Closed).\n" +
                     "• Tags (inclusive tag Block para bloqueios).\n" +
                     "• Sprint (System.IterationPath).\n" +
                     "• Links de predecessora entre work items."),
                    ("Relatório de sincronização",
                     "Ao finalizar, uma janela exibe o resumo: itens atualizados, criados, sem alteração, avisos e erros. Use os filtros para focar nos problemas e copie o log se precisar registrar.")
                },
                "A sincronização respeita somente os campos configurados. A rastreabilidade de código, pull requests e pipelines do Azure DevOps não são afetados."
            ),
            (
                "Exportação",
                "Exporte o cronograma para outros formatos para compartilhar com stakeholders ou integrar com outras ferramentas.",
                new()
                {
                    ("Formatos disponíveis",
                     "• MS Project XML (.xml): compatível com Microsoft Project.\n" +
                     "• OpenProj (.pod): formato aberto para ferramentas como ProjectLibre.\n" +
                     "• Excel XML (.xml): tabela com todas as atividades, datas e recursos.\n" +
                     "• CSV: formato simples para análise em qualquer planilha."),
                    ("Quando usar cada formato",
                     "• Use MS Project XML para enviar o cronograma a stakeholders que usam MS Project.\n" +
                     "• Use Excel/CSV para relatórios, dashboards ou análises personalizadas.\n" +
                     "• Use OpenProj para ambientes sem licença de MS Project.")
                },
                "O CSV é o formato mais portátil para alimentar dashboards em Power BI, Tableau ou Google Sheets."
            ),
            (
                "Health Check",
                "O Health Check identifica problemas no cronograma que precisam de atenção antes que impactem a entrega.",
                new()
                {
                    ("O que é verificado",
                     "Exibir → Health Check do Projeto analisa todas as atividades e lista:\n" +
                     "• Atividades com data de Fim no passado e percentual menor que 100% (em atraso).\n" +
                     "• Atividades sem responsável alocado.\n" +
                     "• Atividades com predecessoras que criam dependências circulares.\n" +
                     "• Stories marcadas como bloqueadas (tag Block)."),
                    ("Como usar",
                     "• Abra o Health Check regularmente nas reuniões de status para revisar o estado do projeto.\n" +
                     "• Clique em uma atividade na lista para selecioná-la na grade e corrigir o problema.\n" +
                     "• Use como checklist antes de enviar um relatório para a gestão.")
                },
                "Execute o Health Check antes de cada reunião de status — ele revela em segundos o que está atrasado e sem dono."
            ),
            (
                "Assistente IA",
                "O Assistente de IA sugere estruturas de tarefas, decomposição de histórias e organização do cronograma a partir de uma descrição em linguagem natural.",
                new()
                {
                    ("Como acessar",
                     "Clique no botão IA na toolbar ou acesse IA → Assistente de Tarefas...\n" +
                     "Descreva o que precisa ser feito e o assistente sugere uma hierarquia de tarefas com estimativas."),
                    ("Casos de uso",
                     "• Criar a estrutura inicial de um projeto a partir de uma descrição.\n" +
                     "• Decompor uma Story grande em Tasks menores.\n" +
                     "• Gerar uma lista de atividades para um tipo de entrega recorrente (ex: setup de ambiente, testes de regressão).\n" +
                     "• Revisar se a decomposição atual está cobrindo todos os aspectos do escopo."),
                    ("Disponibilidade",
                     "O Assistente IA requer conexão com internet e chave de API configurada. Na edição Community, está disponível em modo limitado. A edição Enterprise inclui integração completa com OpenAI e Claude.")
                },
                "Use o Assistente IA para o primeiro brainstorm de tarefas — depois refine manualmente na grade com os detalhes do seu contexto."
            ),
            (
                "Configurações",
                "Personalize o comportamento do NXProject para o seu projeto e equipe.",
                new()
                {
                    ("Calendário de trabalho",
                     "Exibir → Calendário permite configurar:\n" +
                     "• Horas úteis por dia (padrão: 8h).\n" +
                     "• Dias da semana considerados úteis.\n" +
                     "• Feriados: adicione datas específicas que serão ignoradas no cálculo de prazo.\n" +
                     "O calendário é salvo localmente em %LocalAppData%\\NXProject.Community\\nxproject_calender.json."),
                    ("SPF — Story Points de Função",
                     "Exibir → SPF configura a tabela de conversão entre pontos de função e horas estimadas, usada para calcular duração a partir de métricas de complexidade."),
                    ("Configurações de conexão DevOps",
                     "As credenciais de conexão (URL da organização, Team Project, PAT) são salvas de forma segura usando DPAPI (criptografia do Windows ligada ao usuário). Marque Lembrar o token para não precisar digitar a cada importação.\n" +
                     "O caminho do arquivo de Lista de Projetos DevOps também é salvo nas configurações do usuário."),
                    ("Zoom padrão",
                     "O último zoom selecionado é salvo no arquivo .nxp e restaurado ao reabrir o projeto.")
                },
                "O calendário é o coração do cálculo de prazos — configure os feriados do seu país e da empresa antes de começar o planejamento."
            )
        };
    }
}
