// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;

namespace NXProject.Setup;

public partial class MainWindow : Window
{
    private const string DotNetRuntimeInstallerUrl = "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe";

    private readonly string _installDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Apps", "NXProject");

    public MainWindow()
    {
        InitializeComponent();
        // Mostra os caminhos de instalação (app e CLIs) com atalho para conferir.
        AppPathRun.Text = App.Str("Setup_PathApp", _installDir) + "  ";
        CliPathRun.Text = App.Str("Setup_PathCli", NXProject.Services.AiCliInstaller.InstallFolders) + "  ";
        VersionLatestItem.Content = App.Str("Setup_Step3Latest");
        VersionTestedItem.Content = App.Str("Setup_Step3Tested",
            NXProject.Services.AiCliInstaller.TestedVersion(NXData.AgentInstaller.AgentCli.Codex),
            NXProject.Services.AiCliInstaller.TestedVersion(NXData.AgentInstaller.AgentCli.ClaudeCode));
        Loaded += (_, _) => { RefreshStep1State(); RefreshStep2State(); RefreshInstallStatus(); };
        ContentRendered += async (_, _) => await TryShotAsync();
    }

    /// <summary>
    /// Captura para documentação: NXProject-Setup.exe --shot saida.png [--tab 2]. Renderiza só a
    /// própria janela (RenderTargetBitmap) — nunca a tela — e fecha. Igual ao NXPilot-Setup.
    /// </summary>
    private async Task TryShotAsync()
    {
        var args = Environment.GetCommandLineArgs();
        var i = Array.IndexOf(args, "--shot");
        if (i < 0 || i + 1 >= args.Length) return;
        if (args.SkipWhile(a => a != "--tab").Skip(1).FirstOrDefault() == "2") Tabs.SelectedItem = AgentsTab;
        await Task.Delay(3000); // versões dos agentes
        var root = (FrameworkElement)System.Windows.Media.VisualTreeHelper.GetChild(this, 0);
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(root);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using (var fs = File.Create(args[i + 1])) encoder.Save(fs);
        Close();
    }

    private NXData.AgentInstaller.InstallMode SelectedMode =>
        ModeScript.IsChecked == true ? NXData.AgentInstaller.InstallMode.Script : NXData.AgentInstaller.InstallMode.Internal;
    private NXData.AgentInstaller.VersionChoice SelectedVersion =>
        VersionBox.SelectedItem == VersionTestedItem ? NXData.AgentInstaller.VersionChoice.Tested : NXData.AgentInstaller.VersionChoice.Latest;

    // "Copiar": comando oficial para instalar manualmente (na versão escolhida).
    private void OnCopyAgentCommand(object sender, RoutedEventArgs e)
    {
        var cli = (sender as System.Windows.Controls.Button)?.Tag as string == "claude"
            ? NXData.AgentInstaller.AgentCli.ClaudeCode : NXData.AgentInstaller.AgentCli.Codex;
        var command = NXProject.Services.AiCliInstaller.ManualCommand(cli, SelectedVersion);
        try { Clipboard.SetText(command); Step3StatusText.Text = command; }
        catch { Step3StatusText.Text = command; /* área de transferência ocupada: mostra para copiar à mão */ }
    }

    // O NXProject não precisa ser reinstalado sempre: se o exe já está na pasta do usuário,
    // mostra "já instalado" e o botão vira "Verificar atualização" (baixa só se houver versão nova).
    private void RefreshStep2State()
    {
        var exePath = Path.Combine(_installDir, "NXProject.Community.exe");
        if (File.Exists(exePath))
        {
            Step2StatusText.Text = App.Str("Setup_Step2Installed");
            Step2Button.Content = App.Str("Setup_Step2ButtonUpdate");
            OpenAppButton.Visibility = Visibility.Visible;   // já instalado: permite abrir
        }
        else
        {
            Step2StatusText.Text = App.Str("Setup_Step2Status");
            Step2Button.Content = App.Str("Setup_Step2Button");
            OpenAppButton.Visibility = Visibility.Collapsed;
        }
    }

    // Passo 4: a configuracao dos campos do DevOps abre em janela propria — a lista de campos
    // com nome editavel nao cabia na tela principal sem empurrar o resto para fora da vista.
    private void OnOpenFieldsClick(object sender, RoutedEventArgs e)
    {
        var win = new DevOpsFieldsWindow { Owner = this };
        win.ShowDialog();
    }

    // Abre o NXProject sem fechar o Setup (para o usuário ainda poder usar o Passo 3).
    private void OnOpenAppClick(object sender, RoutedEventArgs e)
    {
        var exePath = Path.Combine(_installDir, "NXProject.Community.exe");
        if (!File.Exists(exePath)) return;
        // Se o usuário marcou LLaMA no Passo 3, o app já dispara o download da IA Local ao abrir.
        var started = TryStart(exePath, LlamaCheck.IsChecked == true ? "--install-llama" : null);
        if (started)
            Close();   // abriu o app: pode fechar o Setup
        else
            ShowError(App.Str("Setup_InstalledCantOpen",
                NXProject.Services.UpdateService.DotNetDesktopRuntimeDownloadUrl, _installDir));
    }

    // Mostra um ✓ ao lado do que já está instalado. Codex/Claude: procura no WINDOWS (exe) e
    // também no WSL — para não induzir o usuário a instalar no Windows o que já roda no WSL.
    // LLaMA: modelo GGUF na pasta de recursos.
    private enum InstallWhere { None, Windows, Wsl }

    private void RefreshInstallStatus()
    {
        var codex = NXProject.Services.AiCliInstaller.Locate(NXData.AgentInstaller.AgentCli.Codex);
        var claude = NXProject.Services.AiCliInstaller.Locate(NXData.AgentInstaller.AgentCli.ClaudeCode);
        var codexWin = codex != null;
        var claudeWin = claude != null;
        SetCliStatus(CodexStatus, codexWin ? InstallWhere.Windows : InstallWhere.None, codex?.Version);
        SetCliStatus(ClaudeStatus, claudeWin ? InstallWhere.Windows : InstallWhere.None, claude?.Version);
        SetAgentCheck(NXData.AgentInstaller.AgentCli.Codex, CodexCheck, CodexStatus, codex?.Version);
        SetAgentCheck(NXData.AgentInstaller.AgentCli.ClaudeCode, ClaudeCheck, ClaudeStatus, claude?.Version);
        SetStatus(LlamaStatus, IsLlamaInstalled());
        // 🗑 aparece sempre que há o agente; só remove o que está nas pastas do instalador oficial. Da extensão do
        // VS Code (ou npm etc.) fica desabilitada, com a dica de onde remover — apagar ali quebraria a extensão.
        SetRemoveButton(CodexRemoveButton, NXData.AgentInstaller.AgentCli.Codex, codex);
        SetRemoveButton(ClaudeRemoveButton, NXData.AgentInstaller.AgentCli.ClaudeCode, claude);
        var llama = new NXData.AgentInstaller.LocalAiInstaller().Check();
        LlamaRemoveButton.Visibility = llama.BackendPresent || llama.ModelPresent ? Visibility.Visible : Visibility.Collapsed;

        // Se não achou no Windows, testa o WSL (assíncrono; mostra "verificando" na hora).
        if (!codexWin) { SetChecking(CodexStatus); _ = UpdateWslStatusAsync("codex", CodexStatus); }
        if (!claudeWin) { SetChecking(ClaudeStatus); _ = UpdateWslStatusAsync("claude", ClaudeStatus); }
    }

    private readonly int[] _agentCheck = new int[2];

    /// <summary>Instalar/atualizar deu erro: a caixa continua habilitada para tentar de novo.</summary>
    private readonly HashSet<NXData.AgentInstaller.AgentCli> _agentFailed = new();

    /// <summary>
    /// Igual ao NXPilot-Setup: compara a versão instalada com a escolhida (última ou testada).
    /// Mais nova disponível = status laranja e caixa marcada para atualizar; já atualizado =
    /// caixa desmarcada e desabilitada (salvo se a última tentativa deu erro). Sem internet,
    /// a caixa fica habilitada (não dá para comparar).
    /// </summary>
    private void SetAgentCheck(NXData.AgentInstaller.AgentCli cli, System.Windows.Controls.CheckBox check, System.Windows.Controls.TextBlock status, string? installed)
    {
        var gen = ++_agentCheck[(int)cli]; // só a conferência mais recente mexe na caixa
        check.IsEnabled = true;
        if (installed == null) return;
        var choice = SelectedVersion; // lida aqui, na thread da tela
        _ = Task.Run(async () =>
        {
            try { return (await NXProject.Services.AiCliInstaller.ResolveVersionAsync(cli, choice)).Version; }
            catch { return null; } // sem internet ou serviço fora
        }).ContinueWith(t =>
        {
            if (gen != _agentCheck[(int)cli] || t.Result is not { } target || ParseVersion(target) is not { } tv || ParseVersion(installed) is not { } iv) return;
            if (tv > iv)
            {
                status.Text += " · " + App.Str("Setup_AgentUpdateAvailable", target);
                status.Foreground = System.Windows.Media.Brushes.DarkOrange;
                check.IsChecked = true;
            }
            else if (!_agentFailed.Contains(cli))
            {
                status.Text += " · " + App.Str("Setup_AgentUpToDate");
                check.IsChecked = false;
                check.IsEnabled = false;
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void OnVersionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (IsLoaded) RefreshInstallStatus(); // testada x última muda a versão alvo
    }

    private static Version? ParseVersion(string v) => Version.TryParse(v.Split('-')[0], out var r) ? r : null;

    private static void SetChecking(System.Windows.Controls.TextBlock tb)
    {
        tb.Text = App.Str("Setup_Checking");
        tb.Foreground = System.Windows.Media.Brushes.Gray;
    }

    private static void SetCliStatus(System.Windows.Controls.TextBlock tb, InstallWhere where, string? version = null)
    {
        tb.Text = where switch
        {
            InstallWhere.Windows => App.Str("Setup_InstalledWin") + (version != null ? $" {version}" : ""),
            InstallWhere.Wsl => App.Str("Setup_InstalledWsl"),
            _ => App.Str("Setup_NotInstalled"),
        };
        tb.Foreground = where == InstallWhere.None
            ? System.Windows.Media.Brushes.Gray : System.Windows.Media.Brushes.Green;
    }

    private static void SetStatus(System.Windows.Controls.TextBlock tb, bool ok)
    {
        tb.Text = ok ? App.Str("Setup_Installed") : App.Str("Setup_NotInstalled");
        tb.Foreground = ok ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Gray;
    }

    private async Task UpdateWslStatusAsync(string cli, System.Windows.Controls.TextBlock tb)
    {
        var found = await Task.Run(() => IsCliInWsl(cli));
        SetCliStatus(tb, found ? InstallWhere.Wsl : InstallWhere.None);
    }

    // Procura o CLI dentro do WSL (shell de login carrega o PATH do nvm/npm).
    private static bool IsCliInWsl(string cli)
    {
        try
        {
            var psi = new ProcessStartInfo("wsl.exe",
                $"-- bash -lic \"command -v {cli} >/dev/null 2>&1 && echo FOUND\"")
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            var outp = p.StandardOutput.ReadToEnd();
            p.WaitForExit(8000);
            return outp.Contains("FOUND", StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private static void SetRemoveButton(System.Windows.Controls.Button button, NXData.AgentInstaller.AgentCli cli, NXData.AgentInstaller.AgentLocation? located)
    {
        var ours = NXProject.Services.AiCliInstaller.CanUninstall(cli);
        button.Visibility = located != null || ours ? Visibility.Visible : Visibility.Collapsed;
        button.IsEnabled = ours;
        button.ToolTip = ours ? App.Str("Setup_RemoveAgentTip")
            : located?.Source == "extensão do VS Code" ? App.Str("Setup_RemoveFromVsCode")
            : App.Str("Setup_RemoveNotOurs", located?.Source ?? "");
        System.Windows.Controls.ToolTipService.SetShowOnDisabled(button, true);
    }

    /// <summary>🗑 ao lado do agente: pergunta e remove (o login da pessoa fica).</summary>
    private async void OnRemoveAgentClick(object sender, RoutedEventArgs e)
    {
        var cli = (sender as System.Windows.Controls.Button)?.Tag as string == "claude" ? NXData.AgentInstaller.AgentCli.ClaudeCode : NXData.AgentInstaller.AgentCli.Codex;
        var name = cli == NXData.AgentInstaller.AgentCli.ClaudeCode ? "Claude Code" : "Codex";
        if (MessageBox.Show(this, App.Str("Setup_RemoveAgentAsk", name), "NXProject Setup", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        CodexRemoveButton.IsEnabled = ClaudeRemoveButton.IsEnabled = false;
        try { Step3StatusText.Text = await NXProject.Services.AiCliInstaller.UninstallAsync(cli); }
        catch (Exception ex) { ShowError(ex.Message); }
        finally
        {
            CodexRemoveButton.IsEnabled = ClaudeRemoveButton.IsEnabled = true;
            RefreshInstallStatus();
        }
    }

    /// <summary>🗑 da IA Local: pergunta e remove modelo e backend (a pasta é a mesma do NXPilot).</summary>
    private async void OnRemoveLlamaClick(object sender, RoutedEventArgs e)
    {
        var installer = new NXData.AgentInstaller.LocalAiInstaller();
        var folder = installer.ResolveFolder();
        if (MessageBox.Show(this, App.Str("Setup_RemoveLlamaAsk", folder), "NXProject Setup", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        LlamaRemoveButton.IsEnabled = false;
        try
        {
            await Task.Run(() => installer.Uninstall(folder));
            Step3StatusText.Text = App.Str("Setup_LlamaRemoved");
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { LlamaRemoveButton.IsEnabled = true; RefreshInstallStatus(); }
    }

    // IA Local instalada? Checagem leve (sem LLamaSharp): lê a pasta de recursos no local-ai.json
    // e verifica se o modelo GGUF está lá.
    private const string LlamaModelFileName = "Qwen3-4B-Instruct-2507-Q4_K_M.gguf";
    private static bool IsLlamaInstalled()
    {
        try
        {
            var cfg = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NXProject.Community", "local-ai.json");
            if (!File.Exists(cfg)) return false;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(cfg));
            if (!doc.RootElement.TryGetProperty("ResourcesFolder", out var f)) return false;
            var folder = f.GetString();
            if (string.IsNullOrWhiteSpace(folder)) return false;
            return File.Exists(Path.Combine(folder, LlamaModelFileName))
                   || Directory.Exists(folder) && Directory.GetFiles(folder, "*.gguf").Length > 0;
        }
        catch { return false; }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { /* abrir pasta é conveniência */ }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnOpenAppFolder(object sender, System.Windows.RoutedEventArgs e) => OpenFolder(_installDir);
    // Abre a pasta do agente encontrado (instalador oficial, NXProject ou extensão do VS Code).
    private void OnOpenCliFolder(object sender, System.Windows.RoutedEventArgs e)
        => OpenFolder(Path.GetDirectoryName(NXProject.Services.AiCliInstaller.CodexPath
                                            ?? NXProject.Services.AiCliInstaller.ClaudePath
                                            ?? Path.Combine(NXProject.Services.AiCliInstaller.BinDir, "x"))!);

    private void OnHyperlinkRequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    // Abre o instalador do .NET no navegador para o usuário baixar/instalar manualmente.
    private void OnStep1ManualClick(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(DotNetRuntimeInstallerUrl) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            Step1StatusText.Text = App.Str("Setup_Step1Fail",
                ex.Message, NXProject.Services.UpdateService.DotNetDesktopRuntimeDownloadUrl);
        }
    }

    private void RefreshStep1State()
    {
        var installed = NXProject.Services.UpdateService.IsDesktopRuntimeInstalled();
        Step1StatusText.Text = App.Str(installed ? "Setup_Step1Installed" : "Setup_Step1NotFound");
        Step1Button.IsEnabled = !installed;
        // O botão manual (baixar no navegador) é alternativa à instalação automática:
        // só faz sentido quando o .NET ainda NÃO está instalado.
        Step1ManualButton.IsEnabled = !installed;
    }

    private async void OnStep1Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Step1Button.IsEnabled = false;
            Step1StatusText.Text = App.Str("Setup_Step1Downloading");
            Step1Progress.Value = 0;
            Step1ProgressPanel.Visibility = Visibility.Visible;

            var tempExe = Path.Combine(Path.GetTempPath(), $"windowsdesktop-runtime-{Guid.NewGuid():N}.exe");
            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            using (var response = await client.GetAsync(DotNetRuntimeInstallerUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? 0L;
                await using var stream = await response.Content.ReadAsStreamAsync();
                await using var file = File.Create(tempExe);
                var buffer = new byte[81920];
                long read = 0; int n;
                while ((n = await stream.ReadAsync(buffer)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, n));
                    read += n;
                    Step1Progress.Value = total > 0 ? Math.Min(90, read * 90.0 / total) : 45;
                }
            }

            // Instalação silenciosa (UAC): não dá para medir; barra indeterminada.
            Step1StatusText.Text = App.Str("Setup_Step1Installing");
            Step1Progress.IsIndeterminate = true;
            using var proc = Process.Start(new ProcessStartInfo(tempExe, "/install /quiet /norestart")
            {
                UseShellExecute = true,
                Verb = "runas"
            });
            if (proc != null)
                await proc.WaitForExitAsync();

            try { File.Delete(tempExe); } catch { /* limpeza best-effort */ }

            Step1Progress.IsIndeterminate = false;
            Step1Progress.Value = 100;
            RefreshStep1State();
            if (!Step1Button.IsEnabled)
                Step1StatusText.Text = App.Str("Setup_Step1Success");
        }
        catch (Exception ex)
        {
            Step1StatusText.Text = App.Str("Setup_Step1Fail",
                ex.Message, NXProject.Services.UpdateService.DotNetDesktopRuntimeDownloadUrl);
            Step1Button.IsEnabled = true;
        }
        finally
        {
            Step1Progress.IsIndeterminate = false;
            Step1ProgressPanel.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnStep2Click(object sender, RoutedEventArgs e)
    {
        Step2Button.IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = Visibility.Collapsed;
        InstallProgress.Value = 0;
        await RunInstallAsync();
    }

    // Passo 3: instala Codex/Claude Code (Windows) pelo NXData.AgentInstaller (o mesmo do NXPilot):
    // automática na área do usuário (sem script/admin) ou script oficial; última ou testada.
    private async void OnStep3Click(object sender, RoutedEventArgs e)
    {
        if (CodexCheck.IsChecked != true && ClaudeCheck.IsChecked != true && LlamaCheck.IsChecked != true)
        {
            Step3StatusText.Text = App.Str("Setup_Step3PickOne");
            return;
        }

        Step3Button.IsEnabled = false;
        Step3Progress.IsIndeterminate = true;
        Step3Progress.Visibility = Visibility.Visible;
        var status = new Progress<string>(s => Step3StatusText.Text = s);
        try
        {
            var done = new List<string>();
            var failed = new List<string>();
            foreach (var (cli, check, name) in new[] { (NXData.AgentInstaller.AgentCli.Codex, CodexCheck, "Codex"), (NXData.AgentInstaller.AgentCli.ClaudeCode, ClaudeCheck, "Claude Code") })
            {
                if (check.IsChecked != true || !check.IsEnabled) continue;
                try
                {
                    done.Add((await NXProject.Services.AiCliInstaller.InstallAsync(cli, SelectedMode, SelectedVersion, status)).Message);
                    _agentFailed.Remove(cli);
                }
                catch (Exception ex)
                {
                    _agentFailed.Add(cli); // caixa continua habilitada para tentar de novo
                    failed.Add($"{name}: {ex.Message}");
                }
            }
            if (failed.Count > 0) throw new InvalidOperationException(string.Join(" ", done.Concat(failed)));

            // LLaMA (~2,5 GB) não é baixado aqui: o download roda dentro do NXProject (na tela
            // Gerenciar IA Local, que define a pasta). Aciona ao abrir o app (Passo 2).
            var msg = done.Count > 0 ? App.Str("Setup_Step3Done", string.Join(" ", done)) : string.Empty;
            if (LlamaCheck.IsChecked == true)
                msg = (msg.Length > 0 ? msg + "\n" : "") + App.Str("Setup_Step3LlamaQueued");
            Step3StatusText.Text = msg;
        }
        catch (Exception ex)
        {
            Step3StatusText.Text = App.Str("Setup_Step3Fail", ex.Message);
        }
        finally
        {
            Step3Progress.IsIndeterminate = false;
            Step3Progress.Visibility = Visibility.Collapsed;
            Step3Button.IsEnabled = true;
            RefreshInstallStatus();   // atualiza o ✓ após instalar
        }
    }

    private async void OnRetryClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = Visibility.Collapsed;
        InstallProgress.Value = 0;
        await RunInstallAsync();
    }

    private async Task RunInstallAsync()
    {
        try
        {
            // O NXProject pode estar aberto e segurando DLLs (ex.: Accessibility.dll) em memória,
            // fazendo a cópia falhar com "being used by another process". Encerra antes de copiar.
            Step2StatusText.Text = App.Str("Setup_ClosingApp");
            await Task.Run(StopRunningNxProject);

            Directory.CreateDirectory(_installDir);

            Step2StatusText.Text = App.Str("Setup_InstallingBase");
            await Task.Run(() => CopySetupBundledFiles(_installDir));
            InstallProgress.Value = 15;

            Step2StatusText.Text = App.Str("Setup_CheckingLatest");
            var release = await NXProject.Services.UpdateService.GetLatestReleaseInfoAsync();
            if (release is null)
            {
                ShowError(App.Str("Setup_CannotGetLatest"));
                return;
            }
            InstallProgress.Value = 20;

            Step2StatusText.Text = App.Str("Setup_Downloading", release.TagName);
            // MB e % no texto enquanto baixa (igual ao NXPilot-Setup); a barra vai de 20% a 85%.
            var extractedDir = await NXProject.Services.UpdateService.DownloadAndExtractAsync(
                release.DownloadUrl,
                new Progress<int>(p => InstallProgress.Value = 20 + p * 0.65),
                default,
                new Progress<(long Downloaded, long Total)>(b => Step2StatusText.Text = App.Str("Setup_Downloading", release.TagName) + " " + (b.Total > 0
                    ? $"{b.Downloaded / 1048576d:0.0} MB / {b.Total / 1048576d:0.0} MB ({b.Downloaded * 100 / b.Total}%)"
                    : $"{b.Downloaded / 1048576d:0.0} MB")));

            Step2StatusText.Text = App.Str("Setup_Installing");
            CopyAllFiles(extractedDir, _installDir);
            TryDeleteTempParent(extractedDir);
            InstallProgress.Value = 90;

            var exePath = Path.Combine(_installDir, "NXProject.Community.exe");
            if (!File.Exists(exePath))
            {
                ShowError(App.Str("Setup_IncompleteInstall", Path.GetFileName(exePath)));
                return;
            }

            // Sem o runtime ao lado do exe (copia interrompida, antivirus, disco cheio), o
            // app self-contained abre o dialogo generico "instale o .NET Desktop Runtime" —
            // erro confuso. Falha aqui, apontando os arquivos que faltaram.
            var missing = GetMissingRuntimeFiles(_installDir);
            if (missing.Count > 0)
            {
                ShowError(App.Str("Setup_IncompleteInstall", string.Join(", ", missing)));
                return;
            }

            WriteReadme(_installDir);
            CreateDesktopShortcut(exePath);
            InstallProgress.Value = 100;

            // NÃO abre o app nem fecha o Setup: o usuário ainda pode usar o Passo 3 (instalar IA).
            // Mostra o botão "Abrir NXProject" ao lado de Fechar (o app abre quando ele quiser).
            // O fim fica explícito no texto também (não só na barra): "✓ 100% · …".
            Step2StatusText.Text = "✓ 100% · " + App.Str("Setup_InstalledOpenWhenReady").TrimStart('✓', ' ');
            Step2Button.Content = App.Str("Setup_Step2ButtonUpdate");
            OpenAppButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            ShowError(App.Str("Setup_InstallFail", ex.Message));
        }
        finally
        {
            Step2Button.IsEnabled = true;
        }
    }

    // Encerra qualquer NXProject.Community em execução (o próprio app instalado que pode estar
    // aberto), para liberar as DLLs antes de sobrescrever os arquivos na atualização.
    private static void StopRunningNxProject()
    {
        try
        {
            var procs = System.Diagnostics.Process.GetProcessesByName("NXProject.Community");
            foreach (var p in procs)
            {
                try { p.Kill(true); p.WaitForExit(5000); }
                catch { /* já saiu / sem permissão: segue e tenta copiar mesmo assim */ }
                finally { p.Dispose(); }
            }
            // Aguarda o SO liberar os handles das DLLs após o processo sair.
            if (procs.Length > 0)
                System.Threading.Thread.Sleep(500);
        }
        catch { /* não bloquear a instalação por falha ao listar/matar processos */ }
    }

    private static bool TryStart(string exePath, string? args = null)
    {
        try
        {
            var psi = new ProcessStartInfo(exePath)
            {
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
                UseShellExecute = true
            };
            if (!string.IsNullOrWhiteSpace(args)) psi.Arguments = args;
            using var proc = Process.Start(psi);
            return proc != null;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteReadme(string installDir)
    {
        var text = App.Str("Setup_ReadmeText",
            NXProject.Services.UpdateService.DotNetDesktopRuntimeDownloadUrl);
        File.WriteAllText(Path.Combine(installDir, "README-INSTALACAO.txt"), text);
    }

    private void ShowError(string message)
    {
        Step2StatusText.Text = App.Str("Setup_Interrupted");
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        RetryButton.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Arquivos do runtime self-contained que precisam estar AO LADO do exe: sem eles o
    /// Windows pede para "instalar o .NET Desktop Runtime", mesmo com o app self-contained.
    /// </summary>
    private static readonly string[] RequiredRuntimeFiles =
    {
        "hostfxr.dll", "hostpolicy.dll", "coreclr.dll",
        "System.Private.CoreLib.dll", "PresentationFramework.dll",
        "NXProject.Community.dll", "NXProject.Community.runtimeconfig.json"
    };

    /// <summary>Arquivos essenciais que NAO chegaram na pasta de instalacao.</summary>
    private static List<string> GetMissingRuntimeFiles(string installDir) =>
        RequiredRuntimeFiles.Where(f => !File.Exists(Path.Combine(installDir, f))).ToList();

    // Arquivos do proprio Setup — nao devem ir para a pasta de instalacao do NXProject.
    private static readonly string[] SetupOwnFileNames =
    {
        "NXProject-Setup.exe", "NXProject-Setup.dll", "NXProject-Setup.pdb",
        "NXProject-Setup.deps.json", "NXProject-Setup.runtimeconfig.json"
    };

    // O Setup e publicado self-contained: o runtime .NET/WPF e as bibliotecas de
    // terceiros (PdfSharp, WebView2, CommunityToolkit.Mvvm) ja ficam soltas ao lado
    // do proprio executavel. Aqui so copiamos esses arquivos para a instalacao —
    // sem download, sem zip embutido.
    private static void CopySetupBundledFiles(string installDir)
    {
        var baseDir = AppContext.BaseDirectory;
        foreach (var file in Directory.GetFiles(baseDir, "*", SearchOption.AllDirectories))
        {
            if (SetupOwnFileNames.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase))
                continue;

            var rel = Path.GetRelativePath(baseDir, file);
            var dest = Path.Combine(installDir, rel);
            var destFolder = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destFolder)) Directory.CreateDirectory(destFolder);
            File.Copy(file, dest, overwrite: true);
        }
    }

    private static void CopyAllFiles(string sourceDir, string destDir)
    {
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, file);
            var dest = Path.Combine(destDir, rel);
            var destFolder = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destFolder)) Directory.CreateDirectory(destFolder);
            File.Copy(file, dest, overwrite: true);
        }
    }

    private static void TryDeleteTempParent(string extractedDir)
    {
        try
        {
            var parent = Path.GetDirectoryName(extractedDir);
            if (parent != null && Directory.Exists(parent))
                Directory.Delete(parent, recursive: true);
        }
        catch
        {
            // Limpeza de temporário é best-effort; falha aqui não deve interromper a instalação.
        }
    }

    private static void CreateDesktopShortcut(string exePath)
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var shortcutPath = Path.Combine(desktop, "NXProject.lnk");
            var workingDir = Path.GetDirectoryName(exePath) ?? "";

            // Na atualização o atalho pode já existir (apontando para o exe da versão anterior).
            // Em vez de acumular/deixar um atalho velho, apaga o antigo e recria apontando para o
            // exe atual — fica sempre um único atalho, com o alvo/ícone corretos.
            try { if (File.Exists(shortcutPath)) File.Delete(shortcutPath); }
            catch { /* atalho em uso/bloqueado: o Save abaixo ainda tenta sobrescrever */ }

            var script = $$"""
                $ws = New-Object -ComObject WScript.Shell
                $sc = $ws.CreateShortcut('{{Escape(shortcutPath)}}')
                $sc.TargetPath = '{{Escape(exePath)}}'
                $sc.WorkingDirectory = '{{Escape(workingDir)}}'
                $sc.IconLocation = '{{Escape(exePath)}},0'
                $sc.Save()
                """;

            var psFile = Path.Combine(Path.GetTempPath(), $"nxshortcut_{Guid.NewGuid():N}.ps1");
            File.WriteAllText(psFile, script);

            using var proc = Process.Start(new ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -File \"{psFile}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            proc?.WaitForExit(10_000);

            if (File.Exists(psFile)) File.Delete(psFile);
        }
        catch
        {
            // Atalho é conveniência, não impede a conclusão da instalação.
        }
    }

    private static string Escape(string path) => path.Replace("'", "''");
}
