using DEGMA.Data;
using DEGMA.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace DEGMA.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HomeController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var reports = await _context.Reports.Include(r => r.SideEffects).Include(r => r.Patient).ToListAsync();
            var patients = await _context.Patients.ToListAsync();

            var viewModel = new DashboardViewModel
            {
                TotalPatients = patients.Count,
                TotalReports = reports.Count,

                // Average Loss (WeightChange is negative for loss, so we multiply by -1 for display)
                AverageWeightLoss = reports.Any() ? reports.Average(r => r.WeightChange) * -1 : 0,

                // 1. Total Dosage
                TotalDosageAdministered = reports.Sum(r => r.Dosage),

                // 2. Most Common Side Effect
                MostCommonSideEffect = reports.SelectMany(r => r.SideEffects)
                                              .GroupBy(s => s.Name)
                                              .OrderByDescending(g => g.Count())
                                              .Select(g => g.Key).FirstOrDefault() ?? "N/A",

                // 3. Max Individual Loss (Most negative WeightChange)
                MaxIndividualLoss = reports.Any() ? reports.Min(r => r.WeightChange) * -1 : 0,

                // 4. Reports this week (Assumes you added the Date field)
                ReportsThisWeek = reports.Count(r => r.Date >= DateOnly.FromDateTime(DateTime.Now.AddDays(-7))),

                // 5. Non-Responders (Patients whose latest report is >= starting weight)
                PatientsWithZeroLoss = patients.Count(p => p.Reports.Any() && p.Reports.Last().WeightChange >= 0),

                RecentReports = reports.OrderByDescending(r => r.Date).Take(5).ToList()
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
    }
}
