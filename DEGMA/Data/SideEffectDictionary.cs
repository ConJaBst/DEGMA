using DEGMA.Models;
using Microsoft.EntityFrameworkCore;

namespace DEGMA.Data
{
    /// <summary>
    /// Built-in list of common side effects for GLP-1 / weight-loss injection treatment.
    /// Seeded into the SideEffects table at startup so the report form always has options.
    /// Edit this list to change the defaults — existing rows are never renamed or deleted.
    /// </summary>
    public static class SideEffectDictionary
    {
        public static readonly string[] Names =
        {
            // Gastrointestinal
            "Nausea",
            "Vomiting",
            "Diarrhoea",
            "Constipation",
            "Abdominal pain",
            "Bloating",
            "Heartburn / reflux",
            "Burping",
            "Decreased appetite",

            // General
            "Headache",
            "Fatigue",
            "Dizziness",

            // Other common
            "Injection site reaction",
            "Hair loss",
            "Low blood sugar",
        };

        /// <summary>
        /// Adds any dictionary entries that aren't already in the table (case-insensitive).
        /// Safe to run on every startup.
        /// </summary>
        public static async Task SeedAsync(ApplicationDbContext db)
        {
            var existing = new HashSet<string>(
                await db.SideEffects.Select(s => s.Name).ToListAsync(),
                StringComparer.OrdinalIgnoreCase);

            var missing = Names.Where(n => !existing.Contains(n)).ToList();
            if (missing.Count == 0) return;

            db.SideEffects.AddRange(missing.Select(n => new SideEffect { Name = n }));
            await db.SaveChangesAsync();
        }
    }
}
