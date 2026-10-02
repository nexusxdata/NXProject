// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NXProject.Services;

namespace NXProject.Setup;

/// <summary>
/// Passo 4 do instalador, em janela própria: os campos que o NXProject precisa no Azure DevOps.
/// Ficou fora da tela principal porque a lista de campos com nome editável ocupava mais espaço
/// do que um passo opcional merece — lá sobrou o convite, aqui mora o trabalho.
///
/// A ordem é sempre a mesma — DETECTAR primeiro, criar depois, e só o que ficou marcado. Criar
/// sem olhar seria o pior dos mundos: o campo passa a existir para todos os projetos do
/// processo, e um nome já usado por outra equipe não pode ser reaproveitado às cegas.
/// </summary>
public partial class DevOpsFieldsWindow : Window
{
    private sealed class FieldRow
    {
        public NxDevOpsFieldCatalog.NxFieldSpec Spec { get; init; } = new();
        public CheckBox Create { get; init; } = new();
        public TextBox Name { get; init; } = new();
        public TextBlock Status { get; init; } = new();
        /// <summary>
        /// Work item -> caixa "usar o campo padrão do DevOps aqui". Só existe para o tipo que
        /// tem equivalente de fábrica (datas e HH). Na Task vem marcada; na Story é alternativa.
        /// </summary>
        public Dictionary<string, CheckBox> UseStandardByType { get; } =
            new(StringComparer.CurrentCultureIgnoreCase);
    }

    private readonly List<FieldRow> _fieldRows = new();
    private DevOpsFieldSetupService.ProcessInfo? _process;

    /// <summary>
    /// Recursos OPCIONAIS ligados na configuração do NXProject. Campo opcional só entra marcado
    /// para criação quando o recurso dele está ligado no NX — criar um campo que ninguém vai
    /// preencher é sujeira na organização. Sem NX instalado, vale o padrão de fábrica do NX.
    /// </summary>
    private readonly Dictionary<string, bool> _optionalEnabled =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["approved"] = true,
            ["epicType"] = true,
            ["admGroup"] = true,
            ["blockDuration"] = false,   // desligado por padrão, como no NX
        };

    /// <summary>O campo deve nascer marcado quando não existe no DevOps?</summary>
    private bool AutoCheck(NxDevOpsFieldCatalog.NxFieldSpec spec) =>
        !spec.Optional || !_optionalEnabled.TryGetValue(spec.Key, out var on) || on;

    public DevOpsFieldsWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => { BuildFieldRows(); await TryUseSavedPatAsync(LoadInstalledConfig()); };
    }

    /// <summary>
    /// Aproveita o que o NXProject já tem configurado nesta máquina: organização, projeto, nomes
    /// de campo e — se o usuário mandou lembrar — o token. Quem já usa o NX não tem por que
    /// digitar tudo de novo aqui; e quem está instalando do zero simplesmente não acha o arquivo
    /// e continua preenchendo à mão.
    ///
    /// Leitura direta do JSON de propósito: o instalador não carrega o modelo de configuração
    /// inteiro do app, só os punhados de campo que esta tela usa.
    /// </summary>
    /// <summary>
    /// O token do NXProject so e reaproveitado se ele PUDER criar campos.
    ///
    /// O PAT do dia a dia le e grava work item; criar campo mexe no processo e exige bem mais.
    /// Trazer o token salvo para a tela dava a impressao de que estava tudo pronto, e o problema
    /// so aparecia no fim, depois de tentar criar. Agora o campo nasce VAZIO e so e preenchido
    /// quando a checagem confirma o acesso — senao a tela ja pede um token com permissao, com o
    /// passo a passo a vista.
    /// </summary>
    private async Task TryUseSavedPatAsync(string savedPat)
    {
        if (string.IsNullOrEmpty(savedPat) || string.IsNullOrWhiteSpace(DevOpsOrgBox.Text)) return;

        Step4StatusText.Text = App.Str("Setup_Step4CheckingSavedPat");
        try
        {
            var (perm, _) = await DevOpsFieldSetupService.CanEditProcessAsync(
                DevOpsOrgBox.Text.Trim(), savedPat);
            if (perm == DevOpsFieldSetupService.PermissionProbe.Allowed)
            {
                DevOpsPatBox.Password = savedPat;
                Step4StatusText.Text = App.Str("Setup_Step4SavedPatOk");
                return;
            }
            // Sem acesso (ou nem deu para saber): melhor deixar em branco do que levar a pessoa
            // a tentar criar com um token que ja sabemos insuficiente.
            Step4StatusText.Text = App.Str(perm == DevOpsFieldSetupService.PermissionProbe.Unknown
                ? "Setup_Step4SavedPatUnknown" : "Setup_Step4SavedPatNoAccess");
            PatHelpPanel.Visibility = Visibility.Visible;
        }
        catch (Exception ex) { Step4StatusText.Text = ex.Message; }
    }

    /// <summary>Devolve o token guardado (sem coloca-lo na tela); vazio quando nao ha.</summary>
    private string LoadInstalledConfig()
    {
        try
        {
            var file = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NXProject.Community", "config_nxproject.json");
            if (!System.IO.File.Exists(file)) return "";

            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(file));
            var root = doc.RootElement;
            string Str(string name) => root.TryGetProperty(name, out var v)
                && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() ?? "" : "";

            DevOpsOrgBox.Text = Str("OrganizationUrl");
            DevOpsProjectBox.Text = Str("TeamProject");

            // Nome de campo já configurado vence o padrão: é ele que o NX vai procurar no DevOps.
            var byKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["effort"] = Str("EffortFieldName"),
                ["start"] = Str("StartFieldName"),
                ["finish"] = Str("FinishFieldName"),
                ["percAloc"] = Str("PercAlocFieldName"),
                ["percConclusao"] = Str("PercConclusaoFieldName"),
                ["syncVersion"] = Str("SyncVersionFieldName"),
                ["syncName"] = Str("SyncNameFieldName"),
                ["approved"] = Str("ApprovedFieldName"),
                ["epicType"] = Str("EpicTypeFieldName"),
                ["admGroup"] = Str("AdmGroupFieldName"),
                ["blockDuration"] = Str("BlockDurationFieldName"),
            };
            foreach (var row in _fieldRows)
                if (byKey.TryGetValue(row.Spec.Key, out var n) && !string.IsNullOrWhiteSpace(n))
                    row.Name.Text = n;

            // O token só está guardado se o usuário pediu para lembrar; e vem cifrado (DPAPI do
            // próprio usuário do Windows), então só abre nesta máquina e nesta conta.
            var remember = root.TryGetProperty("RememberToken", out var rt)
                && rt.ValueKind == System.Text.Json.JsonValueKind.True;
            // Os recursos opcionais seguem o que está ligado no NX: é a mesma regra da tela de
            // configuração, para o instalador não sugerir campo de recurso desligado.
            bool Flag(string name, bool fallback) => root.TryGetProperty(name, out var v)
                ? v.ValueKind == System.Text.Json.JsonValueKind.True
                : fallback;
            _optionalEnabled["approved"] = Flag("ApprovedFieldEnabled", true);
            _optionalEnabled["epicType"] = Flag("EpicTypeFieldEnabled", true);
            _optionalEnabled["admGroup"] = Flag("AdmGroupFieldEnabled", true);
            _optionalEnabled["blockDuration"] = Flag("BlockDurationFieldEnabled", false);

            Step4StatusText.Text = App.Str("Setup_Step4LoadedNoPat");
            // O token NAO vai direto para a tela: quem decide e a checagem de acesso.
            return remember ? WindowsDataProtection.Decrypt(Str("EncryptedToken")) : "";
        }
        catch { /* configuração ilegível não impede preencher à mão */ }
        return "";
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Servidor onde os campos serão configurados. Só o Azure DevOps está implementado; o
    /// GitProject (GitHub Projects) avisa e volta — o que muda em cada botão desta janela está
    /// desenhado em <c>Plano_GitProject_Import_Sync.md</c>, na raiz do repositório.
    /// </summary>
    private void OnServerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ServerCombo == null || ServerCombo.SelectedIndex == 0) return;
        ServerCombo.SelectedIndex = 0;
        MessageBox.Show(this, App.Str("Setup_Step4ServerTodo"),
            App.Str("Setup_Step4ServerTodoTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// O que viaja no arquivo de configuração. É de propósito o MÍNIMO que faz uma instalação
    /// falar a mesma língua da outra: nomes de campo, a escolha de campo padrão na Task e quais
    /// recursos opcionais valem. O token NUNCA entra — ele é pessoal e fica só na janela.
    /// Organização e projeto entram como referência de onde a configuração nasceu; quem importa
    /// decide se aproveita (é comum a mesma organização, outro projeto).
    /// </summary>
    private sealed class FieldConfigFile
    {
        public string Format { get; set; } = "nxproject-devops-fields";
        public int Version { get; set; } = 1;
        public string ExportedAt { get; set; } = "";
        public string Organization { get; set; } = "";
        public string Project { get; set; } = "";
        public Dictionary<string, string> Fields { get; set; } = new();
        public List<string> TaskStandard { get; set; } = new();
        public Dictionary<string, bool> OptionalEnabled { get; set; } = new();
    }

    private static readonly System.Text.Json.JsonSerializerOptions ConfigJson =
        new() { WriteIndented = true };

    private void OnExportFieldsClick(object sender, RoutedEventArgs e)
    {
        BuildFieldRows();
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = App.Str("Setup_Step4Export"),
            FileName = "nxproject-devops-fields.json",
            DefaultExt = ".json",
            Filter = "JSON (*.json)|*.json"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var data = new FieldConfigFile
            {
                ExportedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                Organization = DevOpsOrgBox.Text.Trim(),
                Project = DevOpsProjectBox.Text.Trim(),
                Fields = _fieldRows.ToDictionary(r => r.Spec.Key, r => r.Name.Text?.Trim() ?? ""),
                TaskStandard = StandardChoices().Select(c => c.Key + "|" + c.WorkItemType).ToList(),
                OptionalEnabled = new Dictionary<string, bool>(_optionalEnabled, StringComparer.OrdinalIgnoreCase),
            };
            System.IO.File.WriteAllText(dlg.FileName,
                System.Text.Json.JsonSerializer.Serialize(data, ConfigJson));
            Step4StatusText.Text = App.Str("Setup_Step4Exported", dlg.FileName);
        }
        catch (Exception ex) { Step4StatusText.Text = ex.Message; }
    }

    private void OnImportFieldsClick(object sender, RoutedEventArgs e)
    {
        BuildFieldRows();
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = App.Str("Setup_Step4Import"),
            DefaultExt = ".json",
            Filter = "JSON (*.json)|*.json"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var data = System.Text.Json.JsonSerializer.Deserialize<FieldConfigFile>(
                System.IO.File.ReadAllText(dlg.FileName), ConfigJson);
            if (data == null || !string.Equals(data.Format, "nxproject-devops-fields",
                    StringComparison.OrdinalIgnoreCase))
            {
                Step4StatusText.Text = App.Str("Setup_Step4ImportBadFile");
                return;
            }

            // Organizacao e projeto so entram quando a tela esta vazia: quem ja apontou para o
            // seu projeto nao quer ser levado para o do colega sem perceber.
            if (string.IsNullOrWhiteSpace(DevOpsOrgBox.Text) && !string.IsNullOrWhiteSpace(data.Organization))
                DevOpsOrgBox.Text = data.Organization;
            if (string.IsNullOrWhiteSpace(DevOpsProjectBox.Text) && !string.IsNullOrWhiteSpace(data.Project))
                DevOpsProjectBox.Text = data.Project;

            var applied = 0;
            foreach (var row in _fieldRows)
            {
                if (data.Fields.TryGetValue(row.Spec.Key, out var name) && !string.IsNullOrWhiteSpace(name)
                    && !string.Equals(row.Name.Text?.Trim(), name.Trim(), StringComparison.Ordinal))
                {
                    row.Name.Text = name.Trim();
                    applied++;
                }
                foreach (var kv in row.UseStandardByType)
                    kv.Value.IsChecked = data.TaskStandard.Contains(row.Spec.Key + "|" + kv.Key, StringComparer.OrdinalIgnoreCase)
                        // Compatibilidade com o arquivo da primeira versao, que so tinha a Task.
                        || (string.Equals(kv.Key, "Task", StringComparison.OrdinalIgnoreCase)
                            && data.TaskStandard.Contains(row.Spec.Key, StringComparer.OrdinalIgnoreCase));
            }
            foreach (var kv in data.OptionalEnabled)
                _optionalEnabled[kv.Key] = kv.Value;

            // A deteccao anterior era sobre os nomes antigos: sai de cena ate detectar de novo.
            ResetFieldStatus();
            Step4StatusText.Text = App.Str("Setup_Step4Imported", applied.ToString());
        }
        catch (Exception ex) { Step4StatusText.Text = ex.Message; }
    }

    /// <summary>
    /// Leva para a configuração do NXProject o que foi decidido aqui:
    ///
    ///  • os NOMES dos campos, quando editados — sem isso, renomear na tela do instalador seria
    ///    uma armadilha: o campo nasceria com um nome no DevOps e o NX continuaria procurando o
    ///    antigo, dando "campo não encontrado" na primeira importação;
    ///  • a escolha "usar o campo padrão na Task", como TypeFieldMappings["Task"] — o mecanismo
    ///    que o import e o sync já consultam por tipo.
    ///
    /// Reescreve só essas chaves, preservando o resto do arquivo. Sem NXProject instalado não há
    /// o que atualizar, e o que foi feito no DevOps continua valendo.
    /// </summary>
    private void SaveStandardChoicesToConfig()
    {
        var choices = StandardChoices();
        try
        {
            var file = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NXProject.Community", "config_nxproject.json");
            if (!System.IO.File.Exists(file)) return;

            var node = System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(file))
                       as System.Text.Json.Nodes.JsonObject;
            if (node == null) return;

            if (node["TypeFieldMappings"] is not System.Text.Json.Nodes.JsonObject maps)
            {
                maps = new System.Text.Json.Nodes.JsonObject();
                node["TypeFieldMappings"] = maps;
            }
            // Nome de cada campo, como ficou na tela. Só grava o que mudou, para não encher o
            // arquivo de chaves iguais ao padrão.
            var nameProps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["effort"] = "EffortFieldName",
                ["start"] = "StartFieldName",
                ["finish"] = "FinishFieldName",
                ["percAloc"] = "PercAlocFieldName",
                ["percConclusao"] = "PercConclusaoFieldName",
                ["syncVersion"] = "SyncVersionFieldName",
                ["syncName"] = "SyncNameFieldName",
                ["approved"] = "ApprovedFieldName",
                ["epicType"] = "EpicTypeFieldName",
                ["admGroup"] = "AdmGroupFieldName",
                ["blockDuration"] = "BlockDurationFieldName",
            };
            var renamed = 0;
            foreach (var row in _fieldRows)
            {
                if (!nameProps.TryGetValue(row.Spec.Key, out var jsonProp)) continue;
                var typed = row.Name.Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(typed)) continue;
                var current = node[jsonProp]?.GetValue<string>() ?? "";
                if (string.Equals(current, typed, StringComparison.Ordinal)) continue;
                node[jsonProp] = typed;
                renamed++;
            }

            foreach (var row in _fieldRows)
            {
                var prop = row.Spec.Key switch
                {
                    "start" => "StartField",
                    "finish" => "FinishField",
                    "effort" => "EffortField",
                    "percAloc" => "PercAlocField",
                    "percConclusao" => "PercConclusaoField",
                    _ => null
                };
                if (prop == null) continue;
                foreach (var wit in row.UseStandardByType.Keys)
                {
                    var std = row.Spec.OptionalStandardFor(wit);
                    if (string.IsNullOrEmpty(std)) continue;
                    if (maps[wit] is not System.Text.Json.Nodes.JsonObject typeMap)
                    {
                        typeMap = new System.Text.Json.Nodes.JsonObject();
                        maps[wit] = typeMap;
                    }
                    var chosen = choices.Any(c => string.Equals(c.Key, row.Spec.Key, StringComparison.OrdinalIgnoreCase)
                                               && string.Equals(c.WorkItemType, wit, StringComparison.OrdinalIgnoreCase));
                    // Desmarcado volta ao padrão global (campo personalizado): remove a exceção.
                    if (chosen) typeMap[prop] = std; else typeMap.Remove(prop);
                }
            }

            System.IO.File.WriteAllText(file, node.ToJsonString(
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Step4StatusText.Text += " " + (renamed > 0
                ? App.Str("Setup_Step4ConfigUpdatedNames", renamed.ToString())
                : App.Str("Setup_Step4ConfigUpdated"));
        }
        catch { /* configuração ilegível: a criação no DevOps já valeu */ }
    }

    private void OnHyperlinkRequestNavigate(object sender,
        System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch { /* sem navegador padrao: o link continua visivel para copiar */ }
        e.Handled = true;
    }

    /// <summary>Monta uma linha por campo do catálogo. Os nomes já vêm com o padrão atual.</summary>
    private void BuildFieldRows()
    {
        if (_fieldRows.Count > 0) return;
        var host = new StackPanel();
        foreach (var spec in DevOpsFieldSetupService.Catalog)
        {
            var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var cb = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
            var name = new TextBox
            {
                Text = spec.DefaultName, Height = 20, FontSize = 11,
                Margin = new Thickness(0, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center
            };
            // Mexer no nome invalida a detecção anterior: o que estava marcado era sobre o nome antigo.
            name.TextChanged += (_, _) => { ResetFieldStatus(); };
            var info = new TextBlock
            {
                Text = spec.Type + " · " + string.Join(", ", spec.Scope),
                FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0)
            };
            var status = new TextBlock
            {
                FontSize = 10.5, TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Campo com equivalente de fábrica (Data_Inicio -> Start Date, HH -> Original
            // Estimate): a organização escolhe qual usar em CADA tipo. Marcado, o NX passa a ler
            // e gravar o campo padrão naquele work item (via TypeFieldMappings) e o personalizado
            // deixa de fazer falta ali.
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            var row = new FieldRow { Spec = spec, Create = cb, Name = name, Status = status };
            foreach (var wit in spec.Scope)
            {
                var std = spec.OptionalStandardFor(wit);
                if (string.IsNullOrEmpty(std)) continue;
                var useStd = new CheckBox
                {
                    Content = App.Str("Setup_Step4UseStdOnType", wit, ShortFieldName(std)),
                    FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = App.Str("Setup_Step4UseStdOnTaskTip"),
                    Margin = new Thickness(0, 0, 8, 0),
                    // O que o NXProject ja faz hoje nasce marcado (HH da Task).
                    IsChecked = spec.StandardDefaultOn(wit)
                };
                useStd.Click += (_, _) => ResetFieldStatus();
                row.UseStandardByType[wit] = useStd;
                right.Children.Add(useStd);
            }
            right.Children.Add(status);

            Grid.SetColumn(cb, 0); Grid.SetColumn(name, 1); Grid.SetColumn(info, 2); Grid.SetColumn(right, 3);
            grid.Children.Add(cb); grid.Children.Add(name); grid.Children.Add(info); grid.Children.Add(right);
            host.Children.Add(grid);

            _fieldRows.Add(row);
        }
        FieldList.Items.Add(host);
    }

    /// <summary>
    /// Explica, numa janela, por que o DevOps recusou a criação e oferece abrir a página onde se
    /// cria o PAT com o escopo necessário. Criar campo mexe no PROCESSO da organização: não basta
    /// o token do dia a dia, que só lê e grava work item.
    /// </summary>
    private void ShowPatPermissionDialog()
    {
        var ask = MessageBox.Show(this,
            App.Str("Setup_Step4PatDialogBody"),
            App.Str("Setup_Step4PatDialogTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ask != MessageBoxResult.Yes) return;
        try
        {
            var url = DevOpsFieldSetupService.TokenPageUrl(DevOpsOrgBox.Text.Trim());
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            { UseShellExecute = true });
        }
        catch (Exception ex) { Step4StatusText.Text = ex.Message; }
    }

    /// <summary>"Microsoft.VSTS.Scheduling.StartDate" -> "Start Date".</summary>
    private static string ShortFieldName(string referenceName)
    {
        var last = (referenceName ?? "").Split('.').LastOrDefault() ?? "";
        return System.Text.RegularExpressions.Regex.Replace(last, "(?<=[a-z])(?=[A-Z])", " ");
    }

    /// <summary>Campos em que esta instalação optou pelo campo padrão do DevOps, por tipo.</summary>
    private List<DevOpsFieldSetupService.StandardChoice> StandardChoices() => _fieldRows
        .SelectMany(r => r.UseStandardByType
            .Where(kv => kv.Value.IsChecked == true)
            .Select(kv => new DevOpsFieldSetupService.StandardChoice(r.Spec.Key, kv.Key)))
        .ToList();

    /// <summary>Limpa o resultado da última detecção (nome mudou, ou vamos detectar de novo).</summary>
    private void ResetFieldStatus()
    {
        foreach (var r in _fieldRows) { r.Status.Text = ""; r.Create.IsChecked = false; }
        CreateFieldsButton.IsEnabled = false;
        PatHelpPanel.Visibility = Visibility.Collapsed;   // token novo: a dica sai de cena
    }

    private List<(string Key, string Name)> CheckedFields() => _fieldRows
        .Where(r => r.Create.IsChecked == true && !string.IsNullOrWhiteSpace(r.Name.Text))
        .Select(r => (r.Spec.Key, r.Name.Text.Trim())).ToList();

    private bool Step4ConnectionOk()
    {
        if (string.IsNullOrWhiteSpace(DevOpsOrgBox.Text)
            || string.IsNullOrWhiteSpace(DevOpsProjectBox.Text)
            || string.IsNullOrWhiteSpace(DevOpsPatBox.Password))
        {
            Step4StatusText.Text = App.Str("Setup_Step4NeedsConnection");
            return false;
        }
        return true;
    }

    private async void OnDetectFieldsClick(object sender, RoutedEventArgs e)
    {
        BuildFieldRows();
        if (!Step4ConnectionOk()) return;
        ResetFieldStatus();
        DetectFieldsButton.IsEnabled = false;
        Step4StatusText.Text = App.Str("Setup_Step4Detecting");
        try
        {
            var org = DevOpsOrgBox.Text.Trim();
            var proj = DevOpsProjectBox.Text.Trim();
            var pat = DevOpsPatBox.Password;

            _process = await DevOpsFieldSetupService.LoadProcessAsync(org, proj, pat);
            var wanted = _fieldRows
                .Where(r => !string.IsNullOrWhiteSpace(r.Name.Text))
                .Select(r => (r.Spec.Key, Name: r.Name.Text.Trim())).ToList();
            var checks = (await DevOpsFieldSetupService.CheckAsync(org, proj, pat, wanted,
                    default, StandardChoices()))
                .ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);

            var missing = 0; var conflicts = 0;
            foreach (var row in _fieldRows)
            {
                if (!checks.TryGetValue(row.Spec.Key, out var c)) continue;
                switch (c.Status)
                {
                    // Acrescentado a qualquer situacao: dizer QUAL campo de fabrica cobre o tipo
                    // evita a pergunta "por que nao pede o campo na Task?".
                    case DevOpsFieldSetupService.FieldStatus.Ready when c.CoveredByStandard.Count > 0:
                        row.Status.Text = (string.IsNullOrEmpty(c.MatchedByAlias)
                                ? App.Str("Setup_Step4Ready")
                                : App.Str("Setup_Step4ReadyAlias", c.MatchedByAlias))
                            + " · " + App.Str("Setup_Step4Standard", string.Join(" | ", c.CoveredByStandard));
                        row.Status.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
                        break;
                    case DevOpsFieldSetupService.FieldStatus.Ready:
                        // Achado por apelido conta como achado — e a tela diz com que nome, para
                        // quem instala saber que o DevOps chama o campo de outra coisa.
                        row.Status.Text = string.IsNullOrEmpty(c.MatchedByAlias)
                            ? App.Str("Setup_Step4Ready")
                            : App.Str("Setup_Step4ReadyAlias", c.MatchedByAlias);
                        row.Status.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
                        break;
                    case DevOpsFieldSetupService.FieldStatus.NeedsWorkItemTypes:
                        row.Status.Text = App.Str("Setup_Step4MissingIn", string.Join(", ", c.MissingIn))
                            + (string.IsNullOrEmpty(c.MatchedByAlias) ? ""
                               : " " + App.Str("Setup_Step4ViaAlias", c.MatchedByAlias));
                        row.Status.Foreground = new SolidColorBrush(Color.FromRgb(0xB2, 0x6A, 0x00));
                        row.Create.IsChecked = true; missing++;
                        break;
                    case DevOpsFieldSetupService.FieldStatus.Missing:
                        // Nao existe, mas o DevOps ja pode ter um campo de fabrica equivalente:
                        // a sugestao vem da propria deteccao (mesma do NXProject).
                        var alt = c.SuggestedStandard.FirstOrDefault();
                        row.Status.Text = App.Str("Setup_Step4NotFound")
                            + (string.IsNullOrEmpty(alt.Value) ? ""
                               : " · " + App.Str("Setup_Step4SuggestStd", alt.Key,
                                   DevOpsFieldSetupService.ShortFieldName(alt.Value)));
                        row.Status.Foreground = new SolidColorBrush(Color.FromRgb(0xB2, 0x6A, 0x00));
                        // Obrigatório sempre; opcional só quando o recurso está ligado no NX.
                        row.Create.IsChecked = AutoCheck(row.Spec); missing++;
                        break;
                    case DevOpsFieldSetupService.FieldStatus.TypeNarrower:
                        // Serve, com ressalva. Nao entra na fila de criacao — o campo ja existe.
                        row.Status.Text = App.Str("Setup_Step4TypeNarrower", c.FoundType, row.Spec.Type);
                        row.Status.Foreground = new SolidColorBrush(Color.FromRgb(0xB2, 0x6A, 0x00));
                        row.Create.IsChecked = false;
                        break;
                    case DevOpsFieldSetupService.FieldStatus.TypeConflict:
                        // Nome ocupado por um campo de outro tipo: não dá para reaproveitar nem
                        // sobrescrever — quem instala escolhe outro nome (o nx_ ajuda aqui).
                        row.Status.Text = App.Str("Setup_Step4Conflict", c.FoundType, row.Spec.Type);
                        row.Status.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));
                        row.Create.IsChecked = false; conflicts++;
                        break;
                    default:
                        row.Status.Text = c.Note;
                        row.Status.Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77));
                        break;
                }
            }

            // Pre-checagem: pergunta ao DevOps se esta conta pode editar o processo. Melhor
            // saber agora do que no meio da criacao, com metade dos campos no ar.
            var (perm, permDetail) = await DevOpsFieldSetupService.CanEditProcessAsync(org, pat);

            var parts = new List<string>();
            // 401/403 no processo é o caso comum: o token do NX serve para ler e gravar work
            // item, mas criar campo é outra permissão. Aqui a tela para de falar em HTTP e passa
            // a explicar que token precisa ser usado e como tirá-lo.
            if (NeedsStrongerPat(_process.Error))
            {
                Step4StatusText.Text = App.Str("Setup_Step4PatNoAccess");
                PatHelpPanel.Visibility = Visibility.Visible;
                CreateFieldsButton.IsEnabled = false;
                return;
            }
            if (!string.IsNullOrEmpty(_process.Error)) parts.Add(_process.Error);
            else if (!_process.CanCreateFields)
                parts.Add(App.Str("Setup_Step4ProcessSystem", _process.Name));
            else parts.Add(App.Str("Setup_Step4ProcessInherited", _process.Name));
            if (conflicts > 0) parts.Add(App.Str("Setup_Step4ConflictCount", conflicts.ToString()));
            // O resultado da permissao e AVISO, nunca bloqueio: a API mede o direito da conta, e
            // um falso negativo nao pode impedir quem de fato consegue criar.
            switch (perm)
            {
                case DevOpsFieldSetupService.PermissionProbe.Allowed:
                    parts.Add(App.Str("Setup_Step4PermOk")); break;
                case DevOpsFieldSetupService.PermissionProbe.Denied:
                    parts.Add(App.Str("Setup_Step4PermDenied"));
                    PatHelpPanel.Visibility = Visibility.Visible; break;
                case DevOpsFieldSetupService.PermissionProbe.TokenRefused:
                    parts.Add(App.Str("Setup_Step4PermToken"));
                    PatHelpPanel.Visibility = Visibility.Visible; break;
                default:
                    parts.Add(App.Str("Setup_Step4PermUnknown", permDetail)); break;
            }
            parts.Add(missing == 0 ? App.Str("Setup_Step4AllOk")
                                   : App.Str("Setup_Step4MissingCount", missing.ToString()));
            Step4StatusText.Text = string.Join(" · ", parts);
            CreateFieldsButton.IsEnabled = missing > 0 && _process.CanCreateFields;
        }
        catch (Exception ex) { Step4StatusText.Text = ex.Message; }
        finally { DetectFieldsButton.IsEnabled = true; }
    }

    /// <summary>
    /// O erro é de permissão/token, e não de rede ou de nome errado? Usa a mesma regra da
    /// gravação (DevOpsFieldSetupService), que também reconhece a pagina de login devolvida em
    /// HTML quando o PAT nao tem escopo.
    /// </summary>
    private static bool NeedsStrongerPat(string? error) =>
        !string.IsNullOrEmpty(error)
        && (error.Contains("HTTP 401") || error.Contains("HTTP 403") || error.Contains("HTTP 302")
            || DevOpsFieldSetupService.IsAuthProblem(0, error));

    private async void OnCreateFieldsClick(object sender, RoutedEventArgs e)
    {
        if (!Step4ConnectionOk() || _process == null) return;
        var fields = CheckedFields();
        if (fields.Count == 0) { Step4StatusText.Text = App.Str("Setup_Step4NothingChecked"); return; }

        // Confirmação explícita: o campo passa a existir para todos os projetos do processo.
        var list = string.Join(Environment.NewLine, fields.Select(f => "  • " + f.Name));
        var ask = MessageBox.Show(this,
            App.Str("Setup_Step4ConfirmBody", _process.Name, list),
            App.Str("Setup_Step4ConfirmTitle"), MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (ask != MessageBoxResult.OK) return;

        CreateFieldsButton.IsEnabled = false;
        DetectFieldsButton.IsEnabled = false;
        try
        {
            var progress = new Progress<string>(n => Step4StatusText.Text = App.Str("Setup_Step4Creating", n));
            var results = await DevOpsFieldSetupService.CreateAsync(
                DevOpsOrgBox.Text.Trim(), DevOpsProjectBox.Text.Trim(), DevOpsPatBox.Password,
                _process, fields, progress, default, StandardChoices());
            SaveStandardChoicesToConfig();

            var byKey = results.ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var row in _fieldRows)
            {
                if (!byKey.TryGetValue(row.Spec.Key, out var r)) continue;
                row.Status.Text = r.Message;
                row.Status.Foreground = r.Ok
                    ? new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32))
                    : new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));
            }
            var ok = results.Count(r => r.Ok);
            Step4StatusText.Text = App.Str("Setup_Step4CreateDone",
                ok.ToString(), (results.Count - ok).ToString());
            // Recusa por permissão: além do painel amarelo, um POPUP — a linha do campo é
            // estreita e a mensagem do servidor (às vezes a URL da tela de login) não explica
            // nada. Aqui a pessoa lê o motivo e vai direto criar o token com o escopo certo.
            if (results.Any(r => !r.Ok && DevOpsFieldSetupService.IsAuthProblem(
                    r.Message.Contains("HTTP 401") ? 401 : r.Message.Contains("HTTP 403") ? 403 : 0, r.Message)))
            {
                PatHelpPanel.Visibility = Visibility.Visible;
                Step4StatusText.Text += " " + App.Str("Setup_Step4PatNoAccess");
                ShowPatPermissionDialog();
            }
        }
        catch (Exception ex) { Step4StatusText.Text = ex.Message; }
        finally { DetectFieldsButton.IsEnabled = true; }
    }
}
