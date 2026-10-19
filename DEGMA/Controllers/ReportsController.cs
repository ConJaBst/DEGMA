using DEGMA.Data;
using DEGMA.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DEGMA.Controllers
{
    [Authorize]
    public class ReportsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ClinicSettings _clinicSettings;
        private readonly ReportCheckSettings _checks;

        // Reports page shows this many days (that have reports) at a time
        private const int DaysPerPage = 7;
        private const int MaxRows = 1500;

        public ReportsController(ApplicationDbContext context, IOptions<ClinicSettings> clinicSettings,
            IOptions<ReportCheckSettings> checks)
        {
            _context = context;
            _clinicSettings = clinicSettings.Value;
            _checks = checks.Value;
        }
        

        // GET: Reports
        // Review list: every report in a date range, newest first, grouped by day,
        // with values that look like typos or out-of-range vitals flagged.
        // With no dates given it shows the most recent days that have reports.
        public async Task<IActionResult> Index(DateOnly? from, DateOnly? to, bool flagged = false)
        {
            var vm = new ReportReviewViewModel { FlaggedOnly = flagged, Checks = _checks, MaxRows = MaxRows };

            var dates = await _context.Reports
                .Select(r => r.Date)
                .Distinct()
                .OrderByDescending(d => d)
                .ToListAsync();

            if (dates.Count == 0) return View(vm);

            if (from.HasValue && to.HasValue && from > to) (from, to) = (to, from);

            if (!from.HasValue && !to.HasValue) to = dates[0];

            if (!from.HasValue)
            {
                // The DaysPerPage report days ending at "to"
                var window = dates.Where(d => d <= to).Take(DaysPerPage).ToList();
                from = window.Count > 0 ? window[^1] : to;
            }
            else if (!to.HasValue)
            {
                // The DaysPerPage report days starting at "from"
                var window = dates.Where(d => d >= from).OrderBy(d => d).Take(DaysPerPage).ToList();
                to = window.Count > 0 ? window[^1] : from;
            }

            vm.From = from;
            vm.To = to;
            vm.OlderTo = dates.Where(d => d < from).Select(d => (DateOnly?)d).FirstOrDefault();
            vm.NewerFrom = dates.Where(d => d > to).Select(d => (DateOnly?)d).LastOrDefault();

            var reports = await _context.Reports
                .Include(r => r.Patient)
                .Include(r => r.SideEffects)
                .Where(r => r.Date >= from && r.Date <= to)
                .OrderByDescending(r => r.Date)
                .ThenByDescending(r => r.ReportId)
                .Take(MaxRows + 1)
                .AsSplitQuery()
                .ToListAsync();

            if (reports.Count > MaxRows)
            {
                vm.Truncated = true;
                reports.RemoveAt(reports.Count - 1);
            }

            // Each patient's earlier reports, to compare against the previous visit
            var patientIds = reports.Select(r => r.PatientId).Distinct().ToList();
            var history = (await _context.Reports
                    .Where(r => patientIds.Contains(r.PatientId) && r.Date <= to)
                    .Select(r => new { r.ReportId, r.PatientId, r.Date, r.ReportNumber, r.Weight, r.VisceralFat, r.MuscleMass })
                    .ToListAsync())
                .GroupBy(h => h.PatientId)
                .ToDictionary(g => g.Key, g => g
                    .OrderBy(h => h.Date).ThenBy(h => h.ReportNumber).ThenBy(h => h.ReportId)
                    .ToList());

            var rows = new List<ReportReviewRow>();
            foreach (var report in reports)
            {
                var earlier = history[report.PatientId]
                    .TakeWhile(h => h.ReportId != report.ReportId)
                    .ToList();
                var previous = earlier.LastOrDefault();

                var row = new ReportReviewRow
                {
                    Report = report,
                    WeightChangeFromStart = previous == null,
                    WeightChange = report.Weight - (previous?.Weight ?? report.Patient?.StartingWeight ?? report.Weight),
                    Pulse = _checks.PulseStatus(report.Pulse),
                    BloodPressure = _checks.BloodPressureStatus(report.SystolicBp, report.DiastolicBp)
                };

                // Body composition is not measured every visit: compare with the last visit that has a value
                var lastVisceral = earlier.LastOrDefault(h => h.VisceralFat.HasValue)?.VisceralFat;
                var lastMuscle = earlier.LastOrDefault(h => h.MuscleMass.HasValue)?.MuscleMass;
                row.VisceralFatChange = report.VisceralFat.HasValue && lastVisceral.HasValue ? report.VisceralFat - lastVisceral : null;
                row.MuscleMassChange = report.MuscleMass.HasValue && lastMuscle.HasValue ? report.MuscleMass - lastMuscle : null;

                row.WeightFlag = _checks.IsWeightJump(row.WeightChange);
                row.VisceralFatRangeFlag = _checks.IsVisceralFatOutOfRange(report.VisceralFat);
                row.VisceralFatJumpFlag = _checks.IsVisceralFatJump(row.VisceralFatChange);
                row.MuscleMassRangeFlag = _checks.IsMuscleMassOutOfRange(report.MuscleMass);
                row.MuscleMassJumpFlag = _checks.IsMuscleMassJump(row.MuscleMassChange);

                rows.Add(row);
            }

            vm.TotalReports = rows.Count;
            vm.FlaggedReports = rows.Count(r => r.IsFlagged);

            vm.Days = rows
                .Where(r => !flagged || r.IsFlagged)
                .GroupBy(r => r.Report.Date)
                .Select(g => new ReportReviewDay { Date = g.Key, Rows = g.ToList() })
                .ToList();

            return View(vm);
        }

        // GET: Reports/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var report = await _context.Reports
                .Include(r => r.Patient)
                .FirstOrDefaultAsync(m => m.ReportId == id);

            if (report == null) return NotFound();

            return View(report);
        }

        // GET: Reports/Create
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> Create(int? patientId)
        {
            ViewData["PatientId"] = new SelectList(
                _context.Patients.Select(p => new
                {
                    p.PatientId,
                    FullName = p.Firstname + " " + p.Surname
                }),
                "PatientId",
                "FullName",
                patientId
            );

            ViewBag.AllSideEffects = _context.SideEffects.ToList();
            ViewBag.PreselectedPatientId = patientId;
            ViewBag.CurrentBatchNumber = _clinicSettings.CurrentBatchNumber;

            if (patientId.HasValue)
            {
                var lastReport = await _context.Reports
                    .Where(r => r.PatientId == patientId.Value)
                    .OrderByDescending(r => r.Date)
                    .FirstOrDefaultAsync();

                ViewBag.LastDosage = lastReport?.Dosage;
            }

            return View();
        }

        // POST: Reports/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> Create(
            [Bind("ReportId,ReportNumber,WeekNumber,Weight,Dosage,Notes,PatientId,Date,VisceralFat,MuscleMass,Pulse,SystolicBp,DiastolicBp,BatchNumber")]
            Report report, int[] selectedSideEffects, string? otherSideEffects, int? returnPatientId)
        {
            ModelState.Remove("WeightChange");
            ModelState.Remove("Notes");
            ModelState.Remove("Patient");

            if (report.Date == DateOnly.MinValue)
                report.Date = DateOnly.FromDateTime(DateTime.Now);

            if (ModelState.IsValid)
            {
                var patient = await _context.Patients
                    .Include(p => p.Reports)
                    .FirstOrDefaultAsync(p => p.PatientId == report.PatientId);

                if (patient != null)
                {
                    var previousReport = patient.Reports
                        .Where(r => r.Date < report.Date)
                        .OrderByDescending(r => r.Date)
                        .FirstOrDefault();

                    var baseline = previousReport?.Weight ?? patient.StartingWeight;
                    report.WeightChange = report.Weight - baseline;

                    report.ReportNumber = patient.Reports
                        .Count(r => r.Date <= report.Date) + 1;
                }

                report.SideEffects.AddRange(
                    await ResolveSideEffectsAsync(selectedSideEffects, otherSideEffects));

                _context.Add(report);
                await _context.SaveChangesAsync();

                var targetPatientId = returnPatientId ?? report.PatientId;
                return RedirectToAction("Details", "Patients", new { id = targetPatientId });
            }

            foreach (var key in ModelState.Keys)
                foreach (var error in ModelState[key].Errors)
                    Console.WriteLine($"Field: {key}, Error: {error.ErrorMessage}");

            ViewData["PatientId"] = new SelectList(
                _context.Patients.Select(p => new {
                    p.PatientId,
                    FullName = p.Firstname + " " + p.Surname
                }),
                "PatientId",
                "FullName",
                report.PatientId
            );
            ViewBag.AllSideEffects = _context.SideEffects.ToList();
            ViewBag.SelectedSideEffectIds = selectedSideEffects ?? Array.Empty<int>();
            ViewBag.OtherSideEffects = otherSideEffects;
            ViewBag.PreselectedPatientId = returnPatientId;

            if (returnPatientId.HasValue)
            {
                var lastReport = await _context.Reports
                    .Where(r => r.PatientId == returnPatientId.Value)
                    .OrderByDescending(r => r.Date)
                    .FirstOrDefaultAsync();
                ViewBag.LastDosage = lastReport?.Dosage;
            }

            return View(report);
        }

        // GET: Reports/Edit/5
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var report = await _context.Reports
                .Include(r => r.SideEffects)
                .Include(r => r.Patient)
                .FirstOrDefaultAsync(r => r.ReportId == id);

            if (report == null) return NotFound();

            ViewBag.AllSideEffects = await _context.SideEffects.ToListAsync();
            ViewBag.CurrentBatchNumber = _clinicSettings.CurrentBatchNumber;

            var previousReport = await _context.Reports
                .Where(r => r.PatientId == report.PatientId
                         && r.Date < report.Date
                         && r.ReportId != report.ReportId)
                .OrderByDescending(r => r.Date)
                .FirstOrDefaultAsync();

            ViewBag.PreviousVisceralFat = previousReport?.VisceralFat;
            ViewBag.PreviousMuscleMass = previousReport?.MuscleMass;

            if (report.VisceralFat == null)
                report.VisceralFat = previousReport?.VisceralFat;
            if (report.MuscleMass == null)
                report.MuscleMass = previousReport?.MuscleMass;

            return View(report);
        }

        // POST: Reports/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> Edit(int id,
            [Bind("ReportId,ReportNumber,WeekNumber,Weight,Dosage,WeightChange,Notes,PatientId,Date,VisceralFat,MuscleMass,Pulse,SystolicBp,DiastolicBp,BatchNumber")]
            Report report, int[] selectedSideEffects, string? otherSideEffects)
        {
            if (id != report.ReportId) return NotFound();

            ModelState.Remove("WeightChange");
            ModelState.Remove("Notes");
            ModelState.Remove("Patient");

            if (ModelState.IsValid)
            {
                try
                {
                    var existing = await _context.Reports
                        .Include(r => r.SideEffects)
                        .FirstOrDefaultAsync(r => r.ReportId == id);

                    if (existing == null) return NotFound();

                    var patient = await _context.Patients
                        .Include(p => p.Reports)
                        .FirstOrDefaultAsync(p => p.PatientId == report.PatientId);

                    if (patient != null)
                    {
                        var previousReport = patient.Reports
                            .Where(r => r.Date < report.Date && r.ReportId != report.ReportId)
                            .OrderByDescending(r => r.Date)
                            .FirstOrDefault();

                        var baseline = previousReport?.Weight ?? patient.StartingWeight;
                        existing.WeightChange = report.Weight - baseline;
                    }

                    existing.WeekNumber = report.WeekNumber;
                    existing.Date = report.Date;
                    existing.Weight = report.Weight;
                    existing.Dosage = report.Dosage;
                    existing.Notes = report.Notes;
                    existing.VisceralFat = report.VisceralFat;
                    existing.MuscleMass = report.MuscleMass;
                    existing.Pulse = report.Pulse;
                    existing.SystolicBp = report.SystolicBp;
                    existing.DiastolicBp = report.DiastolicBp;
                    existing.BatchNumber = report.BatchNumber;

                    existing.SideEffects.Clear();
                    existing.SideEffects.AddRange(
                        await ResolveSideEffectsAsync(selectedSideEffects, otherSideEffects));

                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ReportExists(report.ReportId)) return NotFound();
                    else throw;
                }

                return RedirectToAction("Details", "Patients", new { id = report.PatientId });
            }

            ViewBag.AllSideEffects = await _context.SideEffects.ToListAsync();
            ViewBag.SelectedSideEffectIds = selectedSideEffects ?? Array.Empty<int>();
            ViewBag.OtherSideEffects = otherSideEffects;
            return View(report);
        }

        // Turns the posted side effect ids plus the free-text "Other" box (comma-separated)
        // into SideEffect rows. Custom names match existing rows case-insensitively;
        // unknown names are added to the table so they appear in the dropdown next time.
        private async Task<List<SideEffect>> ResolveSideEffectsAsync(int[]? selectedIds, string? otherText)
        {
            var all = await _context.SideEffects.ToListAsync();
            var ids = new HashSet<int>(selectedIds ?? Array.Empty<int>());
            var result = all.Where(s => ids.Contains(s.SideEffectId)).ToList();

            var customNames = (otherText ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var name in customNames)
            {
                var match = all.FirstOrDefault(s => string.Equals(s.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    match = new SideEffect { Name = name };
                    _context.SideEffects.Add(match);
                    all.Add(match);
                }
                if (!result.Contains(match)) result.Add(match);
            }

            return result;
        }

        // GET: Reports/Delete/5
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var report = await _context.Reports
                .Include(r => r.Patient)
                .FirstOrDefaultAsync(m => m.ReportId == id);

            if (report == null) return NotFound();

            return View(report);
        }

        // POST: Reports/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var report = await _context.Reports
                .Include(r => r.SideEffects)
                .FirstOrDefaultAsync(r => r.ReportId == id);

            if (report != null)
            {
                var patientId = report.PatientId;
                report.SideEffects.Clear();
                _context.Reports.Remove(report);
                await _context.SaveChangesAsync();
                return RedirectToAction("Details", "Patients", new { id = patientId });
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool ReportExists(int id)
        {
            return _context.Reports.Any(e => e.ReportId == id);
        }

        [HttpGet]
        public async Task<IActionResult> GetPatientData(int id)
        {
            var patient = await _context.Patients
                .Include(p => p.Reports)
                .FirstOrDefaultAsync(p => p.PatientId == id);

            if (patient == null) return NotFound();

            var lastReport = patient.Reports
                .OrderByDescending(r => r.Date)
                .FirstOrDefault();

            var lastWeight = lastReport?.Weight ?? patient.StartingWeight;

            return Json(new
            {
                nextReportNumber = patient.Reports.Count + 1,
                startDate = patient.TreatmentStartDate.ToString("yyyy-MM-dd"),
                startingWeight = patient.StartingWeight,
                lastWeight = lastWeight,
                lastDosage = lastReport?.Dosage,
                lastVisceralFat = lastReport?.VisceralFat,
                lastMuscleMass = lastReport?.MuscleMass
            });
        }

        [HttpPost]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> ClearData()
        {
            await _context.Database.ExecuteSqlRawAsync("DELETE FROM ReportSideEffect");
            await _context.Database.ExecuteSqlRawAsync("DELETE FROM Reports");
            await _context.Database.ExecuteSqlRawAsync("DELETE FROM Patients");
            await _context.Database.ExecuteSqlRawAsync("DELETE FROM SideEffects");

            return RedirectToAction("Index", "Home");
        }
    }
}