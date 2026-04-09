namespace DEGMA.Models
{
    public class CsvRowViewModel
    {
        public int RowIndex { get; set; }

        // Patient fields
        public string Firstname { get; set; } = "";
        public string Surname { get; set; } = "";
        public string DateOfBirth { get; set; } = "";
        public string Email { get; set; } = "";
        public string StartingWeight { get; set; } = "";
        public string TreatmentStartDate { get; set; } = "";

        // Report fields
        public string ReportNumber { get; set; } = "";
        public string WeekNumber { get; set; } = "";
        public string Date { get; set; } = "";
        public string Weight { get; set; } = "";
        public string Dosage { get; set; } = "";
        public string WeightChange { get; set; } = "";
        public string Notes { get; set; } = "";
        public string SideEffects { get; set; } = "";

        // Validation
        public List<string> Errors { get; set; } = new();
        public bool IsValid => !Errors.Any();

        // Duplicate status
        public string DuplicateAction { get; set; } = "none"; // none, skip, overwrite, addduplicate
        public bool IsDuplicate { get; set; } = false;
        public int? ExistingPatientId { get; set; } = null;

        // New side effects that don't exist yet
        public List<string> NewSideEffects { get; set; } = new();
    }

    public class ImportViewModel
    {
        public List<CsvRowViewModel> Rows { get; set; } = new();
        public List<string> NewSideEffectsToCreate { get; set; } = new();
        public bool ConfirmNewSideEffects { get; set; } = false;
    }
}