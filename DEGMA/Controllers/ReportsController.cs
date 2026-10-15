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

        public ReportsController(ApplicationDbContext context, IOptions<ClinicSettings> clinicSettings)
        {
            _context = context;
            _clinicSettings = clinicSettings.Value;
        }
        

        // GET: Reports
        public async Task<IActionResult> Index()
        {
            var applicationDbContext = _context.Reports.Include(r => r.Patient);
            return View(await applicationDbContext.ToListAsync());
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
            [Bind("ReportId,ReportNumber,WeekNumber,Weight,Dosage,Notes,PatientId,Date,VisceralFat,MuscleMass,BatchNumber")]
            Report report, int[] selectedSideEffects, int? returnPatientId)
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

                if (selectedSideEffects != null)
                {
                    foreach (var sideEffectId in selectedSideEffects)
                    {
                        var sideEffect = await _context.SideEffects.FindAsync(sideEffectId);
                        if (sideEffect != null)
                            report.SideEffects.Add(sideEffect);
                    }
                }

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
            [Bind("ReportId,ReportNumber,WeekNumber,Weight,Dosage,WeightChange,Notes,PatientId,Date,VisceralFat,MuscleMass,BatchNumber")]
            Report report, int[] selectedSideEffects)
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
                    existing.BatchNumber = report.BatchNumber;

                    existing.SideEffects.Clear();
                    if (selectedSideEffects != null)
                    {
                        foreach (var seId in selectedSideEffects)
                        {
                            var se = await _context.SideEffects.FindAsync(seId);
                            if (se != null) existing.SideEffects.Add(se);
                        }
                    }

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
            return View(report);
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