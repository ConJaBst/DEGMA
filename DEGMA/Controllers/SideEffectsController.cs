using DEGMA.Data;
using DEGMA.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DEGMA.Controllers
{
    // The report form offers the fixed list in SideEffectDictionary. This page shows how often
    // each side effect is recorded, and lets a Doctor tidy up the extras recorded through
    // "Other" (or kept from older reports). The standard entries can't be renamed or deleted.
    public class SideEffectsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public SideEffectsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: SideEffects
        public async Task<IActionResult> Index()
        {
            return View(await _context.SideEffects.Include(s => s.Reports).ToListAsync());
        }

        // GET: SideEffects/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var sideEffect = await _context.SideEffects
                .FirstOrDefaultAsync(m => m.SideEffectId == id);
            if (sideEffect == null)
            {
                return NotFound();
            }

            return View(sideEffect);
        }

        // GET: SideEffects/Edit/5
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> Edit(int? id)
        {
            var sideEffect = id == null ? null : await _context.SideEffects.FindAsync(id);
            if (sideEffect == null)
            {
                return NotFound();
            }
            if (SideEffectDictionary.Contains(sideEffect.Name))
            {
                return StandardEntry(sideEffect);
            }
            return View(sideEffect);
        }

        // POST: SideEffects/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> Edit(int id, [Bind("SideEffectId,Name")] SideEffect sideEffect)
        {
            if (id != sideEffect.SideEffectId)
            {
                return NotFound();
            }

            var existing = await _context.SideEffects.FindAsync(id);
            if (existing == null)
            {
                return NotFound();
            }
            if (SideEffectDictionary.Contains(existing.Name))
            {
                return StandardEntry(existing);
            }

            var name = (sideEffect.Name ?? "").Trim();
            var canonical = SideEffectDictionary.Canonical(name);
            if (SideEffectDictionary.Contains(canonical))
            {
                // Renaming an extra to a standard side effect (e.g. "Feeling sick" to "Nausea")
                // merges it: its reports move to the standard entry.
                var standard = await _context.SideEffects.Include(s => s.Reports)
                    .FirstAsync(s => s.Name == canonical);
                await _context.Entry(existing).Collection(s => s.Reports).LoadAsync();
                foreach (var report in existing.Reports.ToList())
                {
                    if (!standard.Reports.Contains(report))
                        standard.Reports.Add(report);
                }
                existing.Reports.Clear();
                _context.SideEffects.Remove(existing);
                await _context.SaveChangesAsync();
                TempData["SideEffectSuccess"] = $"Merged into \"{canonical}\".";
                return RedirectToAction(nameof(Index));
            }
            if (await _context.SideEffects.AnyAsync(s => s.SideEffectId != id && s.Name.ToLower() == name.ToLower()))
            {
                ModelState.AddModelError(nameof(SideEffect.Name), "A side effect with that name already exists.");
            }

            if (ModelState.IsValid)
            {
                existing.Name = name;
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(sideEffect);
        }

        // GET: SideEffects/Delete/5
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> Delete(int? id)
        {
            var sideEffect = id == null ? null : await _context.SideEffects
                .FirstOrDefaultAsync(m => m.SideEffectId == id);
            if (sideEffect == null)
            {
                return NotFound();
            }
            if (SideEffectDictionary.Contains(sideEffect.Name))
            {
                return StandardEntry(sideEffect);
            }

            return View(sideEffect);
        }

        // POST: SideEffects/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var sideEffect = await _context.SideEffects.FindAsync(id);
            if (sideEffect != null && SideEffectDictionary.Contains(sideEffect.Name))
            {
                return StandardEntry(sideEffect);
            }
            if (sideEffect != null)
            {
                _context.SideEffects.Remove(sideEffect);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

        private IActionResult StandardEntry(SideEffect sideEffect)
        {
            TempData["SideEffectError"] = $"\"{sideEffect.Name}\" is one of the standard side effects on the report form, so it can't be changed or deleted.";
            return RedirectToAction(nameof(Index));
        }
    }
}
