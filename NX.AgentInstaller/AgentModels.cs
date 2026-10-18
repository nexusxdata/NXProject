// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXPilot — licenciado sob a NXPilot License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

namespace NX.AgentInstaller;

public enum AgentCli { ClaudeCode, Codex }

/// <summary>Interno: o .NET baixa e instala (sem PowerShell). Script: instalador oficial numa janela visível.</summary>
public enum InstallMode { Internal, Script }

/// <summary>A última publicada, ou a testada pelo produto (cai para a última se não existir mais).</summary>
public enum VersionChoice { Latest, Tested }

/// <summary>
/// Configuração. Cada produto informa as versões que ELE testou; as pastas existem para os
/// testes (o padrão é exatamente o dos instaladores oficiais).
/// </summary>
public sealed record AgentInstallOptions
{
    /// <summary>Versões testadas com o NXPilot (18/10/2026). O NXProject pode passar as dele.</summary>
    public string TestedClaudeVersion { get; init; } = "2.1.301";
    public string TestedCodexVersion { get; init; } = "0.162.0";

    public string ClaudeDownloadBase { get; init; } = "https://downloads.claude.ai/claude-code-releases";
    public string CodexReleasesBase { get; init; } = "https://releases.openai.com/codex";
    public string CodexGitHubApi { get; init; } = "https://api.github.com/repos/openai/codex/releases";

    public string UserProfile { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string LocalAppData { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>Grava a pasta do agente no PATH do usuário (HKCU). Desligado nos testes.</summary>
    public bool UpdateUserPath { get; init; } = true;

    /// <summary>Roda o "claude.exe install &lt;versão&gt;" (passo oficial). Trocável nos testes.</summary>
    public Func<string, string, CancellationToken, Task<(int ExitCode, string Output)>>? ClaudeSelfInstall { get; init; }

    /// <summary>Igual ao instalador oficial: CODEX_HOME, ou ~/.codex.</summary>
    public string CodexHome { get; init; } = Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } h
        ? h : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

    public string ClaudeLocalBin => Path.Combine(UserProfile, ".local", "bin");
    public string CodexStandaloneRoot => Path.Combine(CodexHome, "packages", "standalone");
    public string CodexVisibleBin => Path.Combine(LocalAppData, "Programs", "OpenAI", "Codex", "bin");
}

public sealed record AgentInstallResult(AgentCli Cli, string Version, string? ExecutablePath, InstallMode Mode,
    bool FellBackToLatest, string Message);

/// <summary>Onde um agente foi encontrado e a versão que ele informa.</summary>
public sealed record AgentLocation(AgentCli Cli, string Path, string Source, string? Version);

public class AgentInstallException(string message, Exception? inner = null) : Exception(message, inner);
