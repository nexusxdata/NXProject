using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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
            ProjectCalendarService.Load("NXProject.Community");
            StatusLogoImage.Source = ProtectedLogoProvider.GetLogoImage();
            var vm = new MainViewModel("NXProject.Community");
            DataContext = vm;
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

            TaskGridCtrl.HeaderHeightMeasured += h => GanttCtrl.SetHeaderHeight(h);
            TaskGridCtrl.RowTopsMeasured += tops => GanttCtrl.SetRowTops(tops);
            TaskGridCtrl.TaskMoveRequested += (sourceTask, targetTask, insertAfter) =>
            {
                if (vm.MoveTaskByDrop(sourceTask, targetTask, insertAfter))
                    GanttCtrl.ForceRender();
            };

            TaskGridCtrl.TaskIdClicked += OnTaskIdClicked;

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
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
                        () => GanttCtrl.ScrollToProjectStart());
            };

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
            }
        }

        private void OnSprintSettingsClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
                return;

            var window = new Window
            {
                Title = "Configurações de Sprint",
                Owner = this,
                Width = 760,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = (System.Windows.Media.Brush)FindResource("BackgroundBrush"),
                Content = new Controls.SprintSettingsControl
                {
                    DataContext = vm
                }
            };

            window.ShowDialog();
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
                Close();
                return;
            }
        }

        private bool ShowLicenseDialog(bool requireAcceptance)
        {
            var licenseWindow = new CommunityLicenseWindow
            {
                Owner = this
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
