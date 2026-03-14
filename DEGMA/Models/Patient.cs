using Microsoft.AspNetCore.SignalR;
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
        [DataType(DataType.Date)]
        public DateOnly TreatmentStartDate { get; set; } = DateOnly.FromDateTime(DateTime.Now);

        public List<Report> Reports { get; set; } = new List<Report>();
    }
    public class Report
    {
        public int ReportId { get; set; }
        public int ReportNumber { get; set; }
        public int WeekNumber { get; set; }
        public double Weight { get; set; }
        public double Dosage { get; set; }
        public double WeightChange { get; set; }
        public string Notes { get; set; }
        [DataType(DataType.Date)]
        public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Now);
        public List<SideEffect> SideEffects { get; set; } = new List<SideEffect>();
        public int PatientId { get; set; }
        public Patient? Patient { get; set; }
    }
    public class SideEffect
    {
        public int SideEffectId { get; set; }
        public string Name { get; set; }

        public List<Report> Reports { get; set; } = new List<Report>();
    }
}

