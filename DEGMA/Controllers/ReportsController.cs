using DEGMA.Data;
using DEGMA.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace DEGMA.Controllers
{
    [Authorize]
    public class ReportsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ReportsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Reports
        public async Task<IActionResult> Index()
        {
            // We added .Include(r => r.Patient) to load the patient's data
            var applicationDbContext = _context.Reports.Include(r => r.Patient);
            return View(await applicationDbContext.ToListAsync());
        }

        // GET: Reports/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var report = await _context.Reports
                .Include(r => r.Patient)
                .FirstOrDefaultAsync(m => m.ReportId == id);
            if (report == null)
            {
                return NotFound();
            }

            return View(report);
        }

        // GET: Reports/Create
        [Authorize(Roles = "Doctor")]
        public IActionResult Create()
        {
            // This sends your patients to the dropdown (already there)
            ViewData["PatientId"] = new SelectList(
                _context.Patients.Select(p => new
                {
                    p.PatientId,
                    FullName = p.Firstname + " " + p.Surname
                }),
                "PatientId",
                "FullName"
            );

            // ADD THIS LINE: This sends your Side Effects dictionary to the screen
            ViewBag.AllSideEffects = _context.SideEffects.ToList();

            return View();
        }

        // POST: Reports/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> Create([Bind("ReportId,ReportNumber,WeekNumber,Weight,Dosage,WeightChange,Notes,PatientId")] Report report, int[] selectedSideEffects)
        {
            foreach (var key in ModelState.Keys)
            {
                var state = ModelState[key];
                foreach (var error in state.Errors)
                {
                    Console.WriteLine($"Field: {key}, Error: {error.ErrorMessage}");
                }
            }
            if (ModelState.IsValid)
            {
                var patient = await _context.Patients
                    .Include(p => p.Reports)
                    .FirstOrDefaultAsync(p => p.PatientId == report.PatientId);

                if (patient != null)
                {
                    var lastReport = patient.Reports.OrderByDescending(r => r.WeekNumber).FirstOrDefault();
                    var baseline = lastReport?.Weight ?? patient.StartingWeight;
                    report.WeightChange = report.Weight - baseline;
                }
                if (selectedSideEffects != null)
                {
                    foreach (var sideEffectId in selectedSideEffects)
                    {
                        var sideEffect = await _context.SideEffects.FindAsync(sideEffectId);
                        if (sideEffect != null)
                        {
                            report.SideEffects.Add(sideEffect); // Link them to this report
                        }
                    }
                }

                // 2. Save to database
                _context.Add(report);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
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
            return View(report);
        }

        // GET: Reports/Edit/5
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var report = await _context.Reports.FindAsync(id);
            if (report == null)
            {
                return NotFound();
            }
            ViewData["patientId"] = new SelectList(_context.Patients, "PatientId", "PatientId", report.PatientId);
            return View(report);
        }

        // POST: Reports/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> Edit(int id, [Bind("ReportId,ReportNumber,WeekNumber,Weight,Dosage,WeightChange,Notes,patientId")] Report report)
        {
            if (id != report.ReportId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(report);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ReportExists(report.ReportId))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            ViewData["patientId"] = new SelectList(_context.Patients, "PatientId", "PatientId", report.PatientId);
            return View(report);
        }

        // GET: Reports/Delete/5
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var report = await _context.Reports
                .Include(r => r.Patient)
                .FirstOrDefaultAsync(m => m.ReportId == id);
            if (report == null)
            {
                return NotFound();
            }

            return View(report);
        }

        // POST: Reports/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var report = await _context.Reports.FindAsync(id);
            if (report != null)
            {
                _context.Reports.Remove(report);
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

            // Get the most recent report's weight, or starting weight if no reports yet
            var lastWeight = patient.Reports.Any()
                ? patient.Reports.OrderByDescending(r => r.WeekNumber).First().Weight
                : patient.StartingWeight;

            return Json(new
            {
                nextReportNumber = patient.Reports.Count + 1,
                startDate = patient.TreatmentStartDate.ToString("yyyy-MM-dd"),
                startingWeight = patient.StartingWeight,
                lastWeight = lastWeight
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
