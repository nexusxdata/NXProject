using System;
using System.Collections.Generic;
using System.Globalization;
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

        /// <summary>Projeto importado quando o diálogo retorna true.</summary>
        public Project? ImportedProject { get; private set; }

        public TfsImportWindow(string storageKey = "NXProject.Community")
        {
            InitializeComponent();
            _storageKey = string.IsNullOrWhiteSpace(storageKey) ? "NXProject.Community" : storageKey.Trim();

            var saved = TfsConnectionStore.Load(_storageKey);
            OrgUrlBox.Text = saved.OrganizationUrl;
            ProjectBox.Text = saved.TeamProject;
            // HoursPerDay vem do calendário de trabalho — não exibido aqui
            EffortFieldBox.Text = saved.EffortFieldName;
            StartFieldBox.Text = saved.StartFieldName;
            FinishFieldBox.Text = saved.FinishFieldName;
            PercAlocFieldBox.Text = saved.PercAlocFieldName;
            FixedStartTagBox.Text = saved.FixedStartTagName;
            SyncPredecessorLinksCheck.IsChecked = saved.SyncPredecessorLinks;
            FutureSprintDaysBox.Text = saved.FutureSprintDays.ToString(CultureInfo.InvariantCulture);

            if (!string.IsNullOrEmpty(saved.PersonalAccessToken))
            {
                PatBox.Password = saved.PersonalAccessToken;
                RememberTokenCheck.IsChecked = true;
            }

            // Carrega lista de projetos DevOps do path salvo
            if (!string.IsNullOrWhiteSpace(saved.DevOpsProjectListPath))
                LoadProjectList(saved.DevOpsProjectListPath, saved.RootWorkItemId);
            else if (saved.RootWorkItemId > 0)
                RootIdBox.Text = saved.RootWorkItemId.ToString(CultureInfo.InvariantCulture);
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
        }

        private void OnProjectComboChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DevOpsProjectCombo.SelectedItem is DevOpsProject selected)
                RootIdBox.Text = selected.RootWorkItemId.ToString(CultureInfo.InvariantCulture);
        }

        private void OnManageListClick(object sender, RoutedEventArgs e)
        {
            var dlg = new DevOpsProjectListWindow(_devOpsProjectListPath) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                var newPath = dlg.ResultFilePath ?? string.Empty;
                LoadProjectList(newPath);

                var saved = TfsConnectionStore.Load(_storageKey);
                saved.DevOpsProjectListPath = newPath;
                TfsConnectionStore.Save(saved, RememberTokenCheck.IsChecked == true, _storageKey);
            }
        }

        private async void OnImportClick(object sender, RoutedEventArgs e)
        {
            if (_isImporting)
                return;

            HideStatus();

            if (!int.TryParse(RootIdBox.Text?.Trim(), out var rootId) || rootId <= 0)
            {
                ShowStatus("Selecione um projeto da lista ou informe um ID de work item raiz válido.");
                return;
            }

            if (string.IsNullOrWhiteSpace(PatBox.Password))
            {
                ShowStatus("Informe o Personal Access Token.");
                return;
            }

            double hoursPerDay = ProjectCalendarService.WorkingHoursPerDay;

            var options = new TfsConnectionOptions
            {
                OrganizationUrl = OrgUrlBox.Text?.Trim() ?? string.Empty,
                TeamProject = ProjectBox.Text?.Trim() ?? string.Empty,
                PersonalAccessToken = PatBox.Password,
                RootWorkItemId = rootId,
                HoursPerDay = hoursPerDay,
                EffortFieldName = string.IsNullOrWhiteSpace(EffortFieldBox.Text) ? "HH Estimado" : EffortFieldBox.Text.Trim(),
                StartFieldName = string.IsNullOrWhiteSpace(StartFieldBox.Text) ? "Data_Inicio" : StartFieldBox.Text.Trim(),
                FinishFieldName = string.IsNullOrWhiteSpace(FinishFieldBox.Text) ? "Data_Fim" : FinishFieldBox.Text.Trim(),
                PercAlocFieldName = string.IsNullOrWhiteSpace(PercAlocFieldBox.Text) ? "Perc_Alocação" : PercAlocFieldBox.Text.Trim(),
                FixedStartTagName = string.IsNullOrWhiteSpace(FixedStartTagBox.Text) ? "DT-INI-NEG" : FixedStartTagBox.Text.Trim(),
                SyncPredecessorLinks = SyncPredecessorLinksCheck.IsChecked == true,
                FutureSprintDays = int.TryParse(FutureSprintDaysBox.Text?.Trim(), out var fsd) && fsd >= 0 ? fsd : 90,
                DevOpsProjectListPath = _devOpsProjectListPath
            };

            SetImporting(true);
            try
            {
                var importResult = await TfsImportService.ImportAsync(options);
                var project = importResult.Project;

                if (DevOpsProjectCombo.SelectedItem is DevOpsProject selected)
                {
                    project.DevOpsProjectName = selected.Name;
                    project.DevOpsRootWorkItemId = selected.RootWorkItemId;
                }
                else
                {
                    project.DevOpsRootWorkItemId = rootId;
                }

                TfsConnectionStore.Save(options, RememberTokenCheck.IsChecked == true, _storageKey);

                ImportedProject = project;
                DialogResult = true;
                Close();

                if (importResult.Report.HasIssues)
                {
                    var reportWin = new ImportResultWindow(importResult.Report) { Owner = System.Windows.Application.Current.MainWindow };
                    reportWin.Show();
                }
            }
            catch (Exception ex)
            {
                ShowStatus(ex.Message);
            }
            finally
            {
                SetImporting(false);
            }
        }

        private void OnOpenCalendarClick(object sender, RoutedEventArgs e)
        {
            var control = new NXProject.Controls.CalendarSettingsControl("NXProject.Community");
            var window = new Window
            {
                Title = "Calendário de trabalho",
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
            ImportButton.Content = importing ? "Importando..." : "Importar";
            Mouse.OverrideCursor = importing ? Cursors.Wait : null;
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
