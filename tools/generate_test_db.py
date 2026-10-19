"""Regenerate the patient data in DEGMA's test database.

Keeps the "Test Tester" patient (and user accounts, side effects, settings),
deletes every other patient, and generates ~200 realistic patients:

- First patient starts in early December 2023; new patients arrive at a
  slowly increasing rate up to the given "today".
- Mostly weekly visits with occasional missed weeks.
- Weight follows a typical GLP-1 curve (fast early loss that levels off),
  with a few non-responders. Visceral fat, muscle mass, pulse and blood
  pressure move with the weight loss.
- Dose titrates up from 0.25 mg; side effects are most common early on and
  after dose increases.
- Most patients have a goal weight. Many stop coming soon after reaching it,
  some keep coming for maintenance, and some drop out without reaching it.
- A handful of deliberate data-entry mistakes (e.g. swapped digits) so the
  Reports review page has something to flag.

The database must already be migrated to the current schema (start the app
once against it). Run from the repo root:

    python tools/generate_test_db.py DEGMA/degma.db

Use --seed to get a different but repeatable data set, and --today to pin the
end date (defaults to the current date).
"""

import argparse
import math
import random
import sqlite3
from datetime import date, timedelta

TEST_PATIENT = ("Test", "Tester")
CLOSED_DAYS = {(12, 25), (12, 26), (1, 1)}
FIRST_START = date(2023, 12, 4)
PATIENTS = 200

MALE_NAMES = [
    "Thabo", "Sipho", "Kabelo", "Tshepo", "Mandla", "Bongani", "Lebo", "Themba", "Sizwe", "Neo",
    "Pieter", "Johan", "Willem", "Ruan", "Francois", "David", "Michael", "Jason", "Ryan", "Daniel",
    "Pravin", "Rajesh", "Kevin", "Ahmed", "Yusuf", "Lwazi", "Katlego", "Musa", "Andile", "Kagiso",
]
FEMALE_NAMES = [
    "Lerato", "Nomsa", "Thandiwe", "Palesa", "Zanele", "Naledi", "Busisiwe", "Refilwe", "Ayanda", "Mpho",
    "Annelie", "Marike", "Elize", "Charlene", "Michelle", "Sarah", "Jessica", "Nicole", "Megan", "Lauren",
    "Priya", "Anisha", "Fatima", "Zainab", "Kelly", "Nandi", "Dineo", "Karabo", "Lindiwe", "Boitumelo",
]
SURNAMES = [
    "Nkosi", "Dlamini", "Mokoena", "Khumalo", "Ndlovu", "Molefe", "Mahlangu", "Zulu", "Mthembu", "Sithole",
    "van der Merwe", "Botha", "Pretorius", "Venter", "du Plessis", "Coetzee", "Steyn", "Joubert", "Nel", "Kruger",
    "Naidoo", "Pillay", "Govender", "Reddy", "Moodley", "Smith", "Jacobs", "Williams", "Adams", "Petersen",
    "Mohlatsie", "Tau", "Maseko", "Radebe", "Mabaso", "Hendricks", "Fourie", "Olivier", "Daniels", "Abrahams",
]

# Side effect name (matched case-insensitively against the SideEffects table) -> weight
SIDE_EFFECTS = {
    "Nausea": 30, "Diarrhoea": 12, "Constipation": 12, "Fatigue": 10, "Decreased appetite": 8,
    "Headache": 7, "Abdominal pain": 7, "Bloating": 6, "Heartburn / reflux": 6, "Burping": 5,
    "Vomiting": 5, "Injection site reaction": 5, "Dizziness": 4, "Hair Loss": 2, "Low blood sugar": 1,
}

ROUTINE_NOTES = [
    "Feeling good, no concerns.", "Reports reduced cravings.", "Missed a dose last week.",
    "Started walking 30 minutes daily.", "Advised to increase water intake.",
    "Struggling with evening snacking.", "Going on holiday, may miss next visit.",
    "Sleeping better.", "Clothes fitting looser.", "Stressful week at work.",
]


def batch_for(day):
    """New batch roughly every quarter, e.g. 4YIS2501A for Jan-Mar 2025."""
    quarter = (day.month - 1) // 3 + 1
    return f"4YIS{day.year % 100:02d}{quarter:02d}A"


def fmt_dec(x, places):
    return f"{x:.{places}f}"


def fmt_dose(d):
    return f"{d:.2f}".rstrip("0").rstrip(".")


def start_dates(rng, today):
    """Start dates from FIRST_START to ~a week before today, arriving faster over time."""
    span = (today - timedelta(days=7) - FIRST_START).days
    # Inverse CDF of a linearly increasing arrival rate (density ~ 0.3 + t), sampled at evenly
    # spaced points with a little jitter so growth is steady rather than lumpy.
    a = 0.3
    fractions = []
    for i in range(1, PATIENTS):
        u = (i + rng.uniform(-0.4, 0.4)) / PATIENTS
        fractions.append(-a + math.sqrt(a * a + u * (1 + 2 * a)))
    days = [FIRST_START] + [FIRST_START + timedelta(days=int(f * span)) for f in sorted(fractions)]
    return [next_open_day(d) for d in days]


def next_open_day(day):
    """The clinic is open Monday to Friday, except public holidays in CLOSED_DAYS."""
    while (day.month, day.day) in CLOSED_DAYS or day.weekday() >= 5:
        day += timedelta(days=1)
    return day


def make_patient(rng, start, used_names):
    male = rng.random() < 0.42
    while True:
        first = rng.choice(MALE_NAMES if male else FEMALE_NAMES)
        last = rng.choice(SURNAMES)
        if (first, last) not in used_names:
            used_names.add((first, last))
            break
    age = rng.randint(21, 68)
    dob = start - timedelta(days=age * 365 + rng.randint(0, 364))
    weight = rng.gauss(104 if male else 92, 17)
    weight = round(min(165, max(68, weight)), 1)

    non_responder = rng.random() < 0.08
    loss = rng.uniform(0.01, 0.05) if non_responder else min(0.28, max(0.05, rng.gauss(0.16, 0.05)))

    goal = None
    if rng.random() < 0.85:
        goal_fraction = rng.uniform(0.07, 0.20)
        goal = round(weight * (1 - goal_fraction) * 2) / 2

    email = f"{first}.{last}".lower().replace(" ", "") + f"{rng.randint(1, 99)}@example.com"
    return {
        "first": first, "last": last, "dob": dob, "email": email, "start": start,
        "weight": weight, "goal": goal, "male": male,
        "loss": loss, "tau": rng.uniform(10, 22),
        "measures_body": rng.random() > 0.1,
        "vf0": min(30.0, max(3.0, 5 + (weight - 70) * 0.25 + rng.gauss(0, 2) + (2 if male else 0))),
        "mm_ratio": (0.42 if male else 0.33) + rng.gauss(0, 0.025),
        "pulse0": rng.gauss(76, 8),
        "sys0": rng.gauss(123, 10),
        "dia0": rng.gauss(79, 7),
        "sensitivity": rng.uniform(0.5, 1.6),
        "max_dose": rng.choice([0.5, 0.55, 0.6, 0.65, 0.7, 0.75]),
    }


def make_visits(rng, p, today):
    """Yield one dict per visit until the patient stops or we reach today."""
    visits = []
    day = p["start"]
    dose = 0.25
    last_dose_change = 0
    weight = p["weight"]
    reached_goal_at = None
    keeps_coming_after_goal = rng.random() < 0.25
    visits_after_goal = rng.randint(0, 4)

    week_index = 0
    while day <= today:
        weeks = (day - p["start"]).days / 7
        expected_loss = p["loss"] * (1 - math.exp(-weeks / p["tau"]))
        if visits:
            # Drift towards the curve with week-to-week noise (water weight, scale timing).
            target = p["weight"] * (1 - expected_loss)
            weight = weight + (target - weight) * 0.6 + rng.gauss(0, 0.45)
        weight = round(weight, 1)
        loss_fraction = max(0.0, (p["weight"] - weight) / p["weight"])

        notes = []
        dose_increased = False
        if visits and week_index - last_dose_change >= rng.choice([3, 4, 4, 5]) and dose < p["max_dose"]:
            recent = [v["weight"] for v in visits[-3:]]
            slowing = len(recent) == 3 and (recent[0] - weight) < 1.2
            if slowing or rng.random() < 0.35:
                dose = round(min(p["max_dose"], dose + rng.choice([0.05, 0.05, 0.1])), 2)
                last_dose_change = week_index
                dose_increased = True
                notes.append(f"Dose increased to {fmt_dose(dose)} mg.")

        # Side effects: common at the start and right after a dose increase.
        recently_changed = week_index - last_dose_change <= 1 and last_dose_change > 0
        chance = (0.38 if week_index < 4 or recently_changed else 0.12) * p["sensitivity"]
        effects = []
        if rng.random() < chance:
            pool = {k: v for k, v in SIDE_EFFECTS.items() if k != "Hair Loss" or week_index >= 12}
            for _ in range(rng.choice([1, 1, 1, 2, 2, 3])):
                name = rng.choices(list(pool), weights=list(pool.values()))[0]
                if name not in effects:
                    effects.append(name)
        if "Vomiting" in effects and dose > 0.3 and not dose_increased and rng.random() < 0.3:
            dose = round(dose - 0.05, 2)
            last_dose_change = week_index
            notes.append(f"Dose reduced to {fmt_dose(dose)} mg due to side effects.")

        body = p["measures_body"] and rng.random() > 0.1
        vf = round(max(1.0, p["vf0"] * (1 - 1.7 * loss_fraction) + rng.gauss(0, 0.3)), 1) if body else None
        mm = round(p["weight"] * p["mm_ratio"] * (1 - 0.22 * loss_fraction) + rng.gauss(0, 0.3), 1) if body else None
        pulse = int(round(p["pulse0"] - 30 * loss_fraction + rng.gauss(0, 4)))
        systolic = int(round(p["sys0"] - 60 * loss_fraction + rng.gauss(0, 6)))
        diastolic = int(round(p["dia0"] - 40 * loss_fraction + rng.gauss(0, 4)))

        goal_hit = p["goal"] is not None and weight <= p["goal"]
        if goal_hit and reached_goal_at is None:
            reached_goal_at = len(visits)
            notes.append("Reached goal weight - discussed maintenance plan.")

        if not notes and rng.random() < 0.1:
            notes.append(rng.choice(ROUTINE_NOTES))

        visits.append({
            "date": day, "weight": weight, "dose": dose, "effects": effects,
            "vf": vf, "mm": mm, "pulse": min(118, max(48, pulse)),
            "sys": min(185, max(95, systolic)), "dia": min(115, max(58, diastolic)),
            "notes": " ".join(notes) or None,
        })

        # Stopping rules
        if reached_goal_at is not None and not keeps_coming_after_goal:
            if len(visits) - 1 - reached_goal_at >= visits_after_goal:
                break
        dropout = 0.025 if week_index < 8 else 0.008
        if reached_goal_at is not None and keeps_coming_after_goal:
            dropout = 0.03
        if rng.random() < dropout:
            break

        gap = rng.choices([7, 14, 21, 6, 8], weights=[80, 11, 4, 2, 3])[0]
        day += timedelta(days=gap)
        day = next_open_day(day)
        week_index += 1
    return visits


def add_typos(rng, all_visits, count):
    """A few realistic data-entry mistakes for the Reports review page to catch."""
    candidates = [v for v in all_visits if 1 < v["report_number"]]
    for v in rng.sample(candidates, count):
        kind = rng.choice(["weight_digits", "weight_ten", "pulse", "vf_mm", "bp"])
        if kind == "weight_digits":
            w = f"{v['weight']:.1f}"
            if len(w) >= 4 and w[0] != w[1]:
                v["weight"] = float(w[1] + w[0] + w[2:])
        elif kind == "weight_ten":
            v["weight"] = round(v["weight"] + rng.choice([-10, 10]), 1)
        elif kind == "pulse":
            v["pulse"] = rng.choice([v["pulse"] * 2, 17, 7])
        elif kind == "vf_mm" and v["vf"] is not None:
            v["vf"], v["mm"] = v["mm"], v["vf"]
        else:
            v["sys"], v["dia"] = v["dia"], v["sys"]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("db")
    ap.add_argument("--seed", type=int, default=2026)
    ap.add_argument("--today", type=date.fromisoformat, default=date.today())
    ap.add_argument("--typos", type=int, default=8)
    args = ap.parse_args()
    rng = random.Random(args.seed)

    con = sqlite3.connect(args.db)
    con.execute("PRAGMA foreign_keys = ON")
    cols = {r[1] for r in con.execute("PRAGMA table_info(Reports)")}
    if not {"Pulse", "SystolicBp", "DiastolicBp"} <= cols:
        raise SystemExit("Database is not migrated to the current schema. Start the app against it once first.")

    keep = con.execute("SELECT PatientId FROM Patients WHERE Firstname = ? AND Surname = ?", TEST_PATIENT).fetchall()
    keep_ids = [r[0] for r in keep]
    placeholders = ",".join("?" * len(keep_ids)) or "NULL"
    con.execute(f"DELETE FROM ReportSideEffect WHERE ReportsReportId IN "
                f"(SELECT ReportId FROM Reports WHERE PatientId NOT IN ({placeholders}))", keep_ids)
    con.execute(f"DELETE FROM Reports WHERE PatientId NOT IN ({placeholders})", keep_ids)
    con.execute(f"DELETE FROM Patients WHERE PatientId NOT IN ({placeholders})", keep_ids)

    side_effect_ids = {name.lower(): sid for sid, name in con.execute("SELECT SideEffectId, Name FROM SideEffects")}
    missing = [n for n in SIDE_EFFECTS if n.lower() not in side_effect_ids]
    if missing:
        raise SystemExit(f"Side effects missing from the database: {missing}. Start the app once to seed them.")

    used_names = {TEST_PATIENT}
    patients = [make_patient(rng, start, used_names) for start in start_dates(rng, args.today)]

    all_visits = []
    for p in patients:
        p["visits"] = make_visits(rng, p, args.today)
        for i, v in enumerate(p["visits"]):
            v["report_number"] = i + 1
            all_visits.append(v)
    add_typos(rng, all_visits, args.typos)

    reports = 0
    for p in patients:
        cur = con.execute(
            "INSERT INTO Patients (Firstname, Surname, DateOfBirth, Email, StartingWeight, TreatmentStartDate, GoalWeight) "
            "VALUES (?, ?, ?, ?, ?, ?, ?)",
            (p["first"], p["last"], p["dob"].isoformat(), p["email"], p["weight"], p["start"].isoformat(), p["goal"]))
        patient_id = cur.lastrowid
        previous = p["weight"]
        for v in p["visits"]:
            cur = con.execute(
                "INSERT INTO Reports (PatientId, ReportNumber, WeekNumber, Date, Weight, WeightChange, Dosage, Notes, "
                "VisceralFat, MuscleMass, BatchNumber, Pulse, SystolicBp, DiastolicBp) "
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                (patient_id, v["report_number"], (v["date"] - p["start"]).days // 7 + 1, v["date"].isoformat(),
                 v["weight"], round(v["weight"] - previous, 1), fmt_dose(v["dose"]), v["notes"],
                 fmt_dec(v["vf"], 1) if v["vf"] is not None else None,
                 fmt_dec(v["mm"], 1) if v["mm"] is not None else None,
                 batch_for(v["date"]), v["pulse"], v["sys"], v["dia"]))
            previous = v["weight"]
            for name in v["effects"]:
                con.execute("INSERT INTO ReportSideEffect (ReportsReportId, SideEffectsSideEffectId) VALUES (?, ?)",
                            (cur.lastrowid, side_effect_ids[name.lower()]))
            reports += 1

    con.commit()
    # One self-contained file: no -wal/-shm side files to forget.
    con.execute("PRAGMA wal_checkpoint(TRUNCATE)")
    con.execute("PRAGMA journal_mode = DELETE")
    con.execute("VACUUM")
    con.close()

    print(f"Kept {len(keep_ids)} test patient(s); generated {len(patients)} patients with {reports} reports "
          f"from {FIRST_START} to {args.today}. Current batch: {batch_for(args.today)}")


if __name__ == "__main__":
    main()
