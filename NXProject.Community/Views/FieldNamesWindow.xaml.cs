// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NXProject.Services;
using static NXProject.Services.NxDevOpsFieldCatalog;

namespace NXProject.Views
{
    /// <summary>
    /// Nomes dos campos que o NXProject usa, nos dois destinos.
    ///
    /// Tela única, com um seletor de destino, e não uma por servidor: a pergunta é a mesma —
    /// "que campo guarda este conceito" — e a resposta sai do mesmo catálogo. Já tivemos três
    /// cópias divergentes dessa regra entre o NXProject e o Setup, e cada cópia virou defeito.
    ///
    /// O que muda entre os destinos é QUAIS campos existem (o `card_type` só no GitHub; o
    /// `Sync_version` não faz sentido lá) e se o conceito tem equivalente NATIVO no servidor —
    /// e disso o catálogo já dá conta, com <see cref="DevOpsFields"/> e <see cref="GitProjectFields"/>.
    /// </summary>
    public partial class FieldNamesWindow : Window
    {
        private sealed class Row
        {
            public NxFieldSpec Spec { get; init; } = new();
            public TextBox Box { get; init; } = new();
            public TextBlock Where { get; init; } = new();

            /// <summary>Selo do "Detectar": ✔ existe no board, ✖ falta.</summary>
            public TextBlock Status { get; init; } = new();

            /// <summary>
            /// Resultado da última detecção: null = ainda não detectou, true = está no board.
            /// É ele que decide o que o "Criar campos no board" vai criar — criar o que já
            /// existe duplicaria campo, e no GitHub o tipo não se troca depois.
            /// </summary>
            public bool? NoBoard { get; set; }
        }

        private readonly TfsConnectionOptions _options = TfsConnectionStore.Load();
        private readonly List<Row> _rows = new();

        /// <summary>Classificação do GitProject: campo do board preenchido ao criar o item.</summary>
        private readonly System.Collections.ObjectModel.ObservableCollection<GitProjectClassification> _gitClass = new();

        // Listas que vieram da tela de DevOps junto com a seção: campos obrigatórios na criação
        // e os mapeamentos de classificação (picklist).
        private readonly System.Collections.ObjectModel.ObservableCollection<ExtraWorkItemField> _extraFields = new();
        private readonly System.Collections.ObjectModel.ObservableCollection<TfsDevOpsConfigWindow.ClassificationMapping> _classificationMappings = new();

        public FieldNamesWindow()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                CarregaDevOps(); CarregaGit(); BuildRows(); CarregaNiveis();
                // Aberta pelo NXProject-Setup com destino GitProject: nasce na aba dele.
                if (NXProject.CommunityApp.AbrirCamposNoGitProject && TabsControl != null) TabsControl.SelectedIndex = 1;
            };
        }


        private void CarregaGit()
        {
            foreach (var c in _options.GitProject?.Classifications ?? new List<GitProjectClassification>())
                _gitClass.Add(new GitProjectClassification
                { Level = c.Level, Field = c.Field, FieldType = c.FieldType, Values = c.Values, DefaultValue = c.DefaultValue });
            var git = _options.GitProject ?? new GitProjectOptions();
            GitFixedStartTagBox.Text = git.FixedStartTag;
            GitUnplannedTagBox.Text = git.UnplannedTag;
            GitWipTagBox.Text = git.WipTag;
            GitBlockedTagBox.Text = git.BlockedTag;

            // Idem na aba do GitProject: PT quando os nomes salvos sao os em portugues.
            GitNamingCombo.SelectedIndex =
                GitProjectOptions.Matches(GitProjectOptions.Suggested(git.StartDateFields),
                                          new[] { NxDevOpsFieldCatalog.Find("start")?.DefaultName ?? "" }) ? 0 : 1;

            if (_gitClass.Count == 0) SemeiaClassificacaoGit();
            GitClassList.ItemsSource = _gitClass;
        }

        /// <summary>
        /// Confere, no board de verdade, quais campos o NXProject vai encontrar. É o equivalente
        /// do "Detectar campos DevOps" — e serve ao mesmo propósito: descobrir aqui, e não no
        /// meio de um import, que o HH se chama outra coisa (ou não existe).
        ///
        /// Compara pelo APELIDO, não pelo nome exato: um board batizado "Data_Inicio" casa com o
        /// conceito de data de início, e a tela mostra com que nome ele foi achado.
        /// </summary>
        private async void OnGitDetectClick(object sender, RoutedEventArgs e)
        {
            var git = _options.GitProject;
            if (git?.IsConfigured != true)
            {
                GitDetectStatus.Text = AppStrings.Get("Fld_GitDetectNoConn");
                return;
            }

            GitDetectButton.IsEnabled = false;
            GitDetectStatus.Text = AppStrings.Get("Git_Testing");
            try
            {
                var snap = await new GitProjectClient(git).LoadAsync();
                var achados = 0;
                var faltando = 0;

                foreach (var row in _rows)
                {
                    var nomes = NomesGit(row.Spec);
                    var noBoard = snap.FieldNames.FirstOrDefault(n => GitProjectOptions.Matches(n, nomes));
                    row.NoBoard = noBoard != null;

                    if (noBoard != null)
                    {
                        achados++;
                        row.Status.Text = "✔";
                        row.Status.Foreground = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0x10, 0x7C, 0x10));
                        row.Status.ToolTip = AppStrings.Get("Fld_GitRowFound", noBoard, snap.Title);
                    }
                    else
                    {
                        faltando++;
                        row.Status.Text = "✖";
                        row.Status.Foreground = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0xC0, 0x30, 0x30));
                        row.Status.ToolTip = AppStrings.Get("Fld_GitRowMissing",
                            GitProjectOptions.Suggested(nomes), snap.Title);
                    }
                }

                // A classificacao tambem e campo do board: conferir junto evita a surpresa de
                // criar o item e o valor nao ir a lugar nenhum.
                foreach (var c in _gitClass.Where(c => !string.IsNullOrWhiteSpace(c.Field)))
                    if (snap.FieldNames.Any(n => string.Equals(n, c.Field.Trim(), StringComparison.CurrentCultureIgnoreCase)))
                        achados++;
                    else faltando++;

                GitDetectStatus.Text = AppStrings.Get("Fld_GitDetectResult",
                    snap.FieldNames.Count.ToString(), achados.ToString(), faltando.ToString());
            }
            catch (Exception ex)
            {
                GitDetectStatus.Text = ex.Message;
            }
            finally { GitDetectButton.IsEnabled = true; }
        }

        /// <summary>
        /// Primeira vez na aba: a classificação do GitProject nasce com o que o DevOps já
        /// classifica, em vez de uma linha em branco.
        ///
        /// No GitHub uma lista fechada de valores só existe em SINGLE-SELECT — Text, Number e
        /// Date não têm opções, e a outra lista fechada de lá (a label) é do repositório, não do
        /// board. Então um Picklist do DevOps vira single-select aqui, com os mesmos valores, e o
        /// botão "Criar campos no board" o cria de verdade.
        ///
        /// O nome perde o prefixo de referência (Custom.Type → Type): no DevOps o campo é citado
        /// pelo reference name; no GitHub o campo é do board e se chama pelo nome que aparece na
        /// tela.
        /// </summary>
        private void SemeiaClassificacaoGit()
        {
            foreach (var m in _classificationMappings.Where(m => !string.IsNullOrWhiteSpace(m.FieldRef)))
                _gitClass.Add(new GitProjectClassification
                {
                    Level = string.IsNullOrWhiteSpace(m.DevOpsType) || m.DevOpsType == "Todos" ? "Todos" : m.DevOpsType,
                    Field = m.FieldRef.Contains('.') ? m.FieldRef[(m.FieldRef.LastIndexOf('.') + 1)..] : m.FieldRef.Trim(),
                    FieldType = string.Equals(m.FieldType, "Picklist", StringComparison.OrdinalIgnoreCase)
                        ? "Single select" : (m.FieldType ?? "Single select"),
                    Values = m.Values ?? string.Empty,
                    DefaultValue = m.DefaultValue ?? string.Empty
                });

            if (_gitClass.Count == 0) _gitClass.Add(new GitProjectClassification());
        }

        // ── Níveis ───────────────────────────────────────────────────────────────────────

        private sealed class LevelRow
        {
            public NxLevel Level { get; init; }
            public CheckBox InUse { get; init; } = new();
            public TextBox Names { get; init; } = new();

            /// <summary>Selo do "Detectar": ✔ o destino tem um tipo deste nível, ✖ não tem.</summary>
            public TextBlock Status { get; init; } = new();

            /// <summary>Última detecção: null = ainda não detectou. É o que o "Criar" usa.</summary>
            public bool? NoDestino { get; set; }
        }

        private readonly List<LevelRow> _levelRows = new();

        /// <summary>
        /// Uma linha por nível: em uso, e os nomes de tipo que valem por ele. Vale para os DOIS
        /// destinos — no DevOps é o nome do tipo de work item, no GitHub o Issue Type ou o valor
        /// do campo de nível do board.
        /// </summary>
        private void CarregaNiveis()
        {
            var mapa = (_options.Levels ?? new NxLevelMap()).Clone();
            _levelRows.Clear();
            var host = new StackPanel();

            foreach (var nivel in NxLevelMap.AllLevels)
            {
                var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var rotulo = new TextBlock
                {
                    Text = nivel.ToString(), FontSize = 11.5, FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(rotulo, 0); grid.Children.Add(rotulo);

                // Story não desliga: é a folha do cronograma — sem ela não sobra o que planejar.
                var emUso = new CheckBox
                {
                    IsChecked = !mapa.Disabled.Contains(nivel),
                    IsEnabled = nivel != NxLevel.Story,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = AppStrings.Get("Fld_LevelInUseTip")
                };
                Grid.SetColumn(emUso, 1); grid.Children.Add(emUso);

                var nomes = new TextBox
                {
                    Text = string.Join(", ", mapa.Names.TryGetValue(nivel, out var l) ? l : new List<string>()),
                    Height = 22, FontSize = 11, Margin = new Thickness(0, 0, 6, 0),
                    ToolTip = AppStrings.Get("Fld_LevelNamesTip")
                };
                Grid.SetColumn(nomes, 2); grid.Children.Add(nomes);

                // Selo na coluna do nome, como na aba dos campos: o resultado fica ao lado da
                // linha a que se refere, em vez de num texto onde e preciso procurar o nivel.
                var selo = new TextBlock
                {
                    FontSize = 12, FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 1, 0)
                };
                Grid.SetColumn(selo, 2); grid.Children.Add(selo);

                _levelRows.Add(new LevelRow { Level = nivel, InUse = emUso, Names = nomes, Status = selo });
                host.Children.Add(grid);
            }

            LevelRowsList.Items.Clear();
            LevelRowsList.Items.Add(host);
        }

        /// <summary>
        /// Lê os tipos que existem de verdade no servidor e confere o mapa contra eles.
        ///
        /// Não assume nada sozinho: tipo cujo nome já casa com um nível é confirmado; tipo que
        /// não casa é LISTADO para a pessoa decidir. Adivinhar aqui montaria a hierarquia errada
        /// em silêncio, que é exatamente o risco deste recurso.
        ///
        /// Vale para os dois destinos — no Azure DevOps são os tipos de work item do projeto; no
        /// GitProject, as opções do campo de nível do board mais os Issue Types que os itens já
        /// usam.
        /// </summary>
        private async void OnLevelDetectClick(object sender, RoutedEventArgs e)
        {
            LevelDetectButton.IsEnabled = false;
            LevelStatus.Text = AppStrings.Get("Git_Testing");
            try
            {
                var mapa = MapaDaTela(out _);
                var tipos = new List<string>();
                var fonte = "";

                if (!string.IsNullOrWhiteSpace(_options.OrganizationUrl)
                    && !string.IsNullOrWhiteSpace(_options.TeamProject)
                    && !string.IsNullOrWhiteSpace(_options.PersonalAccessToken))
                {
                    var cores = await TfsImportService.ListWorkItemTypeColorsAsync(_options);
                    tipos.AddRange(cores.Keys);
                    fonte = _options.TeamProject!;
                }

                if (_options.GitProject?.IsConfigured == true)
                {
                    var snap = await new GitProjectClient(_options.GitProject).LoadAsync();
                    var campo = snap.Fields.FirstOrDefault(f =>
                        GitProjectOptions.Matches(f.Name, _options.GitProject.LevelFields));
                    if (campo != null) tipos.AddRange(campo.Options.Keys);
                    tipos.AddRange(snap.Items.Select(i => i.IssueType).Where(t => !string.IsNullOrWhiteSpace(t)));
                    fonte = string.IsNullOrEmpty(fonte) ? snap.Title : fonte + " + " + snap.Title;
                }

                tipos = tipos.Where(t => !string.IsNullOrWhiteSpace(t))
                             .Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
                if (tipos.Count == 0)
                {
                    LevelStatus.Text = AppStrings.Get("Fld_LevelsDetectNone");
                    return;
                }

                var porNivel = new List<string>();
                foreach (var nivel in NxLevelMap.AllLevels)
                {
                    var casados = tipos.Where(t => mapa.LevelOf(t) == nivel).ToList();
                    porNivel.Add($"{nivel}: " + (casados.Count > 0 ? string.Join(", ", casados)
                        : AppStrings.Get("Fld_LevelsNoType")));
                    CarimbaNivel(nivel, casados, mapa);
                }
                var sobrando = tipos.Where(t => mapa.LevelOf(t) == null).ToList();

                LevelStatus.Text = AppStrings.Get("Fld_LevelsDetectResult",
                    tipos.Count.ToString(), sobrando.Count.ToString());
                MessageBox.Show(this,
                    AppStrings.Get("Fld_LevelsDetectBody", fonte,
                        string.Join(Environment.NewLine + "   ", porNivel),
                        sobrando.Count == 0 ? "-" : string.Join(", ", sobrando)),
                    AppStrings.Get("Fld_LevelsDetect"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LevelStatus.Text = ex.Message;
            }
            finally { LevelDetectButton.IsEnabled = true; }
        }

        /// <summary>
        /// O selo da linha. Verde quando o destino tem um tipo que casa com o nível; vermelho
        /// quando não tem e o nível está em uso; traço quando o nível está desligado — aí não
        /// faltar tipo é o esperado, não um problema.
        /// </summary>
        private void CarimbaNivel(NxLevel nivel, List<string> casados, NxLevelMap mapa)
        {
            var linha = _levelRows.FirstOrDefault(r => r.Level == nivel);
            if (linha == null) return;

            var ligado = linha.InUse.IsChecked == true;
            linha.NoDestino = casados.Count > 0;

            if (!ligado)
            {
                linha.Status.Text = "–";
                linha.Status.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x80, 0x80, 0x80));
                linha.Status.ToolTip = AppStrings.Get("Fld_LevelSealOff");
                return;
            }

            linha.Status.Text = casados.Count > 0 ? "✔" : "✖";
            linha.Status.Foreground = new System.Windows.Media.SolidColorBrush(
                casados.Count > 0 ? System.Windows.Media.Color.FromRgb(0x10, 0x7C, 0x10)
                                  : System.Windows.Media.Color.FromRgb(0xC0, 0x30, 0x30));
            linha.Status.ToolTip = casados.Count > 0
                ? AppStrings.Get("Fld_LevelSealFound", string.Join(", ", casados))
                : AppStrings.Get("Fld_LevelSealMissing", PrimeiroNome(linha));
        }

        /// <summary>O nome que o NXProject usaria para CRIAR este nível (o primeiro da lista).</summary>
        private static string PrimeiroNome(LevelRow linha) =>
            (linha.Names.Text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0)
            ?? linha.Level.ToString();

        /// <summary>
        /// Cria no destino escolhido os níveis que a detecção marcou de vermelho.
        ///
        /// Os dois destinos são coisas diferentes, e é por isso que o botão pergunta onde:
        ///  • <b>GitProject</b>: o nível é uma opção do campo single-select do board. Dá para
        ///    criar o campo com as opções todas de uma vez — e é o que acontece aqui.
        ///  • <b>Azure DevOps</b>: o nível é um TIPO DE WORK ITEM, e criar tipo muda o processo
        ///    herdado da organização inteira, não só deste projeto. O NXProject não faz isso por
        ///    você: ele diz exatamente o que falta criar, e onde.
        /// </summary>
        private async void OnLevelCreateClick(object sender, RoutedEventArgs e)
        {
            var faltando = _levelRows
                .Where(r => r.InUse.IsChecked == true && r.NoDestino == false)
                .ToList();
            if (_levelRows.All(r => r.NoDestino == null))
            {
                LevelStatus.Text = AppStrings.Get("Fld_LevelsDetectFirst");
                return;
            }
            if (faltando.Count == 0)
            {
                LevelStatus.Text = AppStrings.Get("Fld_LevelsNothingToCreate");
                return;
            }

            var nomes = faltando.Select(PrimeiroNome).ToList();

            // Azure DevOps: tipo de work item é processo da organização — fora do alcance desta
            // tela, e dizer isso é mais útil do que um botão que falha no servidor.
            if (LevelTargetCombo.SelectedIndex != 1)
            {
                MessageBox.Show(this,
                    AppStrings.Get("Fld_LevelsDevOpsManual", string.Join(", ", nomes)),
                    AppStrings.Get("Fld_LevelsCreate"), MessageBoxButton.OK, MessageBoxImage.Information);
                LevelStatus.Text = AppStrings.Get("Fld_LevelsDevOpsStatus", nomes.Count.ToString());
                return;
            }

            var git = _options.GitProject;
            if (git?.IsConfigured != true)
            {
                LevelStatus.Text = AppStrings.Get("Fld_GitDetectNoConn");
                return;
            }

            LevelCreateButton.IsEnabled = false;
            LevelStatus.Text = AppStrings.Get("Git_Testing");
            try
            {
                var cliente = new GitProjectClient(git);
                var snap = await cliente.LoadAsync();
                var campo = snap.Fields.FirstOrDefault(f =>
                    GitProjectOptions.Matches(f.Name, git.LevelFields));

                if (campo != null)
                {
                    // O GitHub não deixa acrescentar opção a um single-select já criado: o que
                    // dá para fazer é dizer qual opção falta e onde acrescentá-la.
                    MessageBox.Show(this,
                        AppStrings.Get("Fld_LevelsGitExisting", campo.Name, string.Join(", ", nomes)),
                        AppStrings.Get("Fld_LevelsCreate"), MessageBoxButton.OK, MessageBoxImage.Information);
                    LevelStatus.Text = AppStrings.Get("Fld_LevelsGitExistingStatus", nomes.Count.ToString());
                    return;
                }

                // Campo ainda não existe: cria com TODAS as opções dos níveis em uso — opção de
                // single-select só pode ser definida na criação.
                var todas = _levelRows.Where(r => r.InUse.IsChecked == true).Select(PrimeiroNome).ToList();
                var nomeCampo = GitProjectOptions.Suggested(git.LevelFields);
                await cliente.CreateFieldAsync(snap.ProjectId, nomeCampo, "SINGLE_SELECT", todas);
                LevelStatus.Text = AppStrings.Get("Fld_LevelsGitCreated", nomeCampo, todas.Count.ToString());
                foreach (var r in _levelRows.Where(r => r.InUse.IsChecked == true)) r.NoDestino = true;
                foreach (var r in _levelRows) CarimbaNivel(r.Level, r.NoDestino == true
                    ? new List<string> { PrimeiroNome(r) } : new List<string>(), MapaDaTela(out _));
            }
            catch (Exception ex)
            {
                LevelStatus.Text = ex.Message;
            }
            finally { LevelCreateButton.IsEnabled = true; }
        }

        private void OnLevelDefaultsClick(object sender, RoutedEventArgs e)
        {
            foreach (var r in _levelRows)
            {
                r.Names.Text = string.Join(", ", NxLevelMap.DefaultNames(r.Level));
                r.InUse.IsChecked = true;
            }
            LevelStatus.Text = AppStrings.Get("Fld_DefaultsDone");
        }

        /// <summary>
        /// O que está na tela vira mapa. Nome repetido entre níveis é ambiguidade: o cronograma
        /// sairia torto em silêncio, então isso aparece como aviso antes de salvar.
        /// </summary>
        private NxLevelMap MapaDaTela(out List<string> ambiguidades)
        {
            var mapa = new NxLevelMap();
            foreach (var r in _levelRows)
            {
                var nomes = (r.Names.Text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                // Nível sem nenhum nome não reconheceria tipo algum: volta ao de fábrica.
                mapa.Names[r.Level] = nomes.Count > 0 ? nomes : NxLevelMap.DefaultNames(r.Level).ToList();
                if (r.InUse.IsChecked != true && r.Level != NxLevel.Story) mapa.Disabled.Add(r.Level);
            }
            ambiguidades = mapa.Ambiguities();
            return mapa;
        }

        // ── Criar no Azure DevOps, exportar e importar ───────────────────────────────────
        //
        // Isto morava na tela do NXProject-Setup, que listava os MESMOS campos desta aqui. Duas
        // telas para a mesma configuracao foi o que ja fez o catalogo divergir entre os dois
        // aplicativos — agora o Setup chama esta, e a lista existe uma vez so.

        /// <summary>O processo do DevOps lido na ultima deteccao: a criacao precisa dele.</summary>
        private DevOpsFieldSetupService.ProcessInfo? _processo;

        /// <summary>
        /// Cria no Azure DevOps os campos que a deteccao apontou como ausentes.
        ///
        /// O PAT vem da propria tela e NAO e gravado: criar campo mexe no processo da organizacao
        /// e exige "Work Items — Read, write & manage" (Project Collection Administrator), escopo
        /// que o token do dia a dia nao tem — e nao deve ter.
        /// </summary>
        private async void OnCreateFieldsClick(object sender, RoutedEventArgs e)
        {
            var org = CreateOrgBox.Text?.Trim() ?? "";
            var proj = CreateProjBox.Text?.Trim() ?? "";
            var pat = CreatePatBox.Password;

            if (string.IsNullOrWhiteSpace(org) || string.IsNullOrWhiteSpace(proj))
            {
                DetectFieldsStatus.Text = AppStrings.Get("Cfg_DetectNeedsConnection");
                return;
            }
            if (string.IsNullOrWhiteSpace(pat))
            {
                DetectFieldsStatus.Text = AppStrings.Get("Cfg_CreateNeedsPat");
                return;
            }

            CreateFieldsButton.IsEnabled = false;
            try
            {
                DetectFieldsStatus.Text = AppStrings.Get("Cfg_Detecting");
                _processo = await DevOpsFieldSetupService.LoadProcessAsync(org, proj, pat);

                // O que falta sai da CHECAGEM, nao de caixas marcadas a mao: a tela ja sabe o que
                // nao existe, e pedir para a pessoa remarcar isso era so uma chance de erro.
                var querer = _devRows
                    .Where(r => !string.IsNullOrWhiteSpace(r.Box.Text))
                    .Select(r => (r.Spec.Key, Name: r.Box.Text.Trim())).ToList();
                var checagem = await DevOpsFieldSetupService.CheckAsync(org, proj, pat, querer,
                    default, EscolhasPadrao());

                var faltando = checagem
                    .Where(c => c.Status is DevOpsFieldSetupService.FieldStatus.Missing
                                        or DevOpsFieldSetupService.FieldStatus.NeedsWorkItemTypes)
                    .Select(c => c.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

                var criar = _devRows
                    .Where(r => faltando.Contains(r.Spec.Key) && Habilitado(r))
                    .Select(r => (r.Spec.Key, Name: r.Box.Text.Trim()))
                    .ToList();

                if (criar.Count == 0)
                {
                    DetectFieldsStatus.Text = AppStrings.Get("Cfg_CreateNothing");
                    return;
                }

                // Confirmacao explicita: o campo passa a existir para TODOS os projetos do
                // processo, nao so para este.
                var lista = string.Join(Environment.NewLine, criar.Select(f => "  • " + f.Name));
                if (MessageBox.Show(this,
                        AppStrings.Get("Cfg_CreateConfirm", _processo.Name, lista),
                        AppStrings.Get("Cfg_CreateFields"), MessageBoxButton.OKCancel,
                        MessageBoxImage.Question) != MessageBoxResult.OK)
                {
                    DetectFieldsStatus.Text = "";
                    return;
                }

                var progresso = new Progress<string>(n => DetectFieldsStatus.Text =
                    AppStrings.Get("Cfg_Creating", n));
                var resultados = await DevOpsFieldSetupService.CreateAsync(
                    org, proj, pat, _processo, criar, progresso, default, EscolhasPadrao());

                var ok = resultados.Count(r => r.Ok);
                DetectFieldsStatus.Text = AppStrings.Get("Cfg_CreateDone",
                    ok.ToString(), (resultados.Count - ok).ToString());

                var problemas = resultados.Where(r => !r.Ok).Select(r => r.Message).ToList();
                if (problemas.Count > 0)
                    MessageBox.Show(this, string.Join(Environment.NewLine, problemas),
                        AppStrings.Get("Cfg_CreateFields"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                DetectFieldsStatus.Text = ex.Message;
            }
            finally { CreateFieldsButton.IsEnabled = true; }
        }

        /// <summary>Campo opcional desligado nao e criado: campo que ninguem preenche e sujeira.</summary>
        private static bool Habilitado(DevRow r) => r.Enabled == null || r.Enabled.IsChecked == true;

        /// <summary>
        /// Arquivo de configuracao de campos: o GP configura uma vez e distribui. Mesmo formato
        /// que o NXProject-Setup gravava, para os arquivos ja exportados continuarem valendo.
        /// </summary>
        private sealed class FieldConfigFile
        {
            // O nome do formato mudou de "nxproject-devops-fields" para "nxproject-fields"
            // quando o arquivo passou a levar tambem o GitProject e os niveis. A leitura aceita
            // os dois: arquivo que alguem ja exportou nao pode deixar de funcionar.
            public string Format { get; set; } = "nxproject-fields";
            public int Version { get; set; } = 2;
            public string ExportedAt { get; set; } = "";
            public string Organization { get; set; } = "";
            public string Project { get; set; } = "";

            // Azure DevOps
            public Dictionary<string, string> Fields { get; set; } = new();
            public List<string> TaskStandard { get; set; } = new();
            public Dictionary<string, bool> OptionalEnabled { get; set; } = new();

            // GitProject — nomes de campo, tags e classificacao. A CONEXAO (dono, board, token)
            // fica de fora de proposito: ela e de cada pessoa, e token nao viaja em arquivo.
            public Dictionary<string, string> GitFields { get; set; } = new();
            public Dictionary<string, string> GitTags { get; set; } = new();
            public List<GitProjectClassification> GitClassifications { get; set; } = new();

            // Faixa de prioridade aceita (P1..P9 por padrao). Viaja junto porque e a mesma
            // decisao de configuracao dos nomes: quem distribui o arquivo para a equipe espera
            // que a faixa combinada va junto, e nao que cada um redigite.
            public int PriorityMin { get; set; }
            public int PriorityMax { get; set; }

            // Mapa de niveis (vale para os dois destinos)
            public Dictionary<string, string> LevelNames { get; set; } = new();
            public List<string> LevelsDisabled { get; set; } = new();
        }

        /// <summary>Formatos aceitos na leitura — o antigo continua valendo.</summary>
        private static bool FormatoConhecido(string? f) =>
            string.Equals(f, "nxproject-fields", StringComparison.OrdinalIgnoreCase)
            || string.Equals(f, "nxproject-devops-fields", StringComparison.OrdinalIgnoreCase);

        private static readonly System.Text.Json.JsonSerializerOptions ConfigJson =
            new() { WriteIndented = true };

        private void OnExportFieldsClick(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = AppStrings.Get("Cfg_ExportFields"),
                FileName = "nxproject-devops-fields.json",
                DefaultExt = ".json",
                Filter = "JSON (*.json)|*.json"
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var mapaNiveis = _levelRows.Count > 0 ? MapaDaTela(out _) : (_options.Levels ?? new NxLevelMap());
                var dados = new FieldConfigFile
                {
                    ExportedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                    Organization = CreateOrgBox.Text?.Trim() ?? "",
                    Project = CreateProjBox.Text?.Trim() ?? "",
                    Fields = _devRows.ToDictionary(r => r.Spec.Key, r => r.Box.Text?.Trim() ?? ""),
                    TaskStandard = EscolhasPadrao().Select(c => c.Key + "|" + c.WorkItemType).ToList(),
                    OptionalEnabled = _devRows.Where(r => r.Enabled != null)
                        .ToDictionary(r => r.Spec.Key, r => r.Enabled!.IsChecked == true,
                                      StringComparer.OrdinalIgnoreCase),

                    GitFields = _rows.ToDictionary(r => r.Spec.Key, r => r.Box.Text?.Trim() ?? ""),
                    GitTags = new Dictionary<string, string>
                    {
                        ["fixedStart"] = GitFixedStartTagBox.Text?.Trim() ?? "",
                        ["unplanned"] = GitUnplannedTagBox.Text?.Trim() ?? "",
                        ["wip"] = GitWipTagBox.Text?.Trim() ?? "",
                        ["blocked"] = GitBlockedTagBox.Text?.Trim() ?? "",
                    },
                    GitClassifications = _gitClass
                        .Where(c => !string.IsNullOrWhiteSpace(c.Field)).ToList(),

                    PriorityMin = LeFaixa(_prioMinBox, 1),
                    PriorityMax = LeFaixa(_prioMaxBox, 9),

                    LevelNames = mapaNiveis.Names.ToDictionary(kv => kv.Key.ToString(),
                                                              kv => string.Join(", ", kv.Value)),
                    LevelsDisabled = mapaNiveis.Disabled.Select(d => d.ToString()).ToList(),
                };
                var json = System.Text.Json.JsonSerializer.Serialize(dados, ConfigJson);

                // Trava: o arquivo é feito para ser DISTRIBUÍDO, e token não viaja em arquivo. A
                // conferência é no texto final, não na lista de campos — assim ela continua
                // valendo se alguém acrescentar uma propriedade nova amanhã e não perceber que
                // ela carrega segredo.
                if (ContemSegredo(json))
                {
                    DetectFieldsStatus.Text = AppStrings.Get("Cfg_ExportHasSecret");
                    return;
                }

                System.IO.File.WriteAllText(dlg.FileName, json);
                DetectFieldsStatus.Text = AppStrings.Get("Cfg_Exported", dlg.FileName);
            }
            catch (Exception ex) { DetectFieldsStatus.Text = ex.Message; }
        }

        /// <summary>
        /// O texto a exportar contém algum token desta máquina? Compara com os tokens que a
        /// configuração guarda (PAT do DevOps e token do GitProject). Nenhum deles deveria estar
        /// lá — esta é a rede para o dia em que estiver.
        /// </summary>
        private bool ContemSegredo(string json)
        {
            foreach (var segredo in new[] { _options.PersonalAccessToken, _options.GitProject?.Token })
                if (!string.IsNullOrWhiteSpace(segredo) && segredo!.Length >= 8
                    && json.Contains(segredo, StringComparison.Ordinal))
                    return true;
            return false;
        }

        private void OnImportFieldsClick(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = AppStrings.Get("Cfg_ImportFields"),
                DefaultExt = ".json",
                Filter = "JSON (*.json)|*.json"
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var dados = System.Text.Json.JsonSerializer.Deserialize<FieldConfigFile>(
                    System.IO.File.ReadAllText(dlg.FileName), ConfigJson);

                // Arquivo de outro formato nao e importado "pelo que der certo": meio importado
                // deixaria a configuracao num estado que ninguem sabe qual e.
                if (dados == null || !FormatoConhecido(dados.Format))
                {
                    DetectFieldsStatus.Text = AppStrings.Get("Cfg_ImportBadFile");
                    return;
                }

                foreach (var r in _devRows)
                {
                    if (dados.Fields.TryGetValue(r.Spec.Key, out var nome) && !string.IsNullOrWhiteSpace(nome))
                        r.Box.Text = nome.Trim();
                    if (r.Enabled != null && dados.OptionalEnabled.TryGetValue(r.Spec.Key, out var ligado))
                        r.Enabled.IsChecked = ligado;
                }

                var padrao = dados.TaskStandard
                    .Select(x => x.Split('|')).Where(p => p.Length == 2)
                    .Select(p => (Chave: p[0], Tipo: p[1])).ToList();
                foreach (var r in _devRows)
                    foreach (var (wit, caixa) in r.UseStandard)
                        caixa.IsChecked = padrao.Any(p =>
                            string.Equals(p.Chave, r.Spec.Key, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(p.Tipo, wit, StringComparison.OrdinalIgnoreCase));

                // Faixa de prioridade: arquivo antigo nao tem (zero), e ai nao se mexe.
                if (dados.PriorityMin >= 1 && _prioMinBox != null) _prioMinBox.Text = "P" + dados.PriorityMin;
                if (dados.PriorityMax >= 1 && _prioMaxBox != null) _prioMaxBox.Text = "P" + dados.PriorityMax;

                // GitProject: nomes, tags e classificacao.
                foreach (var r in _rows)
                    if (dados.GitFields.TryGetValue(r.Spec.Key, out var nomeGit)
                        && !string.IsNullOrWhiteSpace(nomeGit))
                        r.Box.Text = nomeGit.Trim();

                if (dados.GitTags.TryGetValue("fixedStart", out var t1) && t1.Length > 0) GitFixedStartTagBox.Text = t1;
                if (dados.GitTags.TryGetValue("unplanned", out var t2) && t2.Length > 0) GitUnplannedTagBox.Text = t2;
                if (dados.GitTags.TryGetValue("wip", out var t3) && t3.Length > 0) GitWipTagBox.Text = t3;
                if (dados.GitTags.TryGetValue("blocked", out var t4) && t4.Length > 0) GitBlockedTagBox.Text = t4;

                if (dados.GitClassifications.Count > 0)
                {
                    _gitClass.Clear();
                    foreach (var c in dados.GitClassifications) _gitClass.Add(c);
                }

                // Niveis: so quando o arquivo traz, para nao zerar o mapa de quem importa um
                // arquivo antigo (formato 1, que nao tinha esta parte).
                if (dados.LevelNames.Count > 0 && _levelRows.Count > 0)
                    foreach (var r in _levelRows)
                    {
                        if (dados.LevelNames.TryGetValue(r.Level.ToString(), out var nomes)
                            && !string.IsNullOrWhiteSpace(nomes))
                            r.Names.Text = nomes;
                        r.InUse.IsChecked = !dados.LevelsDisabled.Contains(r.Level.ToString(),
                            StringComparer.OrdinalIgnoreCase);
                    }

                DetectFieldsStatus.Text = AppStrings.Get("Cfg_Imported", dlg.FileName);
            }
            catch (Exception ex) { DetectFieldsStatus.Text = ex.Message; }
        }

        /// <summary>
        /// Cria no board os campos que faltam — os do NXProject e os de classificação.
        ///
        /// É o equivalente do "Criar campos" do NXProject-Setup, e o motivo é o mesmo: sem os
        /// campos, o import traz o board sem HH e sem datas e ninguém sabe por quê. A diferença
        /// vem do GitHub: campo é do BOARD, não do item, e o tipo é definitivo — por isso a tela
        /// mostra o que vai criar, com tipo e valores, e espera confirmação.
        /// </summary>
        private async void OnGitCreateClick(object sender, RoutedEventArgs e)
        {
            var git = _options.GitProject;
            if (git?.IsConfigured != true)
            {
                GitDetectStatus.Text = AppStrings.Get("Fld_GitDetectNoConn");
                return;
            }

            GitCreateButton.IsEnabled = GitDetectButton.IsEnabled = false;
            GitDetectStatus.Text = AppStrings.Get("Git_Testing");
            try
            {
                // Token digitado manda, e não é gravado: serve a quem configurou um token de
                // leitura no dia a dia e tem outro, com escrita, só para preparar o board.
                var digitado = GitCreateTokenBox.Password;
                if (!string.IsNullOrWhiteSpace(digitado))
                {
                    git = git.Clone();
                    git.Token = digitado.Trim();
                }

                var cliente = new GitProjectClient(git);
                var snap = await cliente.LoadAsync();

                bool Existe(string nome) =>
                    snap.FieldNames.Any(n => string.Equals(n, nome.Trim(), StringComparison.CurrentCultureIgnoreCase));

                // (nome, tipo na API, opcoes) do que falta. O nome e o que esta NA CAIXA: e ele
                // que o conector vai procurar depois.
                var criar = new List<(string Nome, string Tipo, string[] Opcoes)>();

                foreach (var row in _rows)
                {
                    var nome = row.Box.Text.Trim();
                    if (nome.Length == 0) continue;
                    if (snap.FieldNames.Any(n => GitProjectOptions.Matches(n, NomesGit(row.Spec)))) continue;
                    criar.Add((nome, GitProjectOptions.FieldDataType(row.Spec.Key),
                               GitProjectOptions.DefaultOptions(row.Spec.Key)));
                }

                foreach (var c in _gitClass.Where(c => !string.IsNullOrWhiteSpace(c.Field)))
                {
                    var nome = c.Field.Trim();
                    if (Existe(nome) || criar.Any(x => string.Equals(x.Nome, nome, StringComparison.CurrentCultureIgnoreCase)))
                        continue;
                    var valores = (c.Values ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                                                 .Select(v => v.Trim()).Where(v => v.Length > 0).ToArray();
                    criar.Add((nome, GitProjectOptions.DataTypeOf(c.FieldType), valores));
                }

                if (criar.Count == 0)
                {
                    GitDetectStatus.Text = AppStrings.Get("Fld_GitCreateNone");
                    return;
                }

                var resumo = string.Join(Environment.NewLine + "   ", criar.Select(x =>
                    $"{x.Nome} ({x.Tipo}{(x.Opcoes.Length > 0 ? ": " + string.Join(", ", x.Opcoes) : "")})"));
                if (MessageBox.Show(this, AppStrings.Get("Fld_GitCreateConfirm", snap.Title, resumo),
                        AppStrings.Get("Fld_GitCreate"), MessageBoxButton.OKCancel,
                        MessageBoxImage.Question) != MessageBoxResult.OK)
                {
                    GitDetectStatus.Text = "";
                    return;
                }

                var feitos = 0;
                var erros = new List<string>();
                foreach (var x in criar)
                {
                    try
                    {
                        await cliente.CreateFieldAsync(snap.ProjectId, x.Nome, x.Tipo, x.Opcoes);
                        feitos++;
                        // Criado: o selo da linha vira ✔ na hora, sem precisar detectar de novo.
                        var linha = _rows.FirstOrDefault(r =>
                            string.Equals(r.Box.Text.Trim(), x.Nome, StringComparison.CurrentCultureIgnoreCase));
                        if (linha != null)
                        {
                            linha.NoBoard = true;
                            linha.Status.Text = "✔";
                            linha.Status.Foreground = new System.Windows.Media.SolidColorBrush(
                                System.Windows.Media.Color.FromRgb(0x10, 0x7C, 0x10));
                            linha.Status.ToolTip = AppStrings.Get("Fld_GitRowFound", x.Nome, snap.Title);
                        }
                    }
                    // Um campo que falha nao derruba os outros: o board fica com o que deu certo,
                    // e a mensagem diz exatamente qual nao foi.
                    catch (Exception ex) { erros.Add($"{x.Nome}: {ex.Message}"); }
                }

                GitDetectStatus.Text = AppStrings.Get("Fld_GitCreateDone", feitos.ToString(), erros.Count.ToString());
                if (erros.Count > 0)
                    MessageBox.Show(this, string.Join(Environment.NewLine, erros),
                        AppStrings.Get("Fld_GitCreate"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                GitDetectStatus.Text = ex.Message;
            }
            finally { GitCreateButton.IsEnabled = GitDetectButton.IsEnabled = true; }
        }

        /// <summary>
        /// Uma linha do DevOps: o nome do campo, a caixa que LIGA o recurso (quando ele é
        /// opcional) e as caixas de "usar o campo padrão do processo" em cada tipo que tem um.
        /// </summary>
        private sealed class DevRow
        {
            public NxFieldSpec Spec { get; init; } = new();
            public TextBox Box { get; init; } = new();
            public CheckBox? Enabled { get; init; }

            /// <summary>
            /// Selo do "Detectar": ✔ existe, ✖ falta, ⛔ existe com tipo incompatível. O detalhe
            /// (tipo, apelido que casou, em que work item falta) vai no ToolTip — na linha fica
            /// o que se lê de relance.
            /// </summary>
            public TextBlock Status { get; init; } = new();
            public Dictionary<string, CheckBox> UseStandard { get; } = new(StringComparer.CurrentCultureIgnoreCase);

            /// <summary>
            /// O NOME do campo neste tipo. Marcado "usar o padrao", mostra o campo nativo e fica
            /// so de leitura; desmarcado, e onde se digita o campo proprio daquele tipo — hoje o
            /// nativo e o default, mas o processo pode nao te-lo (a Task do Scrum e o caso).
            /// </summary>
            public Dictionary<string, TextBox> TypeBox { get; } = new(StringComparer.CurrentCultureIgnoreCase);
        }

        private readonly List<DevRow> _devRows = new();

        /// <summary>Campos cujo recurso é opcional: a caixa liga o campo e o que ele alimenta.</summary>
        private static readonly string[] Opcionais = { "epicType", "approved", "blockDuration", "admGroup" };

        /// <summary>
        /// O conceito "HH original" — que a tela NAO mostra em linha separada porque ele divide o
        /// campo com o HH estimado: ate a atividade comecar, os dois sao o mesmo numero; depois,
        /// o campo guarda o plano e o andamento vai para HH Atual e HH Restante.
        /// </summary>
        private static bool EhHoraOriginal(NxFieldSpec spec) =>
            string.Equals(spec.Key, "originalHours", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// As duas caixas da faixa de prioridade. Nascem com a LINHA do campo, e nao no XAML,
        /// porque e ao lado do campo que elas limitam que elas querem dizer alguma coisa.
        /// </summary>
        private TextBox? _prioMinBox;
        private TextBox? _prioMaxBox;

        /// <summary>Lê "P3" ou "3" como 3; qualquer outra coisa fica com o padrão.</summary>
        private static int LeFaixa(TextBox? caixa, int padrao)
        {
            var txt = (caixa?.Text ?? "").Trim().TrimStart('P', 'p');
            return int.TryParse(txt, out var v) && v >= 1 && v <= 99 ? v : padrao;
        }

        /// <summary>
        /// Monta a aba do Azure DevOps a partir do catálogo — a mesma lista da aba do GitProject.
        /// Antes era um mosaico de grades, e a caixa que liga um campo opcional ficava longe do
        /// campo que ela liga.
        /// </summary>
        private void CarregaDevOps()
        {
            var saved = _options;

            // A combo nasce no padrao que a configuracao JA usa, e e posicionada ANTES de montar
            // as linhas: se fosse depois, o evento dela reescreveria os nomes salvos.
            NamingCombo.SelectedIndex = EhPadraoIngles(saved) ? 1 : 0;

            _devRows.Clear();
            var host = new StackPanel();

            foreach (var spec in DevOpsFields)
            {
                // HH original NAO tem linha propria: ele e o MESMO numero do HH estimado em
                // outro momento (ver docs/hh-modelo.pt-BR.md). Duas caixas com o mesmo nome so
                // davam a impressao de dois campos — e a chance de elas discordarem.
                if (EhHoraOriginal(spec)) continue;
                var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                CheckBox? liga = null;
                UIElement esquerda;
                if (Opcionais.Contains(spec.Key))
                {
                    // Campo opcional: a caixa fica NA LINHA dele, e desligada ela apaga o campo —
                    // nome preenchido com recurso desligado só enganava quem lia a tela.
                    liga = new CheckBox
                    {
                        Content = Conceito(spec), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center,
                        ToolTip = spec.Purpose, IsChecked = EstaLigado(spec.Key, saved)
                    };
                    liga.Checked += (_, _) => AplicaLinha(spec.Key);
                    liga.Unchecked += (_, _) => AplicaLinha(spec.Key);
                    esquerda = liga;
                }
                else
                {
                    esquerda = new TextBlock
                    {
                        Text = Conceito(spec), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center,
                        ToolTip = spec.Purpose
                    };
                }
                Grid.SetColumn(esquerda, 0); grid.Children.Add(esquerda);

                var caixa = new TextBox
                {
                    Text = NomeDevOps(spec, saved), Height = 22, FontSize = 11,
                    Margin = new Thickness(0, 0, 8, 0), IsEnabled = liga == null || liga.IsChecked == true
                };
                Grid.SetColumn(caixa, 1); grid.Children.Add(caixa);

                // O selo divide a coluna do nome com a caixa: fica logo ao lado do campo a que
                // se refere, sem mexer no resto do layout.
                var selo = new TextBlock
                {
                    FontSize = 12, FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 1, 0)
                };
                Grid.SetColumn(selo, 1); grid.Children.Add(selo);

                var opcoes = new WrapPanel { Orientation = Orientation.Horizontal };
                var row = new DevRow { Spec = spec, Box = caixa, Enabled = liga, Status = selo };
                foreach (var wit in spec.Scope)
                {
                    var nativo = spec.OptionalStandardFor(wit);
                    if (string.IsNullOrEmpty(nativo)) continue;
                    var usar = new CheckBox
                    {
                        // A chave do Setup nao existe aqui: usar a do NXProject, senao a tela mostra o
                        // proprio nome da chave — foi o que aconteceu.
                        Content = AppStrings.Get("Fld_UseStdOnType", wit, NxDevOpsFieldCatalog.FriendlyName(nativo)),
                        FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 10, 0),
                        ToolTip = AppStrings.Get("Cfg_TaskStdTip"),
                        // Sem nada configurado ainda, a caixa nasce como o NX JA se comporta: na
                        // Task ele usa o campo nativo (Original Estimate / Completed Work) mesmo no
                        // padrao portugues, porque quem nomeia esse campo e o processo do DevOps,
                        // nao o NXProject. Nascer desmarcada dizia o contrario do que acontecia.
                        IsChecked = saved.TypeFieldMappings.TryGetValue(wit, out var cfg)
                                    && CampoDoTipo(cfg, spec.Key) is { } jaConfig
                            ? !string.IsNullOrWhiteSpace(jaConfig)
                            : spec.StandardDefaultOn(wit)
                    };
                    var nomeNoTipo = new TextBox
                    {
                        Width = 150, Height = 22, FontSize = 11, Margin = new Thickness(0, 0, 10, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        ToolTip = AppStrings.Get("Fld_TypeFieldTip", wit)
                    };
                    void AplicaNoTipo()
                    {
                        var marcado = usar.IsChecked == true;
                        nomeNoTipo.IsEnabled = !marcado;
                        if (marcado) nomeNoTipo.Text = NxDevOpsFieldCatalog.FriendlyName(nativo);
                        else if (string.IsNullOrWhiteSpace(nomeNoTipo.Text) || nomeNoTipo.Text == NxDevOpsFieldCatalog.FriendlyName(nativo))
                            nomeNoTipo.Text = caixa.Text;   // parte do nome da Story e segue editavel
                    }
                    nomeNoTipo.Text = saved.TypeFieldMappings.TryGetValue(wit, out var cfgTipo)
                                      && CampoDoTipo(cfgTipo, spec.Key) is { Length: > 0 } nomeSalvo
                                      && !nomeSalvo.Contains('.')
                        ? nomeSalvo : "";
                    AplicaNoTipo();
                    usar.Checked += (_, _) => AplicaNoTipo();
                    usar.Unchecked += (_, _) => AplicaNoTipo();

                    row.UseStandard[wit] = usar;
                    row.TypeBox[wit] = nomeNoTipo;
                    opcoes.Children.Add(usar);
                    opcoes.Children.Add(nomeNoTipo);
                }
                // Campo de lista fechada (EPIC_TYPE, por exemplo) não tem caixa de "usar o campo
                // padrão": a coluna fica livre para dizer QUE VALORES ele aceita.
                if (RotuloValores(spec) is { } valores) opcoes.Children.Add(valores);
                // A faixa aceita e EDITAVEL e mora na propria linha: ela e tao parte da
                // prioridade quanto o nome do campo, e, separada la embaixo, ninguem ligava uma
                // coisa na outra. O formulario do DevOps vai de 1 a 9, mas a instalacao PADRAO
                // para em 4 — dai o aviso ao lado, em vez de um limite escondido no codigo.
                if (string.Equals(spec.Key, "priority", StringComparison.OrdinalIgnoreCase))
                {
                    var faixa = TaskPriorityRange.FromOptions(saved);
                    TextBox CaixaFaixa(int valor) => new()
                    {
                        Text = "P" + valor, Width = 46, Height = 22, FontSize = 11,
                        Margin = new Thickness(4, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center,
                        ToolTip = AppStrings.Get("Cfg_TaskPrioRangeTip")
                    };
                    _prioMinBox = CaixaFaixa(faixa.Min);
                    _prioMaxBox = CaixaFaixa(faixa.Max);
                    TextBlock Rotulo(string chave, double esquerda) => new()
                    {
                        Text = AppStrings.Get(chave), FontSize = 10.5,
                        Foreground = System.Windows.Media.Brushes.Gray,
                        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(esquerda, 0, 0, 0)
                    };
                    opcoes.Children.Add(Rotulo("Cfg_TaskPrioMin", 8));
                    opcoes.Children.Add(_prioMinBox);
                    opcoes.Children.Add(Rotulo("Cfg_TaskPrioMax", 10));
                    opcoes.Children.Add(_prioMaxBox);
                    opcoes.Children.Add(Rotulo("Fld_PrioStdLimit", 8));
                }
                Grid.SetColumn(opcoes, 2); grid.Children.Add(opcoes);

                _devRows.Add(row);
                host.Children.Add(grid);
            }

            DevRowsList.Items.Clear();
            DevRowsList.Items.Add(host);

            // A criacao usa estes campos; comecam com o que ja estiver configurado e podem ser
            // trocados na hora (o Setup manda para ca quem ainda nao configurou nada).
            CreateOrgBox.Text = saved.OrganizationUrl ?? "";
            CreateProjBox.Text = saved.TeamProject ?? "";

            SyncVersionFieldBox.Text = saved.SyncVersionFieldName;
            SyncNameFieldBox.Text = saved.SyncNameFieldName;
            FixedStartTagBox.Text = saved.FixedStartTagName;
            UnplannedTagBox.Text = saved.UnplannedTagName;
            WipTagBox.Text = saved.WipTagName;
            BlockedTagBox.Text = saved.BlockedTagName;
            SyncPredecessorLinksCheck.IsChecked = saved.SyncPredecessorLinks;
            EnforceStoryCompletionWithTasksCheck.IsChecked = saved.EnforceStoryCompletionWithTasks;
            FutureSprintDaysBox.Text = saved.FutureSprintDays.ToString();

            foreach (var f in saved.ExtraCreateFields)
                _extraFields.Add(new ExtraWorkItemField { Ref = f.Ref, Value = f.Value });
            ExtraFieldsList.ItemsSource = _extraFields;

            foreach (var kv in saved.TypeFieldMappings)
                foreach (var def in kv.Value.CustomDevopsFields ?? new List<ClassificationFieldDef>())
                    _classificationMappings.Add(new TfsDevOpsConfigWindow.ClassificationMapping
                    {
                        DevOpsType = kv.Key == "*" ? "Todos" : kv.Key,
                        FieldRef = def.Field,
                        FieldType = string.IsNullOrWhiteSpace(def.FieldType) ? "Picklist" : def.FieldType,
                        Values = def.Values ?? string.Empty,
                        DefaultValue = def.DefaultValue ?? string.Empty
                    });
            if (_classificationMappings.Count == 0)
                _classificationMappings.Add(new TfsDevOpsConfigWindow.ClassificationMapping());
            ClassificationMappingsList.ItemsSource = _classificationMappings;
        }

        /// <summary>
        /// A configuracao salva esta no padrao ingles? Decide pela data de inicio, que e o campo
        /// onde os dois padroes mais divergem: no EN ela e o nativo do processo (ou o nome em
        /// ingles), no PT e o campo proprio do NXProject.
        /// </summary>
        private static bool EhPadraoIngles(TfsConnectionOptions o)
        {
            var pt = Find("start")?.DefaultName ?? "";
            return !string.IsNullOrWhiteSpace(o.StartFieldName)
                   && !string.Equals(o.StartFieldName.Trim(), pt.Trim(), StringComparison.CurrentCultureIgnoreCase);
        }

        /// <summary>Caixa do campo opcional segue o liga/desliga da linha.</summary>
        private void AplicaLinha(string chave)
        {
            var row = _devRows.FirstOrDefault(r => r.Spec.Key == chave);
            if (row?.Enabled != null) row.Box.IsEnabled = row.Enabled.IsChecked == true;
        }

        private static string NomeCurto(string referencia) =>
            referencia.Contains('.') ? referencia[(referencia.LastIndexOf('.') + 1)..] : referencia;

        private static string? CampoDoTipo(TypeFieldConfig cfg, string chave) => chave switch
        {
            "effort" => cfg.EffortField,
            "start" => cfg.StartField,
            "finish" => cfg.FinishField,
            "originalHours" => cfg.OriginalHoursField,
            "remainingHours" => cfg.RemainingHoursField,
            "currentHours" => cfg.CurrentHoursField,
            _ => null
        };

        private static bool EstaLigado(string chave, TfsConnectionOptions o) => chave switch
        {
            "epicType" => o.EpicTypeFieldEnabled,
            "approved" => o.ApprovedFieldEnabled,
            "blockDuration" => o.BlockDurationFieldEnabled,
            "admGroup" => o.AdmGroupFieldEnabled,
            _ => true
        };

        private static string NomeDevOps(NxFieldSpec spec, TfsConnectionOptions o) => spec.Key switch
        {
            "effort" => o.EffortFieldName,
            "originalHours" => o.OriginalHoursFieldName,
            "remainingHours" => o.RemainingHoursFieldName,
            "currentHours" => o.CurrentHoursFieldName,
            "start" => o.StartFieldName,
            "finish" => o.FinishFieldName,
            "percAloc" => o.PercAlocFieldName,
            "percConclusao" => o.PercConclusaoFieldName,
            "priority" => o.PriorityFieldName,
            "syncVersion" => o.SyncVersionFieldName,
            "syncName" => o.SyncNameFieldName,
            "approved" => o.ApprovedFieldName,
            "epicType" => o.EpicTypeFieldName,
            "admGroup" => o.AdmGroupFieldName,
            "blockDuration" => o.BlockDurationFieldName,
            _ => spec.DefaultName
        };

        /// <summary>Grava a aba do Azure DevOps.</summary>
        private void SalvaDevOps()
        {
            string Txt(TextBox b, string padrao) => string.IsNullOrWhiteSpace(b.Text) ? padrao : b.Text.Trim();

            foreach (var row in _devRows)
            {
                var v = Txt(row.Box, row.Spec.DefaultName);
                var ligado = row.Enabled == null || row.Enabled.IsChecked == true;
                switch (row.Spec.Key)
                {
                    // O HH original acompanha o HH estimado: e o mesmo campo, e a tela mostra
                    // uma linha so. Quem precisar separa-los aponta o outro campo no arquivo de
                    // configuracao — na tela, dois nomes iguais nao eram escolha, eram ruido.
                    case "effort": _options.EffortFieldName = v; _options.OriginalHoursFieldName = v; break;
                    case "originalHours": _options.OriginalHoursFieldName = v; break;
                    case "remainingHours": _options.RemainingHoursFieldName = v; break;
                    case "currentHours": _options.CurrentHoursFieldName = v; break;
                    case "start": _options.StartFieldName = v; break;
                    case "finish": _options.FinishFieldName = v; break;
                    case "percAloc": _options.PercAlocFieldName = v; break;
                    case "percConclusao": _options.PercConclusaoFieldName = v; break;
                    case "priority": _options.PriorityFieldName = v; break;
                    case "syncVersion": _options.SyncVersionFieldName = v; break;
                    case "syncName": _options.SyncNameFieldName = v; break;
                    case "approved": _options.ApprovedFieldName = v; _options.ApprovedFieldEnabled = ligado; break;
                    case "epicType": _options.EpicTypeFieldName = v; _options.EpicTypeFieldEnabled = ligado; break;
                    case "admGroup": _options.AdmGroupFieldName = v; _options.AdmGroupFieldEnabled = ligado; break;
                    case "blockDuration": _options.BlockDurationFieldName = v; _options.BlockDurationFieldEnabled = ligado; break;
                }
            }

            var prioMin = LeFaixa(_prioMinBox, 1);
            var prioMax = Math.Max(prioMin, LeFaixa(_prioMaxBox, 9));
            _options.TaskPriorityMin = prioMin;
            _options.TaskPriorityMax = prioMax;
            // "Faixa personalizada" deixa de ser uma caixa a marcar: faixa diferente da do
            // formulario do DevOps (1 a 9) JA E a personalizacao, e dizer isso em dois lugares
            // so dava chance de os dois discordarem.
            _options.TaskPriorityRangeEnabled = prioMin != 1 || prioMax != 9;
            _options.SyncVersionFieldName = Txt(SyncVersionFieldBox, "Sync_version");
            _options.SyncNameFieldName = Txt(SyncNameFieldBox, "Sync_Name");
            _options.FixedStartTagName = Txt(FixedStartTagBox, "DT-INI-NEG");
            _options.UnplannedTagName = Txt(UnplannedTagBox, "NP");
            _options.WipTagName = Txt(WipTagBox, "WIP");
            _options.BlockedTagName = Txt(BlockedTagBox, "BLOCK");
            _options.SyncPredecessorLinks = SyncPredecessorLinksCheck.IsChecked == true;
            _options.EnforceStoryCompletionWithTasks = EnforceStoryCompletionWithTasksCheck.IsChecked == true;
            _options.FutureSprintDays = int.TryParse(FutureSprintDaysBox.Text?.Trim(), out var fd) && fd >= 0 ? fd : 90;
            _options.ExtraCreateFields = _extraFields
                .Where(f => !string.IsNullOrWhiteSpace(f.Ref))
                .Select(f => new ExtraWorkItemField { Ref = f.Ref.Trim(), Value = f.Value?.Trim() ?? "" })
                .ToList();

            var mappings = new Dictionary<string, TypeFieldConfig>(_options.TypeFieldMappings, StringComparer.OrdinalIgnoreCase);
            foreach (var cfg in mappings.Values) cfg.CustomDevopsFields = [];

            foreach (var row in _devRows)
                foreach (var (wit, caixa) in row.UseStandard)
                {
                    if (!mappings.TryGetValue(wit, out var cfg)) mappings[wit] = cfg = new TypeFieldConfig();
                    // Marcado: grava a referencia do campo NATIVO. Desmarcado: grava o nome
                    // digitado para aquele tipo, e so cai no campo global quando esta em branco.
                    string? valorNoTipo;
                    if (caixa.IsChecked == true) valorNoTipo = row.Spec.OptionalStandardFor(wit);
                    else
                    {
                        var digitado = row.TypeBox.TryGetValue(wit, out var bx) ? bx.Text?.Trim() : null;
                        valorNoTipo = string.IsNullOrWhiteSpace(digitado) ? null : digitado;
                    }
                    switch (row.Spec.Key)
                    {
                        case "effort": cfg.EffortField = valorNoTipo; break;
                        case "start": cfg.StartField = valorNoTipo; break;
                        case "finish": cfg.FinishField = valorNoTipo; break;
                        case "originalHours": cfg.OriginalHoursField = valorNoTipo; break;
                        case "remainingHours": cfg.RemainingHoursField = valorNoTipo; break;
                        case "currentHours": cfg.CurrentHoursField = valorNoTipo; break;
                    }
                }

            foreach (var g in _classificationMappings
                         .Where(m => !string.IsNullOrWhiteSpace(m.FieldRef))
                         .GroupBy(m => string.Equals(m.DevOpsType, "Todos", StringComparison.OrdinalIgnoreCase) ? "*" : m.DevOpsType,
                                  StringComparer.OrdinalIgnoreCase))
            {
                if (!mappings.TryGetValue(g.Key, out var cfg)) cfg = new TypeFieldConfig();
                cfg.CustomDevopsFields = g.Select(m => new ClassificationFieldDef
                {
                    Field = m.FieldRef.Trim(),
                    FieldType = string.IsNullOrWhiteSpace(m.FieldType) ? "Picklist" : m.FieldType.Trim(),
                    Values = string.IsNullOrWhiteSpace(m.Values) ? null : m.Values.Trim(),
                    DefaultValue = string.IsNullOrWhiteSpace(m.DefaultValue) ? null : m.DefaultValue.Trim()
                }).ToList();
                mappings[g.Key] = cfg;
            }
            _options.TypeFieldMappings = mappings;
        }

        /// <summary>
        /// O padrão de nomes (PT/EN) move as caixas de "usar o campo padrão" e renomeia o que não
        /// tem nativo. Não renomeia nada no servidor: muda o que esta tela passa a usar.
        /// </summary>
        private void OnNamingChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_devRows.Count == 0) return;
            var naming = NamingCombo?.SelectedIndex == 1
                ? NxFieldNaming.English : NxFieldNaming.Portuguese;
            var ingles = naming == NxFieldNaming.English;

            foreach (var row in _devRows)
            {
                foreach (var (wit, caixa) in row.UseStandard)
                {
                    caixa.IsChecked = ingles || row.Spec.StandardDefaultOn(wit);
                    // A caixa do tipo acompanha: marcada mostra o nativo, desmarcada volta ao nome
                    // de fabrica do padrao escolhido — senao ficava um nome em portugues ao lado
                    // de uma configuracao em ingles.
                    if (row.TypeBox.TryGetValue(wit, out var bxTipo))
                    {
                        var nativo = row.Spec.OptionalStandardFor(wit);
                        bxTipo.IsEnabled = caixa.IsChecked != true;
                        bxTipo.Text = caixa.IsChecked == true
                            ? NxDevOpsFieldCatalog.FriendlyName(nativo)
                            : DefaultFieldFor(row.Spec.Key, wit, naming) is { Length: > 0 } v2 && !v2.Contains('.')
                                ? v2 : row.Spec.DefaultName;
                    }
                }

                // O nome do campo da STORY segue o padrao escolhido. Antes a busca usava o
                // primeiro tipo do escopo, que para os campos de horas e a Story: com a Task
                // tambem no escopo, fixar "User Story" mantem a caixa da esquerda sendo a dela.
                var valor = DefaultFieldFor(row.Spec.Key, "User Story", naming);
                // O padrao ingles pode responder com a REFERENCIA do campo nativo
                // ("Microsoft.VSTS.Scheduling.OriginalEstimate"). Na caixa vai o nome amigavel
                // dele ("Original Estimate"), que e como o campo aparece no DevOps — antes a
                // referencia era descartada e a caixa ficava com o nome em portugues, dizendo o
                // contrario do padrao escolhido.
                if (!string.IsNullOrWhiteSpace(valor))
                    row.Box.Text = valor.Contains('.') ? NxDevOpsFieldCatalog.FriendlyName(valor) : valor;
            }
        }

        /// <summary>
        /// O que está marcado como "usar o campo padrão", por tipo. A detecção precisa disso para
        /// não acusar como ausente um campo que a organização resolveu pelo nativo.
        /// </summary>
        private List<DevOpsFieldSetupService.StandardChoice> EscolhasPadrao() =>
            _devRows.SelectMany(r => r.UseStandard
                        .Where(kv => kv.Value.IsChecked == true)
                        .Select(kv => new DevOpsFieldSetupService.StandardChoice(r.Spec.Key, kv.Key)))
                    .ToList();

        private void OnAddGitClass(object sender, RoutedEventArgs e)
            => _gitClass.Add(new GitProjectClassification());

        private void OnRemoveGitClass(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is GitProjectClassification c) _gitClass.Remove(c);
        }



        private static string? Nativo(string chave, string tipo) =>
            NxDevOpsFieldCatalog.Find(chave)?.OptionalStandardFor(tipo);

        private NxFieldNaming Naming => NamingCombo?.SelectedIndex == 1
            ? NxFieldNaming.English : NxFieldNaming.Portuguese;

        private void BuildRows()
        {
            _rows.Clear();
            var host = new StackPanel();
            foreach (var spec in GitProjectFields)
            {
                if (EhHoraOriginal(spec)) continue;   // mesma razao da aba do DevOps
                var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });

                var nome = new TextBlock
                {
                    Text = Conceito(spec), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0), ToolTip = spec.Purpose
                };
                Grid.SetColumn(nome, 0); grid.Children.Add(nome);

                var caixa = new TextBox
                {
                    Text = ValorAtual(spec), Height = 22, FontSize = 11,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                Grid.SetColumn(caixa, 1); grid.Children.Add(caixa);

                // Onde o campo vive: tipos de work item no DevOps; no GitHub é sempre o board,
                // porque lá campo é do projeto, não do item — e isso muda o que se pode esperar.
                var valoresGit = ValoresAceitos(spec);
                var onde = new TextBlock
                {
                    Text = valoresGit.Length == 0
                        ? AppStrings.Get("Fld_WhereBoard")
                        : AppStrings.Get("Fld_WhereBoard") + " · " + AppStrings.Get("Fld_ValuesPrefix", valoresGit),
                    FontSize = 10.5, Foreground = System.Windows.Media.Brushes.Gray,
                    VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap
                };
                Grid.SetColumn(onde, 3); grid.Children.Add(onde);

                // Selo por linha: o resultado da detecção fica ao lado do campo, e não num
                // popup com a lista inteira — ali era preciso procurar o nome no meio do texto
                // para saber de qual linha se tratava.
                var selo = new TextBlock
                {
                    FontSize = 12, FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(selo, 2); grid.Children.Add(selo);

                _rows.Add(new Row { Spec = spec, Box = caixa, Where = onde, Status = selo });
                host.Children.Add(grid);
            }

            RowsList.Items.Clear();
            RowsList.Items.Add(host);
            // O padrão PT/EN só decide entre campo nativo e campo próprio do NXProject, e isso é
            // conversa de DevOps: no GitHub não existe campo nativo nenhum.
            StatusText.Text = AppStrings.Get("Fld_HintGit");
        }

        /// <summary>
        /// Os VALORES que o campo aceita, quando ele tem uma lista fechada.
        ///
        /// Saber o nome do campo não basta: quem cria o EPIC_TYPE no servidor precisa saber que
        /// ali vai DELIVERY, BACKLOG ou EPIC_GROUP — e esses valores só existiam no código e na
        /// documentação. A lista sai das mesmas constantes que o import e o sync comparam, para
        /// a tela não virar uma quarta cópia que envelhece sozinha.
        /// </summary>
        private static string ValoresAceitos(NxFieldSpec spec) => spec.Key switch
        {
            // A prioridade nao e texto livre: o card oferece P1 a P9. A faixa fica na linha
            // porque e ela, e nao o nome do campo, que responde "o que posso pôr aqui".
            "priority" => FaixaDePrioridade(),
            "epicType" => string.Join(" · ", NXProject.Models.EpicTypes.Delivery,
                                             NXProject.Models.EpicTypes.Backlog,
                                             NXProject.Models.EpicTypes.Group),
            "cardType" => string.Join(" · ", GitProjectOptions.DefaultOptions("cardType")),
            "approved" => AppStrings.Get("Fld_ValuesBoolean"),
            _ => ""
        };

        /// <summary>A faixa de prioridade, escrita como o card a mostra: P1 a P9.</summary>
        private static string FaixaDePrioridade() => "P1 … P9";

        /// <summary>Rótulo cinza com os valores aceitos, ou null quando o campo é livre.</summary>
        private static TextBlock? RotuloValores(NxFieldSpec spec)
        {
            var valores = ValoresAceitos(spec);
            if (valores.Length == 0) return null;
            return new TextBlock
            {
                Text = AppStrings.Get("Fld_ValuesPrefix", valores),
                FontSize = 10.5, Foreground = System.Windows.Media.Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
                ToolTip = AppStrings.Get("Fld_ValuesTip")
            };
        }

        /// <summary>Nome do conceito para a pessoa — não a chave interna.</summary>
        private static string Conceito(NxFieldSpec spec) =>
            AppStrings.Get("Fld_Concept_" + spec.Key) is { Length: > 0 } s && !s.StartsWith("Fld_Concept_")
                ? s : spec.DefaultName;

        /// <summary>
        /// Nome que a caixa mostra: o que o board JA tem configurado, quando tem; senao o nome de
        /// fabrica no padrao escolhido na combo.
        /// </summary>
        private string ValorAtual(NxFieldSpec spec)
        {
            var salvo = GitProjectOptions.Suggested(NomesGit(spec));
            return string.IsNullOrWhiteSpace(salvo) ? PadraoGit(spec) : salvo;
        }

        /// <summary>Nome de fabrica deste campo no board, no padrao PT/EN da aba do GitProject.</summary>
        private string PadraoGit(NxFieldSpec spec) =>
            GitProjectOptions.DefaultFieldName(spec.Key, GitIngles) is { Length: > 0 } v ? v : spec.DefaultName;

        /// <summary>A aba do GitProject esta no padrao ingles? E o default dela.</summary>
        private bool GitIngles => GitNamingCombo?.SelectedIndex != 0;


        /// <summary>Nomes aceitos no GitProject para esta chave, com o preferido na frente.</summary>
        private string[] NomesGit(NxFieldSpec spec)
        {
            var git = _options.GitProject ?? new GitProjectOptions();
            return spec.Key switch
            {
                "effort" => git.EffortFields,
                "originalHours" => git.OriginalHoursFields,
                "remainingHours" => git.RemainingHoursFields,
                "currentHours" => git.CurrentHoursFields,
                "start" => git.StartDateFields,
                "finish" => git.FinishDateFields,
                "percConclusao" => git.PercentFields,
                "percAloc" => git.AllocationFields,
                "blockDuration" => git.BlockHoursFields,
                "cardType" => git.LevelFields,
                "syncVersion" => git.SyncVersionFields,
                "admGroup" => git.AdmGroupFields,
                "epicType" => git.EpicTypeFields,
                "syncName" => git.SyncNameFields,
                _ => new[] { spec.EnglishName.Length > 0 ? spec.EnglishName : spec.DefaultName }
            };
        }

        private void OnTabChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.OriginalSource is TabControl && RowsList != null && _rows.Count == 0) BuildRows();
        }


        /// <summary>Volta os nomes do board ao padrao de fabrica do idioma escolhido.</summary>
        private void OnDefaultsClick(object sender, RoutedEventArgs e)
        {
            AplicaPadraoGit();
            RestauraClassificacaoPadrao();
            StatusText.Text = AppStrings.Get("Fld_DefaultsDone");
        }

        private void OnGitNamingChanged(object sender, SelectionChangedEventArgs e) => AplicaPadraoGit();

        private void AplicaPadraoGit()
        {
            if (_rows.Count == 0) return;
            foreach (var row in _rows) row.Box.Text = PadraoGit(row.Spec);
        }

        /// <summary>
        /// O mesmo para a aba do DevOps: reaplica o padrao da combo sobre nomes editados. A combo
        /// so dispara quando a escolha muda — sem botao, quem errou um nome nao tinha como voltar.
        /// </summary>
        private void OnDevDefaultsClick(object sender, RoutedEventArgs e)
        {
            OnNamingChanged(sender, null!);
            RestauraClassificacaoPadrao();
            StatusText.Text = AppStrings.Get("Fld_DefaultsDone");
        }

        /// <summary>
        /// Devolve a classificacao de fabrica (Custom.Type e seus valores) nas duas abas.
        ///
        /// So mexe na LINHA do campo padrao: campo que a empresa acrescentou fica como esta —
        /// "restaurar padrao" nao pode ser um jeito disfarcado de apagar configuracao. Quando a
        /// linha nao existe mais, ela volta; quando existe, so a lista de valores e reposta, que
        /// e o caso de quem instalou antes de um valor novo entrar no padrao.
        /// </summary>
        private void RestauraClassificacaoPadrao()
        {
            var iDev = IndiceDe(_classificationMappings.Select(m => m.FieldRef));
            var padraoDev = new TfsDevOpsConfigWindow.ClassificationMapping
            {
                DevOpsType = iDev >= 0 ? _classificationMappings[iDev].DevOpsType : "Feature",
                FieldRef = NxDevOpsFieldCatalog.DefaultClassificationField,
                FieldType = "Picklist",
                Values = NxDevOpsFieldCatalog.DefaultClassificationValues,
                DefaultValue = iDev >= 0 ? _classificationMappings[iDev].DefaultValue : string.Empty
            };
            // Troca o item inteiro: as classes de linha nao notificam mudanca de propriedade, e
            // so a colecao avisa a grade.
            if (iDev >= 0) _classificationMappings[iDev] = padraoDev;
            else _classificationMappings.Add(padraoDev);

            var iGit = IndiceDe(_gitClass.Select(c => c.Field));
            var curto = NxDevOpsFieldCatalog.DefaultClassificationField;
            curto = curto[(curto.LastIndexOf('.') + 1)..];
            var padraoGit = new GitProjectClassification
            {
                Level = iGit >= 0 ? _gitClass[iGit].Level : "Feature",
                Field = curto,
                FieldType = "Single select",
                Values = NxDevOpsFieldCatalog.DefaultClassificationValues,
                DefaultValue = iGit >= 0 ? _gitClass[iGit].DefaultValue : string.Empty
            };
            if (iGit >= 0) _gitClass[iGit] = padraoGit;
            else _gitClass.Add(padraoGit);

            static int IndiceDe(System.Collections.Generic.IEnumerable<string> campos)
            {
                var i = 0;
                foreach (var c in campos)
                {
                    if (NxDevOpsFieldCatalog.IsDefaultClassificationField(c)) return i;
                    i++;
                }
                return -1;
            }
        }

        /// <summary>
        /// Grava as DUAS abas de uma vez. A janela é uma só e o botão também: salvar o que está
        /// à vista e abandonar a outra aba seria perder trabalho sem avisar.
        /// </summary>
        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            SalvaDevOps();

            if (_levelRows.Count > 0)
            {
                var mapa = MapaDaTela(out var ambiguas);
                if (ambiguas.Count > 0 && MessageBox.Show(this,
                        AppStrings.Get("Fld_LevelsAmbiguous", string.Join(Environment.NewLine + "   ", ambiguas)),
                        AppStrings.Get("Fld_TabLevels"), MessageBoxButton.OKCancel,
                        MessageBoxImage.Warning) != MessageBoxResult.OK)
                {
                    TabsControl.SelectedIndex = 2;
                    return;
                }
                _options.Levels = mapa;
                // Passa a valer já nesta sessão: o board e o import consultam o mapa publicado,
                // e esperar o próximo start faria a tela parecer não ter salvado nada.
                NxLevelMap.Current = mapa;
            }


            var git = _options.GitProject ??= new GitProjectOptions();

            // O nome escolhido entra NA FRENTE dos apelidos: continua reconhecendo o que o board
            // já tiver, e passa a sugerir o que a pessoa escreveu.
            string[] Com(string escolhido, string[] atuais) =>
                new[] { escolhido }.Concat(atuais).Distinct(StringComparer.CurrentCultureIgnoreCase).ToArray();

            foreach (var r in _rows)
            {
                // Nome em branco volta ao padrão: campo sem nome não acha nada, e o board abriria
                // sem HH e sem datas, sem dizer por quê.
                var v = string.IsNullOrWhiteSpace(r.Box.Text)
                    ? GitProjectOptions.Suggested(NomesGit(r.Spec)) : r.Box.Text.Trim();
                switch (r.Spec.Key)
                {
                    case "effort":
                        git.EffortFields = Com(v, git.EffortFields);
                        git.OriginalHoursFields = Com(v, git.OriginalHoursFields);
                        break;
                    // No GitHub nao ha campo nativo de horas: os tres sao campos do board, e o
                    // botao de criar campos os cria com tipo NUMBER.
                    case "originalHours": git.OriginalHoursFields = Com(v, git.OriginalHoursFields); break;
                    case "remainingHours": git.RemainingHoursFields = Com(v, git.RemainingHoursFields); break;
                    case "currentHours": git.CurrentHoursFields = Com(v, git.CurrentHoursFields); break;
                    case "start": git.StartDateFields = Com(v, git.StartDateFields); break;
                    case "finish": git.FinishDateFields = Com(v, git.FinishDateFields); break;
                    case "percConclusao": git.PercentFields = Com(v, git.PercentFields); break;
                    case "percAloc": git.AllocationFields = Com(v, git.AllocationFields); break;
                    case "blockDuration": git.BlockHoursFields = Com(v, git.BlockHoursFields); break;
                    case "cardType": git.LevelFields = Com(v, git.LevelFields); break;
                    // Os dois do controle de concorrência: sem eles o sync grava sem saber se
                    // alguém mexeu depois — e o relatório do sync avisa exatamente isso.
                    case "syncVersion": git.SyncVersionFields = Com(v, git.SyncVersionFields); break;
                    case "syncName": git.SyncNameFields = Com(v, git.SyncNameFields); break;
                    case "admGroup": git.AdmGroupFields = Com(v, git.AdmGroupFields); break;
                    case "epicType": git.EpicTypeFields = Com(v, git.EpicTypeFields); break;
                }
            }

            string Tag(TextBox b, string padrao) => string.IsNullOrWhiteSpace(b.Text) ? padrao : b.Text.Trim();
            git.FixedStartTag = Tag(GitFixedStartTagBox, "DT-INI-NEG");
            git.UnplannedTag = Tag(GitUnplannedTagBox, "NP");
            git.WipTag = Tag(GitWipTagBox, "WIP");
            git.BlockedTag = Tag(GitBlockedTagBox, "BLOCK");

            // Classificação sem campo é linha vazia da tela: não vira configuração.
            git.Classifications = _gitClass
                .Where(c => !string.IsNullOrWhiteSpace(c.Field))
                .Select(c => new GitProjectClassification
                {
                    Level = c.Level, Field = c.Field.Trim(),
                    FieldType = string.IsNullOrWhiteSpace(c.FieldType) ? "Single select" : c.FieldType,
                    Values = c.Values?.Trim() ?? "",
                    DefaultValue = c.DefaultValue?.Trim() ?? ""
                })
                .ToList();

            TfsConnectionStore.Save(_options, !string.IsNullOrEmpty(_options.PersonalAccessToken));
            DialogResult = true;
        }





        private async void OnDetectFieldsClick(object sender, RoutedEventArgs e)
        {
            var opts = _options;
            if (string.IsNullOrWhiteSpace(opts.OrganizationUrl) || string.IsNullOrWhiteSpace(opts.TeamProject)
                || string.IsNullOrWhiteSpace(opts.PersonalAccessToken))
            {
                DetectFieldsStatus.Text = AppStrings.Get("Cfg_DetectNeedsConnection");
                return;
            }

            // Cada campo vai com os nomes ALTERNATIVOS que a importacao aceita — senao "HH
            // Estimado", que na API e "Esforco Estimado", apareceria como inexistente.
            // A lista vem das LINHAS da tela, nao de caixas nomeadas uma a uma: campo novo no
            // catalogo entra na deteccao sozinho, sem ninguem lembrar de acrescentar aqui.
            var wanted = _devRows
                .Select(r => (Label: Conceito(r.Spec), Name: r.Box.Text, Kind: r.Spec.Key))
                .ToList();

            // Onde cada campo DEVE existir para o recurso funcionar. E o que transforma
            // "existe/nao existe" em "da para habilitar ou nao".
            //
            // Vem do catalogo — a mesma lista que o instalador usa para criar, porque a tela nao
            // pode cobrar um campo num work item que o Setup nem cria. Saem da conta os tipos
            // cobertos por campo PADRAO do DevOps: os que o NX sempre usou assim (HH na Task) e os
            // que esta instalacao escolheu, via TypeFieldMappings. Sem esse desconto, quem optou
            // pelo Start Date de fabrica na Task continuaria vendo "falta em: Task" para sempre.
            bool CoveredByStandard(string kind, string workItemType)
            {
                var spec = NxDevOpsFieldCatalog.Find(kind);
                if (spec == null) return false;
                if (spec.StandardFor(workItemType).Length > 0) return true;
                if (!opts.TypeFieldMappings.TryGetValue(workItemType, out var cfg)) return false;
                var configured = kind switch
                {
                    "effort" => cfg.EffortField,
                    "start" => cfg.StartField,
                    "finish" => cfg.FinishField,
                    "percAloc" => cfg.PercAlocField,
                    "percConclusao" => cfg.PercConclusaoField,
                    // Os tres campos de horas POR TIPO faltavam aqui, e era so isso que fazia o
                    // detector cobrar o campo personalizado na Task mesmo com "usar Original
                    // Estimate" marcado: na Task o sync grava no nativo, nunca no personalizado.
                    "originalHours" => cfg.OriginalHoursField,
                    "remainingHours" => cfg.RemainingHoursField,
                    "currentHours" => cfg.CurrentHoursField,
                    _ => null
                };
                return !string.IsNullOrWhiteSpace(configured);
            }

            string[] Scope(string kind) => NxDevOpsFieldCatalog.Scope(kind)
                .Where(t => !CoveredByStandard(kind, t)).ToArray();

            try
            {
                DetectFieldsButton.IsEnabled = false;
                DetectFieldsStatus.Text = AppStrings.Get("Cfg_DetectRunning");
                // MESMO detector do NXProject-Setup: a diferenca entre as duas telas e so o
                // botao de CRIAR, que fica no instalador. Antes eram duas implementacoes, e elas
                // divergiam (apelidos, conflito de tipo, cobertura por campo padrao).
                var checks = await DevOpsFieldSetupService.CheckAsync(
                    opts.OrganizationUrl, opts.TeamProject, opts.PersonalAccessToken,
                    wanted.Where(w => !string.IsNullOrWhiteSpace(w.Name))
                          .Select(w => (w.Kind, w.Name.Trim())),
                    default, EscolhasPadrao());
                var byKind = checks.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);

                var lines = new List<string>();
                var missing = 0;

                // O selo de cada linha: verde existe, vermelho falta, ⛔ tipo incompatível. O
                // texto detalhado continua sendo montado — agora ele vai para o ToolTip da
                // própria linha, que é onde se procura quando a dúvida é sobre AQUELE campo.
                void Selo(string kind, string texto, string cor)
                {
                    var alvo = _devRows.FirstOrDefault(r =>
                        string.Equals(r.Spec.Key, kind, StringComparison.OrdinalIgnoreCase));
                    if (alvo == null) return;
                    alvo.Status.Text = texto;
                    alvo.Status.Foreground = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(cor));
                }
                void Detalhe(string kind, string texto)
                {
                    var alvo = _devRows.FirstOrDefault(r =>
                        string.Equals(r.Spec.Key, kind, StringComparison.OrdinalIgnoreCase));
                    if (alvo != null) alvo.Status.ToolTip = texto;
                }

                foreach (var r in _devRows) { r.Status.Text = ""; r.Status.ToolTip = null; }

                foreach (var (label, name, kind) in wanted)
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (!byKind.TryGetValue(kind, out var pr)) continue;
                    var scope = Scope(kind);
                    if (pr.Status == DevOpsFieldSetupService.FieldStatus.Missing)
                    {
                        // Campo OPCIONAL que nao existe nao e erro: o NX funciona sem ele, e
                        // pintar de vermelho fazia a tela pedir um campo que ninguem precisa
                        // criar. Fica o traco cinza, que diz "nao tem, e tudo bem" — vermelho
                        // continua so para o que falta de verdade.
                        if (NxDevOpsFieldCatalog.Find(kind)?.Optional == true)
                        {
                            var ondeFalta = Scope(kind);
                            Selo(kind, "–", "#808080");
                            Detalhe(kind, $"'{name.Trim()}' " + AppStrings.Get("Cfg_DetectNotFound")
                                + " — " + AppStrings.Get("Fld_OptionalMissing")
                                + (ondeFalta.Length > 0
                                    ? " (" + AppStrings.Get("Cfg_DetectExpectedIn", string.Join(", ", ondeFalta)) + ")"
                                    : ""));
                            lines.Add($"– {label}: " + AppStrings.Get("Fld_OptionalMissing"));
                            continue;
                        }
                        missing++;
                        // Dizer ONDE o campo deveria estar e o que torna o aviso acionavel.
                        var need = scope.Length > 0
                            ? " — " + AppStrings.Get("Cfg_DetectExpectedIn", string.Join(", ", scope))
                            : "";
                        // Sugestao vinda da deteccao (a mesma do NXProject-Setup): nao precisa
                        // criar campo onde o DevOps ja tem um de fabrica.
                        var alt = pr.SuggestedStandard.FirstOrDefault();
                        var suggest = string.IsNullOrEmpty(alt.Value) ? ""
                            : "  💡 " + AppStrings.Get("Cfg_DetectSuggestStd", alt.Key,
                                DevOpsFieldSetupService.ShortFieldName(alt.Value));
                        var faltaTexto = $"'{name.Trim()}' " + AppStrings.Get("Cfg_DetectNotFound") + need + suggest;
                        Selo(kind, "✖", "#C03030");
                        Detalhe(kind, faltaTexto);
                        lines.Add($"❌ {label}: {faltaTexto}");
                        continue;
                    }
                    if (pr.Status == DevOpsFieldSetupService.FieldStatus.Unknown)
                    {
                        Selo(kind, "…", "#808080");
                        Detalhe(kind, pr.Note);
                        lines.Add($"… {label}: {pr.Note}");
                        continue;
                    }
                    var tp = string.IsNullOrEmpty(pr.FoundType) ? "" : $" [{pr.FoundType}]";
                    // So avisa falta onde o campo FAZ FALTA: cobrar "Esforco Estimado" na Task
                    // ou na Project so gerava ruido, porque o NX nunca usa o campo ali.
                    var gap = pr.MissingIn.Count > 0
                        ? "  ⚠ " + AppStrings.Get("Cfg_DetectMissingIn", string.Join(", ", pr.MissingIn))
                        : "";
                    // Achado por nome ALTERNATIVO: o que voce digitou nao existe no DevOps. O NX
                    // funciona assim mesmo (a importacao usa a mesma lista), mas quem configurou
                    // precisa saber — senao um nome errado passa por validado.
                    var via = string.IsNullOrEmpty(pr.MatchedByAlias)
                        ? ""
                        : "  ⚠ " + AppStrings.Get("Cfg_DetectViaAlias", pr.MatchedByAlias);
                    // Tipo do campo diferente do que o NX grava: incompativel bloqueia, numerico
                    // mais estreito (inteiro onde vai decimal) e so aviso.
                    var typeNote = pr.Status switch
                    {
                        DevOpsFieldSetupService.FieldStatus.TypeConflict =>
                            "  ⛔ " + AppStrings.Get("Cfg_DetectTypeConflict", pr.FoundType,
                                NxDevOpsFieldCatalog.Find(kind)?.Type ?? ""),
                        DevOpsFieldSetupService.FieldStatus.TypeNarrower =>
                            "  ⚠ " + AppStrings.Get("Cfg_DetectTypeNarrower", pr.FoundType,
                                NxDevOpsFieldCatalog.Find(kind)?.Type ?? ""),
                        _ => ""
                    };
                    // Tipo coberto por campo de fabrica (sempre, ou por escolha desta instalacao).
                    var std = pr.CoveredByStandard.Count > 0
                        ? "  · " + AppStrings.Get("Cfg_DetectStandard", string.Join(" | ", pr.CoveredByStandard))
                        : "";
                    if (pr.Status == DevOpsFieldSetupService.FieldStatus.TypeConflict) missing++;
                    var conflito = pr.Status == DevOpsFieldSetupService.FieldStatus.TypeConflict;
                    var mark = conflito ? "⛔" : "✅";
                    var detalhe = $"{pr.ReferenceName}{tp}{via}{typeNote}{gap}{std}";
                    // Existe, mas com ressalva (apelido, tipo mais estreito, falta num tipo):
                    // amarelo, porque "tudo certo" ali seria mentira.
                    Selo(kind, conflito ? "⛔" : "✔",
                        conflito ? "#C03030"
                        : (via.Length > 0 || typeNote.Length > 0 || gap.Length > 0) ? "#B8860B" : "#107C10");
                    Detalhe(kind, detalhe);
                    lines.Add($"{mark} {label}: {detalhe}");
                }

                // Campos com checkbox: a deteccao MARCA o que existe no tipo em que o NX usa e
                // DESMARCA o que nao existe. Nada e gravado aqui — so vale se voce Salvar, e o
                // Cancelar desfaz tudo.
                var toggled = new List<string>();
                void ApplyCheck(string kind, CheckBox box, TextBox nameBox, string label)
                {
                    var name = nameBox.Text?.Trim() ?? "";
                    if (string.IsNullOrWhiteSpace(name)) return;
                    if (!byKind.TryGetValue(kind, out var pr)) return;
                    // Existir na organizacao nao basta: tem que existir NO TIPO em que o NX usa —
                    // faltar em TODOS os tipos do escopo e o mesmo que nao existir.
                    var scope = Scope(kind);
                    var usable = pr.Status is DevOpsFieldSetupService.FieldStatus.Ready
                                           or DevOpsFieldSetupService.FieldStatus.TypeNarrower
                                           or DevOpsFieldSetupService.FieldStatus.NeedsWorkItemTypes
                        && (scope.Length == 0 || pr.MissingIn.Count < scope.Length);
                    if ((box.IsChecked == true) == usable) return;
                    box.IsChecked = usable;
                    nameBox.IsEnabled = usable;
                    toggled.Add((usable ? "✔ " : "✖ ") + label);
                }
                // As caixas agora vivem NA LINHA de cada campo: o detector percorre as linhas
                // opcionais em vez de conhecer os nomes dos controles um a um.
                foreach (var row in _devRows.Where(r => r.Enabled != null))
                    ApplyCheck(row.Spec.Key, row.Enabled!, row.Box, Conceito(row.Spec));

                // Achado por APELIDO: a tela passa a mostrar o nome que a organizacao usa DE
                // FATO, e nao o nome padrao que ninguem cadastrou la. O NX ja importava por
                // apelido — o que faltava era a configuracao dizer a verdade sobre o que ela
                // esta lendo. Campo coberto pelo nativo fica como esta: la nao ha nome a trocar.
                foreach (var row in _devRows)
                {
                    if (!byKind.TryGetValue(row.Spec.Key, out var pr)) continue;
                    if (string.IsNullOrWhiteSpace(pr.MatchedByAlias)) continue;
                    if (string.Equals(row.Box.Text?.Trim(), pr.MatchedByAlias, StringComparison.CurrentCultureIgnoreCase))
                        continue;
                    row.Box.Text = pr.MatchedByAlias;
                    toggled.Add("✎ " + Conceito(row.Spec) + " → \"" + pr.MatchedByAlias + "\"");
                }
                // Duas opcoes desta tela nao dependem de CAMPO:
                //  • predecessoras: depende do TIPO DE LINK do processo — isso da para detectar;
                //  • fechar Story so com Task: e regra do proprio NX, sem contraparte no DevOps —
                //    o detector apenas devolve o valor recomendado, e diz que foi por isso.
                var hasDep = await TfsImportService.HasPredecessorLinkTypeAsync(opts);
                if (hasDep is bool dep && (SyncPredecessorLinksCheck.IsChecked == true) != dep)
                {
                    SyncPredecessorLinksCheck.IsChecked = dep;
                    toggled.Add((dep ? "✔ " : "✖ ") + AppStrings.Get("Cfg_SyncPredecessorLinks"));
                }
                if (EnforceStoryCompletionWithTasksCheck.IsChecked != true)
                {
                    EnforceStoryCompletionWithTasksCheck.IsChecked = true;
                    toggled.Add("✔ " + AppStrings.Get("Cfg_EnforceStoryCompletionWithTasks")
                                + " (" + AppStrings.Get("Cfg_DetectNxRule") + ")");
                }

                if (toggled.Count > 0)
                    lines.Add(Environment.NewLine + AppStrings.Get("Cfg_DetectToggled")
                              + Environment.NewLine + string.Join(Environment.NewLine, toggled));

                DetectFieldsStatus.Text = missing == 0
                    ? AppStrings.Get("Cfg_DetectAllOk")
                    : AppStrings.Get("Cfg_DetectMissing", missing.ToString());
                MessageBox.Show(this,
                    string.Join(Environment.NewLine, lines) + Environment.NewLine + Environment.NewLine
                    + AppStrings.Get("Cfg_DetectFooter"),
                    AppStrings.Get("Cfg_DetectFields"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                DetectFieldsStatus.Text = "";
                MessageBox.Show(this, ex.Message, "NXProject", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { DetectFieldsButton.IsEnabled = true; }
        }

        private void OnAddClassificationMapping(object sender, RoutedEventArgs e)
            => _classificationMappings.Add(new TfsDevOpsConfigWindow.ClassificationMapping { DevOpsType = "Feature", FieldRef = string.Empty });

        private void OnRemoveClassificationMapping(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TfsDevOpsConfigWindow.ClassificationMapping m)
                _classificationMappings.Remove(m);
        }

        private void OnAddExtraField(object sender, RoutedEventArgs e)
            => _extraFields.Add(new ExtraWorkItemField());

        private void OnRemoveExtraField(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ExtraWorkItemField field)
                _extraFields.Remove(field);
        }

        private void OnBrowseTaskPlanFileClick(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = AppStrings.Get("Cfg_TaskPlanFile"),
                Filter = "Planilha do Excel (*.xlsx)|*.xlsx|Todos os arquivos (*.*)|*.*",
                CheckFileExists = true
            };
            if (!string.IsNullOrWhiteSpace(TaskPlanFileBox.Text))
                try { dlg.InitialDirectory = System.IO.Path.GetDirectoryName(TaskPlanFileBox.Text.Trim()); } catch { }
            if (dlg.ShowDialog(this) == true)
                TaskPlanFileBox.Text = dlg.FileName;
        }

    }
}
