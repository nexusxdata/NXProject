using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using NXProject.Models;
using NXProject.Services;

namespace NXProject.Views
{
    public partial class TfsImportWindow : Window
    {
        private readonly string _storageKey;
        private bool _isImporting;

        /// <summary>Projeto importado quando o diálogo retorna true.</summary>
        public Project? ImportedProject { get; private set; }

        public TfsImportWindow(string storageKey = "NXProject.Community")
        {
            InitializeComponent();
            _storageKey = string.IsNullOrWhiteSpace(storageKey) ? "NXProject.Community" : storageKey.Trim();

            var saved = TfsConnectionStore.Load(_storageKey);
            OrgUrlBox.Text = saved.OrganizationUrl;
            ProjectBox.Text = saved.TeamProject;
            HoursPerDayBox.Text = saved.HoursPerDay.ToString(CultureInfo.CurrentCulture);
            EffortFieldBox.Text = saved.EffortFieldName;
            StartFieldBox.Text = saved.StartFieldName;
            FinishFieldBox.Text = saved.FinishFieldName;
            if (saved.RootWorkItemId > 0)
                RootIdBox.Text = saved.RootWorkItemId.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(saved.PersonalAccessToken))
            {
                PatBox.Password = saved.PersonalAccessToken;
                RememberTokenCheck.IsChecked = true;
            }
        }

        private async void OnImportClick(object sender, RoutedEventArgs e)
        {
            if (_isImporting)
                return;

            HideStatus();

            if (!int.TryParse(RootIdBox.Text?.Trim(), out var rootId) || rootId <= 0)
            {
                ShowStatus("Informe um ID de work item raiz válido.");
                return;
            }

            if (string.IsNullOrWhiteSpace(PatBox.Password))
            {
                ShowStatus("Informe o Personal Access Token.");
                return;
            }

            double hoursPerDay = 8.0;
            if (!string.IsNullOrWhiteSpace(HoursPerDayBox.Text) &&
                double.TryParse(HoursPerDayBox.Text.Trim(), NumberStyles.Any, CultureInfo.CurrentCulture, out var hpd) &&
                hpd > 0)
                hoursPerDay = hpd;

            var options = new TfsConnectionOptions
            {
                OrganizationUrl = OrgUrlBox.Text?.Trim() ?? string.Empty,
                TeamProject = ProjectBox.Text?.Trim() ?? string.Empty,
                PersonalAccessToken = PatBox.Password,
                RootWorkItemId = rootId,
                HoursPerDay = hoursPerDay,
                EffortFieldName = string.IsNullOrWhiteSpace(EffortFieldBox.Text) ? "HH Estimado" : EffortFieldBox.Text.Trim(),
                StartFieldName = string.IsNullOrWhiteSpace(StartFieldBox.Text) ? "Data_Inicio" : StartFieldBox.Text.Trim(),
                FinishFieldName = string.IsNullOrWhiteSpace(FinishFieldBox.Text) ? "Data_Fim" : FinishFieldBox.Text.Trim()
            };

            SetImporting(true);
            try
            {
                var project = await TfsImportService.ImportAsync(options);

                TfsConnectionStore.Save(options, RememberTokenCheck.IsChecked == true, _storageKey);

                ImportedProject = project;
                DialogResult = true;
                Close();
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
