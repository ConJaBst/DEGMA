using DEGMA.Data;
using DEGMA.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DEGMA.Services
{
    public record BackupResult(bool Success, DateTime Time, string Trigger, List<string> Copies, List<string> Errors);

    // Copies the SQLite database to a local folder and a cloud-synced folder.
    // Registered as a singleton so the scheduler and the manual button share one lock.
    public class BackupService
    {
        private const string StatusFileName = "last-backup.json";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IOptionsMonitor<BackupSettings> _settings;
        private readonly ILogger<BackupService> _logger;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public BackupService(
            IServiceScopeFactory scopeFactory,
            IOptionsMonitor<BackupSettings> settings,
            ILogger<BackupService> logger)
        {
            _scopeFactory = scopeFactory;
            _settings = settings;
            _logger = logger;
        }

        public string LocalFolder => Expand(_settings.CurrentValue.LocalFolder);
        public string CloudFolder => Expand(_settings.CurrentValue.CloudFolder);

        public async Task<BackupResult> RunAsync(string trigger)
        {
            await _lock.WaitAsync();
            try
            {
                return await RunCoreAsync(trigger);
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task<BackupResult> RunCoreAsync(string trigger)
        {
            var now = DateTime.Now;
            var fileName = $"degma_{now:yyyyMMdd_HHmmss}.db";
            var copies = new List<string>();
            var errors = new List<string>();

            // Use SQLite's online backup API rather than a file copy, so changes still
            // sitting in degma.db-wal are included and the copy is never half-written.
            var snapshot = Path.Combine(Path.GetTempPath(), fileName);
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    await using var source = new SqliteConnection(db.Database.GetConnectionString());
                    await using var destination = new SqliteConnection(
                        new SqliteConnectionStringBuilder { DataSource = snapshot, Pooling = false }.ToString());
                    await source.OpenAsync();
                    await destination.OpenAsync();
                    source.BackupDatabase(destination);

                    // The copy inherits WAL mode; switch it back so each backup is one self-contained file.
                    await using var cmd = destination.CreateCommand();
                    cmd.CommandText = "PRAGMA journal_mode=DELETE;";
                    await cmd.ExecuteNonQueryAsync();
                }

                foreach (var (label, folder) in new[] { ("Local", LocalFolder), ("Cloud", CloudFolder) })
                {
                    if (string.IsNullOrWhiteSpace(folder))
                    {
                        if (label == "Cloud")
                            errors.Add("Cloud folder is not configured.");
                        continue;
                    }

                    try
                    {
                        Directory.CreateDirectory(folder);
                        var target = Path.Combine(folder, fileName);
                        File.Copy(snapshot, target, overwrite: true);
                        copies.Add(target);
                        Prune(folder);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "{Label} backup to {Folder} failed", label, folder);
                        errors.Add($"{label} copy to {folder} failed: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database snapshot failed");
                errors.Add($"Could not read the database: {ex.Message}");
            }
            finally
            {
                TryDelete(snapshot);
            }

            var result = new BackupResult(copies.Count > 0 && errors.Count == 0, now, trigger, copies, errors);
            SaveStatus(result);

            if (result.Success)
                _logger.LogInformation("{Trigger} backup created: {Copies}", trigger, string.Join(", ", copies));

            return result;
        }

        // The last attempt, read from the local backup folder so it survives restarts.
        public BackupResult? GetLastResult()
        {
            try
            {
                var path = Path.Combine(LocalFolder, StatusFileName);
                return File.Exists(path)
                    ? JsonSerializer.Deserialize<BackupResult>(File.ReadAllText(path))
                    : null;
            }
            catch
            {
                return null;
            }
        }

        public DateTime? LastSuccessfulBackup()
        {
            // The newest backup file is the source of truth; status may record a failure.
            try
            {
                var latest = Directory.Exists(LocalFolder)
                    ? new DirectoryInfo(LocalFolder).GetFiles("degma_*.db")
                        .OrderByDescending(f => f.CreationTime).FirstOrDefault()
                    : null;
                return latest?.CreationTime;
            }
            catch
            {
                return null;
            }
        }

        public TimeOnly DailyTime =>
            TimeOnly.TryParse(_settings.CurrentValue.DailyTime, out var t) ? t : new TimeOnly(18, 0);

        private void SaveStatus(BackupResult result)
        {
            try
            {
                Directory.CreateDirectory(LocalFolder);
                File.WriteAllText(Path.Combine(LocalFolder, StatusFileName), JsonSerializer.Serialize(result));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not write backup status file");
            }
        }

        private void Prune(string folder)
        {
            var keepDays = _settings.CurrentValue.KeepDays;
            if (keepDays <= 0)
                return;

            var cutoff = DateTime.Now.AddDays(-keepDays);
            foreach (var file in new DirectoryInfo(folder).GetFiles("degma_*.db"))
            {
                if (file.CreationTime < cutoff)
                    TryDelete(file.FullName);
            }
        }

        private void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete {Path}", path);
            }
        }

        private static string Expand(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return "";

            var expanded = Environment.ExpandEnvironmentVariables(folder);
            // An unset variable (e.g. %OneDrive% with OneDrive not installed) stays as-is.
            return expanded.Contains('%') ? "" : expanded;
        }
    }
}
