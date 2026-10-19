using DEGMA.Data;
using DEGMA.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace DEGMA.Controllers
{
    [Authorize]
    public class BackupController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly BackupService _backupService;

        public BackupController(ApplicationDbContext context, BackupService backupService)
        {
            _context = context;
            _backupService = backupService;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> RunNow()
        {
            var result = await _backupService.RunAsync("Manual");

            if (result.Success)
                TempData["BackupSuccess"] = $"Backup created in {result.Copies.Count} locations.";
            else
                TempData["BackupError"] = string.Join(" ", result.Errors);

            return RedirectToAction("Index", "Home");
        }

        // Confirmation step: shows what restoring the chosen backup will replace.
        [HttpGet]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> ConfirmRestore(string file)
        {
            var backup = _backupService.FindBackup(file);
            if (backup == null)
            {
                TempData["BackupError"] = "Choose a backup to restore.";
                return RedirectToAction("Index", "Home");
            }

            var invalid = await _backupService.ValidateAsync(backup.FullPath);
            if (invalid != null)
            {
                TempData["BackupError"] = $"{backup.FileName} cannot be restored. {invalid}";
                return RedirectToAction("Index", "Home");
            }

            var counts = await _backupService.CountRecordsAsync(backup.FullPath);
            ViewBag.BackupPatients = counts?.Patients;
            ViewBag.BackupReports = counts?.Reports;
            ViewBag.CurrentPatients = await _context.Patients.CountAsync();
            ViewBag.CurrentReports = await _context.Reports.CountAsync();

            return View(backup);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> Restore(string file, bool confirmed)
        {
            if (!confirmed)
                return RedirectToAction(nameof(ConfirmRestore), new { file });

            var result = await _backupService.RestoreAsync(file);

            if (result.Success)
                TempData["BackupSuccess"] = $"Database restored from the backup of " +
                    $"{_backupService.FindBackup(file)?.Time.ToString("dd MMM yyyy HH:mm") ?? file}. " +
                    $"The data from before the restore was saved as {Path.GetFileName(result.PreRestoreBackup!.Copies[0])}.";
            else
                TempData["BackupError"] = result.Error;

            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> Download(bool includePrime, bool includePatients, bool includeReports)
        {
            var files = new Dictionary<string, byte[]>();

            if (includePrime)
                files["DEGMA_Full_Backup.csv"] = Encoding.UTF8.GetBytes(await BuildPrimeCsv());

            if (includePatients)
                files["DEGMA_Patients.csv"] = Encoding.UTF8.GetBytes(await BuildPatientsCsv());

            if (includeReports)
                files["DEGMA_Reports.csv"] = Encoding.UTF8.GetBytes(await BuildReportsCsv());

            if (files.Count == 0)
                return RedirectToAction("Index", "Home");

            if (files.Count == 1)
            {
                var file = files.First();
                return File(file.Value, "text/csv", file.Key);
            }

            using var zipStream = new MemoryStream();
            using (var archive = new System.IO.Compression.ZipArchive(
                zipStream, System.IO.Compression.ZipArchiveMode.Create, true))
            {
                foreach (var file in files)
                {
                    var entry = archive.CreateEntry(file.Key);
                    using var entryStream = entry.Open();
                    entryStream.Write(file.Value, 0, file.Value.Length);
                }
            }

            return File(zipStream.ToArray(), "application/zip",
                $"DEGMA_Backup_{DateTime.Now:yyyyMMdd_HHmm}.zip");
        }

        private async Task<string> BuildPrimeCsv()
        {
            var reports = await _context.Reports
                .Include(r => r.Patient)
                .Include(r => r.SideEffects)
                .OrderBy(r => r.Patient.Surname)
                .ThenBy(r => r.WeekNumber)
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("PatientId,Firstname,Surname,DateOfBirth,Email,StartingWeight," +
              "TreatmentStartDate,ReportId,ReportNumber,WeekNumber,Date," +
              "Weight,Dosage,WeightChange,Notes,SideEffects,VisceralFat,MuscleMass,BatchNumber,Pulse,SystolicBp,DiastolicBp");

            foreach (var r in reports)
            {
                var sideEffects = string.Join("|", r.SideEffects.Select(se => se.Name));
                sb.AppendLine(string.Join(",",
                r.Patient.PatientId,
                Escape(r.Patient.Firstname),
                Escape(r.Patient.Surname),
                r.Patient.DateOfBirth.ToString("yyyy-MM-dd"),
                Escape(r.Patient.Email),
                r.Patient.StartingWeight.ToString("F2", CultureInfo.InvariantCulture),
                r.Patient.TreatmentStartDate.ToString("yyyy-MM-dd"),
                r.ReportId,
                r.ReportNumber,
                r.WeekNumber,
                r.Date.ToString("yyyy-MM-dd"),
                r.Weight.ToString("F2", CultureInfo.InvariantCulture),
                r.Dosage.ToString("F2", CultureInfo.InvariantCulture),
                r.WeightChange.ToString("F2", CultureInfo.InvariantCulture),
                Escape(r.Notes ?? ""),
                Escape(sideEffects),
                r.VisceralFat.HasValue ? r.VisceralFat.Value.ToString("F2", CultureInfo.InvariantCulture) : "",
                r.MuscleMass.HasValue ? r.MuscleMass.Value.ToString("F2", CultureInfo.InvariantCulture) : "",
                Escape(r.BatchNumber ?? ""),
                r.Pulse?.ToString(CultureInfo.InvariantCulture) ?? "",
                r.SystolicBp?.ToString(CultureInfo.InvariantCulture) ?? "",
                r.DiastolicBp?.ToString(CultureInfo.InvariantCulture) ?? ""
            ));
            }

            return sb.ToString();
        }

        private async Task<string> BuildPatientsCsv()
        {
            var patients = await _context.Patients
                .OrderBy(p => p.Surname)
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("PatientId,Firstname,Surname,DateOfBirth,Email,StartingWeight,TreatmentStartDate");

            foreach (var p in patients)
            {
                sb.AppendLine(string.Join(",",
                    p.PatientId,
                    Escape(p.Firstname),
                    Escape(p.Surname),
                    p.DateOfBirth.ToString("yyyy-MM-dd"),
                    Escape(p.Email),
                    p.StartingWeight.ToString("F2", CultureInfo.InvariantCulture),
                    p.TreatmentStartDate.ToString("yyyy-MM-dd")
                ));
            }

            return sb.ToString();
        }

        private async Task<string> BuildReportsCsv()
        {
            var reports = await _context.Reports
                .Include(r => r.SideEffects)
                .OrderBy(r => r.PatientId)
                .ThenBy(r => r.WeekNumber)
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("ReportId,PatientId,ReportNumber,WeekNumber,Date,Weight,Dosage,WeightChange,Notes,SideEffects");

            foreach (var r in reports)
            {
                var sideEffects = string.Join("|", r.SideEffects.Select(se => se.Name));
                sb.AppendLine(string.Join(",",
                    r.ReportId,
                    r.PatientId,
                    r.ReportNumber,
                    r.WeekNumber,
                    r.Date.ToString("yyyy-MM-dd"),
                    r.Weight.ToString("F2", CultureInfo.InvariantCulture),
                    r.Dosage.ToString("F2", CultureInfo.InvariantCulture),
                    r.WeightChange.ToString("F2", CultureInfo.InvariantCulture),
                    Escape(r.Notes ?? ""),
                    Escape(sideEffects)
                ));
            }

            return sb.ToString();
        }

        private string Escape(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }
    }
}