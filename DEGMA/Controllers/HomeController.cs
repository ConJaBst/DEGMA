using DEGMA.Data;
using DEGMA.Models;
using DEGMA.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace DEGMA.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _configuration;

        public HomeController(ApplicationDbContext context,
            IWebHostEnvironment env,
            IConfiguration configuration)
        {
            _context = context;
            _env = env;
            _configuration = configuration;
        }


        [HttpPost]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> WipeData()
        {
            // Remove join table data first (ReportSideEffect)
            var reports = await _context.Reports
                .Include(r => r.SideEffects)
                .ToListAsync();

            foreach (var report in reports)
                report.SideEffects.Clear();

            await _context.SaveChangesAsync();

            // Now remove reports and patients
            _context.Reports.RemoveRange(_context.Reports);
            _context.Patients.RemoveRange(_context.Patients);
            _context.SideEffects.RemoveRange(_context.SideEffects);

            await _context.SaveChangesAsync();

            TempData["ImportSuccess"] = "All patient, report and side effect data wiped successfully.";
            return RedirectToAction("Index");
        }




        public async Task<IActionResult> Index()
        {
            var today = DateOnly.FromDateTime(DateTime.Now);

            var patients = await _context.Patients
                .AsNoTracking()
                .Include(p => p.Reports)
                .ToListAsync();

            // Ignore future-dated reports so "latest" means the latest visit that has happened.
            var visits = patients
                .SelectMany(p => p.Reports.Where(r => r.Date <= today).Select(r => (Patient: p, Report: r)))
                .ToList();

            // Each patient's starting weight and latest recorded weight
            var progress = patients
                .Select(p => new
                {
                    Patient = p,
                    Latest = p.Reports.Where(r => r.Date <= today)
                        .OrderByDescending(r => r.Date).ThenByDescending(r => r.ReportNumber)
                        .FirstOrDefault()
                })
                .Where(x => x.Latest != null && x.Patient.StartingWeight > 0)
                .Select(x => new
                {
                    x.Patient,
                    x.Latest,
                    LossKg = x.Patient.StartingWeight - x.Latest!.Weight,
                    LossPercent = (x.Patient.StartingWeight - x.Latest.Weight) / x.Patient.StartingWeight * 100
                })
                .ToList();

            var activeSince = today.AddDays(-30);
            var active = progress.Where(x => x.Latest!.Date > activeSince).ToList();

            var thisWeek = visits.Count(v => v.Report.Date > today.AddDays(-7));
            var lastWeek = visits.Count(v => v.Report.Date > today.AddDays(-14) && v.Report.Date <= today.AddDays(-7));

            var batch = _configuration["ClinicSettings:CurrentBatchNumber"] ?? "";
            var batchReports = visits.Where(v => v.Report.BatchNumber == batch).Select(v => v.Report).ToList();

            var viewModel = new DashboardViewModel
            {
                TotalPatients = patients.Count,
                ActivePatients = active.Count,
                PatientsWithReports = progress.Count,
                AverageTotalLossKg = progress.Any() ? progress.Average(x => x.LossKg) : 0,
                AverageTotalLossPercent = progress.Any() ? progress.Average(x => x.LossPercent) : 0,
                ReachedFivePercent = progress.Count(x => x.LossPercent >= 5),
                ReachedTenPercent = progress.Count(x => x.LossPercent >= 10),

                VisitsThisWeek = thisWeek,
                VisitsLastWeek = lastWeek,
                VisitsChangePercent = lastWeek > 0 ? (thisWeek - lastWeek) * 100.0 / lastWeek : null,

                // Average % change from starting weight (negative = loss), by weeks since treatment start.
                // Weeks with fewer than 5 patients are left out so one patient can't skew the curve.
                ProgressCurve = visits
                    .Where(v => v.Patient.StartingWeight > 0 && v.Report.Date >= v.Patient.TreatmentStartDate)
                    .GroupBy(v => (v.Report.Date.DayNumber - v.Patient.TreatmentStartDate.DayNumber) / 7)
                    .Where(g => g.Key <= 52)
                    .Select(g => new
                    {
                        Week = g.Key,
                        // One value per patient per week (their last visit that week)
                        PerPatient = g.GroupBy(v => v.Patient.PatientId)
                            .Select(pg => pg.OrderByDescending(v => v.Report.Date).First())
                            .Select(v => (v.Report.Weight - v.Patient.StartingWeight) / v.Patient.StartingWeight * 100)
                            .ToList()
                    })
                    .Where(w => w.PerPatient.Count >= 5)
                    .OrderBy(w => w.Week)
                    .Select(w => new WeekPoint(w.Week, w.PerPatient.Average(), w.PerPatient.Count))
                    .ToList(),

                // Last 12 months, oldest first
                MonthlyActivity = Enumerable.Range(0, 12).Reverse()
                    .Select(i => new DateOnly(today.Year, today.Month, 1).AddMonths(-i))
                    .Select(m => new MonthPoint(
                        m.ToString("MMM yy"),
                        patients.Count(p => p.TreatmentStartDate.Year == m.Year && p.TreatmentStartDate.Month == m.Month),
                        visits.Count(v => v.Report.Date.Year == m.Year && v.Report.Date.Month == m.Month)))
                    .ToList(),

                // Latest dose of every patient who has had a visit
                DoseBreakdown = progress
                    .GroupBy(x => x.Latest!.Dosage)
                    .Select(g => new DoseCount(g.Key, g.Count()))
                    .OrderBy(d => d.Dosage)
                    .ToList(),

                CurrentBatchNumber = batch,
                BatchMgUsed = batchReports.Sum(r => r.Dosage),
                BatchVisits = batchReports.Count,
                BatchFirstUsed = batchReports.Any() ? batchReports.Min(r => r.Date) : null,

                RecentReports = visits
                    .OrderByDescending(v => v.Report.Date).ThenByDescending(v => v.Report.ReportId)
                    .Take(8)
                    .Select(v => { v.Report.Patient = v.Patient; return v.Report; })
                    .ToList()
            };

            return View(viewModel);
        }

        // ... Error and Privacy actions ...
        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [HttpPost]
        [Authorize(Roles = "Doctor")]
        public IActionResult UpdateBatchNumber(string batchNumber)
        {
            SettingsFiles.SetLocalValue(_env.ContentRootPath, "ClinicSettings", "CurrentBatchNumber", batchNumber);

            // Reload configuration so it takes effect immediately
            _configuration["ClinicSettings:CurrentBatchNumber"] = batchNumber;

            TempData["BatchSuccess"] = $"Batch number updated to {batchNumber}.";
            return RedirectToAction("Index");
        }
    }
}
