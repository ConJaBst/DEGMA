using DEGMA.Data;
using DEGMA.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DEGMA.Controllers
{
    public class ImportController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ImportController(ApplicationDbContext context)
        {
            _context = context;
        }
        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile csvFile)
        {
            if (csvFile == null || csvFile.Length == 0) return RedirectToAction("Index", "Home");

            var model = new ImportViewModel();
            var existingSideEffectNames = await _context.SideEffects.Select(s => s.Name).ToListAsync();
            var detectedNewSideEffects = new HashSet<string>();

            using (var reader = new StreamReader(csvFile.OpenReadStream()))
            {
                await reader.ReadLineAsync(); // Skip header
                int index = 0;

                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // SMART SPLIT: Handles commas inside quotes
                    var parts = Regex.Split(line, ",(?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)");

                    if (parts.Length < 15) continue;

                    var row = new CsvRowViewModel
                    {
                        RowIndex = index++,
                        // col 0 = PatientId (DB key, skip)
                        Firstname = Unescape(parts[1]),
                        Surname = Unescape(parts[2]),
                        DateOfBirth = Unescape(parts[3]),
                        Email = Unescape(parts[4]),
                        StartingWeight = Unescape(parts[5]),
                        TreatmentStartDate = Unescape(parts[6]),
                        // col 7 = ReportId (DB key, skip)
                        ReportNumber = Unescape(parts[8]),
                        WeekNumber = Unescape(parts[9]),
                        Date = Unescape(parts[10]),
                        Weight = Unescape(parts[11]),
                        Dosage = Unescape(parts[12]),
                        WeightChange = Unescape(parts[13]),
                        Notes = Unescape(parts[14]),
                        SideEffects = parts.Length > 15 ? Unescape(parts[15]) : ""
                    };

                    var existingPatient = await _context.Patients.FirstOrDefaultAsync(p => p.Email == row.Email);
                    if (existingPatient != null)
                    {
                        row.IsDuplicate = true;
                        row.ExistingPatientId = existingPatient.PatientId;
                        row.DuplicateAction = "skip";
                    }

                    if (!string.IsNullOrWhiteSpace(row.SideEffects))
                    {
                        foreach (var name in row.SideEffects.Split('|'))
                        {
                            var trimmed = name.Trim();
                            if (!string.IsNullOrEmpty(trimmed) && !existingSideEffectNames.Contains(trimmed))
                                detectedNewSideEffects.Add(trimmed);
                        }
                    }

                    model.Rows.Add(row);
                }
            }

            model.NewSideEffectsToCreate = detectedNewSideEffects.ToList();
            return View("ReviewImport", model);
        }

        // Mirrors the backup's Escape() — strips surrounding quotes and unescapes doubled quotes
        private static string Unescape(string value)
        {
            value = value.Trim();
            if (value.StartsWith('"') && value.EndsWith('"'))
                value = value[1..^1].Replace("\"\"", "\"");
            return value;
        }   

        [HttpGet]
        public IActionResult Review()
        {
            var json = HttpContext.Session.GetString("ImportData");
            if (string.IsNullOrEmpty(json)) return RedirectToAction("Index", "Home");
            return View(JsonSerializer.Deserialize<ImportViewModel>(json));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Confirm(ImportViewModel model)
        {

            // Check if the data actually made it here
            if (model == null || model.Rows == null)
            {
                TempData["ImportError"] = "Import data was lost. Please try uploading again.";
                return RedirectToAction("Index", "Home");
            }
            var masterSideEffects = await _context.SideEffects.ToListAsync();

            // 1. Create new side effects if needed
            if (model.ConfirmNewSideEffects && model.NewSideEffectsToCreate != null)
            {
                foreach (var name in model.NewSideEffectsToCreate)
                {
                    if (!masterSideEffects.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        var newSe = new SideEffect { Name = name };
                        _context.SideEffects.Add(newSe);
                        masterSideEffects.Add(newSe);
                    }
                }
                await _context.SaveChangesAsync();
            }

            int count = 0;
            foreach (var row in model.Rows.Where(r => r.DuplicateAction != "skip"))
            {
                DateOnly.TryParse(row.DateOfBirth, CultureInfo.InvariantCulture, out var dob);

                // 2. Find or Create Patient 
                // We check Database first, then we check the 'Local' memory tracker
                var patient = await _context.Patients.FirstOrDefaultAsync(p => p.Firstname == row.Firstname && p.Surname == row.Surname && p.DateOfBirth == dob);

                if (patient == null)
                {
                    patient = _context.Patients.Local.FirstOrDefault(p => p.Firstname == row.Firstname && p.Surname == row.Surname && p.DateOfBirth == dob);
                }

                if (patient == null)
                {
                    patient = new Patient
                    {
                        Firstname = row.Firstname,
                        Surname = row.Surname,
                        DateOfBirth = dob,
                        Email = row.Email,
                        StartingWeight = double.Parse(row.StartingWeight, CultureInfo.InvariantCulture),
                        TreatmentStartDate = DateOnly.Parse(row.TreatmentStartDate, CultureInfo.InvariantCulture)
                    };
                    _context.Patients.Add(patient);
                }

                // 3. Create the Report
                var report = new Report
                {
                    Patient = patient, // EF links the ID automatically
                    ReportNumber = int.Parse(row.ReportNumber),
                    WeekNumber = int.Parse(row.WeekNumber),
                    Date = DateOnly.Parse(row.Date, CultureInfo.InvariantCulture),
                    Weight = double.Parse(row.Weight, CultureInfo.InvariantCulture),
                    Dosage = decimal.Parse(row.Dosage, CultureInfo.InvariantCulture),
                    WeightChange = double.Parse(row.WeightChange, CultureInfo.InvariantCulture),
                    Notes = row.Notes ?? string.Empty
                };

                // 4. Link Side Effects (Handles the | pipe)
                if (!string.IsNullOrEmpty(row.SideEffects))
                {
                    var seNames = row.SideEffects.Split('|', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var name in seNames)
                    {
                        var se = masterSideEffects.FirstOrDefault(s => s.Name.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
                        if (se != null) report.SideEffects.Add(se);
                    }
                }

                _context.Reports.Add(report);
                count++;
            }

            await _context.SaveChangesAsync();
            HttpContext.Session.Remove("ImportData");
            TempData["ImportSuccess"] = $"Processed {count} entries successfully.";
            return RedirectToAction("Index", "Home");
        }

        private List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var current = new System.Text.StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\"') inQuotes = !inQuotes;
                else if (c == ',' && !inQuotes) { result.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }
            result.Add(current.ToString());
            return result;
        }

        private void ValidateRow(CsvRowViewModel row)
        {
            if (string.IsNullOrEmpty(row.Firstname)) row.Errors.Add("Name required");
            if (!double.TryParse(row.Weight, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) row.Errors.Add("Weight error");
        }

        private async Task CheckDuplicate(CsvRowViewModel row)
        {
            if (!DateOnly.TryParse(row.DateOfBirth, CultureInfo.InvariantCulture, out var dob)) return;
            var existing = await _context.Patients.AnyAsync(p => p.Firstname == row.Firstname && p.Surname == row.Surname && p.DateOfBirth == dob);
            if (existing) { row.IsDuplicate = true; row.DuplicateAction = "skip"; }
        }

        private async Task CheckNewSideEffects(CsvRowViewModel row)
        {
            if (string.IsNullOrEmpty(row.SideEffects)) return;
            foreach (var n in row.SideEffects.Split('|'))
            {
                var trimmed = n.Trim();
                if (!await _context.SideEffects.AnyAsync(s => s.Name == trimmed)) row.NewSideEffects.Add(trimmed);
            }
        }
    }
}