using DEGMA.Data;
using DEGMA.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace DEGMA.Controllers
{
    [Authorize]
    public class ImportController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ImportController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Upload page (redirects to dashboard)
        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile csvFile)
        {
            Console.WriteLine("Starting...");
            if (csvFile == null || csvFile.Length == 0)
            {
                TempData["ImportError"] = "Please select a CSV file.";
                return RedirectToAction("Index", "Home");
            }

            var rows = new List<CsvRowViewModel>();

            using var reader = new StreamReader(csvFile.OpenReadStream());
            var header = await reader.ReadLineAsync(); // skip header

            if (header == null)
            {
                TempData["ImportError"] = "CSV file is empty.";
                return RedirectToAction("Index", "Home");
            }

            int rowIndex = 0;
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var cols = ParseCsvLine(line);
                if (cols.Count < 16)
                {
                    rows.Add(new CsvRowViewModel
                    {
                        RowIndex = rowIndex++,
                        Errors = new List<string> { "Row has too few columns." }
                    });
                    continue;
                }

                var row = new CsvRowViewModel
                {
                    RowIndex = rowIndex++,
                    Firstname = cols[1].Trim(),
                    Surname = cols[2].Trim(),
                    DateOfBirth = cols[3].Trim(),
                    Email = cols[4].Trim(),
                    StartingWeight = cols[5].Trim(),
                    TreatmentStartDate = cols[6].Trim(),
                    ReportNumber = cols[8].Trim(),
                    WeekNumber = cols[9].Trim(),
                    Date = cols[10].Trim(),
                    Weight = cols[11].Trim(),
                    Dosage = cols[12].Trim(),
                    WeightChange = cols[13].Trim(),
                    Notes = cols[14].Trim(),
                    SideEffects = cols[15].Trim()
                };

                ValidateRow(row);
                await CheckDuplicate(row);
                await CheckNewSideEffects(row);

                rows.Add(row);
            }

            // Collect all new side effects across all rows
            var allNewSideEffects = rows
                .SelectMany(r => r.NewSideEffects)
                .Distinct()
                .ToList();

            var model = new ImportViewModel
            {
                Rows = rows,
                NewSideEffectsToCreate = allNewSideEffects
            };

            // Store in TempData as JSON to pass to review page
            TempData["ImportData"] = System.Text.Json.JsonSerializer.Serialize(model);
            return RedirectToAction("Review");
        }

        // GET: Review page
        [HttpGet]
        public IActionResult Review()
        {
            if (TempData["ImportData"] is not string json)
                return RedirectToAction("Index", "Home");

            var model = System.Text.Json.JsonSerializer.Deserialize<ImportViewModel>(json);
            TempData["ImportData"] = json; // keep it for the POST
            return View(model);
        }

        // POST: Confirm import
        [HttpPost]
        public async Task<IActionResult> Confirm(ImportViewModel model)
        {
            int imported = 0;
            int skipped = 0;

            // Create new side effects if confirmed
            if (model.ConfirmNewSideEffects && model.NewSideEffectsToCreate?.Any() == true)
            {
                foreach (var seName in model.NewSideEffectsToCreate)
                {
                    if (!await _context.SideEffects.AnyAsync(s => s.Name == seName))
                    {
                        _context.SideEffects.Add(new SideEffect { Name = seName });
                    }
                }
                await _context.SaveChangesAsync();
            }

            foreach (var row in model.Rows)
            {
                if (row.DuplicateAction == "skip")
                {
                    skipped++;
                    continue;
                }

                // Parse patient
                if (!DateOnly.TryParse(row.DateOfBirth, out var dob)) continue;
                if (!DateOnly.TryParse(row.TreatmentStartDate, out var treatStart)) continue;
                if (!double.TryParse(row.StartingWeight, NumberStyles.Any, CultureInfo.InvariantCulture, out var startWeight)) continue;

                Patient patient;

                if (row.DuplicateAction == "overwrite" && row.ExistingPatientId.HasValue)
                {
                    // Update existing patient
                    patient = await _context.Patients
                        .Include(p => p.Reports)
                        .FirstAsync(p => p.PatientId == row.ExistingPatientId.Value);

                    patient.Firstname = row.Firstname;
                    patient.Surname = row.Surname;
                    patient.DateOfBirth = dob;
                    patient.Email = row.Email;
                    patient.StartingWeight = startWeight;
                    patient.TreatmentStartDate = treatStart;
                }
                else
                {
                    // Check if we already created this patient in this import session
                    patient = await _context.Patients.FirstOrDefaultAsync(p =>
                        p.Firstname == row.Firstname &&
                        p.Surname == row.Surname &&
                        p.DateOfBirth == dob)
                        ?? new Patient
                        {
                            Firstname = row.Firstname,
                            Surname = row.Surname,
                            DateOfBirth = dob,
                            Email = row.Email,
                            StartingWeight = startWeight,
                            TreatmentStartDate = treatStart
                        };

                    if (patient.PatientId == 0)
                        _context.Patients.Add(patient);
                }

                await _context.SaveChangesAsync();

                // Parse and add report
                if (!DateOnly.TryParse(row.Date, out var reportDate)) continue;
                if (!double.TryParse(row.Weight, NumberStyles.Any, CultureInfo.InvariantCulture, out var weight)) continue;
                if (!double.TryParse(row.Dosage, NumberStyles.Any, CultureInfo.InvariantCulture, out var dosage)) continue;
                if (!double.TryParse(row.WeightChange, NumberStyles.Any, CultureInfo.InvariantCulture, out var weightChange)) continue;
                if (!int.TryParse(row.ReportNumber, out var reportNum)) continue;
                if (!int.TryParse(row.WeekNumber, out var weekNum)) continue;

                var report = new Report
                {
                    PatientId = patient.PatientId,
                    ReportNumber = reportNum,
                    WeekNumber = weekNum,
                    Date = reportDate,
                    Weight = weight,
                    Dosage = dosage,
                    WeightChange = weightChange,
                    Notes = row.Notes
                };

                // Link side effects
                if (!string.IsNullOrWhiteSpace(row.SideEffects))
                {
                    var seNames = row.SideEffects.Split('|');
                    foreach (var seName in seNames)
                    {
                        var se = await _context.SideEffects
                            .FirstOrDefaultAsync(s => s.Name == seName.Trim());
                        if (se != null)
                            report.SideEffects.Add(se);
                    }
                }

                _context.Reports.Add(report);
                await _context.SaveChangesAsync();
                imported++;
            }

            TempData["ImportSuccess"] = $"{imported} rows imported, {skipped} skipped.";
            return RedirectToAction("Index", "Home");
        }

        // --- Helpers ---

        private void ValidateRow(CsvRowViewModel row)
        {
            if (string.IsNullOrWhiteSpace(row.Firstname))
                row.Errors.Add("First name is missing.");
            if (string.IsNullOrWhiteSpace(row.Surname))
                row.Errors.Add("Surname is missing.");
            if (!DateOnly.TryParse(row.DateOfBirth, out _))
                row.Errors.Add("Date of birth is invalid or missing.");
            if (string.IsNullOrWhiteSpace(row.Email) || !row.Email.Contains('@'))
                row.Errors.Add("Email is missing or invalid.");
            if (!double.TryParse(row.StartingWeight, NumberStyles.Any,
                CultureInfo.InvariantCulture, out var sw) || sw <= 0)
                row.Errors.Add("Starting weight is invalid or zero.");
            if (!DateOnly.TryParse(row.TreatmentStartDate, out _))
                row.Errors.Add("Treatment start date is invalid.");
            if (!double.TryParse(row.Weight, NumberStyles.Any,
                CultureInfo.InvariantCulture, out var w) || w <= 0)
                row.Errors.Add("Weight is invalid or zero.");
            if (!double.TryParse(row.Dosage, NumberStyles.Any,
                CultureInfo.InvariantCulture, out var d) || d <= 0)
                row.Errors.Add("Dosage is invalid or zero.");
            if (!int.TryParse(row.WeekNumber, out var wn) || wn <= 0)
                row.Errors.Add("Week number is invalid.");
            if (!int.TryParse(row.ReportNumber, out var rn) || rn <= 0)
                row.Errors.Add("Report number is invalid.");
            if (!DateOnly.TryParse(row.Date, out var reportDate))
            {
                row.Errors.Add("Report date is invalid.");
            }
            else if (DateOnly.TryParse(row.TreatmentStartDate, out var treatStart)
                     && reportDate < treatStart)
            {
                row.Errors.Add("Report date is before treatment start date.");
            }
        }

        private async Task CheckDuplicate(CsvRowViewModel row)
        {
            if (!DateOnly.TryParse(row.DateOfBirth, out var dob)) return;

            var existing = await _context.Patients.FirstOrDefaultAsync(p =>
                p.Firstname == row.Firstname &&
                p.Surname == row.Surname &&
                p.DateOfBirth == dob);

            if (existing != null)
            {
                row.IsDuplicate = true;
                row.ExistingPatientId = existing.PatientId;
                row.DuplicateAction = "skip"; // default to safe option
            }
        }

        private async Task CheckNewSideEffects(CsvRowViewModel row)
        {
            if (string.IsNullOrWhiteSpace(row.SideEffects)) return;

            var names = row.SideEffects.Split('|').Select(s => s.Trim());
            foreach (var name in names)
            {
                if (!await _context.SideEffects.AnyAsync(s => s.Name == name))
                    row.NewSideEffects.Add(name);
            }
        }

        private List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var current = new System.Text.StringBuilder();

            foreach (char c in line)
            {
                if (c == '"') { inQuotes = !inQuotes; }
                else if (c == ',' && !inQuotes) { result.Add(current.ToString()); current.Clear(); }
                else { current.Append(c); }
            }
            result.Add(current.ToString());
            return result;
        }
    }
}