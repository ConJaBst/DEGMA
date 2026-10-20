using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
namespace DEGMA.Models
{
    public class Patient
    {
        public int PatientId { get; set; }
        public string Firstname {  get; set; }
        public string Surname { get; set; }
        [DataType(DataType.Date)] public DateOnly DateOfBirth { get; set; }
        public string Email { get; set; }
        public double StartingWeight { get; set; }
        public double? GoalWeight { get; set; }
        [DataType(DataType.Date)]
        public DateOnly TreatmentStartDate { get; set; } = DateOnly.FromDateTime(DateTime.Now);

        public List<Report> Reports { get; set; } = new List<Report>();
    }
    public class Report : IValidatableObject
    {
        public int ReportId { get; set; }
        public int ReportNumber { get; set; }
        public int WeekNumber { get; set; }
        public double Weight { get; set; }
        public decimal Dosage { get; set; }
        public double WeightChange { get; set; }
        public string? Notes { get; set; }
        [DataType(DataType.Date)]
        public DateOnly Date { get; set; }
        public List<SideEffect> SideEffects { get; set; } = new List<SideEffect>();
        public int PatientId { get; set; }
        public Patient? Patient { get; set; }
        public decimal? VisceralFat { get; set; }
        public decimal? MuscleMass { get; set; }

        // Vitals are optional. If blood pressure is entered, both numbers are needed
        // (see Validate below).
        [Range(20, 250, ErrorMessage = "Pulse must be between 20 and 250 bpm.")]
        [Display(Name = "Pulse (bpm)")]
        public int? Pulse { get; set; }

        [Range(50, 300, ErrorMessage = "Systolic BP must be between 50 and 300 mmHg.")]
        [Display(Name = "Systolic BP")]
        public int? SystolicBp { get; set; }

        [Range(20, 200, ErrorMessage = "Diastolic BP must be between 20 and 200 mmHg.")]
        [Display(Name = "Diastolic BP")]
        public int? DiastolicBp { get; set; }

        public string BloodPressureDisplay =>
            SystolicBp.HasValue && DiastolicBp.HasValue ? $"{SystolicBp}/{DiastolicBp}" : "—";

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (SystolicBp.HasValue && !DiastolicBp.HasValue)
                yield return new ValidationResult("Enter the diastolic BP too, or leave both blank.", new[] { nameof(DiastolicBp) });
            if (DiastolicBp.HasValue && !SystolicBp.HasValue)
                yield return new ValidationResult("Enter the systolic BP too, or leave both blank.", new[] { nameof(SystolicBp) });
        }

        public string? BatchNumber { get; set; }
    }
    [Index(nameof(Name), IsUnique = true)]
    public class SideEffect
    {
        public int SideEffectId { get; set; }
        public string Name { get; set; }

        public List<Report> Reports { get; set; } = new List<Report>();
    }
}

