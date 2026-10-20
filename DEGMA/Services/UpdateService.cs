using DEGMA.Models;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DEGMA.Services
{
    public record UpdateCheckResult(
        string CurrentVersion,
        bool UpdateAvailable,
        string? LatestVersion,
        string? ReleaseName,
        string? ReleaseUrl,
        bool CanInstall,
        string? CannotInstallReason,
        string? Error);

    public record UpdateNotice(bool Success, string From, string To, string? Error, DateTime Time);

    public record InstallStartResult(bool Started, string? Error, string From, string To);

    // Checks GitHub for a newer release and installs it in place:
    //   1. backs up the database (aborts if that fails),
    //   2. downloads the release zip and checks it against GitHub's SHA-256 digest,
    //   3. starts a small PowerShell updater and shuts DEGMA down,
    //   4. the updater unzips the release over the install folder (the zip never contains
    //      degma.db or appsettings.json), records the outcome and starts DEGMA again,
    //   5. on the next start DEGMA reads the outcome and shows it to the user.
    public class UpdateService
    {
        public const string ResultFileName = "update-result.json";
        private const string DownloadClient = "updates-download";

        private readonly IHttpClientFactory _httpFactory;
        private readonly IOptionsMonitor<UpdateSettings> _settings;
        private readonly BackupService _backup;
        private readonly IWebHostEnvironment _env;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly ILogger<UpdateService> _logger;
        private readonly SemaphoreSlim _installLock = new(1, 1);
        private bool _installing;

        public UpdateService(
            IHttpClientFactory httpFactory,
            IOptionsMonitor<UpdateSettings> settings,
            BackupService backup,
            IWebHostEnvironment env,
            IHostApplicationLifetime lifetime,
            ILogger<UpdateService> logger)
        {
            _httpFactory = httpFactory;
            _settings = settings;
            _backup = backup;
            _env = env;
            _lifetime = lifetime;
            _logger = logger;
            Notice = ReadAndClearResultFile();
        }

        public static void AddHttpClients(IServiceCollection services)
        {
            var agent = $"DEGMA/{DisplayVersion}";
            services.AddHttpClient(nameof(UpdateService), c =>
            {
                c.Timeout = TimeSpan.FromSeconds(20);
                c.DefaultRequestHeaders.UserAgent.ParseAdd(agent);
                c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            });
            services.AddHttpClient(DownloadClient, c =>
            {
                c.Timeout = TimeSpan.FromMinutes(20);
                c.DefaultRequestHeaders.UserAgent.ParseAdd(agent);
            });
        }

        // "3.4" for a release built with publish.ps1 -Version v3.4; "0.0.0" for a dev build.
        public static string DisplayVersion { get; } = ReadVersion();

        public static Version CurrentVersion => ParseVersion(DisplayVersion) ?? new Version(0, 0);

        // The outcome of the last update, shown once after DEGMA restarts.
        public UpdateNotice? Notice { get; private set; }

        public void DismissNotice() => Notice = null;

        public string? CannotInstallReason()
        {
            if (_installing)
                return "An update is already being installed.";
            if (!OperatingSystem.IsWindows() || !_env.IsProduction())
                return "Updates can only be installed on the clinic PC.";

            var exe = Environment.ProcessPath;
            if (exe == null || !string.Equals(Path.GetFileName(exe), "DEGMA.exe", StringComparison.OrdinalIgnoreCase))
                return "Updates can only be installed when running DEGMA.exe.";
            if (!SamePath(Path.GetDirectoryName(exe)!, _env.ContentRootPath))
                return "Start DEGMA.exe from its own folder to install updates.";
            return null;
        }

        public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
        {
            var reason = CannotInstallReason();
            try
            {
                var release = await GetLatestReleaseAsync(ct);
                var newer = ParseVersion(release.Tag) is Version latest && latest > CurrentVersion;
                return new UpdateCheckResult(DisplayVersion, newer, release.Tag.TrimStart('v', 'V'),
                    release.Name, release.HtmlUrl, newer && reason == null, reason, null);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UpdateException)
            {
                _logger.LogWarning(ex, "Update check failed");
                var message = ex is UpdateException ? ex.Message : "Couldn't reach GitHub. Check the internet connection.";
                return new UpdateCheckResult(DisplayVersion, false, null, null, null, false, reason, message);
            }
        }

        public async Task<InstallStartResult> InstallAsync()
        {
            if (!await _installLock.WaitAsync(0))
                return new InstallStartResult(false, "An update is already being installed.", DisplayVersion, "");
            try
            {
                if (CannotInstallReason() is string reason)
                    return new InstallStartResult(false, reason, DisplayVersion, "");

                var release = await GetLatestReleaseAsync(default);
                var to = release.Tag.TrimStart('v', 'V');
                if (!(ParseVersion(release.Tag) > CurrentVersion))
                    return new InstallStartResult(false, $"DEGMA is already up to date (v{DisplayVersion}).", DisplayVersion, to);

                var backup = await _backup.RunAsync("Pre-update");
                if (backup.Copies.Count == 0)
                    return new InstallStartResult(false,
                        "The update was not installed because the backup failed: " + string.Join(" ", backup.Errors),
                        DisplayVersion, to);

                var workDir = Path.Combine(Path.GetTempPath(), "DEGMA-update", release.Tag);
                if (Directory.Exists(workDir))
                    Directory.Delete(workDir, recursive: true);
                Directory.CreateDirectory(workDir);

                var zipPath = Path.Combine(workDir, release.Asset.Name);
                await DownloadAsync(release.Asset, zipPath);
                VerifyPackage(zipPath);

                var script = WriteUpdaterScript(workDir, zipPath, to);
                StartUpdater(script, workDir);

                _installing = true;
                _logger.LogWarning("Installing update {From} -> {To}; shutting down", DisplayVersion, to);

                // Give the browser time to receive the "updating" page, then close so the
                // updater can replace DEGMA.exe.
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                    _lifetime.StopApplication();
                });

                return new InstallStartResult(true, null, DisplayVersion, to);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                           or InvalidDataException or UpdateException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Update failed before installing");
                var message = ex is HttpRequestException or TaskCanceledException
                    ? "Couldn't download the update. Check the internet connection and try again."
                    : ex.Message;
                return new InstallStartResult(false, message, DisplayVersion, "");
            }
            finally
            {
                _installLock.Release();
            }
        }

        // ---- GitHub ----

        private record Asset(string Name, long Size, string Url, string? Sha256);
        private record Release(string Tag, string? Name, string? HtmlUrl, Asset Asset);

        private async Task<Release> GetLatestReleaseAsync(CancellationToken ct)
        {
            var settings = _settings.CurrentValue;
            var url = $"{settings.ApiBaseUrl.TrimEnd('/')}/repos/{settings.Repository}/releases/latest";
            using var response = await _httpFactory.CreateClient(nameof(UpdateService)).GetAsync(url, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new UpdateException("No releases were found on GitHub.");
            response.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? throw new UpdateException("The latest release has no version tag.");

            Asset? asset = null;
            foreach (var a in root.GetProperty("assets").EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                if (!name.EndsWith(settings.AssetSuffix, StringComparison.OrdinalIgnoreCase))
                    continue;
                var digest = a.TryGetProperty("digest", out var d) ? d.GetString() : null;
                asset = new Asset(name, a.GetProperty("size").GetInt64(),
                    a.GetProperty("browser_download_url").GetString() ?? "",
                    digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null);
                break;
            }
            if (asset == null)
                throw new UpdateException($"Release {tag} has no '*{settings.AssetSuffix}' file to install.");

            return new Release(tag,
                root.TryGetProperty("name", out var n) ? n.GetString() : null,
                root.TryGetProperty("html_url", out var h) ? h.GetString() : null,
                asset);
        }

        private async Task DownloadAsync(Asset asset, string path)
        {
            var uri = new Uri(asset.Url);
            if (!IsTrustedHost(uri))
                throw new UpdateException($"Refusing to download the update from {uri.Host}.");

            using var response = await _httpFactory.CreateClient(DownloadClient)
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using (var file = File.Create(path))
                await response.Content.CopyToAsync(file);

            var length = new FileInfo(path).Length;
            if (length != asset.Size)
                throw new UpdateException($"The download is incomplete ({length} of {asset.Size} bytes). Try again.");

            if (asset.Sha256 != null)
            {
                await using var stream = File.OpenRead(path);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream));
                if (!hash.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new UpdateException("The download doesn't match the release's checksum, so it was not installed.");
            }
        }

        private bool IsTrustedHost(Uri uri)
        {
            var host = uri.Host;
            var apiHost = new Uri(_settings.CurrentValue.ApiBaseUrl).Host;
            if (!string.Equals(apiHost, "api.github.com", StringComparison.OrdinalIgnoreCase))
                return string.Equals(host, apiHost, StringComparison.OrdinalIgnoreCase); // test stand-in
            return uri.Scheme == Uri.UriSchemeHttps &&
                   (host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));
        }

        private static void VerifyPackage(string zipPath)
        {
            using var zip = ZipFile.OpenRead(zipPath);
            if (!zip.Entries.Any(e => e.FullName.Equals("DEGMA.exe", StringComparison.OrdinalIgnoreCase)))
                throw new UpdateException("The release zip doesn't contain DEGMA.exe, so it was not installed.");
            if (zip.Entries.Any(e => e.FullName.Equals("degma.db", StringComparison.OrdinalIgnoreCase) ||
                                     e.FullName.Equals(SettingsFiles.LocalFile, StringComparison.OrdinalIgnoreCase)))
                throw new UpdateException("The release zip contains a database or settings file, so it was not installed.");
        }

        // ---- Updater ----

        private string WriteUpdaterScript(string workDir, string zipPath, string to)
        {
            static string Q(string s) => "'" + s.Replace("'", "''") + "'";

            var script = $$"""
                # Written by DEGMA to install v{{to}}. Waits for DEGMA to close, copies the new
                # files over the install folder, records the outcome and starts DEGMA again.
                $ErrorActionPreference = 'Stop'
                $installDir = {{Q(_env.ContentRootPath)}}
                $zip        = {{Q(zipPath)}}
                $staging    = {{Q(Path.Combine(workDir, "files"))}}
                $log        = {{Q(Path.Combine(workDir, "update.log"))}}
                $appPid     = {{Environment.ProcessId}}
                $exe        = Join-Path $installDir 'DEGMA.exe'
                $resultFile = Join-Path $installDir {{Q(ResultFileName)}}

                function Log($message) { Add-Content -Path $log -Value "$(Get-Date -Format s)  $message" }

                $ok = $false
                $err = $null
                try {
                    Log "Waiting for DEGMA (PID $appPid) to close"
                    try { Wait-Process -Id $appPid -Timeout 120 -ErrorAction Stop } catch { }
                    if (Get-Process -Id $appPid -ErrorAction SilentlyContinue) { throw 'DEGMA did not close.' }

                    Log 'Unpacking the update'
                    Expand-Archive -Path $zip -DestinationPath $staging -Force

                    # DEGMA.exe can stay locked for a moment after the process exits.
                    for ($i = 1; $i -le 15; $i++) {
                        try {
                            Copy-Item -Path (Join-Path $staging '*') -Destination $installDir -Recurse -Force
                            $ok = $true
                            break
                        } catch {
                            $err = $_.Exception.Message
                            Log "Copy attempt $i failed: $err"
                            Start-Sleep -Seconds 2
                        }
                    }
                    if (-not $ok) { throw "Could not replace the program files: $err" }
                    Log 'Update installed'
                } catch {
                    $err = $_.Exception.Message
                    Log "Update failed: $err"
                }

                $result = [ordered]@{
                    success = $ok
                    from    = {{Q(DisplayVersion)}}
                    to      = {{Q(to)}}
                    error   = $(if ($ok) { $null } else { "$err (details: $log)" })
                    time    = (Get-Date).ToString('o')
                }
                $result | ConvertTo-Json | Set-Content -Path $resultFile -Encoding UTF8

                Log 'Starting DEGMA'
                Start-Process -FilePath $exe -WorkingDirectory $installDir
                """;

            var path = Path.Combine(workDir, "update.ps1");
            File.WriteAllText(path, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return path;
        }

        private static void StartUpdater(string script, string workDir)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"",
                WorkingDirectory = workDir,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }

        private UpdateNotice? ReadAndClearResultFile()
        {
            var path = Path.Combine(_env.ContentRootPath, ResultFileName);
            if (!File.Exists(path))
                return null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var r = doc.RootElement;
                string Str(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
                var notice = new UpdateNotice(
                    r.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True,
                    Str("from"), Str("to"),
                    Str("error") is { Length: > 0 } e ? e : null,
                    DateTime.TryParse(Str("time"), out var t) ? t : DateTime.Now);
                File.Delete(path);
                if (notice.Success)
                    _logger.LogInformation("Updated from {From} to {To}", notice.From, notice.To);
                else
                    _logger.LogError("Update from {From} to {To} failed: {Error}", notice.From, notice.To, notice.Error);
                return notice;
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                _logger.LogWarning(ex, "Could not read {File}", path);
                return null;
            }
        }

        // ---- Helpers ----

        private static string ReadVersion()
        {
            var info = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }

        private static Version? ParseVersion(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            // Always compare as major.minor.patch, so "v3.4" equals "3.4.0".
            var parts = text.Trim().TrimStart('v', 'V').Split('-', '+')[0].Split('.');
            var numbers = new int[3];
            for (var i = 0; i < parts.Length && i < 3; i++)
            {
                if (!int.TryParse(parts[i], out numbers[i]))
                    return null;
            }
            return new Version(numbers[0], numbers[1], numbers[2]);
        }

        private static bool SamePath(string a, string b) =>
            string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);

        private class UpdateException(string message) : Exception(message);
    }
}
