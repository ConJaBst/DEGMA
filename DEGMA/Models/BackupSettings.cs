namespace DEGMA.Models
{
    public class BackupSettings
    {
        // Folder on this PC. Environment variables (e.g. %USERPROFILE%) are expanded.
        public string LocalFolder { get; set; } = @"C:\DEGMA_Backups";

        // Folder synced to the cloud by OneDrive / Google Drive for Desktop.
        // Leave empty to skip the cloud copy.
        public string CloudFolder { get; set; } = @"%OneDrive%\DEGMA_Backups";

        // Time of day (24h, HH:mm) the automatic backup runs.
        public string DailyTime { get; set; } = "18:00";

        // Backups older than this are deleted from both folders. 0 keeps everything.
        public int KeepDays { get; set; } = 30;
    }
}
