using DEGMA.Data;
using DEGMA.Models;
using DEGMA.Services;
using DinkToPdf;
using DinkToPdf.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;

namespace DEGMA.Controllers
{
    [Authorize]
    public class PatientsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IConverter _pdfConverter;
        private readonly ICompositeViewEngine _viewEngine;
        private readonly ITempDataProvider _tempDataProvider;
        private readonly EmailService _emailService;

        public PatientsController(ApplicationDbContext context, IConverter pdfConverter,
            ICompositeViewEngine viewEngine, ITempDataProvider tempDataProvider,
            EmailService emailService)
        {
            _context = context;
            _pdfConverter = pdfConverter;
            _viewEngine = viewEngine;
            _tempDataProvider = tempDataProvider;
            _emailService = emailService;
        }

        // GET: Patients
        public async Task<IActionResult> Index(string searchString, string sortOrder)
        {
            ViewData["NameSortParam"] = sortOrder == "name" ? "name_desc" : "name";
            ViewData["AgeSortParam"] = sortOrder == "age" ? "age_desc" : "age";
            ViewData["WeightSortParam"] = sortOrder == "weight" ? "weight_desc" : "weight";
            ViewData["RecentSortParam"] = sortOrder == "recent" ? "recent_desc" : "recent";
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentSort"] = sortOrder;

            var patients = _context.Patients
                .Include(p => p.Reports)
                .AsQueryable();

            // --- SEARCH ---
            if (!string.IsNullOrEmpty(searchString))
            {
                string lower = searchString.ToLower();

                patients = patients.Where(p =>
                    p.Firstname.ToLower().Contains(lower) ||
                    p.Surname.ToLower().Contains(lower) ||
                    p.DateOfBirth.ToString().Contains(lower)
                );
            }

            // --- SORT ---
            patients = sortOrder switch
            {
                "name" => patients.OrderBy(p => p.Surname),
                "name_desc" => patients.OrderByDescending(p => p.Surname),
                "age" => patients.OrderBy(p => p.DateOfBirth),        // oldest first
                "age_desc" => patients.OrderByDescending(p => p.DateOfBirth), // youngest first
                "weight" => patients.OrderBy(p => p.StartingWeight),
                "weight_desc" => patients.OrderByDescending(p => p.StartingWeight),
                "recent" => patients.OrderByDescending(p => p.Reports
                                    .OrderByDescending(r => r.Date)
                                    .Select(r => r.Date)
                                    .FirstOrDefault()),
                "recent_desc" => patients.OrderBy(p => p.Reports
                                    .OrderByDescending(r => r.Date)
                                    .Select(r => r.Date)
                                    .FirstOrDefault()),
                _ => patients.OrderBy(p => p.Surname) // default
            };

            return View(await patients.ToListAsync());
        }

        // GET: Patients/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var patient = await _context.Patients
                .Include(p => p.Reports.OrderByDescending(r => r.Date))
                .ThenInclude(r => r.SideEffects)
                .FirstOrDefaultAsync(m => m.PatientId == id);

            if (patient == null) return NotFound();

            return View(patient);
        }

        // GET: Patients/Create
        [Authorize(Roles = "Doctor")]
        public IActionResult Create()
        {
            return View();
        }

        // POST: Patients/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("PatientId,Firstname,Surname,DateOfBirth,Email,StartingWeight,GoalWeight")] Patient patient)
        {
            if (ModelState.IsValid)
            {
                _context.Add(patient);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(patient);
        }

        // GET: Patients/Edit/5
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var patient = await _context.Patients.FindAsync(id);
            if (patient == null)
            {
                return NotFound();
            }
            return View(patient);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id,
        [Bind("PatientId,Firstname,Surname,DateOfBirth,Email,StartingWeight,GoalWeight,TreatmentStartDate")]
        Patient patient)
        {
            if (id != patient.PatientId) return NotFound();

            ModelState.Remove("Reports");

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(patient);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!PatientExists(patient.PatientId)) return NotFound();
                    else throw;
                }

                return RedirectToAction("Details", new { id = patient.PatientId });
            }

            return View(patient);
        }

        // GET: Patients/Delete/5
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var patient = await _context.Patients
                .FirstOrDefaultAsync(m => m.PatientId == id);
            if (patient == null)
            {
                return NotFound();
            }

            return View(patient);
        }

        // POST: Patients/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var patient = await _context.Patients
                .Include(p => p.Reports)
                    .ThenInclude(r => r.SideEffects)
                .FirstOrDefaultAsync(p => p.PatientId == id);

            if (patient != null)
            {
                // Clear side effect links on each report first
                foreach (var report in patient.Reports)
                    report.SideEffects.Clear();

                await _context.SaveChangesAsync();

                _context.Patients.Remove(patient);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

        private bool PatientExists(int id)
        {
            return _context.Patients.Any(e => e.PatientId == id);
        }

        // Shared helper to render the HTML for the PDF
        private async Task<string> RenderPatientReportHtml(int id)
        {
            var patient = await _context.Patients
                .Include(p => p.Reports)
                .ThenInclude(r => r.SideEffects)
                .FirstOrDefaultAsync(p => p.PatientId == id);

            if (patient == null) return null;

            // Render the Razor view to an HTML string
            var viewResult = _viewEngine.FindView(ControllerContext, "ReportTemplate", false);
            using var sw = new StringWriter();
            var viewContext = new ViewContext(
                ControllerContext,
                viewResult.View,
                new ViewDataDictionary<Patient>(new EmptyModelMetadataProvider(), new ModelStateDictionary())
                {
                    Model = patient
                },
                new TempDataDictionary(HttpContext, _tempDataProvider),
                sw,
                new HtmlHelperOptions()
            );
            await viewResult.View.RenderAsync(viewContext);
            return sw.ToString();
        }

        public async Task<IActionResult> ReportPreview(int id)
        {
            var patient = await _context.Patients
                .Include(p => p.Reports)
                .FirstOrDefaultAsync(p => p.PatientId == id);

            if (patient == null) return NotFound();
            return View("ReportTemplate", patient);
        }

        public async Task<IActionResult> DownloadReport(int id)
        {
            var patient = await _context.Patients
                .Include(p => p.Reports)
                .FirstOrDefaultAsync(p => p.PatientId == id);

            if (patient == null) return NotFound();

            var html = await RenderViewToString("ReportTemplate", patient);

            var doc = new HtmlToPdfDocument()
            {
                GlobalSettings = {
        ColorMode = ColorMode.Color,
        Orientation = Orientation.Portrait,
        PaperSize = PaperKind.A4,
        Margins = new MarginSettings { Top = 10, Bottom = 10, Left = 10, Right = 10 },
        DPI = 150
    },
                Objects = {
        new ObjectSettings {
            HtmlContent = html,
            WebSettings = {
                DefaultEncoding = "utf-8",
                EnableIntelligentShrinking = true,
                PrintMediaType = false
            }
        }
    }
            };

            var pdf = _pdfConverter.Convert(doc);
            var filename = $"Report_{patient.Surname}_{patient.Firstname}_{DateTime.Now:yyyyMMdd}.pdf";
            return File(pdf, "application/pdf", filename);
        }


        private async Task<string> RenderViewToString(string viewName, object model)
        {
            ViewData.Model = model;
            using var sw = new StringWriter();

            var viewResult = _viewEngine.FindView(ControllerContext, viewName, false);
            var viewContext = new ViewContext(
                ControllerContext,
                viewResult.View,
                new ViewDataDictionary(
                    new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(),
                    new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary())
                {
                    Model = model
                },
                new TempDataDictionary(HttpContext, _tempDataProvider),
                sw,
                new HtmlHelperOptions()
            );

            await viewResult.View.RenderAsync(viewContext);
            return sw.ToString();
        }
        // GET: Show confirmation modal data
        [HttpGet]
        public async Task<IActionResult> EmailReportConfirm(int id)
        {
            var patient = await _context.Patients
                .FirstOrDefaultAsync(p => p.PatientId == id);

            if (patient == null) return NotFound();

            return Json(new
            {
                patientName = $"{patient.Firstname} {patient.Surname}",
                patientEmail = patient.Email,
                patientId = patient.PatientId
            });
        }

        // POST: Generate PDF and send email
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> EmailReport(int id)
        {
            var patient = await _context.Patients
                .Include(p => p.Reports)
                .FirstOrDefaultAsync(p => p.PatientId == id);

            if (patient == null) return NotFound();

            // Generate the PDF
            var html = await RenderViewToString("ReportTemplate", patient);

            var doc = new HtmlToPdfDocument()
            {
                GlobalSettings = {
            ColorMode   = ColorMode.Color,
            Orientation = Orientation.Portrait,
            PaperSize   = PaperKind.A4,
            Margins     = new MarginSettings { Top = 10, Bottom = 10, Left = 10, Right = 10 },
            DPI         = 150
        },
                Objects = {
            new ObjectSettings {
                HtmlContent = html,
                WebSettings = {
                    DefaultEncoding       = "utf-8",
                    EnableIntelligentShrinking = true,
                    PrintMediaType        = false
                }
            }
        }
            };

            var pdf = _pdfConverter.Convert(doc);
            var filename = $"Report_{patient.Surname}_{patient.Firstname}_{DateTime.Now:yyyyMMdd}.pdf";

            // Send the email
            try
            {
                await _emailService.SendReportAsync(
                    recipientEmail: patient.Email,
                    recipientName: $"{patient.Firstname} {patient.Surname}",
                    subject: $"Treatment Report — {patient.Firstname} {patient.Surname}",
                    body: $"Dear {patient.Firstname},\n\nPlease find your treatment report attached.\n\nKind regards,\nYour Clinic",
                    pdfAttachment: pdf,
                    attachmentFilename: filename
                );

                TempData["EmailSuccess"] = $"Report successfully sent to {patient.Email}.";
            }
            catch (Exception ex)
            {
                TempData["EmailError"] = $"Failed to send email: {ex.Message}";
            }

            return RedirectToAction("Details", new { id });
        }

    }
}
