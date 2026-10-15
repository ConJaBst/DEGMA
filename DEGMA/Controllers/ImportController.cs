using DEGMA.Data;
using DEGMA.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DEGMA.Controllers
{
    public class ImportController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;
        private const int PageSize = ImportViewModel.PageSize;

        public ImportController(ApplicationDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // --- UPLOAD: Parse entire CSV, save to temp file, redirect to review ---
        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile csvFile)
        {
            if (csvFile == null || csvFile.Length == 0)
            {
                TempData["ImportError"] = "Please select a CSV file.";
                return RedirectToAction("Index", "Home");
            }

            var existingSideEffectNames = await _context.SideEffects
                .Select(s => s.Name).ToListAsync();
            var detectedNewSideEffects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allRows = new List<CsvRowViewModel>();

            using var reader = new StreamReader(csvFile.OpenReadStream());
            await reader.ReadLineAsync(); // skip header
            int index = 0;

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = ParseCsvLine(line);
                if (parts.Count < 15) continue;

                var row = new CsvRowViewModel
                {
                    RowIndex = index++,
                    Firstname = parts[1].Trim(),
                    Surname = parts[2].Trim(),
                    DateOfBirth = parts[3].Trim(),
                    Email = parts[4].Trim(),
                    StartingWeight = parts[5].Trim(),
                    TreatmentStartDate = parts[6].Trim(),
                    ReportNumber = parts[8].Trim(),
                    WeekNumber = parts[9].Trim(),
                    Date = parts[10].Trim(),
                    Weight = parts[11].Trim(),
                    Dosage = parts[12].Trim(),
                    WeightChange = parts[13].Trim(),
                    Notes = parts[14].Trim(),
                    SideEffects = parts.Count > 15 ? parts[15].Trim() : "",
                    VisceralFat = parts.Count > 16 ? parts[16].Trim() : "",
                    MuscleMass = parts.Count > 17 ? parts[17].Trim() : "",
                    BatchNumber = parts.Count > 18 ? parts[18].Trim() : ""
                };

                ValidateRow(row);
                await CheckDuplicate(row);

                if (!string.IsNullOrWhiteSpace(row.SideEffects))
                {
                    foreach (var name in row.SideEffects.Split('|'))
                    {
                        var trimmed = name.Trim();
                        if (!string.IsNullOrEmpty(trimmed) &&
                            !existingSideEffectNames.Contains(trimmed))
                            detectedNewSideEffects.Add(trimmed);
                    }
                }

                allRows.Add(row);
            }

            // Save everything to a temp file so we don't hold it in memory/session
            var tempId = Guid.NewGuid().ToString("N");
            var tempData = new ImportTempData
            {
                AllRows = allRows,
                NewSideEffectsToCreate = detectedNewSideEffects.ToList()
            };

            await SaveTempFile(tempId, tempData);

            return RedirectToAction("Review", new { tempId, page = 1 });
        }

        // --- REVIEW: Paginated review page ---
        [HttpGet]
        public async Task<IActionResult> Review(string tempId, int page = 1)
        {
            var tempData = await LoadTempFile(tempId);
            if (tempData == null)
            {
                TempData["ImportError"] = "Import session expired. Please upload again.";
                return RedirectToAction("Index", "Home");
            }

            var allRows = tempData.AllRows;
            int totalPages = (int)Math.Ceiling(allRows.Count / (double)PageSize);
            page = Math.Clamp(page, 1, Math.Max(1, totalPages));

            var pageRows = allRows
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .ToList();

            var model = new ImportViewModel
            {
                TempFileId = tempId,
                CurrentPage = page,
                TotalPages = totalPages,
                TotalRows = allRows.Count,
                InvalidRows = allRows.Count(r => !r.IsValid),
                DuplicateRows = allRows.Count(r => r.IsDuplicate),
                Rows = pageRows,
                NewSideEffectsToCreate = tempData.NewSideEffectsToCreate
            };

            return View("ReviewImport", model);
        }

        // --- SAVE PAGE: Save edits from current page back to temp file ---
        [HttpPost]
        public async Task<IActionResult> SavePage(ImportViewModel model, int nextPage)
        {
            var tempData = await LoadTempFile(model.TempFileId);
            if (tempData == null)
            {
                TempData["ImportError"] = "Import session expired. Please upload again.";
                return RedirectToAction("Index", "Home");
            }

            // Merge edited rows back into the full list
            foreach (var editedRow in model.Rows)
            {
                var existing = tempData.AllRows
                    .FirstOrDefault(r => r.RowIndex == editedRow.RowIndex);

                if (existing == null) continue;

                // Update all editable fields
                existing.Firstname = editedRow.Firstname;
                existing.Surname = editedRow.Surname;
                existing.DateOfBirth = editedRow.DateOfBirth;
                existing.Email = editedRow.Email;
                existing.StartingWeight = editedRow.StartingWeight;
                existing.TreatmentStartDate = editedRow.TreatmentStartDate;
                existing.WeekNumber = editedRow.WeekNumber;
                existing.Date = editedRow.Date;
                existing.Weight = editedRow.Weight;
                existing.Dosage = editedRow.Dosage;
                existing.SideEffects = editedRow.SideEffects;
                existing.Notes = editedRow.Notes;
                existing.DuplicateAction = editedRow.DuplicateAction;

                // Re-validate after edits
                existing.Errors.Clear();
                ValidateRow(existing);
            }

            // Save new side effects confirmation
            tempData.NewSideEffectsToCreate = model.NewSideEffectsToCreate ?? new();

            await SaveTempFile(model.TempFileId, tempData);

            // If nextPage is 0 it means "Confirm Import"
            if (nextPage == 0)
                return RedirectToAction("ConfirmAll",
                    new
                    {
                        tempId = model.TempFileId,
                        confirmSideEffects = model.ConfirmNewSideEffects
                    });

            return RedirectToAction("Review",
                new { tempId = model.TempFileId, page = nextPage });
        }

        [HttpGet]
        public async Task<IActionResult> ConfirmAll(string tempId, bool confirmSideEffects)
        {
            var tempData = await LoadTempFile(tempId);
            if (tempData == null)
            {
                TempData["ImportError"] = "Import session expired. Please upload again.";
                return RedirectToAction("Index", "Home");
            }

            var masterSideEffects = await _context.SideEffects.ToListAsync();

            // Create new side effects if confirmed
            if (confirmSideEffects && tempData.NewSideEffectsToCreate.Any())
            {
                foreach (var name in tempData.NewSideEffectsToCreate)
                {
                    if (!masterSideEffects.Any(s =>
                        s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        var newSe = new SideEffect { Name = name };
                        _context.SideEffects.Add(newSe);
                        masterSideEffects.Add(newSe);
                    }
                }
                await _context.SaveChangesAsync();
            }

            int imported = 0;
            var skippedRows = new List<(int RowIndex, string Reason)>();

            var rowsToProcess = tempData.AllRows
                .Where(r => r.DuplicateAction != "skip")
                .ToList();

            foreach (var chunk in rowsToProcess.Chunk(100))
            {
                foreach (var row in chunk)
                {
                    // Skip invalid rows
                    if (!row.IsValid)
                    {
                        skippedRows.Add((row.RowIndex, string.Join(", ", row.Errors)));
                        continue;
                    }

                    // Parse all fields
                    if (!DateOnly.TryParse(row.DateOfBirth, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var dob)) continue;
                    if (!DateOnly.TryParse(row.TreatmentStartDate, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var treatStart)) continue;
                    if (!double.TryParse(row.StartingWeight, NumberStyles.Any,
                        CultureInfo.InvariantCulture, out var startingWeight)) continue;
                    if (!DateOnly.TryParse(row.Date, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var reportDate)) continue;
                    if (!double.TryParse(row.Weight, NumberStyles.Any,
                        CultureInfo.InvariantCulture, out var weight)) continue;
                    if (!double.TryParse(row.Dosage, NumberStyles.Any,
                        CultureInfo.InvariantCulture, out var dosage)) continue;
                    if (!double.TryParse(row.WeightChange, NumberStyles.Any,
                        CultureInfo.InvariantCulture, out var weightChange)) continue;
                    if (!double.TryParse(row.WeekNumber, NumberStyles.Any,
                        CultureInfo.InvariantCulture, out var weekNumD)) continue;

                    // Find or create patient
                    Patient patient = null;

                    if (row.DuplicateAction == "overwrite" && row.ExistingPatientId.HasValue)
                    {
                        patient = await _context.Patients.FirstOrDefaultAsync(p =>
                            p.PatientId == row.ExistingPatientId.Value);

                        if (patient != null)
                        {
                            patient.Firstname = row.Firstname;
                            patient.Surname = row.Surname;
                            patient.DateOfBirth = dob;
                            patient.Email = row.Email;
                            patient.StartingWeight = startingWeight;
                            patient.TreatmentStartDate = treatStart;
                        }
                    }

                    if (patient == null)
                    {
                        patient = await _context.Patients.FirstOrDefaultAsync(p =>
                            p.Firstname == row.Firstname &&
                            p.Surname == row.Surname &&
                            p.DateOfBirth == dob)
                            ?? _context.Patients.Local.FirstOrDefault(p =>
                                p.Firstname == row.Firstname &&
                                p.Surname == row.Surname &&
                                p.DateOfBirth == dob);
                    }

                    if (patient == null)
                    {
                        patient = new Patient
                        {
                            Firstname = row.Firstname,
                            Surname = row.Surname,
                            DateOfBirth = dob,
                            Email = row.Email,
                            StartingWeight = startingWeight,
                            TreatmentStartDate = treatStart
                        };
                        _context.Patients.Add(patient);
                        await _context.SaveChangesAsync(); // get PatientId before linking reports
                    }

                    // Always calculate ReportNumber — ignore CSV value
                    int reportNum = await _context.Reports
                        .CountAsync(r => r.PatientId == patient.PatientId) + 1;

                    // Parse optional fields
                    decimal? visceralFat = null;
                    if (decimal.TryParse(row.VisceralFat, NumberStyles.Any,
                        CultureInfo.InvariantCulture, out var vf))
                        visceralFat = vf;

                    decimal? muscleMass = null;
                    if (decimal.TryParse(row.MuscleMass, NumberStyles.Any,
                        CultureInfo.InvariantCulture, out var mm))
                        muscleMass = mm;

                    var report = new Report
                    {
                        PatientId = patient.PatientId,
                        ReportNumber = reportNum,
                        WeekNumber = (int)weekNumD,
                        Date = reportDate,
                        Weight = weight,
                        Dosage = (decimal)dosage,
                        WeightChange = weightChange,
                        Notes = row.Notes ?? string.Empty,
                        VisceralFat = visceralFat,
                        MuscleMass = muscleMass,
                        BatchNumber = string.IsNullOrWhiteSpace(row.BatchNumber) ? null : row.BatchNumber
                    };

                    // Link side effects
                    if (!string.IsNullOrWhiteSpace(row.SideEffects))
                    {
                        foreach (var name in row.SideEffects.Split('|',
                            StringSplitOptions.RemoveEmptyEntries))
                        {
                            var se = masterSideEffects.FirstOrDefault(s =>
                                s.Name.Trim().Equals(name.Trim(),
                                    StringComparison.OrdinalIgnoreCase));
                            if (se != null) report.SideEffects.Add(se);
                        }
                    }

                    _context.Reports.Add(report);
                    imported++;
                }

                // Save each chunk independently
                await _context.SaveChangesAsync();
            }

            // Clean up temp file
            DeleteTempFile(tempId);

            TempData["ImportSuccess"] = $"Successfully imported {imported} reports.";

            if (skippedRows.Any())
            {
                TempData["ImportSkipped"] = skippedRows.Count;
                TempData["ImportSkippedDetails"] = JsonSerializer.Serialize(skippedRows
                    .Take(50)
                    .Select(s => $"Row {s.RowIndex + 1}: {s.Reason}")
                    .ToList());
            }

            return RedirectToAction("Index", "Home");
        }

        // --- Temp file helpers ---
        private string GetTempPath(string tempId) =>
            Path.Combine(_env.ContentRootPath, "App_Data", "ImportTemp", $"{tempId}.json");

        private async Task SaveTempFile(string tempId, ImportTempData data)
        {
            var path = GetTempPath(tempId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await System.IO.File.WriteAllTextAsync(path,
                JsonSerializer.Serialize(data));
        }

        private async Task<ImportTempData?> LoadTempFile(string tempId)
        {
            var path = GetTempPath(tempId);
            if (!System.IO.File.Exists(path)) return null;
            var json = await System.IO.File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<ImportTempData>(json);
        }

        private void DeleteTempFile(string tempId)
        {
            var path = GetTempPath(tempId);
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }

        // --- Validation & helpers ---
        private void ValidateRow(CsvRowViewModel row)
        {
            if (string.IsNullOrWhiteSpace(row.Firstname))
                row.Errors.Add("First name is missing.");
            if (string.IsNullOrWhiteSpace(row.Surname))
                row.Errors.Add("Surname is missing.");
            if (!DateOnly.TryParse(row.DateOfBirth, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _))
                row.Errors.Add("Date of birth is invalid.");
            if (string.IsNullOrWhiteSpace(row.Email) || !row.Email.Contains('@'))
                row.Errors.Add("Email is missing or invalid.");
            if (!double.TryParse(row.StartingWeight, NumberStyles.Any,
                CultureInfo.InvariantCulture, out var sw) || sw <= 0)
                row.Errors.Add("Starting weight is invalid.");
            if (!DateOnly.TryParse(row.TreatmentStartDate, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _))
                row.Errors.Add("Treatment start date is invalid.");
            if (!double.TryParse(row.Weight, NumberStyles.Any,
                CultureInfo.InvariantCulture, out var w) || w <= 0)
                row.Errors.Add("Weight is invalid.");
            if (!double.TryParse(row.Dosage, NumberStyles.Any,
                CultureInfo.InvariantCulture, out var d) || d <= 0)
                row.Errors.Add("Dosage is invalid.");
            if (!double.TryParse(row.WeekNumber, NumberStyles.Any,
                CultureInfo.InvariantCulture, out var wn) || wn <= 0)
                row.Errors.Add("Week number is invalid.");
            //if (!double.TryParse(row.ReportNumber, NumberStyles.Any,
            //    CultureInfo.InvariantCulture, out var rn) || rn <= 0)
            //    row.Errors.Add("Report number is invalid.");
            if (!DateOnly.TryParse(row.Date, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var reportDate))
                row.Errors.Add("Report date is invalid.");
            else if (DateOnly.TryParse(row.TreatmentStartDate, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var treatStart) && reportDate < treatStart)
                row.Errors.Add("Report date is before treatment start date.");
        }

        private async Task CheckDuplicate(CsvRowViewModel row)
        {
            if (!DateOnly.TryParse(row.DateOfBirth, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dob)) return;

            var existing = await _context.Patients.FirstOrDefaultAsync(p =>
                p.Firstname == row.Firstname &&
                p.Surname == row.Surname &&
                p.DateOfBirth == dob);

            if (existing != null)
            {
                row.IsDuplicate = true;
                row.ExistingPatientId = existing.PatientId;
                row.DuplicateAction = "skip";
            }
        }

        private List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    { current.Append('"'); i++; }
                    else inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                { result.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }

            result.Add(current.ToString());
            return result;
        }
    }
}