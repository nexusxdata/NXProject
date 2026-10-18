// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Threading.Tasks;
using NX.AgentInstaller;

namespace NXProject.Services;

/// <summary>
/// Instala os CLIs de IA (Codex e Claude Code) no Windows — agora pelo pacote compartilhado
/// NX.AgentInstaller (o mesmo do NXPilot), que segue os instaladores OFICIAIS de hoje:
/// <list type="bullet">
/// <item>Automática (padrão): o .NET baixa, confere o SHA-256 do fornecedor e instala na área do
/// usuário (sem admin), nas mesmas pastas do instalador oficial — sem rodar script (antivírus).</item>
/// <item>Script: o instalador oficial numa janela do PowerShell (ou o comando para copiar).</item>
/// </list>
/// Versão: a última, ou a testada — se a testada não existir mais, instala a última e avisa
/// (antes o Setup tentava uma versão retirada e falhava). No WSL a instalação continua manual.
/// </summary>
public static class AiCliInstaller
{
    private static readonly AgentInstallOptions Options = new();
    private static readonly AgentInstaller Installer = new(Options);

    /// <summary>Pasta antiga (até out/2026 o NX instalava aqui); ainda é procurada.</summary>
    public static string BinDir => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NXProject", "bin");

    /// <summary>Pastas onde a instalação automática coloca os agentes (área do usuário).</summary>
    public static string InstallFolders => $"{Options.ClaudeLocalBin} · {Options.CodexVisibleBin}";

    /// <summary>codex.exe encontrado (PATH, instalador oficial, NXProject, extensão do VS Code) ou null.</summary>
    public static string? CodexPath => AgentLocator.Find(AgentCli.Codex, Options)?.Path;
    /// <summary>claude.exe encontrado (PATH, instalador oficial, NXProject, extensão do VS Code) ou null.</summary>
    public static string? ClaudePath => AgentLocator.Find(AgentCli.ClaudeCode, Options)?.Path;

    public static AgentLocation? Locate(AgentCli cli) => AgentLocator.Find(cli, Options);

    public static string TestedVersion(AgentCli cli) =>
        cli == AgentCli.ClaudeCode ? Options.TestedClaudeVersion : Options.TestedCodexVersion;

    /// <summary>Comando oficial para instalar manualmente (PowerShell).</summary>
    public static string ManualCommand(AgentCli cli, VersionChoice choice) =>
        Installer.ScriptCommand(cli, choice == VersionChoice.Tested ? TestedVersion(cli) : null);

    public static Task<AgentInstallResult> InstallAsync(AgentCli cli, InstallMode mode, VersionChoice choice, IProgress<string>? status = null) =>
        Installer.InstallAsync(cli, mode, choice, status);

    // Compatibilidade com quem já chamava: instalação automática da última versão.
    public static Task InstallCodexAsync(IProgress<string>? status = null) =>
        InstallAsync(AgentCli.Codex, InstallMode.Internal, VersionChoice.Latest, status);

    public static Task InstallClaudeCodeAsync(IProgress<string>? status = null) =>
        InstallAsync(AgentCli.ClaudeCode, InstallMode.Internal, VersionChoice.Latest, status);
}
