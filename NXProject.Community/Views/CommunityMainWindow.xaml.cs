using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using NXProject.Community.Services;
using NXProject.Services;
using NXProject.ViewModels;

namespace NXProject.Views
{
    public partial class CommunityMainWindow : Window
    {
        private static readonly string LicenseAcceptanceDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NXProject.Community");

        private static readonly string LicenseAcceptanceFile =
            Path.Combine(LicenseAcceptanceDirectory, "license.accepted");

        private static readonly string AiLastOpenedFile =
            Path.Combine(LicenseAcceptanceDirectory, "ai.last-opened.txt");

        private bool _licenseAccepted;
        private bool _allowClose;
        private bool _aiOpenedOnFirstAccess;
        private bool _expandedLayout;

        public CommunityMainWindow()
        {
            InitializeComponent();
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            if (v != null)
                Title = $"NXProject Community {v.Major}.{v.Minor}.{v.Build}";
            ProjectCalendarService.Load("NXProject.Community");
            StatusLogoImage.Source = ProtectedLogoProvider.GetLogoImage();
            var vm = new MainViewModel("NXProject.Community");
            DataContext = vm;

            // Atualiza o banner quando um projeto é aberto/carregado
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(vm.Project))
                    UpdateDevOpsProjectBanner(vm.Project.DevOpsProjectName, vm.Project.DevOpsRootWorkItemId);
            };

            var syncingVerticalScroll = false;

            TaskGridCtrl.VerticalScrollChanged += offset =>
            {
                if (syncingVerticalScroll) return;
                syncingVerticalScroll = true;
                GanttCtrl.SyncVerticalOffset(offset);
                syncingVerticalScroll = false;
            };

            GanttCtrl.VerticalScrollChanged += offset =>
            {
                if (syncingVerticalScroll) return;
                syncingVerticalScroll = true;
                TaskGridCtrl.SyncVerticalOffset(offset);
                syncingVerticalScroll = false;
            };

            TaskGridCtrl.HeaderHeightMeasured += h =>
            {
                // Em modo Dia o cabeçalho do Gantt tem 3 tiers (60px); não deixar TaskGrid sobrescrever.
                if (GanttCtrl.DayHeaderMode > 0)
                    GanttCtrl.SetHeaderHeight(60.0);
                else
                    GanttCtrl.SetHeaderHeight(h);
            };
            TaskGridCtrl.RowTopsMeasured += tops => GanttCtrl.SetRowTops(tops);
            TaskGridCtrl.TaskMoveRequested += (sourceTask, targetTask, insertAfter) =>
            {
                if (vm.MoveTaskByDrop(sourceTask, targetTask, insertAfter))
                    GanttCtrl.ForceRender();
            };

            TaskGridCtrl.TaskIdClicked += OnTaskIdClicked;
            TaskGridCtrl.HighlightPredecessorsRequested += task =>
                GanttCtrl.HighlightPredecessors(task?.Model.PredecessorIds ?? []);

            TaskGridCtrl.TaskSprintChangeRequested += (task, sprint) =>
            {
                vm.ApplyTaskSprintChange(task, sprint);
                GanttCtrl.ForceRender();
            };

            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.SelectedTask))
                    GanttCtrl.SelectedTask = vm.SelectedTask;

                if (args.PropertyName == nameof(MainViewModel.SelectedZoom))
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
                    {
                        ZoomLabel.Text = vm.SelectedZoom;
                        GanttCtrl.ZoomLevel = vm.SelectedZoom;
                        GanttCtrl.ForceRender();
                        GanttCtrl.ScrollToProjectStart();
                    });
                }
            };

            ZoomLabel.Text = vm.SelectedZoom;

            GanttCtrl.TaskClicked += task =>
            {
                vm.SelectedTask = task;
            };

            SubscribeTaskEvents(vm.FlatTasks);
            vm.FlatTasks.CollectionChanged += (_, args) =>
            {
                if (args.OldItems != null)
                {
                    foreach (var item in args.OldItems)
                        if (item is TaskViewModel task)
                            task.PropertyChanged -= OnTaskPropertyChanged;
                }

                if (args.NewItems != null)
                {
                    foreach (var item in args.NewItems)
                        if (item is TaskViewModel task)
                            task.PropertyChanged += OnTaskPropertyChanged;
                }

                if (args.Action == NotifyCollectionChangedAction.Reset)
                    SubscribeTaskEvents(vm.FlatTasks);

                if (args.Action == NotifyCollectionChangedAction.Add ||
                    args.Action == NotifyCollectionChangedAction.Remove ||
                    args.Action == NotifyCollectionChangedAction.Reset)
                {
                    GanttCtrl.ForceRender();
                    TaskGridCtrl.FocusSelectedTask();
                }
            };

            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.Project))
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                        () => GanttCtrl.ScrollToProjectStart());
            };

            Loaded += OnCommunityWindowLoaded;
            Closing += OnCommunityWindowClosing;
            ApplyLayoutMode(expanded: false);
        }

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnAboutClick(object sender, RoutedEventArgs e)
        {
            var about = new CommunityAboutWindow
            {
                Owner = this
            };
            about.ShowDialog();
        }

        private void OnLicenseClick(object sender, RoutedEventArgs e)
        {
            ShowLicenseDialog(requireAcceptance: false);
        }

        private void OnAzureDevOpsBacklogHelpClick(object sender, RoutedEventArgs e)
        {
            new AzureDevOpsBacklogHelpWindow
            {
                Owner = this
            }.ShowDialog();
        }

        private void OnFeaturesHelpClick(object sender, RoutedEventArgs e)
        {
            new FeaturesHelpWindow { Owner = this }.ShowDialog();
        }

        private void OnLanguageClick(object sender, RoutedEventArgs e)
        {
            new LanguageWindow { Owner = this }.ShowDialog();
        }

        private void OnAppSettingsClick(object sender, RoutedEventArgs e)
        {
            new AppSettingsWindow { Owner = this }.ShowDialog();
        }

        private void OnScheduleUsageHelpClick(object sender, RoutedEventArgs e)
        {
            new ScheduleUsageHelpWindow
            {
                Owner = this
            }.ShowDialog();
        }

        private async void OnCheckUpdateClick(object sender, RoutedEventArgs e)
        {
            try
            {
                IsEnabled = false;
                var release = await NXProject.Services.UpdateService.CheckForUpdateAsync();
                IsEnabled = true;

                if (release is null)
                {
                    MessageBox.Show(
                        "Voce ja esta usando a versao mais recente.",
                        "Verificar Atualizacao",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var choice = ShowUpdateDialog(release.TagName);

                if (choice == UpdateChoice.Auto)
                {
                    var progressWindow = new UpdateProgressWindow(release.DownloadUrl) { Owner = this };
                    progressWindow.ShowDialog();
                }
                else if (choice == UpdateChoice.Manual)
                {
                    ShowDownloadLinkDialog(release.HtmlUrl, release.DownloadUrl, release.TagName);
                }
            }
            catch (Exception ex)
            {
                IsEnabled = true;
                MessageBox.Show(
                    $"Nao foi possivel verificar atualizacoes.\n\n{ex.Message}",
                    "Verificar Atualizacao",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private enum UpdateChoice { Auto, Manual, Cancel }

        private UpdateChoice ShowUpdateDialog(string tagName)
        {
            var dlg = new System.Windows.Window
            {
                Title = "Atualização disponível",
                Owner = this,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                Width = 360,
                Height = 230,
                Background = System.Windows.Media.Brushes.White
            };

            var result = UpdateChoice.Cancel;

            var root = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(24, 20, 24, 20) };

            root.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"Nova versão disponível: {tagName}",
                FontSize = 13,
                FontWeight = System.Windows.FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(43, 87, 154)),
                Margin = new System.Windows.Thickness(0, 0, 0, 8)
            });
            root.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "Como deseja atualizar?",
                Margin = new System.Windows.Thickness(0, 0, 0, 14),
                Foreground = System.Windows.Media.Brushes.DimGray
            });

            var btnAuto = new System.Windows.Controls.Button { Content = "⬇  Atualizar automaticamente", Height = 32, IsDefault = true, HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch, Margin = new System.Windows.Thickness(0, 0, 0, 6) };
            var btnManual = new System.Windows.Controls.Button { Content = "🌐  Baixar manualmente", Height = 32, HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch, Margin = new System.Windows.Thickness(0, 0, 0, 6) };
            var btnCancel = new System.Windows.Controls.Button { Content = "Agora não", Height = 32, IsCancel = true, HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch };

            btnAuto.Click   += (_, _) => { result = UpdateChoice.Auto;   dlg.DialogResult = true; dlg.Close(); };
            btnManual.Click += (_, _) => { result = UpdateChoice.Manual; dlg.DialogResult = true; dlg.Close(); };
            btnCancel.Click += (_, _) => { dlg.Close(); };

            root.Children.Add(btnAuto);
            root.Children.Add(btnManual);
            root.Children.Add(btnCancel);

            dlg.Content = root;
            dlg.ShowDialog();
            return result;
        }

        private void ShowDownloadLinkDialog(string htmlUrl, string downloadUrl, string tagName)
        {
            var dlg = new System.Windows.Window
            {
                Title = $"Download — {tagName}",
                Owner = this,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                Width = 500,
                Height = 220,
                Background = System.Windows.Media.Brushes.White
            };

            var root = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(20) };
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

            void AddRow(int row, string label, string url)
            {
                var lbl = new System.Windows.Controls.TextBlock
                {
                    Text = label,
                    FontWeight = System.Windows.FontWeights.SemiBold,
                    Margin = new System.Windows.Thickness(0, row == 0 ? 0 : 12, 0, 4)
                };
                System.Windows.Controls.Grid.SetRow(lbl, row * 2);
                root.Children.Add(lbl);

                var panel = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal
                };
                var box = new System.Windows.Controls.TextBox
                {
                    Text = url,
                    IsReadOnly = true,
                    Width = 360,
                    Margin = new System.Windows.Thickness(0, 0, 6, 0),
                    VerticalContentAlignment = System.Windows.VerticalAlignment.Center
                };
                var btnCopy = new System.Windows.Controls.Button { Content = "Copiar", Width = 70 };
                btnCopy.Click += (_, _) =>
                {
                    System.Windows.Clipboard.SetText(url);
                    btnCopy.Content = "Copiado!";
                };
                panel.Children.Add(box);
                panel.Children.Add(btnCopy);
                System.Windows.Controls.Grid.SetRow(panel, row * 2 + 1);
                root.Children.Add(panel);
            }

            // Adiciona linhas extras no Grid para os dois grupos
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

            AddRow(0, "Pagina do release:", htmlUrl);
            AddRow(1, "Link direto do ZIP:", downloadUrl);

            dlg.Content = root;
            dlg.ShowDialog();
        }

        private void OnAiAssistantClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
                return;

            var aiWindow = new CommunityAIWindow(vm)
            {
                Owner = this
            };
            aiWindow.ShowDialog();
        }

        private void OnTaskIdClicked(TaskViewModel task)
        {
            if (DataContext is not MainViewModel vm)
                return;

            var dialog = new TfsWorkItemEditWindow(task) { Owner = this };
            if (dialog.ShowDialog() == true)
                vm.Project.IsDirty = true;
        }

        private async void OnSyncTfsClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
                return;

            var options = Services.TfsConnectionStore.Load("NXProject.Community");
            if (string.IsNullOrWhiteSpace(options.OrganizationUrl) ||
                string.IsNullOrWhiteSpace(options.TeamProject) ||
                string.IsNullOrWhiteSpace(options.PersonalAccessToken))
            {
                MessageBox.Show(
                    "Configure a conexão e marque \"Lembrar o token\" primeiro em Arquivo → Importar → TFS / Azure DevOps.",
                    "Sincronizar TFS/DevOps", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                "Isto vai atualizar os work items vinculados no DevOps (título/descrição, horas, e datas conforme as regras). Continuar?",
                "Sincronizar TFS/DevOps", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK)
                return;

            if (!ConfirmKnownTfsResources(vm))
                return;

            if (!ConfirmCompletedTfsState(vm))
                return;

            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            Services.TfsImportService.SyncReport? report = null;
            try
            {
                report = await Services.TfsImportService.SyncAsync(vm.Project, options);
            }
            catch (Exception ex)
            {
                System.Windows.Input.Mouse.OverrideCursor = null;
                MessageBox.Show(
                    $"Erro ao sincronizar:\n{ex.Message}\n\nTipo: {ex.GetType().Name}\n\n{ex.StackTrace}",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            finally
            {
                System.Windows.Input.Mouse.OverrideCursor = null;
            }

            vm.Project.IsDirty = true;
            vm.RefreshTasks();
            try
            {
                new SyncResultWindow(report) { Owner = this }.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Erro ao abrir resultado:\n{ex.Message}\n\nTipo: {ex.GetType().Name}\n\n{ex.StackTrace}",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static bool ConfirmKnownTfsResources(MainViewModel vm)
        {
            var manualResources = vm.FlatTasks
                .Where(t => t.Model.TfsId.HasValue)
                .SelectMany(t => t.Model.Resources.Select(a => new
                {
                    Task = t,
                    Resource = a.Resource ?? vm.Project.Resources.FirstOrDefault(r => r.Id == a.ResourceId)
                }))
                .Where(x => x.Resource != null && !x.Resource.IsImportedFromTfs)
                .GroupBy(x => x.Resource!.Id)
                .Select(g => new
                {
                    Resource = g.First().Resource!,
                    Count = g.Select(x => x.Task.Model.Id).Distinct().Count()
                })
                .OrderBy(x => x.Resource.Name)
                .ToList();

            if (manualResources.Count == 0)
                return true;

            var sample = string.Join(Environment.NewLine,
                manualResources
                    .Take(8)
                    .Select(x => $"- {x.Resource.DisplayName} ({x.Count} atividade(s))"));
            var suffix = manualResources.Count > 8
                ? $"{Environment.NewLine}- ... e mais {manualResources.Count - 8}"
                : string.Empty;

            MessageBox.Show(
                "Existem recursos marcados com * que nao foram identificados no TFS/DevOps:"
                + Environment.NewLine + Environment.NewLine
                + sample + suffix
                + Environment.NewLine + Environment.NewLine
                + "Ajuste a alocacao para um recurso importado do TFS/DevOps e sincronize novamente.",
                "Sincronizar TFS/DevOps",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        private static bool ConfirmCompletedTfsState(MainViewModel vm)
        {
            var completedNotClosed = vm.FlatTasks
                .Where(t => !t.IsSummary
                            && t.Model.TfsId.HasValue
                            && t.Model.TfsId.Value > 0
                            && t.PercentComplete >= 100
                            && !string.Equals(t.TfsState?.Trim(), "Closed", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (completedNotClosed.Count == 0)
                return true;

            var sample = string.Join(Environment.NewLine,
                completedNotClosed
                    .Take(8)
                    .Select(t => $"- #{t.TfsId}: {t.Name} ({t.TfsState ?? "sem estado"})"));
            var suffix = completedNotClosed.Count > 8
                ? $"{Environment.NewLine}- ... e mais {completedNotClosed.Count - 8}"
                : string.Empty;

            var decision = MessageBox.Show(
                "Existem atividades com 100% de conclusao, mas o estado no TFS/DevOps nao esta como Closed:"
                + Environment.NewLine + Environment.NewLine
                + sample + suffix
                + Environment.NewLine + Environment.NewLine
                + "Sim = atualizar o status para Closed no TFS e sincronizar."
                + Environment.NewLine
                + "Nao = sincronizar mantendo o status atual."
                + Environment.NewLine
                + "Cancelar = nao sincronizar.",
                "Sincronizar TFS/DevOps",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (decision == MessageBoxResult.Cancel)
                return false;

            if (decision == MessageBoxResult.Yes)
            {
                foreach (var task in completedNotClosed)
                    task.TfsState = "Closed";
                vm.Project.IsDirty = true;
            }

            return true;
        }

        private void OnImportTfsClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
                return;

            var dialog = new TfsImportWindow("NXProject.Community")
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true && dialog.ImportedProject is { } project)
            {
                vm.ApplyImportedProject(
                    project,
                    $"Projeto importado do TFS: {project.Name}");
                UpdateDevOpsProjectBanner(project.DevOpsProjectName, project.DevOpsRootWorkItemId);
            }
        }

        private void OnDevOpsProjectListClick(object sender, RoutedEventArgs e)
        {
            var saved = Services.TfsConnectionStore.Load("NXProject.Community");
            var dlg = new DevOpsProjectListWindow(saved.DevOpsProjectListPath) { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ResultFilePath))
            {
                saved.DevOpsProjectListPath = dlg.ResultFilePath;
                Services.TfsConnectionStore.Save(saved, !string.IsNullOrWhiteSpace(saved.PersonalAccessToken), "NXProject.Community");
            }
        }

        private void UpdateDevOpsProjectBanner(string? name, int id)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                DevOpsProjectNameLabel.Text = name;
                DevOpsProjectIdLabel.Text = id > 0 ? $"(ID: {id})" : string.Empty;
                DevOpsProjectBanner.Visibility = Visibility.Visible;
            }
            else if (id > 0)
            {
                DevOpsProjectNameLabel.Text = $"ID {id}";
                DevOpsProjectIdLabel.Text = string.Empty;
                DevOpsProjectBanner.Visibility = Visibility.Visible;
            }
            else
            {
                DevOpsProjectBanner.Visibility = Visibility.Collapsed;
            }
        }

        private void OnSprintSettingsClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
                return;

            new SprintManagerWindow(vm) { Owner = this }.ShowDialog();
            GanttCtrl.ForceRender();
        }

        private void OnSfpSettingsClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
                return;

            var window = new Window
            {
                Title = "Configuracoes de SPF",
                Owner = this,
                Width = 820,
                Height = 560,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = (System.Windows.Media.Brush)FindResource("BackgroundBrush"),
                Content = new Controls.SfpSettingsControl
                {
                    DataContext = vm
                }
            };

            window.ShowDialog();
        }

        private void OnResourceFilterClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            var resources = vm.Project.Resources;
            if (!resources.Any()) return;

            var dlg = new ResourceFilterWindow(resources, vm.ResourceFilter) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                vm.SetResourceFilter(dlg.SelectedResourceIds);
                UpdateResourceFilterLabel(vm);
            }
        }

        private void UpdateResourceFilterLabel(MainViewModel vm)
        {
            if (vm.ResourceFilter == null)
                ResourceFilterLabel.Text = string.Empty;
            else
                ResourceFilterLabel.Text = $"({vm.ResourceFilter.Count})";
        }

        private void OnZoomMenuClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            var levels = vm.ZoomLevels;
            var idx = levels.IndexOf(vm.SelectedZoom);
            var next = levels[(idx + 1) % levels.Count];
            ApplyZoom(next);
        }

        private void ApplyZoom(string zoom)
        {
            if (DataContext is not MainViewModel vm) return;
            vm.SelectedZoom = zoom;
            ZoomLabel.Text = zoom;
            GanttCtrl.ZoomLevel = zoom;

            // Dia, Semana, Trimestre e Semestre mostram header por dia; Sprint e Mês usam view por sprint
            bool dayMode = zoom is "Dia" or "Semana" or "Trimestre" or "Semestre";
            int currentMode = GanttCtrl.DayHeaderMode;
            int newMode = dayMode ? (currentMode == 0 ? 1 : currentMode) : 0;
            ApplyDayHeaderMode(newMode);

            GanttCtrl.ForceRender();
        }

        private void OnDayHeaderToggled(object sender, RoutedEventArgs e)
        {
            // Cicla: 0 (off) → 1 (dia1: seg/qua/sex) → 2 (dia2: dígito compacto) → 0
            int next = (GanttCtrl.DayHeaderMode + 1) % 3;
            ApplyDayHeaderMode(next);
        }

        private void ApplyDayHeaderMode(int mode)
        {
            GanttCtrl.DayHeaderMode = mode;
            DayHeaderToggle.IsChecked = mode > 0;

            if (mode > 0)
            {
                TaskGridCtrl.SetColumnHeaderHeight(60.0);
                GanttCtrl.SetHeaderHeight(60.0);
            }
            else
            {
                TaskGridCtrl.SetColumnHeaderHeight(40.0);
                GanttCtrl.SetHeaderHeight(40.0);
            }

            DayHeaderToggle.ToolTip = mode switch
            {
                0 => "Visão por dia (clique para ativar Dia 1)",
                1 => "Visão Dia 1 — segunda/quarta/sexta destacadas (clique para Dia 2)",
                2 => "Visão Dia 2 — dígito compacto por dia (clique para desativar)",
                _ => "Visão por dia"
            };

            GanttCtrl.ForceRender();
        }

        private void OnRefreshViewClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.RefreshTasks();
            GanttCtrl.ForceRender();
        }

        private void OnExportPdfClick(object sender, RoutedEventArgs e)
        {
            // 1. Opções de layout
            var pdfOpts = new PdfExportOptionsWindow { Owner = this };
            if (pdfOpts.ShowDialog() != true) return;

            var vm = DataContext as MainViewModel;
            var projectName = vm?.Project?.Name ?? "Cronograma";

            // 2. Destino do arquivo
            var dlg = new SaveFileDialog
            {
                Title      = Str("Pdf_SaveTitle"),
                Filter     = Str("Pdf_Filter"),
                FileName   = $"{SanitizeFileName(projectName)}{Str("Pdf_FileSuffix")}",
                DefaultExt = "pdf"
            };
            if (dlg.ShowDialog(this) != true) return;

            // 3. Branding
            var nxLogo      = ProtectedLogoProvider.GetLogoImage();
            var appOpts     = TfsConnectionStore.Load();
            var companyName = appOpts.CompanyName ?? string.Empty;
            System.Windows.Media.Imaging.BitmapImage? companyLogo = null;
            if (!string.IsNullOrWhiteSpace(appOpts.CompanyLogoBase64))
            {
                try
                {
                    var bytes = Convert.FromBase64String(appOpts.CompanyLogoBase64);
                    var bmp   = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption  = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.StreamSource = new System.IO.MemoryStream(bytes);
                    bmp.EndInit();
                    bmp.Freeze();
                    companyLogo = bmp;
                }
                catch { }
            }

            // 4. Exporta
            try
            {
                PdfExportService.Export(
                    tableVisual:     TaskGridCtrl,
                    ganttVisual:     GanttCtrl,
                    projectName:     projectName,
                    nxLogo:          nxLogo,
                    companyName:     companyName,
                    companyLogo:     companyLogo,
                    filePath:        dlg.FileName,
                    layoutMode:      pdfOpts.LayoutMode,
                    pageSize:        pdfOpts.PageSize,
                    exportedOnLabel: Str("Pdf_FooterExported"),
                    scheduleSubject: Str("Pdf_SubjectSchedule"));

                // 5. Oferecer abrir o PDF imediatamente
                var result = MessageBox.Show(
                    $"{Str("Pdf_SuccessMsg")}\n{dlg.FileName}\n\n{Str("Pdf_OpenNow")}",
                    Str("Pdf_SuccessTitle"), MessageBoxButton.YesNo, MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{Str("Pdf_ErrorMsg")}\n{ex.Message}",
                    Str("Pdf_ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string Str(string key)
            => Application.Current.TryFindResource(key) as string ?? key;

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var result  = new System.Text.StringBuilder();
            foreach (var c in name)
                result.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return result.ToString();
        }

        private void OnRecalcDatesClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
                return;

            vm.RecalculateScheduleRespectingAssignments();
            GanttCtrl.ForceRender();
        }

        private void OnCalendarSettingsClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
                return;

            var durations = vm.CaptureTaskWorkingDurations();
            var control = new Controls.CalendarSettingsControl("NXProject.Community");
            var window = new Window
            {
                Title = "Calendario de trabalho",
                Owner = this,
                Width = 720,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = (System.Windows.Media.Brush)FindResource("BackgroundBrush"),
                Content = control
            };

            control.Saved += (_, _) =>
            {
                vm.RecalculateScheduleFromCalendar(durations);
                GanttCtrl.ForceRender();
                window.DialogResult = true;
                window.Close();
            };

            window.ShowDialog();
        }

        private void OnResourceAllocationClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
                return;

            var window = new ResourceAllocationWindow(vm)
            {
                Owner = this
            };
            window.ShowDialog();
            GanttCtrl.ForceRender();
        }

        private void OnDelayedTasksClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
                return;

            new DelayedTasksWindow(vm) { Owner = this }.ShowDialog();
        }

        private void OnPeopleClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
                return;

            try
            {
                new PeopleWindow(vm) { Owner = this }.ShowDialog();
                GanttCtrl.ForceRender();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao abrir Pessoas:\n{ex.Message}", "Pessoas",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnToolbarButtonClick(object sender, RoutedEventArgs e)
        {
            TaskGridCtrl.FocusSelectedTask();
        }

        private void OnLayoutToggleClick(object sender, RoutedEventArgs e)
        {
            ApplyLayoutMode(!_expandedLayout);
        }

        private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(TaskViewModel.PredecessorsText))
                GanttCtrl.ForceRender();
        }

        private void SubscribeTaskEvents(System.Collections.Generic.IEnumerable<TaskViewModel> tasks)
        {
            foreach (var task in tasks)
            {
                task.PropertyChanged -= OnTaskPropertyChanged;
                task.PropertyChanged += OnTaskPropertyChanged;
            }
        }

        private void OnCommunityWindowLoaded(object sender, RoutedEventArgs e)
        {

            if (_licenseAccepted)
                return;

            if (HasAcceptedLicense())
            {
                _licenseAccepted = true;
                return;
            }

            _licenseAccepted = ShowLicenseDialog(requireAcceptance: true);
            if (!_licenseAccepted)
            {
                _allowClose = true;
                Application.Current.Shutdown();
                return;
            }
        }

        private bool ShowLicenseDialog(bool requireAcceptance)
        {
            var licenseWindow = new CommunityLicenseWindow
            {
                Owner = this,
                RequireAcceptance = requireAcceptance
            };

            var accepted = licenseWindow.ShowDialog() == true;
            if (!accepted && !requireAcceptance)
                return false;

            if (accepted && requireAcceptance)
                PersistLicenseAcceptance();

            return accepted;
        }

        private static bool HasAcceptedLicense()
        {
            return File.Exists(LicenseAcceptanceFile);
        }

        private static void PersistLicenseAcceptance()
        {
            Directory.CreateDirectory(LicenseAcceptanceDirectory);
            File.WriteAllText(LicenseAcceptanceFile, "accepted");
        }

        private void OnCommunityWindowClosing(object? sender, CancelEventArgs e)
        {
            if (_allowClose)
                return;

            if (DataContext is not MainViewModel vm)
                return;

            if (!vm.Project.IsDirty)
                return;

            var decision = MessageBox.Show(
                "O projeto possui alteracoes nao salvas. Deseja salvar antes de fechar?",
                "Salvar projeto",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (decision == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (decision == MessageBoxResult.Yes)
            {
                vm.SaveProjectCommand.Execute(null);
                if (vm.Project.IsDirty)
                {
                    e.Cancel = true;
                    return;
                }
            }

            _allowClose = true;
        }

        private void OpenAiAssistantOnFirstAccess()
        {
            if (_aiOpenedOnFirstAccess || DataContext is not MainViewModel vm)
                return;

            if (WasAiAutoOpenedToday())
                return;

            _aiOpenedOnFirstAccess = true;
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                if (!IsLoaded || !IsVisible)
                    return;

                var aiWindow = new CommunityAIWindow(vm)
                {
                    Owner = this
                };
                aiWindow.ShowDialog();
                PersistAiLastOpenedDate();
            }));
        }

        private static bool WasAiAutoOpenedToday()
        {
            if (!File.Exists(AiLastOpenedFile))
                return false;

            var content = File.ReadAllText(AiLastOpenedFile).Trim();
            return DateOnly.TryParse(content, out var lastOpenedDate) &&
                   lastOpenedDate == DateOnly.FromDateTime(DateTime.Today);
        }

        private static void PersistAiLastOpenedDate()
        {
            Directory.CreateDirectory(LicenseAcceptanceDirectory);
            File.WriteAllText(
                AiLastOpenedFile,
                DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd"));
        }

        private void ApplyLayoutMode(bool expanded)
        {
            _expandedLayout = expanded;

            TaskPaneColumn.Width = expanded
                ? new GridLength(920)
                : new GridLength(660);
            GanttPaneColumn.Width = expanded
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(1, GridUnitType.Star);

            TaskGridCtrl.SetPresentationMode(expanded);
            LayoutToggleText.Text = expanded ? "⤡" : "⤢";
            LayoutToggleButton.ToolTip = expanded
                ? "Voltar para a visualização compacta"
                : "Abrir a tabela com colunas mais legíveis";

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                GanttCtrl.ForceRender();
            }));
        }
    }
}
