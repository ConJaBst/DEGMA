using DEGMA.Data;
using DEGMA.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DEGMA.Services
{
    public record BackupResult(bool Success, DateTime Time, string Trigger, List<string> Copies, List<string> Errors);

    public record BackupFile(string FileName, DateTime Time, long Size, string FullPath, List<string> Locations);

    public record RestoreResult(bool Success, string? Error, BackupResult? PreRestoreBackup);

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

        // Backups in the local and cloud folders, newest first. A file present in both
        // folders is listed once; the local copy is used when restoring.
        public List<BackupFile> ListBackups()
        {
            var backups = new Dictionary<string, BackupFile>(StringComparer.OrdinalIgnoreCase);

            foreach (var (label, folder) in new[] { ("Local", LocalFolder), ("Cloud", CloudFolder) })
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                        continue;

                    foreach (var file in new DirectoryInfo(folder).GetFiles("degma_*.db"))
                    {
                        if (!IsBackupFileName(file.Name))
                            continue;

                        if (backups.TryGetValue(file.Name, out var existing))
                            existing.Locations.Add(label);
                        else
                            backups[file.Name] = new BackupFile(file.Name, BackupTime(file), file.Length,
                                file.FullName, new List<string> { label });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not list backups in {Folder}", folder);
                }
            }

            return backups.Values.OrderByDescending(b => b.Time).ToList();
        }

        // Looks a backup up by file name only, so callers can never point at arbitrary paths.
        public BackupFile? FindBackup(string? fileName)
        {
            if (string.IsNullOrEmpty(fileName) || !IsBackupFileName(fileName))
                return null;

            return ListBackups().FirstOrDefault(b =>
                string.Equals(b.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        }

        // Opens the backup read-only and checks it is an intact DEGMA database.
        // Returns null when valid, otherwise the reason it is not.
        public async Task<string?> ValidateAsync(string path)
        {
            try
            {
                await using var connection = OpenReadOnly(path);
                await connection.OpenAsync();

                await using var check = connection.CreateCommand();
                check.CommandText = "PRAGMA integrity_check;";
                var integrity = (await check.ExecuteScalarAsync())?.ToString();
                if (integrity != "ok")
                    return $"Integrity check failed: {integrity}";

                await using var tables = connection.CreateCommand();
                tables.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' " +
                                     "AND name IN ('__EFMigrationsHistory', 'Patients', 'Reports');";
                if (Convert.ToInt32(await tables.ExecuteScalarAsync()) != 3)
                    return "The file is not a DEGMA database.";

                return null;
            }
            catch (SqliteException ex)
            {
                return $"The file is not a readable SQLite database: {ex.Message}";
            }
        }

        // Patient and report counts in a backup, for the confirmation page.
        public async Task<(int Patients, int Reports)?> CountRecordsAsync(string path)
        {
            try
            {
                await using var connection = OpenReadOnly(path);
                await connection.OpenAsync();
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT (SELECT COUNT(*) FROM Patients), (SELECT COUNT(*) FROM Reports);";
                await using var reader = await cmd.ExecuteReaderAsync();
                return await reader.ReadAsync() ? (reader.GetInt32(0), reader.GetInt32(1)) : null;
            }
            catch
            {
                return null;
            }
        }

        // Replaces the live database with a backup. A "Pre-restore" backup of the current
        // data is taken first, and the restore is aborted if that backup fails.
        public async Task<RestoreResult> RestoreAsync(string fileName)
        {
            var backup = FindBackup(fileName);
            if (backup == null)
                return new RestoreResult(false, "That backup was not found in the backup folders.", null);

            // Work from a private copy: the pre-restore backup may prune the original, and
            // OneDrive may touch files in the cloud folder while we read them.
            var staged = Path.Combine(Path.GetTempPath(), $"restore_{Guid.NewGuid():N}.db");
            try
            {
                File.Copy(backup.FullPath, staged);

                var invalid = await ValidateAsync(staged);
                if (invalid != null)
                    return new RestoreResult(false, invalid, null);

                var safety = await RunAsync("Pre-restore");
                if (safety.Copies.Count == 0)
                    return new RestoreResult(false,
                        "Restore cancelled: could not back up the current data first. " + string.Join(" ", safety.Errors),
                        safety);

                await _lock.WaitAsync();
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    // Copy pages through SQLite's backup API into the live database rather than
                    // swapping files, so the running app's -wal/-shm files stay consistent.
                    await using (var source = OpenReadOnly(staged))
                    await using (var destination = new SqliteConnection(db.Database.GetConnectionString()))
                    {
                        await source.OpenAsync();
                        await destination.OpenAsync();
                        source.BackupDatabase(destination);
                    }

                    // Drop pooled connections that may hold the old schema, then bring an
                    // older backup up to the current schema.
                    SqliteConnection.ClearAllPools();
                    await db.Database.MigrateAsync();

                    _logger.LogWarning("Database restored from {Backup}; pre-restore backup: {Copies}",
                        backup.FullPath, string.Join(", ", safety.Copies));

                    return new RestoreResult(true, null, safety);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Restore from {Backup} failed", backup.FullPath);
                    return new RestoreResult(false, $"Restore failed: {ex.Message}", safety);
                }
                finally
                {
                    _lock.Release();
                }
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Could not read backup {Backup}", backup.FullPath);
                return new RestoreResult(false, $"Could not read the backup file: {ex.Message}", null);
            }
            finally
            {
                TryDelete(staged);
            }
        }

        private static SqliteConnection OpenReadOnly(string path) =>
            new(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());

        private static bool IsBackupFileName(string fileName) =>
            System.Text.RegularExpressions.Regex.IsMatch(fileName, @"^degma_\d{8}_\d{6}\.db$")
            && Path.GetFileName(fileName) == fileName;

        // The time is in the file name; fall back to the file date if it does not parse.
        private static DateTime BackupTime(FileInfo file) =>
            DateTime.TryParseExact(file.Name[6..^3], "yyyyMMdd_HHmmss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var time)
                ? time
                : file.CreationTime;

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
