using DEGMA.Data;
using DEGMA.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DEGMA.Controllers
{
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
            return View(await _context.SideEffects.ToListAsync());
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

        // GET: SideEffects/Create
        [Authorize(Roles = "Doctor")]
        public IActionResult Create()
        {
            return View();
        }

        // POST: SideEffects/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> Create([Bind("SideEffectId,Name")] SideEffect sideEffect)
        {
            if (ModelState.IsValid)
            {
                _context.Add(sideEffect);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(sideEffect);
        }

        // GET: SideEffects/Edit/5
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var sideEffect = await _context.SideEffects.FindAsync(id);
            if (sideEffect == null)
            {
                return NotFound();
            }
            return View(sideEffect);
        }

        // POST: SideEffects/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> Edit(int id, [Bind("SideEffectId,Name")] SideEffect sideEffect)
        {
            if (id != sideEffect.SideEffectId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(sideEffect);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!SideEffectExists(sideEffect.SideEffectId))
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
            return View(sideEffect);
        }

        // GET: SideEffects/Delete/5
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> Delete(int? id)
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

        // POST: SideEffects/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Doctor")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var sideEffect = await _context.SideEffects.FindAsync(id);
            if (sideEffect != null)
            {
                _context.SideEffects.Remove(sideEffect);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool SideEffectExists(int id)
        {
            return _context.SideEffects.Any(e => e.SideEffectId == id);
        }
    }
}
