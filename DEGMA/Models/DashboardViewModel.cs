namespace DEGMA.Models
{
    public class DashboardViewModel
    {
        public int TotalPatients { get; set; }
        public int TotalReports { get; set; }
        public double AverageWeightLoss { get; set; }
        public double TotalDosageAdministered { get; set; }
        public string MostCommonSideEffect { get; set; } = "None";
        public double MaxIndividualLoss { get; set; }
        public int ReportsThisWeek { get; set; }
        public int PatientsWithZeroLoss { get; set; }

        // For a "Recent Activity" list
        public List<Report> RecentReports { get; set; } = new List<Report>();
    }
}