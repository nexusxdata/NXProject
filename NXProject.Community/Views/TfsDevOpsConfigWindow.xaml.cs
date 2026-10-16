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

        /// <param name="abaInicial">
        /// Qual aba abrir: 0 = Azure DevOps, 1 = GitHub Projects, 2 = Campos e tags. Quem chega
        /// por um erro do GitProject ja cai na aba dele, em vez de procurar.
        /// </param>
        public TfsDevOpsConfigWindow(string storageKey = "NXProject.Community",
            string? currentProjectName = null, int abaInicial = 0)
        {
            InitializeComponent();
            // Aberta pelo Setup (StartupUri), o construtor vem sem parametro: a aba pedida
            // chega pela App, que foi quem leu a linha de comando.
            var aba = abaInicial > 0 ? abaInicial : NXProject.CommunityApp.AbaDaConexao;
            if (aba > 0) Abas.SelectedIndex = aba;
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
            DevOpsEnabledCheck.IsChecked = _savedFields.DevOpsEnabled;
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
            // Conexão desligada não precisa de URL nem de PAT: é justamente o caso de quem usa
            // só o GitProject. Exigir os dois aqui impediria de desligar.
            if (DevOpsEnabledCheck.IsChecked == true
                && (string.IsNullOrWhiteSpace(OrgUrlBox.Text) || string.IsNullOrWhiteSpace(PatBox.Password)))
            {
                ShowStatus(AppStrings.Get("Cfg_UrlPatRequired"));
                return;
            }

            var options = BuildOptions();

            // A aba do GitHub Projects grava junto: e a MESMA conexao do projeto, so que do
            // outro destino. Recusa dela (preenchida pela metade) impede o salvar inteiro e
            // leva a tela para a aba onde esta o problema.
            if (GitPanel.AplicarEm(options) is { } problemaGit)
            {
                Abas.SelectedIndex = 1;
                ShowStatus(problemaGit);
                return;
            }

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


        /// <summary>
        /// A configuração a salvar: parte do que JÁ ESTÁ GRAVADO e sobrescreve só o que ESTA
        /// tela edita.
        ///
        /// Antes ela montava um objeto novo campo a campo, e tudo o que não estivesse na lista
        /// era apagado ao salvar a conexão. Foi assim que o cadastro de origem dos projetos
        /// (PortfolioProjectConfigs) sumiu: salvar a conexão zerava o portfólio, e a tela de
        /// importação, sem cadastro, ficava com a origem do projeto anterior — um projeto do
        /// DevOps importando como GitProject. Partir do salvo faz um campo novo sobreviver por
        /// construção, em vez de depender de alguém lembrar de acrescentá-lo aqui.
        /// </summary>
        private TfsConnectionOptions BuildOptions()
        {
            var o = TfsConnectionStore.Load(_storageKey);

            o.OrganizationUrl     = OrgUrlBox.Text?.Trim() ?? string.Empty;
            o.TeamProject         = ProjectBox.Text?.Trim() ?? string.Empty;
            o.PersonalAccessToken = PatBox.Password;
            o.HoursPerDay         = ProjectCalendarService.WorkingHoursPerDay;
            o.EnableOrgPeopleDiscovery = EnableOrgDiscoveryCheck.IsChecked == true;
            o.DevOpsEnabled       = DevOpsEnabledCheck.IsChecked == true;
            o.DevOpsProjectListPath = _devOpsProjectListPath;
            o.ExtraCreateFields   = [.. _extraFields.Where(f => !string.IsNullOrWhiteSpace(f.Ref))];
            // Mapeamento por tipo e a conexao do GitProject sao editados em outras telas: aqui
            // vale o que elas gravaram (o painel do GitProject sobrescreve no salvar).
            o.TypeFieldMappings   = BuildTypeFieldMappings();
            return o;
        }

        /// <summary>
        /// Mapeamento por tipo (usar o campo padrão do DevOps, classificação) é editado em
        /// "Configurar Campos". Aqui ele é apenas REPASSADO: salvar a conexão não pode desfazer
        /// o que a outra tela gravou.
        /// </summary>
        private Dictionary<string, TypeFieldConfig> BuildTypeFieldMappings() =>
            new(_savedFields.TypeFieldMappings, StringComparer.OrdinalIgnoreCase);

    }
}
