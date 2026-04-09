using DEGMA.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace DEGMA.Controllers
{
    [Authorize]
    public class BackupController : Controller
    {
        private readonly ApplicationDbContext _context;

        public BackupController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> Download(bool includePrime, bool includePatients, bool includeReports)
        {
            var files = new Dictionary<string, byte[]>();

            if (includePrime)
            {
                var csv = await BuildPrimeCsv();
                files["DEGMA_Full_Backup.csv"] = Encoding.UTF8.GetBytes(csv);
            }

            if (includePatients)
            {
                var csv = await BuildPatientsCsv();
                files["DEGMA_Patients.csv"] = Encoding.UTF8.GetBytes(csv);
            }

            if (includeReports)
            {
                var csv = await BuildReportsCsv();
                files["DEGMA_Reports.csv"] = Encoding.UTF8.GetBytes(csv);
            }

            if (files.Count == 0)
                return RedirectToAction("Index", "Home");

            // If only one file selected, return it directly
            if (files.Count == 1)
            {
                var file = files.First();
                return File(file.Value, "text/csv", file.Key);
            }

            // Multiple files — zip them
            using var zipStream = new System.IO.MemoryStream();
            using (var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create, true))
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

        // --- Prime CSV: every report with all associated info ---
        private async Task<string> BuildPrimeCsv()
        {
            var reports = await _context.Reports
                .Include(r => r.Patient)
                .Include(r => r.SideEffects)
                .OrderBy(r => r.Patient.Surname)
                .ThenBy(r => r.WeekNumber)
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("PatientId,Firstname,Surname,DateOfBirth,Email,StartingWeight,TreatmentStartDate," +
                          "ReportId,ReportNumber,WeekNumber,Date,Weight,Dosage,WeightChange,Notes,SideEffects");

            foreach (var r in reports)
            {
                var sideEffects = string.Join("|", r.SideEffects.Select(se => se.Name));
                sb.AppendLine(string.Join(",",
                    r.Patient.PatientId,
                    Escape(r.Patient.Firstname),
                    Escape(r.Patient.Surname),
                    r.Patient.DateOfBirth.ToString("yyyy-MM-dd"),
                    Escape(r.Patient.Email),
                    r.Patient.StartingWeight,
                    r.Patient.TreatmentStartDate.ToString("yyyy-MM-dd"),
                    r.ReportId,
                    r.ReportNumber,
                    r.WeekNumber,
                    r.Date.ToString("yyyy-MM-dd"),
                    r.Weight,
                    r.Dosage,
                    r.WeightChange,
                    Escape(r.Notes ?? ""),
                    Escape(sideEffects)
                ));
            }

            return sb.ToString();
        }

        // --- Patients CSV: patient info only ---
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
                    p.StartingWeight,
                    p.TreatmentStartDate.ToString("yyyy-MM-dd")
                ));
            }

            return sb.ToString();
        }

        // --- Reports CSV: report info only, no patient info ---
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
                    r.Weight,
                    r.Dosage,
                    r.WeightChange,
                    Escape(r.Notes ?? ""),
                    Escape(sideEffects)
                ));
            }

            return sb.ToString();
        }

        // Wraps fields in quotes if they contain commas or quotes
        private string Escape(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }
    }
}