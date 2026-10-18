// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXPilot — licenciado sob a NXPilot License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System.Diagnostics;

namespace NX.AgentInstaller;

/// <summary>
/// Acha o agente instalado, na ordem: PATH (usuário e máquina), pastas dos instaladores oficiais
/// (~/.local/bin, …\Programs\OpenAI\Codex\bin), pasta do NXProject e o binário que vem nas
/// extensões do VS Code. Não roda nada além de "--version".
/// </summary>
public static class AgentLocator
{
    public static IEnumerable<AgentLocation> Candidates(AgentCli cli, AgentInstallOptions? options = null)
    {
        var o = options ?? new AgentInstallOptions();
        var exe = cli == AgentCli.ClaudeCode ? "claude" : "codex";

        var path = (Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) + ";" +
                    Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) + ";" +
                    Environment.GetEnvironmentVariable("PATH"))
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Environment.ExpandEnvironmentVariables)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in path)
            foreach (var ext in new[] { ".exe", ".cmd" })
                if (File.Exists(Path.Combine(dir, exe + ext))) yield return new(cli, Path.Combine(dir, exe + ext), "PATH", null);

        var official = cli == AgentCli.ClaudeCode
            ? Path.Combine(o.ClaudeLocalBin, "claude.exe")
            : Path.Combine(o.CodexVisibleBin, "codex.exe");
        if (File.Exists(official)) yield return new(cli, official, "instalador oficial", null);

        var nxproject = Path.Combine(o.LocalAppData, "NXProject", "bin", exe + ".exe");
        if (File.Exists(nxproject)) yield return new(cli, nxproject, "NXProject", null);

        var extensions = Path.Combine(o.UserProfile, ".vscode", "extensions");
        if (Directory.Exists(extensions))
        {
            var dirs = cli == AgentCli.ClaudeCode
                ? Directory.EnumerateDirectories(extensions, "anthropic.claude-code-*").Select(d => Path.Combine(d, "resources", "native-binary", "claude.exe"))
                : Directory.EnumerateDirectories(extensions, "openai.chatgpt-*").Select(d => Path.Combine(d, "bin", "windows-x86_64", "codex.exe"));
            foreach (var f in dirs.Where(File.Exists).OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase))
                yield return new(cli, f, "extensão do VS Code", null);
        }
    }

    /// <summary>O primeiro encontrado, com a versão (null se não houver nenhum).</summary>
    public static AgentLocation? Find(AgentCli cli, AgentInstallOptions? options = null) =>
        Candidates(cli, options).FirstOrDefault() is { } l ? l with { Version = Version(l.Path) } : null;

    /// <summary>"2.1.301 (Claude Code)" / "codex-cli 0.163.0" → "2.1.301" / "0.163.0".</summary>
    public static string? Version(string exePath)
    {
        try
        {
            var psi = new ProcessStartInfo(exePath, "--version") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi);
            if (p == null) return null;
            var output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(10_000)) { try { p.Kill(true); } catch { } return null; }
            var m = System.Text.RegularExpressions.Regex.Match(output, @"\d+\.\d+\.\d+(-[0-9A-Za-z.\-]+)?");
            return m.Success ? m.Value : null;
        }
        catch { return null; }
    }
}
