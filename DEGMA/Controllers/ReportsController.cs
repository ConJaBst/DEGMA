using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using DEGMA.Data;
using DEGMA.Models;

namespace DEGMA.Controllers
{
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
        public IActionResult Create()
        {
            // This sends your patients to the dropdown (already there)
            ViewData["PatientId"] = new SelectList(_context.Patients, "PatientId", "Firstname", "Surname");

            // ADD THIS LINE: This sends your Side Effects dictionary to the screen
            ViewBag.AllSideEffects = _context.SideEffects.ToList();

            return View();
        }

        // POST: Reports/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("ReportId,ReportNumber,WeekNumber,Weight,Dosage,WeightChange,Notes,PatientId")] Report report, int[] selectedSideEffects)
        {
            if (ModelState.IsValid)
            {
                        var patient = await _context.Patients.FindAsync(report.PatientId);
                        if (patient != null)
                        {
                            report.WeightChange = report.Weight - patient.StartingWeight;
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
            ViewData["PatientId"] = new SelectList(_context.Patients, "PatientId", "Firstname", report.PatientId);
            ViewBag.AllSideEffects = _context.SideEffects.ToList();
            return View(report);
        }

        // GET: Reports/Edit/5
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
    }
}
