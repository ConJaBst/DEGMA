namespace DEGMA.Models
{
    public class DashboardViewModel
    {
        // Headline numbers
        public int TotalPatients { get; set; }
        public int ActivePatients { get; set; }          // seen in the last 30 days
        public int PatientsWithReports { get; set; }
        public double AverageTotalLossKg { get; set; }   // starting weight -> latest weight
        public double AverageTotalLossPercent { get; set; }
        public int ReachedFivePercent { get; set; }
        public int ReachedTenPercent { get; set; }
        public int VisitsThisWeek { get; set; }          // last 7 days
        public int VisitsLastWeek { get; set; }          // the 7 days before that
        public double? VisitsChangePercent { get; set; } // null when last week had no visits

        // Charts
        public List<WeekPoint> ProgressCurve { get; set; } = new();
        public List<MonthPoint> MonthlyActivity { get; set; } = new();

        // Latest dose of each patient
        public List<DoseCount> DoseBreakdown { get; set; } = new();

        // Current batch
        public string CurrentBatchNumber { get; set; } = "";
        public decimal BatchMgUsed { get; set; }
        public int BatchVisits { get; set; }
        public DateOnly? BatchFirstUsed { get; set; }

        public List<Report> RecentReports { get; set; } = new();
    }

    public record WeekPoint(int Week, double AverageChangePercent, int Patients);

    public record MonthPoint(string Label, int NewPatients, int Visits);

    public record DoseCount(decimal Dosage, int Patients);
}
