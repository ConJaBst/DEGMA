using DEGMA.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DEGMA.Controllers
{
    [Authorize(Roles = "Doctor")]
    public class UpdatesController : Controller
    {
        private readonly UpdateService _updates;

        public UpdatesController(UpdateService updates)
        {
            _updates = updates;
        }

        // GET: Updates/Check (called by the navbar button)
        public async Task<IActionResult> Check(CancellationToken ct)
        {
            return Json(await _updates.CheckAsync(ct));
        }

        // POST: Updates/Install
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Install()
        {
            var result = await _updates.InstallAsync();
            if (!result.Started)
            {
                TempData["UpdateError"] = result.Error;
                return Redirect(Request.Headers.Referer.ToString() is { Length: > 0 } back ? back : "/");
            }
            return View("Installing", result);
        }

        // GET: Updates/Status (polled by the "installing" page while DEGMA restarts)
        [AllowAnonymous]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public IActionResult Status()
        {
            var notice = _updates.Notice;
            return Json(new { version = UpdateService.DisplayVersion, notice });
        }

        // POST: Updates/Dismiss (hides the "DEGMA was updated" banner)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Dismiss()
        {
            _updates.DismissNotice();
            return Redirect(Request.Headers.Referer.ToString() is { Length: > 0 } back ? back : "/");
        }
    }
}
