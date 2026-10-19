// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NXProject.Models;
using NXProject.Services;

namespace NXProject.Views
{
    public partial class TfsImportWindow : Window
    {
        private readonly string _storageKey;
        private bool _isImporting;
        private string _devOpsProjectListPath = string.Empty;
        private List<DevOpsProject> _devOpsProjects = new();
        private TfsConnectionOptions _savedOptions = new();

        /// <summary>Projeto importado quando o diálogo retorna true.</summary>
        public Project? ImportedProject { get; private set; }

        public TfsImportWindow(string storageKey = "NXProject.Community")
        {
            InitializeComponent();
            _storageKey = string.IsNullOrWhiteSpace(storageKey) ? "NXProject.Community" : storageKey.Trim();

            _savedOptions = TfsConnectionStore.Load(_storageKey);
            UpdateViewLogButton();

            // A origem reabre onde parou. Fica ANTES de carregar o portfolio porque o
            // SelectionChanged da combo valida a conexao, e validar durante a montagem da tela
            // abriria a janela de conexao por cima dela.
            _restaurandoOrigem = true;
            SourceCombo.SelectedIndex = _savedOptions.LastImportSource == NxBackendKind.GitProject ? 1 : 0;
            _restaurandoOrigem = false;

            // A raiz que reabre é a da origem escolhida (DevOps: work item Project; GitHub: issue raiz).
            var lastRoot = _savedOptions.LastImportSource == NxBackendKind.GitProject
                ? _savedOptions.GitLastRootIssue : _savedOptions.RootWorkItemId;
            if (!string.IsNullOrWhiteSpace(_savedOptions.DevOpsProjectListPath))
                LoadProjectList(_savedOptions.DevOpsProjectListPath, lastRoot);
            else if (lastRoot > 0)
                RootIdBox.Text = lastRoot.ToString(CultureInfo.InvariantCulture);
        }

        // Autorização de sincronização: agora vem do grupo administrador do NX (campo Adm_NX do
        // work item Project), lido do TFS na importação. Persiste o nome do grupo no Portfólio para
        // exibição; ainda respeita um "somente leitura" antigo já gravado no Portfólio (compat.).
        private void ApplyReadOnlyFromPortfolioOrCheckbox(Project project)
        {
            var rootId = project.DevOpsRootWorkItemId;
            var entry = rootId > 0 ? _devOpsProjects.FirstOrDefault(p => p.RootWorkItemId == rootId) : null;

            // O "somente leitura" do Portfólio é LEGADO: só vale quando NÃO há grupo
            // administrador. Com Adm_NX definido (no work item ou no cadastro), quem autoriza é
            // a checagem ao vivo do grupo — carimbar o flag aqui gravaria "somente leitura" no
            // XML e o cronograma nasceria bloqueado mesmo para quem tem escrita.
            var hasAdmGroup = !string.IsNullOrWhiteSpace(project.AdmGroupName)
                              || !string.IsNullOrWhiteSpace(entry?.AdmGroupName);
            if (hasAdmGroup)
                project.ReadOnly = false;
            else if (entry?.ReadOnly is { } defined)
                project.ReadOnly = defined;   // compat.: Portfólio antigo com "somente leitura"

            if (rootId > 0 && !string.IsNullOrWhiteSpace(_devOpsProjectListPath))
            {
                if (entry == null)
                {
                    entry = new DevOpsProject
                    {
                        Name = string.IsNullOrWhiteSpace(project.DevOpsProjectName) ? project.Name : project.DevOpsProjectName!,
                        RootWorkItemId = rootId,
                        Owner = project.DevOpsProjectOwner ?? "",
                        Process = project.DevOpsProcess ?? ""
                    };
                    _devOpsProjects.Add(entry);
                }
                // Se o work item trouxe o grupo Adm_NX preenchido, atualiza o cadastro do
                // Portfólio (inclusive projetos criados à mão com o grupo ainda vazio/nulo).
                if (!string.IsNullOrWhiteSpace(project.AdmGroupName))
                    entry.AdmGroupName = project.AdmGroupName!;
                try { DevOpsProjectListService.Save(_devOpsProjects, _devOpsProjectListPath); } catch { /* portfólio é conveniência */ }
            }
        }

        private void LoadProjectList(string path, int selectId = 0)
        {
            _devOpsProjectListPath = path;
            _devOpsProjects = DevOpsProjectListService.Load(path);

            DevOpsProjectCombo.ItemsSource = null;
            DevOpsProjectCombo.ItemsSource = _devOpsProjects;
            ListPathLabel.Text = path;

            if (selectId > 0)
            {
                foreach (var p in _devOpsProjects)
                {
                    if (p.RootWorkItemId == selectId)
                    {
                        DevOpsProjectCombo.SelectedItem = p;
                        break;
                    }
                }
            }

            if (DevOpsProjectCombo.SelectedItem == null && selectId > 0)
                RootIdBox.Text = selectId.ToString(CultureInfo.InvariantCulture);
            SyncReadOnlyBox();
            SyncImportConfig();
        }

        private void OnProjectComboChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DevOpsProjectCombo.SelectedItem is DevOpsProject selected)
                RootIdBox.Text = selected.RootWorkItemId.ToString(CultureInfo.InvariantCulture);
            SyncReadOnlyBox();
            SyncImportConfig();
        }

        // Mostra o grupo administrador do NX do projeto selecionado no Portfólio (informativo).
        private void SyncReadOnlyBox()
        {
            if (AdmGroupHint == null) return;
            var selected = DevOpsProjectCombo.SelectedItem as DevOpsProject;
            AdmGroupHint.Text = !string.IsNullOrWhiteSpace(selected?.AdmGroupName)
                ? AppStrings.Get("Imp_AdmGroupSelected", selected!.AdmGroupName)
                : AppStrings.Get("Imp_AdmGroupInfo");
        }

        /// <summary>
        /// A ORIGEM segue o projeto escolhido no portfolio. O destino e do PROJETO — esta
        /// gravado no cadastro dele (PortfolioProjectConfig.BackendKind), e e de la que o
        /// TaskBoard e o Sincronizar ja nascem. A combo so estava ignorando isso: escolher um
        /// projeto de GitProject e continuar vendo "Azure DevOps" embaixo era a tela
        /// contradizendo o cadastro.
        ///
        /// Sem cadastro (projeto que nao esta no portfolio), fica a ultima origem usada.
        /// </summary>
        private void SyncOrigemDoProjeto()
        {
            if (SourceCombo == null) return;
            var nome = (DevOpsProjectCombo.SelectedItem as DevOpsProject)?.Name;
            if (string.IsNullOrWhiteSpace(nome))
            {
                // Sem projeto do portfolio: importacao so por ID. Aqui a combo e a escolha da
                // pessoa, e o botao continua liberado.
                SourceCombo.IsEnabled = true;
                SourceCombo.ToolTip = AppStrings.Get("Imp_SourceTip");
                if (ImportButton != null) { ImportButton.IsEnabled = true; ImportButton.ToolTip = null; }
                if (NoSourceHint != null) NoSourceHint.Visibility = Visibility.Collapsed;
                return;
            }

            var cfg = TfsConnectionStore.Load(_storageKey).PortfolioProjectConfigs
                .FirstOrDefault(c => string.Equals(c.ProjectName, nome, StringComparison.CurrentCultureIgnoreCase));

            // A origem e do PROJETO, e so dele. Cadastrada, a combo vira SO LEITURA: trocar ali
            // seria dizer que o projeto passou a vir de outro servidor, e isso se muda no
            // cadastro (Gerenciar Portfólio). SEM cadastro, nao ha de onde importar — o botao
            // fica desabilitado e o hint diz o que fazer, em vez de a tela escolher um servidor
            // por conta propria: importar um board do GitHub contra o DevOps so devolve um
            // "work item nao encontrado" que nao explica nada.
            //
            // Default NAO e "sem origem": e "o padrao do NXProject", que e o Azure DevOps — e
            // assim que estao todos os cadastros feitos antes de o GitProject existir. Tratar
            // Default como ausencia fazia a combo FICAR NA ORIGEM DO PROJETO ANTERIOR: era
            // exatamente isso que mandava um projeto do DevOps importar como GitProject.
            var cadastrada = cfg != null;
            SourceCombo.IsEnabled = !cadastrada;
            SourceCombo.ToolTip = AppStrings.Get(cadastrada ? "Imp_SourceFromProject" : "Imp_NoSourceHint");
            if (ImportButton != null)
            {
                ImportButton.IsEnabled = cadastrada;
                ImportButton.ToolTip = cadastrada ? null : AppStrings.Get("Imp_NoSourceHint");
            }
            if (NoSourceHint != null)
                NoSourceHint.Visibility = cadastrada ? Visibility.Collapsed : Visibility.Visible;
            if (!cadastrada) return;

            var indice = cfg!.BackendKind == NxBackendKind.GitProject ? 1 : 0;   // Default e DevOps
            if (SourceCombo.SelectedIndex == indice) return;

            // Sem passar pela validacao da combo: aqui nao e escolha do usuario, e sim o
            // cadastro do projeto sendo refletido na tela.
            _restaurandoOrigem = true;
            SourceCombo.SelectedIndex = indice;
            _restaurandoOrigem = false;
        }

        private void SyncImportConfig()
        {
            if (LoadTasksOnImportBox == null) return;
            SyncOrigemDoProjeto();
            // Sempre reposiciona pelo projeto selecionado: sem cadastro no Portfólio, nasce
            // desmarcada. Nunca herda o que ficou marcado para o projeto anterior.
            var selected = DevOpsProjectCombo.SelectedItem as DevOpsProject;
            LoadTasksOnImportBox.IsEnabled = selected != null;
            LoadTasksOnImportBox.IsChecked = selected?.LoadTasksOnImport == true;
            UpdateViewLogButton();
        }

        /// <summary>O log e de UM projeto: trocar a combo para outro projeto desabilita o botao,
        /// senao o log do projeto anterior parecia ser do que esta selecionado agora.</summary>
        private void UpdateViewLogButton()
        {
            if (ViewLogButton == null) return;
            var log = TfsImportService.LastImportLog;
            var rootId = (DevOpsProjectCombo.SelectedItem as DevOpsProject)?.RootWorkItemId
                         ?? (int.TryParse(RootIdBox.Text?.Trim(), out var typed) ? typed : 0);
            ViewLogButton.IsEnabled = log != null && log.RootWorkItemId == rootId && rootId > 0;
            ViewLogButton.ToolTip = log != null && ViewLogButton.IsEnabled
                ? AppStrings.Get("Imp_ViewLogOf", log.ProjectName)
                : AppStrings.Get("Imp_ViewLogTip");
        }

        private void OnManageListClick(object sender, RoutedEventArgs e)
        {
            var dlg = new DevOpsProjectListWindow(_devOpsProjectListPath) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                var newPath = dlg.ResultFilePath ?? string.Empty;
                // Pré-seleciona na combo o projeto que estava marcado na grid do portfólio.
                LoadProjectList(newPath, dlg.SelectedProject?.RootWorkItemId ?? 0);

                var saved = TfsConnectionStore.Load(_storageKey);
                saved.DevOpsProjectListPath = newPath;
                TfsConnectionStore.Save(saved, !string.IsNullOrEmpty(saved.PersonalAccessToken), _storageKey);
            }
        }

        /// <summary>Nomes dos campos, nos dois destinos.</summary>
        private void OnOpenFieldsClick(object sender, RoutedEventArgs e)
        {
            new FieldNamesWindow { Owner = this }.ShowDialog();
        }

        private void OnOpenConfigClick(object sender, RoutedEventArgs e)
        {
            var dlg = new TfsDevOpsConfigWindow(_storageKey) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _savedOptions = TfsConnectionStore.Load(_storageKey);
                UpdateConfigHint();
            }
        }

        /// <summary>
        /// Origem da importação: Azure DevOps ou GitHub Projects. Sem a conexão do GitProject
        /// configurada, abre a tela de conexão em vez de recusar — é o passo que falta.
        /// </summary>
        private void OnSourceChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SourceCombo == null || _restaurandoOrigem) return;
            GravaOrigemEscolhida();
            if (SourceCombo.SelectedIndex == 0) return;

            var options = TfsConnectionStore.Load();
            if (options.GitProject?.IsConfigured == true) return;

            if (new TfsDevOpsConfigWindow(abaInicial: 1) { Owner = this }.ShowDialog() != true
                || TfsConnectionStore.Load().GitProject?.IsConfigured != true)
                SourceCombo.SelectedIndex = 0;   // volta, e o próprio evento regrava a escolha
        }

        /// <summary>
        /// Guarda a origem escolhida. Com um projeto do portfólio selecionado, ela vira o
        /// DESTINO DAQUELE PROJETO — é onde o NXProject guarda isso, e é de lá que o TaskBoard
        /// e o Sincronizar nascem. Assim, escolher o projeto de novo traz a origem certa, sem
        /// depender de "a última vez que usei a tela".
        ///
        /// Sem projeto selecionado, fica só a preferência da tela.
        /// </summary>
        private void GravaOrigemEscolhida()
        {
            var escolhida = SourceCombo.SelectedIndex == 1
                ? NxBackendKind.GitProject : NxBackendKind.AzureDevOps;
            var opcoes = TfsConnectionStore.Load(_storageKey);
            var mudou = false;

            if (opcoes.LastImportSource != escolhida)
            {
                opcoes.LastImportSource = escolhida;
                mudou = true;
            }

            var nome = (DevOpsProjectCombo?.SelectedItem as DevOpsProject)?.Name;
            if (!string.IsNullOrWhiteSpace(nome))
            {
                var cfg = opcoes.PortfolioProjectConfigs.FirstOrDefault(c =>
                    string.Equals(c.ProjectName, nome, StringComparison.CurrentCultureIgnoreCase));
                if (cfg == null)
                {
                    // Projeto ainda sem cadastro: nasce aqui, com o destino escolhido. Antes era
                    // preciso ir ao "Gerenciar Portfólio" só para dizer de onde o projeto vem.
                    cfg = new PortfolioProjectConfig { ProjectName = nome! };
                    opcoes.PortfolioProjectConfigs.Add(cfg);
                }
                if (cfg.BackendKind != escolhida)
                {
                    cfg.BackendKind = escolhida;
                    mudou = true;
                }
            }

            if (!mudou) return;
            // rememberToken: o token ja esta salvo; gravar sem ele apagaria o que existe.
            TfsConnectionStore.Save(opcoes, rememberToken: true, _storageKey);
            _savedOptions = opcoes;
        }

        /// <summary>
        /// Restaurando a escolha salva: o evento da combo nao deve validar conexao nem regravar
        /// nessa hora — a tela ainda esta sendo montada.
        /// </summary>
        private bool _restaurandoOrigem;

        /// <summary>A importação roda contra o GitHub Projects?</summary>
        private bool DoGitProject => SourceCombo?.SelectedIndex == 1;

        private async void OnImportClick(object sender, RoutedEventArgs e)
        {
            if (_isImporting)
                return;

            HideStatus();

            _savedOptions = TfsConnectionStore.Load(_storageKey);
            // Cada origem valida a SUA conexao: exigir PAT do DevOps para importar do GitHub
            // era a tela recusando por um dado que nao tem nada a ver com o que vai acontecer.
            if (DoGitProject)
            {
                if (_savedOptions.GitProject?.IsConfigured != true)
                {
                    ShowStatus(AppStrings.Get("Imp_NoGitConnection"));
                    return;
                }
            }
            else if (string.IsNullOrWhiteSpace(_savedOptions.OrganizationUrl)
                     || string.IsNullOrWhiteSpace(_savedOptions.PersonalAccessToken))
            {
                ShowStatus(AppStrings.Get("Imp_NoConnectionStatus"));
                return;
            }

            // A raiz tem significados diferentes: no DevOps e o ID do work item tipo Project e
            // e obrigatorio; no GitHub e o numero da issue marcada como Project — e valor <= 0
            // quer dizer "o board inteiro", que e como o Discovery cadastra um board sem raiz.
            int.TryParse(RootIdBox.Text?.Trim(), out var rootId);
            if (!DoGitProject && rootId <= 0)
            {
                ShowStatus(AppStrings.Get("Imp_SelectOrEnterId"));
                return;
            }

            var options = _savedOptions;
            // Cada origem guarda a SUA última raiz: a issue do GitHub no RootWorkItemId virava a
            // raiz do Azure DevOps (work item inexistente) para o Sincronizar, o Portfólio e o NXPilot.
            if (DoGitProject) options.GitLastRootIssue = rootId;
            else options.RootWorkItemId = rootId;
            options.DevOpsProjectListPath = _devOpsProjectListPath;
            var selectedPortfolioProject = DevOpsProjectCombo.SelectedItem as DevOpsProject;
            // Marcação válida só para ESTA importação: quem manda no padrão é o cadastro do
            // Portfólio (SyncImportConfig semeia a caixa). Gravar de volta aqui fazia a marcação
            // de um projeto sobrar marcada para o próximo; o default é sempre sem marcar.
            var loadTasksOnImport = LoadTasksOnImportBox.IsChecked == true;

            SetImporting(true);
            try
            {
                // Progresso por etapa (Progress<T> despacha para a thread da UI).
                var progress = new Progress<string>(step => ImportStepText.Text = step);
                // Mesma janela, duas origens: o que muda e de onde vem o cronograma; o que
                // acontece depois — calendario, recalculo, relatorio — e identico.
                var importResult = DoGitProject
                    ? await GitProjectImportService.ImportAsync(options.GitProject!,
                        rootId > 0 ? rootId : 0, progress)
                    : await TfsImportService.ImportAsync(options, progress, loadTasksOnImport: loadTasksOnImport);
                var project = importResult.Project;

                // Grava a origem (organização + Team Project) para a sincronização
                // usar o projeto do cronograma aberto, não a config global.
                project.DevOpsOrganizationUrl = options.OrganizationUrl?.Trim();
                project.DevOpsTeamProject = options.TeamProject?.Trim();

                if (project.Tasks.Count == 0)
                {
                    var warn = AppStrings.Get("Imp_EmptyResult");
                    ShowStatus(warn);
                    MessageBox.Show(this, warn, AppStrings.Get("Imp_ErrorTitle"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (selectedPortfolioProject is DevOpsProject selected)
                {
                    project.DevOpsProjectName = selected.Name;
                    project.DevOpsRootWorkItemId = selected.RootWorkItemId;
                }
                else
                {
                    project.DevOpsRootWorkItemId = rootId;
                }

                // Somente leitura: se o Portfólio JÁ define (não-nulo), usa o dele; senão usa o
                // checkbox e GRAVA no Portfólio (cria/atualiza a entrada deste projeto).
                ApplyReadOnlyFromPortfolioOrCheckbox(project);

                TfsConnectionStore.Save(options, !string.IsNullOrEmpty(options.PersonalAccessToken), _storageKey);

                ResourceKindConfigService.ApplyTo(project.Resources);
                ImportedProject = project;
                DialogResult = true;
                Close();

                // Deu certo: nao prende o usuario numa janela de log so para ele fechar. O relatorio
                // fica guardado e sai pelo botao "Ver log". Com ERRO, ai sim abre sozinha.
                TfsImportService.LastImportLog = new TfsImportService.LastImportLogInfo
                {
                    Report = importResult.Report,
                    RootWorkItemId = project.DevOpsRootWorkItemId,
                    ProjectName = selectedPortfolioProject?.Name ?? project.Name ?? ""
                };
                if (importResult.Report.HasIssues)
                {
                    var reportWin = new ImportResultWindow(importResult.Report) { Owner = System.Windows.Application.Current.MainWindow };
                    reportWin.Title += " — " + TfsImportService.LastImportLog.ProjectName;
                    reportWin.Show();
                }
            }
            catch (Exception ex)
            {
                var (msg, isAuth) = BuildImportErrorMessage(ex);
                ShowStatus(msg);

                if (isAuth)
                {
                    var res = MessageBox.Show(this,
                        msg + "\n\n" + AppStrings.Get("Imp_OpenTokensPrompt"),
                        AppStrings.Get("Imp_ErrorTitle"),
                        MessageBoxButton.YesNo, MessageBoxImage.Error);
                    if (res == MessageBoxResult.Yes)
                        OpenTokensPage();
                }
                else
                {
                    MessageBox.Show(this, msg, AppStrings.Get("Imp_ErrorTitle"),
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                SetImporting(false);
            }
        }

        // Mostra o relatorio da ultima importacao (a janela nao abre mais sozinha quando da certo).
        private void OnViewLogClick(object sender, RoutedEventArgs e)
        {
            var log = TfsImportService.LastImportLog;
            if (log == null)
            {
                ShowStatus(AppStrings.Get("Imp_NoLogYet"));
                return;
            }
            // Titulo com o Work Item Project: deixa claro de QUAL importacao e este log.
            var win = new ImportResultWindow(log.Report) { Owner = this };
            if (!string.IsNullOrWhiteSpace(log.ProjectName)) win.Title += " — " + log.ProjectName;
            win.Show();
        }

        // Abre a página de Personal Access Tokens da organização no navegador.
        private void OpenTokensPage()
        {
            try
            {
                var org = _savedOptions.OrganizationUrl?.Trim().TrimEnd('/');
                if (string.IsNullOrWhiteSpace(org)) return;
                var url = org + "/_usersSettings/tokens";
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                if (TfsErrorDialog.IsAuthError(ex)) { TfsErrorDialog.Show(this, AppStrings.Get("Tfs_ActionImport"), ex); return; }
                MessageBox.Show(this, ex.Message, AppStrings.Get("Imp_ErrorTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // Monta uma mensagem de erro legível: encadeia InnerExceptions (redes/TLS
        // costumam trazer a causa real só no Inner) e destaca falha de PAT/autenticação.
        // Retorna (mensagem, ehFalhaDeAutenticacao).
        private static (string Message, bool IsAuth) BuildImportErrorMessage(Exception ex)
        {
            var parts = new List<string>();
            for (var e = ex; e != null; e = e.InnerException)
            {
                var m = e.Message?.Trim();
                if (!string.IsNullOrEmpty(m) && !parts.Contains(m))
                    parts.Add(m);
            }
            if (parts.Count == 0)
                parts.Add(ex.GetType().Name);

            var full = string.Join("\n", parts);
            var lower = full.ToLowerInvariant();

            bool looksAuth = lower.Contains("autentica") || lower.Contains("401") ||
                             lower.Contains("403") || lower.Contains("unauthorized") ||
                             lower.Contains("forbidden") || lower.Contains("html") ||
                             lower.Contains("login") || lower.Contains("pat") ||
                             lower.Contains("sign in") || lower.Contains("tf400813");

            if (looksAuth)
                return (AppStrings.Get("Imp_AuthError") + "\n\n" + full, true);

            return (full, false);
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            UpdateConfigHint();
        }

        private void UpdateConfigHint()
        {
            bool configured = !string.IsNullOrWhiteSpace(_savedOptions.OrganizationUrl)
                && !string.IsNullOrWhiteSpace(_savedOptions.PersonalAccessToken);
            NotConfiguredHint.Visibility = configured ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnOpenCalendarClick(object sender, RoutedEventArgs e)
        {
            var control = new NXProject.Controls.CalendarSettingsControl("NXProject.Community");
            var window = new Window
            {
                Title = AppStrings.Get("Imp_CalendarWindowTitle"),
                Owner = this,
                Width = 720,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = control
            };
            control.Saved += (_, _) => { window.Close(); };
            window.ShowDialog();
        }

        private void SetImporting(bool importing)
        {
            _isImporting = importing;
            ImportButton.IsEnabled = !importing;
            ImportButton.Content = importing ? AppStrings.Get("Imp_Importing") : AppStrings.Get("Imp_Import");
            Mouse.OverrideCursor = importing ? Cursors.Wait : null;
            ImportProgressPanel.Visibility = importing ? Visibility.Visible : Visibility.Collapsed;
            if (importing) ImportStepText.Text = "";
        }

        private void ShowStatus(string message)
        {
            StatusText.Text = message;
            StatusText.Visibility = Visibility.Visible;
        }

        private void HideStatus()
        {
            StatusText.Visibility = Visibility.Collapsed;
        }
    }
}
