namespace DEGMA.Models
{
    public class CsvRowViewModel
    {
        public int RowIndex { get; set; }
        public string Firstname { get; set; } = "";
        public string Surname { get; set; } = "";
        public string DateOfBirth { get; set; } = "";
        public string Email { get; set; } = "";
        public string StartingWeight { get; set; } = "";
        public string TreatmentStartDate { get; set; } = "";
        public string ReportNumber { get; set; } = "";
        public string WeekNumber { get; set; } = "";
        public string Date { get; set; } = "";
        public string Weight { get; set; } = "";
        public string Dosage { get; set; } = "";
        public string WeightChange { get; set; } = "";
        public string Notes { get; set; } = "";
        public string SideEffects { get; set; } = "";
        public List<string> Errors { get; set; } = new();
        public bool IsValid => !Errors.Any();
        public string DuplicateAction { get; set; } = "none";
        public bool IsDuplicate { get; set; } = false;
        public int? ExistingPatientId { get; set; } = null;
        public string VisceralFat { get; set; } = "";
        public string MuscleMass { get; set; } = "";
        public string BatchNumber { get; set; } = "";
        public List<string> NewSideEffects { get; set; } = new();

    }

    public class ImportViewModel
    {
        public List<CsvRowViewModel> Rows { get; set; } = new();
        public List<string> NewSideEffectsToCreate { get; set; } = new();
        public bool ConfirmNewSideEffects { get; set; } = false;

        // Pagination
        public string TempFileId { get; set; } = "";
        public int CurrentPage { get; set; } = 1;
        public int TotalPages { get; set; } = 1;
        public int TotalRows { get; set; } = 0;
        public int InvalidRows { get; set; } = 0;
        public int DuplicateRows { get; set; } = 0;
        public const int PageSize = 50;
    }

    public class ImportTempData
    {
        public List<CsvRowViewModel> AllRows { get; set; } = new();
        public List<string> NewSideEffectsToCreate { get; set; } = new();
    }
}