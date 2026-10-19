namespace DEGMA.Models
{
    // Reports page: a day-by-day list for spotting data-entry mistakes
    public class ReportReviewViewModel
    {
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
        public bool FlaggedOnly { get; set; }

        // Paging by days that have reports: links to the next older / newer set
        public DateOnly? OlderTo { get; set; }
        public DateOnly? NewerFrom { get; set; }

        public List<ReportReviewDay> Days { get; set; } = new();
        public int TotalReports { get; set; }
        public int FlaggedReports { get; set; }
        public bool Truncated { get; set; }
        public int MaxRows { get; set; }

        public ReportCheckSettings Checks { get; set; } = new();
    }

    public class ReportReviewDay
    {
        public DateOnly Date { get; set; }
        public List<ReportReviewRow> Rows { get; set; } = new();
        public int FlaggedCount => Rows.Count(r => r.IsFlagged);
    }

    public class ReportReviewRow
    {
        public Report Report { get; set; } = null!;

        // Live comparison with the patient's previous report (or starting weight for the first one)
        public double WeightChange { get; set; }
        public bool WeightChangeFromStart { get; set; }
        public decimal? VisceralFatChange { get; set; }
        public decimal? MuscleMassChange { get; set; }

        public bool WeightFlag { get; set; }
        public bool VisceralFatRangeFlag { get; set; }
        public bool VisceralFatJumpFlag { get; set; }
        public bool MuscleMassRangeFlag { get; set; }
        public bool MuscleMassJumpFlag { get; set; }
        public VitalStatus Pulse { get; set; }
        public VitalStatus BloodPressure { get; set; }

        public bool IsFlagged =>
            WeightFlag || VisceralFatRangeFlag || VisceralFatJumpFlag
            || MuscleMassRangeFlag || MuscleMassJumpFlag
            || Pulse is VitalStatus.High or VitalStatus.Low
            || BloodPressure is VitalStatus.High or VitalStatus.Low;
    }
}
