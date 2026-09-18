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
        }

        // Projeto (cronograma) aberto — habilita o campo do path da planilha de Task Plan.
        private readonly string? _currentProjectName;

        public TfsDevOpsConfigWindow(string storageKey = "NXProject.Community", string? currentProjectName = null)
        {
            InitializeComponent();
            _storageKey = string.IsNullOrWhiteSpace(storageKey) ? "NXProject.Community" : storageKey.Trim();
            _currentProjectName = string.IsNullOrWhiteSpace(currentProjectName) ? null : currentProjectName.Trim();

            // Planilha de Task Plan associada ao projeto aberto (configuração local).
            if (_currentProjectName != null)
            {
                var tp = Community.Services.TaskPlanSettingsStore.Load();
                TaskPlanFileBox.Text = tp.GetProjectFile(_currentProjectName) ?? "";
            }
            else
            {
                TaskPlanFileBox.IsEnabled = false;
                TaskPlanFileBrowse.IsEnabled = false;
            }

            var saved = TfsConnectionStore.Load(_storageKey);
            OrgUrlBox.Text = saved.OrganizationUrl;
            ProjectBox.Text = saved.TeamProject;
            EffortFieldBox.Text = saved.EffortFieldName;
            StartFieldBox.Text = saved.StartFieldName;
            FinishFieldBox.Text = saved.FinishFieldName;
            PercAlocFieldBox.Text = saved.PercAlocFieldName;
            PercConclusaoFieldBox.Text = saved.PercConclusaoFieldName;
            EpicTypeFieldEnabledBox.IsChecked = saved.EpicTypeFieldEnabled;
            EpicTypeFieldBox.Text = string.IsNullOrWhiteSpace(saved.EpicTypeFieldName) ? "EPIC_TYPE" : saved.EpicTypeFieldName;
            EpicTypeFieldBox.IsEnabled = saved.EpicTypeFieldEnabled;
            ApprovedFieldEnabledBox.IsChecked = saved.ApprovedFieldEnabled;
            ApprovedFieldBox.Text = string.IsNullOrWhiteSpace(saved.ApprovedFieldName) ? "Approved" : saved.ApprovedFieldName;
            ApprovedFieldBox.IsEnabled = saved.ApprovedFieldEnabled;
            BlockDurationFieldEnabledBox.IsChecked = saved.BlockDurationFieldEnabled;
            BlockDurationFieldBox.Text = string.IsNullOrWhiteSpace(saved.BlockDurationFieldName)
                ? "block_duration_hours" : saved.BlockDurationFieldName;
            BlockDurationFieldBox.IsEnabled = saved.BlockDurationFieldEnabled;
            AdmGroupFieldEnabledBox.IsChecked = saved.AdmGroupFieldEnabled;
            AdmGroupFieldBox.Text = string.IsNullOrWhiteSpace(saved.AdmGroupFieldName) ? "Adm_NX" : saved.AdmGroupFieldName;
            AdmGroupFieldBox.IsEnabled = saved.AdmGroupFieldEnabled;
            TaskPriorityRangeEnabledBox.IsChecked = saved.TaskPriorityRangeEnabled;
            TaskPriorityMinBox.Text = saved.TaskPriorityMin.ToString(CultureInfo.InvariantCulture);
            TaskPriorityMaxBox.Text = saved.TaskPriorityMax.ToString(CultureInfo.InvariantCulture);
            TaskPriorityMinBox.IsEnabled = saved.TaskPriorityRangeEnabled;
            TaskPriorityMaxBox.IsEnabled = saved.TaskPriorityRangeEnabled;
            SyncVersionFieldBox.Text = saved.SyncVersionFieldName;
            SyncNameFieldBox.Text = saved.SyncNameFieldName;
            FixedStartTagBox.Text = saved.FixedStartTagName;
            UnplannedTagBox.Text = saved.UnplannedTagName;
            WipTagBox.Text = saved.WipTagName;
            BlockedTagBox.Text = saved.BlockedTagName;
            SyncPredecessorLinksCheck.IsChecked = saved.SyncPredecessorLinks;
            EnforceStoryCompletionWithTasksCheck.IsChecked = saved.EnforceStoryCompletionWithTasks;
            EnableOrgDiscoveryCheck.IsChecked = saved.EnableOrgPeopleDiscovery;
            FutureSprintDaysBox.Text = saved.FutureSprintDays.ToString(CultureInfo.InvariantCulture);

            foreach (var f in saved.ExtraCreateFields)
                _extraFields.Add(new ExtraWorkItemField { Ref = f.Ref, Value = f.Value });
            ExtraFieldsList.ItemsSource = _extraFields;

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
                    DevOpsType = "Feature", FieldRef = "Custom.Type", FieldType = "Picklist",
                    Values = "Architecture,Burocracy,Docs,Feature,Hotfix,Refactor",
                });
            ClassificationMappingsList.ItemsSource = _classificationMappings;

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

        // Os nomes dos campos opcionais só são editáveis com a leitura habilitada.
        private void OnEpicTypeFieldEnabledChanged(object sender, RoutedEventArgs e)
        {
            if (EpicTypeFieldBox == null) return;
            EpicTypeFieldBox.IsEnabled = EpicTypeFieldEnabledBox.IsChecked == true;
        }

        private void OnApprovedFieldEnabledChanged(object sender, RoutedEventArgs e)
        {
            if (ApprovedFieldBox == null) return;
            ApprovedFieldBox.IsEnabled = ApprovedFieldEnabledBox.IsChecked == true;
        }

        private void OnBlockDurationFieldEnabledChanged(object sender, RoutedEventArgs e)
        {
            if (BlockDurationFieldBox == null) return;
            BlockDurationFieldBox.IsEnabled = BlockDurationFieldEnabledBox.IsChecked == true;
        }

        /// <summary>
        /// Pergunta ao DevOps quais dos campos digitados existem e em quais tipos de work item.
        /// So informa e SUGERE: nunca marca nem desmarca um checkbox sozinho — desligar um recurso
        /// em uso por causa de uma leitura seria pior que o problema.
        /// </summary>
        private async void OnDetectFieldsClick(object sender, RoutedEventArgs e)
        {
            var opts = BuildOptions();
            if (string.IsNullOrWhiteSpace(opts.OrganizationUrl) || string.IsNullOrWhiteSpace(opts.TeamProject)
                || string.IsNullOrWhiteSpace(opts.PersonalAccessToken))
            {
                DetectFieldsStatus.Text = AppStrings.Get("Cfg_DetectNeedsConnection");
                return;
            }

            // Cada campo vai com os nomes ALTERNATIVOS que a importacao aceita — senao "HH
            // Estimado", que na API e "Esforco Estimado", apareceria como inexistente.
            var wanted = new List<(string Label, string Name, string Kind)>
            {
                (AppStrings.Get("Cfg_EffortField"), EffortFieldBox.Text, "effort"),
                (AppStrings.Get("Cfg_StartField"), StartFieldBox.Text, "start"),
                (AppStrings.Get("Cfg_FinishField"), FinishFieldBox.Text, "finish"),
                (AppStrings.Get("Cfg_PercAlocField"), PercAlocFieldBox.Text, "percAloc"),
                (AppStrings.Get("Cfg_PercConclusaoField"), PercConclusaoFieldBox.Text, "percConclusao"),
                (AppStrings.Get("Cfg_EpicTypeField"), EpicTypeFieldBox.Text, "epicType"),
                (AppStrings.Get("Cfg_ApprovedField"), ApprovedFieldBox.Text, "approved"),
                (AppStrings.Get("Cfg_AdmGroupField"), AdmGroupFieldBox.Text, "admGroup"),
                (AppStrings.Get("Cfg_BlockDurationField"), BlockDurationFieldBox.Text, "blockDuration"),
                (AppStrings.Get("Cfg_SyncVersionField"), SyncVersionFieldBox.Text, "syncVersion"),
                (AppStrings.Get("Cfg_SyncNameField"), SyncNameFieldBox.Text, "syncName"),
            };

            // Onde cada campo DEVE existir para o recurso funcionar. E o que transforma
            // "existe/nao existe" em "da para habilitar ou nao".
            static string[] Scope(string kind) => kind switch
            {
                "effort" or "start" or "finish" or "syncVersion" or "syncName"
                    => new[] { "User Story", "Feature", "Epic" },
                "percAloc" or "percConclusao" => new[] { "User Story" },
                "epicType" => new[] { "Epic" },
                "approved" or "blockDuration" => new[] { "Task" },
                "admGroup" => new[] { "Project" },
                _ => System.Array.Empty<string>()
            };

            try
            {
                DetectFieldsButton.IsEnabled = false;
                DetectFieldsStatus.Text = AppStrings.Get("Cfg_DetectRunning");
                var probes = await TfsImportService.ProbeFieldsAsync(
                    opts, wanted.Select(w => (w.Name, TfsImportService.FieldAliases(w.Kind))));

                var lines = new List<string>();
                var missing = 0;
                foreach (var (label, name, _) in wanted)
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var pr = probes.FirstOrDefault(x =>
                        string.Equals(x.Configured, name.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (pr is null) continue;
                    var scope = Scope(wanted.First(w => w.Label == label).Kind);
                    if (!pr.Exists)
                    {
                        missing++;
                        // Dizer ONDE o campo deveria estar e o que torna o aviso acionavel.
                        var need = scope.Length > 0
                            ? " — " + AppStrings.Get("Cfg_DetectExpectedIn", string.Join(", ", scope))
                            : "";
                        lines.Add($"❌ {label}: '{name.Trim()}' "
                                  + AppStrings.Get("Cfg_DetectNotFound") + need);
                        continue;
                    }
                    var where = pr.Types.Count > 0 ? string.Join(", ", pr.Types)
                                                   : AppStrings.Get("Cfg_DetectNoType");
                    var tp = string.IsNullOrEmpty(pr.FieldType) ? "" : $" [{pr.FieldType}]";
                    // So avisa falta onde o campo FAZ FALTA: cobrar "Esforco Estimado" na Task
                    // ou na Project so gerava ruido, porque o NX nunca usa o campo ali.
                    var gapTypes = pr.MissingTypes
                        .Where(t => scope.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();
                    var gap = gapTypes.Count > 0
                        ? "  ⚠ " + AppStrings.Get("Cfg_DetectMissingIn", string.Join(", ", gapTypes))
                        : "";
                    // Achado por nome ALTERNATIVO: o que voce digitou nao existe no DevOps. O NX
                    // funciona assim mesmo (a importacao usa a mesma lista), mas quem configurou
                    // precisa saber — senao um nome errado passa por validado.
                    var via = pr.MatchedByAlias
                        ? "  ⚠ " + AppStrings.Get("Cfg_DetectViaAlias", pr.MatchedBy)
                        : "";
                    lines.Add($"✅ {label}: {pr.ReferenceName} {tp} — {where}{via}{gap}".Replace("  —", " —"));
                }

                // Campos com checkbox: a deteccao MARCA o que existe no tipo em que o NX usa e
                // DESMARCA o que nao existe. Nada e gravado aqui — so vale se voce Salvar, e o
                // Cancelar desfaz tudo.
                var toggled = new List<string>();
                void ApplyCheck(string kind, CheckBox box, TextBox nameBox, string label)
                {
                    var name = nameBox.Text?.Trim() ?? "";
                    if (string.IsNullOrWhiteSpace(name)) return;
                    var pr = probes.FirstOrDefault(x =>
                        string.Equals(x.Configured, name, StringComparison.OrdinalIgnoreCase));
                    if (pr is null) return;
                    // Existir na organizacao nao basta: tem que existir NO TIPO em que o NX usa.
                    var scope = Scope(kind);
                    var usable = pr.Exists && (scope.Length == 0
                        || scope.Any(t => pr.Types.Contains(t, StringComparer.OrdinalIgnoreCase)));
                    if ((box.IsChecked == true) == usable) return;
                    box.IsChecked = usable;
                    nameBox.IsEnabled = usable;
                    toggled.Add((usable ? "✔ " : "✖ ") + label);
                }
                ApplyCheck("epicType", EpicTypeFieldEnabledBox, EpicTypeFieldBox, AppStrings.Get("Cfg_EpicTypeField"));
                ApplyCheck("approved", ApprovedFieldEnabledBox, ApprovedFieldBox, AppStrings.Get("Cfg_ApprovedField"));
                ApplyCheck("admGroup", AdmGroupFieldEnabledBox, AdmGroupFieldBox, AppStrings.Get("Cfg_AdmGroupField"));
                ApplyCheck("blockDuration", BlockDurationFieldEnabledBox, BlockDurationFieldBox,
                           AppStrings.Get("Cfg_BlockDurationField"));
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

        private void OnAdmGroupFieldEnabledChanged(object sender, RoutedEventArgs e)
        {
            if (AdmGroupFieldBox == null) return;
            AdmGroupFieldBox.IsEnabled = AdmGroupFieldEnabledBox.IsChecked == true;
        }

        private void OnTaskPriorityRangeEnabledChanged(object sender, RoutedEventArgs e)
        {
            if (TaskPriorityMinBox == null || TaskPriorityMaxBox == null) return;
            var enabled = TaskPriorityRangeEnabledBox.IsChecked == true;
            TaskPriorityMinBox.IsEnabled = enabled;
            TaskPriorityMaxBox.IsEnabled = enabled;
        }

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

        private void OnAddClassificationMapping(object sender, RoutedEventArgs e)
            => _classificationMappings.Add(new ClassificationMapping { DevOpsType = "Feature", FieldRef = string.Empty });

        private void OnRemoveClassificationMapping(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ClassificationMapping m)
                _classificationMappings.Remove(m);
        }

        private void OnAddExtraField(object sender, RoutedEventArgs e)
            => _extraFields.Add(new ExtraWorkItemField());

        private void OnRemoveExtraField(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ExtraWorkItemField field)
                _extraFields.Remove(field);
        }

        private void ShowStatus(string message)
        {
            StatusText.Text = message;
            StatusText.Visibility = Visibility.Visible;
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
            var path = TaskPlanFileBox.Text?.Trim() ?? "";
            var tp = Community.Services.TaskPlanSettingsStore.Load();
            if (string.Equals(tp.GetProjectFile(_currentProjectName) ?? "", path, StringComparison.OrdinalIgnoreCase))
                return;
            tp.SetProjectFile(_currentProjectName, path);
            Community.Services.TaskPlanSettingsStore.Save(tp);
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

        private TfsConnectionOptions BuildOptions() => new()
        {
            OrganizationUrl     = OrgUrlBox.Text?.Trim() ?? string.Empty,
            TeamProject         = ProjectBox.Text?.Trim() ?? string.Empty,
            PersonalAccessToken = PatBox.Password,
            RootWorkItemId      = TfsConnectionStore.Load(_storageKey).RootWorkItemId,
            HoursPerDay         = ProjectCalendarService.WorkingHoursPerDay,
            EffortFieldName     = string.IsNullOrWhiteSpace(EffortFieldBox.Text)    ? "HH Estimado"   : EffortFieldBox.Text.Trim(),
            StartFieldName      = string.IsNullOrWhiteSpace(StartFieldBox.Text)     ? "Data_Inicio"   : StartFieldBox.Text.Trim(),
            FinishFieldName     = string.IsNullOrWhiteSpace(FinishFieldBox.Text)    ? "Data_Fim"      : FinishFieldBox.Text.Trim(),
            PercAlocFieldName   = string.IsNullOrWhiteSpace(PercAlocFieldBox.Text)  ? "Perc_Alocacao" : PercAlocFieldBox.Text.Trim(),
            PercConclusaoFieldName = string.IsNullOrWhiteSpace(PercConclusaoFieldBox.Text) ? "Perc_Conclusao" : PercConclusaoFieldBox.Text.Trim(),
            EpicTypeFieldEnabled = EpicTypeFieldEnabledBox.IsChecked == true,
            EpicTypeFieldName = string.IsNullOrWhiteSpace(EpicTypeFieldBox.Text) ? "EPIC_TYPE" : EpicTypeFieldBox.Text.Trim(),
            BlockDurationFieldEnabled = BlockDurationFieldEnabledBox.IsChecked == true,
            BlockDurationFieldName = string.IsNullOrWhiteSpace(BlockDurationFieldBox.Text)
                ? "block_duration_hours" : BlockDurationFieldBox.Text.Trim(),
            ApprovedFieldEnabled = ApprovedFieldEnabledBox.IsChecked == true,
            ApprovedFieldName = string.IsNullOrWhiteSpace(ApprovedFieldBox.Text) ? "Approved" : ApprovedFieldBox.Text.Trim(),
            AdmGroupFieldEnabled = AdmGroupFieldEnabledBox.IsChecked == true,
            AdmGroupFieldName = string.IsNullOrWhiteSpace(AdmGroupFieldBox.Text) ? "Adm_NX" : AdmGroupFieldBox.Text.Trim(),
            TaskPriorityRangeEnabled = TaskPriorityRangeEnabledBox.IsChecked == true,
            TaskPriorityMin = int.TryParse(TaskPriorityMinBox.Text?.Trim(), out var prioMin) && prioMin >= 1 ? prioMin : 1,
            TaskPriorityMax = int.TryParse(TaskPriorityMaxBox.Text?.Trim(), out var prioMax) && prioMax >= 1 ? prioMax : 4,
            SyncVersionFieldName = string.IsNullOrWhiteSpace(SyncVersionFieldBox.Text) ? "Sync_version" : SyncVersionFieldBox.Text.Trim(),
            SyncNameFieldName   = string.IsNullOrWhiteSpace(SyncNameFieldBox.Text)   ? "Sync_Name"    : SyncNameFieldBox.Text.Trim(),
            FixedStartTagName   = string.IsNullOrWhiteSpace(FixedStartTagBox.Text)  ? "DT-INI-NEG"   : FixedStartTagBox.Text.Trim(),
            UnplannedTagName    = string.IsNullOrWhiteSpace(UnplannedTagBox.Text)   ? "NP"           : UnplannedTagBox.Text.Trim(),
            WipTagName          = string.IsNullOrWhiteSpace(WipTagBox.Text)         ? "WIP"          : WipTagBox.Text.Trim(),
            BlockedTagName      = string.IsNullOrWhiteSpace(BlockedTagBox.Text)     ? "BLOCK"        : BlockedTagBox.Text.Trim(),
            SyncPredecessorLinks = SyncPredecessorLinksCheck.IsChecked == true,
            EnforceStoryCompletionWithTasks = EnforceStoryCompletionWithTasksCheck.IsChecked == true,
            EnableOrgPeopleDiscovery = EnableOrgDiscoveryCheck.IsChecked == true,
            FutureSprintDays    = int.TryParse(FutureSprintDaysBox.Text?.Trim(), out var fsd) && fsd >= 0 ? fsd : 90,
            DevOpsProjectListPath = _devOpsProjectListPath,
            ExtraCreateFields   = [.. _extraFields.Where(f => !string.IsNullOrWhiteSpace(f.Ref))],
            ClassificationPicklistValues = TfsConnectionStore.Load(_storageKey).ClassificationPicklistValues,
            TypeFieldMappings = BuildTypeFieldMappings()
        };

        private Dictionary<string, TypeFieldConfig> BuildTypeFieldMappings()
        {
            var saved = TfsConnectionStore.Load(_storageKey);
            var mappings = new Dictionary<string, TypeFieldConfig>(saved.TypeFieldMappings, StringComparer.OrdinalIgnoreCase);

            // Limpa CustomDevopsFields de todos os tipos antes de reaplicar
            foreach (var cfg in mappings.Values)
                cfg.CustomDevopsFields = [];

            // Agrupa por tipo DevOps e salva lista de campos
            var grouped = _classificationMappings
                .Where(m => !string.IsNullOrWhiteSpace(m.FieldRef))
                .GroupBy(m => string.Equals(m.DevOpsType, "Todos", StringComparison.OrdinalIgnoreCase) ? "*" : m.DevOpsType,
                         StringComparer.OrdinalIgnoreCase);

            foreach (var g in grouped)
            {
                if (!mappings.TryGetValue(g.Key, out var cfg))
                    cfg = new TypeFieldConfig();
                cfg.CustomDevopsFields = g.Select(m => new ClassificationFieldDef
                {
                    Field     = m.FieldRef.Trim(),
                    FieldType = string.IsNullOrWhiteSpace(m.FieldType) ? "Picklist" : m.FieldType.Trim(),
                    Values    = string.IsNullOrWhiteSpace(m.Values)    ? null       : m.Values.Trim(),
                }).ToList();
                mappings[g.Key] = cfg;
            }

            return mappings;
        }
    }
}
