namespace DEGMA.Services
{
    // Runs the daily backup inside the app, so it keeps working while Windows is locked.
    // Checks once a minute instead of sleeping until the target time, so a backup missed
    // while the PC was asleep or the app was closed runs as soon as it is back.
    public class BackupScheduler : BackgroundService
    {
        private readonly BackupService _backup;
        private readonly ILogger<BackupScheduler> _logger;

        public BackupScheduler(BackupService backup, ILogger<BackupScheduler> logger)
        {
            _backup = backup;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            do
            {
                try
                {
                    if (IsDue())
                    {
                        var result = await _backup.RunAsync("Scheduled");
                        if (!result.Success)
                            _logger.LogWarning("Scheduled backup had errors: {Errors}", string.Join("; ", result.Errors));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Scheduled backup failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        private bool IsDue()
        {
            var now = DateTime.Now;
            var dueToday = DateOnly.FromDateTime(now).ToDateTime(_backup.DailyTime);

            // Most recent scheduled slot that has already passed.
            var lastSlot = now >= dueToday ? dueToday : dueToday.AddDays(-1);
            var last = _backup.LastSuccessfulBackup();

            if (last != null && last >= lastSlot)
                return false;

            // Don't retry a failed attempt every minute; wait an hour between attempts.
            var lastAttempt = _backup.GetLastResult();
            return lastAttempt == null || lastAttempt.Success || now - lastAttempt.Time > TimeSpan.FromHours(1);
        }
    }
}
