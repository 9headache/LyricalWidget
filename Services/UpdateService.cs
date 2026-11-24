using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using LyricalWidget.Common;

namespace LyricalWidget.Services;

public sealed class UpdateInfo
{
    public string TagName { get; set; } = string.Empty;
    public Version Version { get; set; } = new(0, 0, 0);
    public string HtmlUrl { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public bool IsZip { get; set; }
}

public sealed record UpdateCheckResult(UpdateInfo? Update, bool IsUpToDate, string? ErrorMessage);

/// <summary>
/// Checks for new releases on GitHub and performs in-place application updates.
/// </summary>
public static class UpdateService
{
    public static async Task<UpdateCheckResult> CheckForUpdateAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LyricalWidget");

            using var response = await client.GetAsync(AppVersionInfo.ReleasesApiUrl);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(null, false, $"GitHub API returned {(int)response.StatusCode} ({response.ReasonPhrase})");
            }

            string json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out var tp) ? tp.GetString() ?? "" : "";
            string htmlUrl = root.TryGetProperty("html_url", out var hp) ? hp.GetString() ?? "" : "";
            string body = root.TryGetProperty("body", out var bp) ? bp.GetString() ?? "" : "";

            if (!TryParseCleanVersion(tagName, out var remoteVer))
            {
                return new UpdateCheckResult(null, false, $"Could not parse release tag: {tagName}");
            }

            TryParseCleanVersion(AppVersionInfo.Version, out var currentVer);

            if (remoteVer <= currentVer)
            {
                return new UpdateCheckResult(null, true, null);
            }

            string downloadUrl = "";
            string assetName = "";
            bool isZip = false;

            if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsProp.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    string dUrl = asset.TryGetProperty("browser_download_url", out var d) ? d.GetString() ?? "" : "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                        name.Contains("LyricalWidget", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = dUrl;
                        assetName = name;
                        isZip = false;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    foreach (var asset in assetsProp.EnumerateArray())
                    {
                        string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        string dUrl = asset.TryGetProperty("browser_download_url", out var d) ? d.GetString() ?? "" : "";
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = dUrl;
                            assetName = name;
                            isZip = false;
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    foreach (var asset in assetsProp.EnumerateArray())
                    {
                        string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        string dUrl = asset.TryGetProperty("browser_download_url", out var d) ? d.GetString() ?? "" : "";
                        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = dUrl;
                            assetName = name;
                            isZip = true;
                            break;
                        }
                    }
                }
            }

            var update = new UpdateInfo
            {
                TagName = tagName,
                Version = remoteVer,
                HtmlUrl = htmlUrl,
                Body = body,
                DownloadUrl = downloadUrl,
                AssetName = assetName,
                IsZip = isZip
            };

            return new UpdateCheckResult(update, false, null);
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(null, false, ex.Message);
        }
    }

    public static async Task<bool> DownloadAndApplyUpdateAsync(UpdateInfo update)
    {
        string? currentExe = Environment.ProcessPath;
        string currentDir = !string.IsNullOrEmpty(currentExe) ? Path.GetDirectoryName(currentExe)! : "";

        if (string.IsNullOrEmpty(currentExe) ||
            string.IsNullOrEmpty(update.DownloadUrl) ||
            Path.GetFileName(currentExe).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase) ||
            !CanWriteToFolder(currentDir))
        {
            Process.Start(new ProcessStartInfo(update.HtmlUrl) { UseShellExecute = true });
            return false;
        }

        string tempDir = Path.Combine(Path.GetTempPath(), "LyricalWidget_Update");
        if (Directory.Exists(tempDir))
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
        Directory.CreateDirectory(tempDir);

        string newExePath = Path.Combine(tempDir, "LyricalWidget.new.exe");

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LyricalWidget");

        if (update.IsZip)
        {
            string zipPath = Path.Combine(tempDir, "update.zip");
            using (var response = await client.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync();
                await using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await stream.CopyToAsync(fs);
            }

            using var archive = ZipFile.OpenRead(zipPath);
            var entry = archive.Entries.FirstOrDefault(e => e.Name.Equals("LyricalWidget.exe", StringComparison.OrdinalIgnoreCase))
                        ?? archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                throw new FileNotFoundException("LyricalWidget.exe was not found inside the update package.");
            }
            entry.ExtractToFile(newExePath, overwrite: true);
        }
        else
        {
            using (var response = await client.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync();
                await using var fs = new FileStream(newExePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await stream.CopyToAsync(fs);
            }
        }

        var fi = new FileInfo(newExePath);
        if (!fi.Exists || fi.Length < 1024 * 1024)
        {
            throw new InvalidOperationException($"Downloaded binary is incomplete ({fi?.Length ?? 0} bytes).");
        }

        string cmdPath = Path.Combine(tempDir, "updater.cmd");
        string cmdContent =
@"@echo off
set PID=%1
set SOURCE=%2
set TARGET=%3
set TEMPDIR=%4

:waitloop
tasklist /fi ""PID eq %PID%"" 2>nul | find ""%PID%"" >nul
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto waitloop
)

:retrymove
move /y %SOURCE% %TARGET% >nul 2>nul
if errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto retrymove
)

start """" %TARGET%
rd /s /q %TEMPDIR% 2>nul
exit
";
        await File.WriteAllTextAsync(cmdPath, cmdContent);

        int currentPid = Environment.ProcessId;
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{cmdPath}\" {currentPid} \"{newExePath}\" \"{currentExe}\" \"{tempDir}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process.Start(psi);
        Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
        return true;
    }

    public static bool TryParseCleanVersion(string raw, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(raw)) return false;

        string clean = raw.Trim().TrimStart('v', 'V');
        int splitIdx = clean.IndexOfAny(new[] { '·', '+', '-', ' ' });
        if (splitIdx >= 0)
        {
            clean = clean.Substring(0, splitIdx).Trim();
        }

        var parts = clean.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        int major = 0, minor = 0, build = 0, rev = 0;
        if (!int.TryParse(parts[0], out major)) return false;
        if (!int.TryParse(parts[1], out minor)) return false;
        if (parts.Length > 2 && int.TryParse(parts[2], out int b)) build = b;
        if (parts.Length > 3 && int.TryParse(parts[3], out int r)) rev = r;

        version = parts.Length switch
        {
            2 => new Version(major, minor),
            3 => new Version(major, minor, build),
            _ => new Version(major, minor, build, rev)
        };
        return true;
    }

    private static bool CanWriteToFolder(string dir)
    {
        try
        {
            string testPath = Path.Combine(dir, $".perm_test_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testPath, "test");
            File.Delete(testPath);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
