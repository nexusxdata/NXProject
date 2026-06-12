using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace NXProject.Services;

public static class UpdateService
{
    private const string ApiUrl = "https://api.github.com/repos/nexusxdata/NXProject/releases/latest";
    private const string AssetName = "NXProject.Community-Release.zip";

    public record ReleaseInfo(string TagName, string DownloadUrl);

    public static async Task<ReleaseInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        using var client = CreateClient();
        var release = await client.GetFromJsonAsync<GithubRelease>(ApiUrl, ct);
        if (release is null) return null;

        var latest = ParseVersion(release.TagName);
        var current = GetCurrentVersion();
        if (latest <= current) return null;

        var asset = release.Assets?.Find(a => a.Name == AssetName);
        if (asset is null) return null;

        return new ReleaseInfo(release.TagName, asset.BrowserDownloadUrl);
    }

    public static async Task<string> DownloadAndExtractAsync(
        string downloadUrl,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        using var client = CreateClient();

        var tempDir = Path.Combine(Path.GetTempPath(), $"nxupdate_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var zipPath = Path.Combine(tempDir, "update.zip");

        using (var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0L;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            await using var file = File.Create(zipPath);

            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                downloaded += read;
                if (total > 0)
                    progress?.Report((int)(downloaded * 100 / total));
            }
        }

        progress?.Report(100);

        var extractDir = Path.Combine(tempDir, "extracted");
        ZipFile.ExtractToDirectory(zipPath, extractDir);
        File.Delete(zipPath);

        return extractDir;
    }

    public static void LaunchUpdaterAndExit(string extractedDir)
    {
        var exePath = Process.GetCurrentProcess().MainModule!.FileName!;
        var appDir = Path.GetDirectoryName(exePath)!;
        var exeName = Path.GetFileName(exePath);
        var dateSuffix = DateTime.Now.ToString("yyyy_MM_dd");
        var oldName = $"old_{dateSuffix}_{exeName}";
        var scriptPath = Path.Combine(appDir, "_nxupdate.ps1");

        var script = $$"""
            $pid_target = {{Environment.ProcessId}}
            $app_dir = '{{Escape(appDir)}}'
            $exe_name = '{{exeName}}'
            $old_name = '{{oldName}}'
            $src_dir  = '{{Escape(extractedDir)}}'
            $script_path = '{{Escape(scriptPath)}}'

            while (Get-Process -Id $pid_target -ErrorAction SilentlyContinue) {
                Start-Sleep -Milliseconds 300
            }

            $exe_path = Join-Path $app_dir $exe_name
            if (Test-Path $exe_path) {
                Rename-Item -Path $exe_path -NewName $old_name -Force
            }

            Get-ChildItem -Path $src_dir -Recurse | ForEach-Object {
                $rel  = $_.FullName.Substring($src_dir.Length).TrimStart([char]'\', [char]'/')
                $dest = Join-Path $app_dir $rel
                if ($_.PSIsContainer) {
                    New-Item -ItemType Directory -Path $dest -Force | Out-Null
                } else {
                    Copy-Item -Path $_.FullName -Destination $dest -Force
                }
            }

            Remove-Item -Path $src_dir -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -Path (Split-Path $src_dir) -Recurse -Force -ErrorAction SilentlyContinue

            Start-Process (Join-Path $app_dir $exe_name)

            Start-Sleep -Seconds 1
            Remove-Item -Path $script_path -Force -ErrorAction SilentlyContinue
            """;

        File.WriteAllText(scriptPath, script, System.Text.Encoding.UTF8);

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-WindowStyle Hidden -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        System.Windows.Application.Current.Shutdown();
    }

    private static string Escape(string path) => path.Replace("'", "''");

    private static Version GetCurrentVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v ?? new Version(0, 0, 0);
    }

    private static Version ParseVersion(string tag)
    {
        var s = tag.TrimStart('v', 'V');
        return Version.TryParse(s, out var v) ? v : new Version(0, 0, 0);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NXProject-Updater/1.0");
        return client;
    }

    private sealed class GithubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("assets")]
        public List<GithubAsset>? Assets { get; set; }
    }

    private sealed class GithubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";
    }
}
