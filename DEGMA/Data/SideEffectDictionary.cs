using DEGMA.Models;
using Microsoft.EntityFrameworkCore;

namespace DEGMA.Data
{
    /// <summary>
    /// The 15 side effects offered on the report form: the most common and clinically
    /// important ones for GLP-1 / weight-loss injection treatment. Anything else is
    /// recorded through the form's "Other" box.
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
        /// Other names for the same side effect. Entries recorded under these names are
        /// merged into the dictionary entry, so the list has no repeats.
        /// </summary>
        public static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Diarrhea"] = "Diarrhoea",
            ["Heartburn"] = "Heartburn / reflux",
            ["Reflux"] = "Heartburn / reflux",
            ["Indigestion"] = "Heartburn / reflux",
            ["Stomach pain"] = "Abdominal pain",
            ["Stomach ache"] = "Abdominal pain",
            ["Loss of appetite"] = "Decreased appetite",
            ["Injection site pain"] = "Injection site reaction",
            ["Hypoglycaemia"] = "Low blood sugar",
            ["Hypoglycemia"] = "Low blood sugar",
            ["Tiredness"] = "Fatigue",
        };

        private static readonly HashSet<string> NameSet = new(Names, StringComparer.OrdinalIgnoreCase);

        public static bool Contains(string name) => NameSet.Contains(name.Trim());

        /// <summary>
        /// The name to store for a side effect: the dictionary spelling when it's in the
        /// dictionary or a known alias, otherwise the trimmed name as typed.
        /// </summary>
        public static string Canonical(string name)
        {
            var trimmed = name.Trim();
            if (Aliases.TryGetValue(trimmed, out var canonical))
                return canonical;
            return Names.FirstOrDefault(n => string.Equals(n, trimmed, StringComparison.OrdinalIgnoreCase)) ?? trimmed;
        }

        /// <summary>
        /// Runs at startup; safe to run every time:
        /// - merges alias entries (e.g. "Diarrhea") into their dictionary entry, keeping every report's link,
        /// - fixes the spelling/case of dictionary entries (e.g. "Hair Loss" to "Hair loss"),
        /// - adds dictionary entries that are missing,
        /// - deletes entries that aren't in the dictionary and aren't on any report.
        /// Entries outside the dictionary that are on reports are kept, so history is never lost.
        /// </summary>
        public static async Task SeedAsync(ApplicationDbContext db)
        {
            var all = await db.SideEffects.Include(s => s.Reports).ToListAsync();

            foreach (var name in Names)
            {
                var matches = all.Where(s => string.Equals(Canonical(s.Name), name, StringComparison.OrdinalIgnoreCase)).ToList();

                // Prefer the row already spelled exactly right, then the most used one.
                var keep = matches
                    .OrderByDescending(s => s.Name == name)
                    .ThenByDescending(s => s.Reports.Count)
                    .FirstOrDefault();

                if (keep == null)
                {
                    keep = new SideEffect { Name = name };
                    db.SideEffects.Add(keep);
                    all.Add(keep);
                    continue;
                }

                foreach (var duplicate in matches.Where(s => s != keep))
                {
                    foreach (var report in duplicate.Reports.ToList())
                    {
                        if (!keep.Reports.Contains(report))
                            keep.Reports.Add(report);
                    }
                    duplicate.Reports.Clear();
                    db.SideEffects.Remove(duplicate);
                    all.Remove(duplicate);
                }

                if (keep.Name != name)
                {
                    // SQLite's unique index compares case-sensitively, so rename after the
                    // duplicates are gone.
                    await db.SaveChangesAsync();
                    keep.Name = name;
                }
            }

            foreach (var unused in all.Where(s => !Contains(s.Name) && s.Reports.Count == 0).ToList())
                db.SideEffects.Remove(unused);

            await db.SaveChangesAsync();
        }
    }
}
