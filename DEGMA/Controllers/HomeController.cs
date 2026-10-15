using DEGMA.Data;
using DEGMA.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

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
            var reports = await _context.Reports.Include(r => r.SideEffects).Include(r => r.Patient).ToListAsync();
            var patients = await _context.Patients.ToListAsync();

            var viewModel = new DashboardViewModel
            {
                TotalPatients = patients.Count,
                TotalReports = reports.Count,

                // Average Loss (WeightChange is negative for loss, so we multiply by -1 for display)
                AverageWeightLoss = reports.Any() ? reports.Average(r => r.WeightChange) * -1 : 0,

                // 1. Total Dosage
                TotalDosageAdministered = Math.Round((double)reports.Sum(r => r.Dosage)),

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

            ViewBag.CurrentBatchNumber = _configuration["ClinicSettings:CurrentBatchNumber"];
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
            var appSettingsPath = Path.Combine(_env.ContentRootPath, "appsettings.json");
            var json = System.IO.File.ReadAllText(appSettingsPath);
            var node = JsonNode.Parse(json)!;

            node["ClinicSettings"]!["CurrentBatchNumber"] = batchNumber;

            System.IO.File.WriteAllText(appSettingsPath,
                node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            // Reload configuration so it takes effect immediately
            _configuration["ClinicSettings:CurrentBatchNumber"] = batchNumber;

            TempData["BatchSuccess"] = $"Batch number updated to {batchNumber}.";
            return RedirectToAction("Index");
        }
    }
}
