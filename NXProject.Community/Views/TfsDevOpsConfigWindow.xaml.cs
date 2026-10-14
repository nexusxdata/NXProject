// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NXProject.Models;
using NXProject.Services;

namespace NXProject.Views
{
    public partial class TfsDevOpsConfigWindow : Window
    {
        private readonly string _storageKey;

        /// <summary>
        /// Configuração como está gravada. Nomes de campo, tags, campos obrigatórios e
        /// classificação agora são editados em "Configurar Campos": esta tela os REPASSA sem
        /// tocar, senão salvar a conexão apagaria o que a outra tela acabou de gravar.
        /// </summary>
        private readonly TfsConnectionOptions _savedFields;
        private string _devOpsProjectListPath = string.Empty;
        private readonly System.Collections.ObjectModel.ObservableCollection<ExtraWorkItemField> _extraFields = new();
        private readonly System.Collections.ObjectModel.ObservableCollection<ClassificationMapping> _classificationMappings = new();

        public sealed class ClassificationMapping
        {
            public static readonly string[] AllTypes       = ["Epic", "Feature", "Story", "Task", "Todos"];
            public static readonly string[] AllFieldTypes  = ["Picklist", "Integer", "Text", "Date"];
            public string[] AvailableTypes      => AllTypes;
            public string[] AvailableFieldTypes => AllFieldTypes;
            public string DevOpsType { get; set; } = "Feature";
            public string FieldRef   { get; set; } = string.Empty;
            public string FieldType  { get; set; } = "Picklist";
            /// <summary>Valores separados por vírgula; viram o combo ao editar classificação deste tipo.</summary>
            public string Values     { get; set; } = string.Empty;
            /// <summary>Valor gravado ao criar o item quando ele não tem classificação.</summary>
            public string DefaultValue { get; set; } = string.Empty;
        }

        // Projeto (cronograma) aberto — habilita o campo do path da planilha de Task Plan.
        private readonly string? _currentProjectName;

        public TfsDevOpsConfigWindow(string storageKey = "NXProject.Community", string? currentProjectName = null)
        {
            InitializeComponent();
            _storageKey = string.IsNullOrWhiteSpace(storageKey) ? "NXProject.Community" : storageKey.Trim();
            _savedFields = TfsConnectionStore.Load(_storageKey);
            _currentProjectName = string.IsNullOrWhiteSpace(currentProjectName) ? null : currentProjectName.Trim();

            // Planilha de Task Plan associada ao projeto aberto (configuração local).
            if (_currentProjectName != null)
            {
                var tp = Community.Services.TaskPlanSettingsStore.Load();
            }

            var saved = TfsConnectionStore.Load(_storageKey);
            OrgUrlBox.Text = saved.OrganizationUrl;
            ProjectBox.Text = saved.TeamProject;
            EnableOrgDiscoveryCheck.IsChecked = saved.EnableOrgPeopleDiscovery;

            // Carrega mapeamentos de classificação por tipo
            foreach (var kv in saved.TypeFieldMappings)
            {
                foreach (var fd in kv.Value.CustomDevopsFields)
                    _classificationMappings.Add(new ClassificationMapping
                    {
                        DevOpsType = kv.Key,
                        FieldRef   = fd.Field,
                        FieldType  = fd.FieldType,
                        Values     = fd.Values ?? string.Empty,
                    });
            }
            // Padrão: Feature → Custom.Type (Picklist) com valores de exemplo
            if (_classificationMappings.Count == 0)
                _classificationMappings.Add(new ClassificationMapping
                {
                    DevOpsType = "Feature", FieldType = "Picklist",
                    FieldRef = NxDevOpsFieldCatalog.DefaultClassificationField,
                    Values = NxDevOpsFieldCatalog.DefaultClassificationValues,
                });

            if (!string.IsNullOrEmpty(saved.PersonalAccessToken))
            {
                PatBox.Password = saved.PersonalAccessToken;
                RememberTokenCheck.IsChecked = true;
            }

            if (!string.IsNullOrWhiteSpace(saved.DevOpsProjectListPath))
            {
                _devOpsProjectListPath = saved.DevOpsProjectListPath;
                ListPathLabel.Text = _devOpsProjectListPath;
            }
        }

        // Abre a página de Personal Access Tokens da organização digitada acima.
        private void OnOpenTokensPageClick(object sender, RoutedEventArgs e)
        {
            var org = OrgUrlBox.Text?.Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(org))
            {
                MessageBox.Show(this, AppStrings.Get("Cfg_OpenTokensNeedUrl"),
                    AppStrings.Get("Cfg_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(org + "/_usersSettings/tokens") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, AppStrings.Get("Cfg_Title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }




        /// <summary>
        /// Pergunta ao DevOps quais dos campos digitados existem e em quais tipos de work item.
        /// So informa e SUGERE: nunca marca nem desmarca um checkbox sozinho — desligar um recurso
        /// em uso por causa de uma leitura seria pior que o problema.
        /// </summary>
        /// <summary>
        /// Excecoes da Task marcadas nesta tela, no formato que o detector entende. Sao a mesma
        /// coisa que o instalador manda: campo padrao do DevOps no lugar do personalizado.
        /// </summary>




        private void OnManageListClick(object sender, RoutedEventArgs e)
        {
            var dlg = new DevOpsProjectListWindow(_devOpsProjectListPath, BuildOptions()) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _devOpsProjectListPath = dlg.ResultFilePath ?? string.Empty;
                ListPathLabel.Text = string.IsNullOrWhiteSpace(_devOpsProjectListPath)
                    ? AppStrings.Get("Imp_NoPortfolio")
                    : _devOpsProjectListPath;
            }
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




        private void ShowStatus(string message)
        {
            StatusText.Text = message;
            StatusText.Visibility = Visibility.Visible;
        }


        /// <summary>Nomes dos campos, tags e classificação — agora numa tela só, para os dois destinos.</summary>
        private void OnOpenFieldsClick(object sender, RoutedEventArgs e)
        {
            new FieldNamesWindow { Owner = this }.ShowDialog();
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(OrgUrlBox.Text) || string.IsNullOrWhiteSpace(PatBox.Password))
            {
                ShowStatus(AppStrings.Get("Cfg_UrlPatRequired"));
                return;
            }

            var options = BuildOptions();
            TfsConnectionStore.Save(options, RememberTokenCheck.IsChecked == true, _storageKey);
            // Conexão nova (URL, projeto ou PAT): descarta metadados lidos com a anterior.
            TfsImportService.ResetMetadataCaches();
            SaveTaskPlanFileAssociation();
            DialogResult = true;
            Close();
        }

        private void SaveTaskPlanFileAssociation()
        {
            if (_currentProjectName == null) return;
            var tp = Community.Services.TaskPlanSettingsStore.Load();
            // O caminho da planilha e editado em "Configurar Campos"; aqui so se reaplica o
            // que esta salvo, para salvar a conexao nao desfazer a associacao.
            var path = Community.Services.TaskPlanSettingsStore.Load().GetProjectFile(_currentProjectName) ?? "";
            if (string.Equals(tp.GetProjectFile(_currentProjectName) ?? "", path, StringComparison.OrdinalIgnoreCase))
                return;
            tp.SetProjectFile(_currentProjectName, path);
            Community.Services.TaskPlanSettingsStore.Save(tp);
        }


        private TfsConnectionOptions BuildOptions() => new()
        {
            OrganizationUrl     = OrgUrlBox.Text?.Trim() ?? string.Empty,
            TeamProject         = ProjectBox.Text?.Trim() ?? string.Empty,
            PersonalAccessToken = PatBox.Password,
            RootWorkItemId      = TfsConnectionStore.Load(_storageKey).RootWorkItemId,
            HoursPerDay         = ProjectCalendarService.WorkingHoursPerDay,
            EffortFieldName = _savedFields.EffortFieldName,
            StartFieldName = _savedFields.StartFieldName,
            FinishFieldName = _savedFields.FinishFieldName,
            PercAlocFieldName = _savedFields.PercAlocFieldName,
            PercConclusaoFieldName = _savedFields.PercConclusaoFieldName,
            EpicTypeFieldEnabled = _savedFields.EpicTypeFieldEnabled,
            EpicTypeFieldName = _savedFields.EpicTypeFieldName,
            BlockDurationFieldEnabled = _savedFields.BlockDurationFieldEnabled,
            BlockDurationFieldName = _savedFields.BlockDurationFieldName,
            ApprovedFieldEnabled = _savedFields.ApprovedFieldEnabled,
            ApprovedFieldName = _savedFields.ApprovedFieldName,
            AdmGroupFieldEnabled = _savedFields.AdmGroupFieldEnabled,
            AdmGroupFieldName = _savedFields.AdmGroupFieldName,
            TaskPriorityRangeEnabled = _savedFields.TaskPriorityRangeEnabled,
            TaskPriorityMin = _savedFields.TaskPriorityMin,
            TaskPriorityMax = _savedFields.TaskPriorityMax,
            SyncVersionFieldName = _savedFields.SyncVersionFieldName,
            SyncNameFieldName = _savedFields.SyncNameFieldName,
            FixedStartTagName = _savedFields.FixedStartTagName,
            UnplannedTagName = _savedFields.UnplannedTagName,
            WipTagName = _savedFields.WipTagName,
            BlockedTagName = _savedFields.BlockedTagName,
            SyncPredecessorLinks = _savedFields.SyncPredecessorLinks,
            EnforceStoryCompletionWithTasks = _savedFields.EnforceStoryCompletionWithTasks,
            EnableOrgPeopleDiscovery = EnableOrgDiscoveryCheck.IsChecked == true,
            FutureSprintDays = _savedFields.FutureSprintDays,
            DevOpsProjectListPath = _devOpsProjectListPath,
            ExtraCreateFields   = [.. _extraFields.Where(f => !string.IsNullOrWhiteSpace(f.Ref))],
            ClassificationPicklistValues = TfsConnectionStore.Load(_storageKey).ClassificationPicklistValues,
            TypeFieldMappings = BuildTypeFieldMappings()
        };

        /// <summary>
        /// Mapeamento por tipo (usar o campo padrão do DevOps, classificação) é editado em
        /// "Configurar Campos". Aqui ele é apenas REPASSADO: salvar a conexão não pode desfazer
        /// o que a outra tela gravou.
        /// </summary>
        private Dictionary<string, TypeFieldConfig> BuildTypeFieldMappings() =>
            new(_savedFields.TypeFieldMappings, StringComparer.OrdinalIgnoreCase);

    }
}
