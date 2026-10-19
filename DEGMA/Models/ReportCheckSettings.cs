namespace DEGMA.Models
{
    public enum VitalStatus { Unknown, Low, Normal, High }

    // Limits used to flag out-of-range vitals and likely data-entry mistakes
    // (Reports page, report readout on the patient profile).
    // Any value can be overridden in the "ReportChecks" section of appsettings.json.
    public class ReportCheckSettings
    {
        // Weight: flag a change bigger than this (up or down) since the previous report
        public double WeightChangeKg { get; set; } = 3;

        // Visceral fat (kg): plausible range, and the biggest believable change between reports
        public decimal VisceralFatMin { get; set; } = 1;
        public decimal VisceralFatMax { get; set; } = 60;
        public decimal VisceralFatChange { get; set; } = 3;

        // Muscle mass (kg): plausible range, and the biggest believable change between reports
        public decimal MuscleMassMin { get; set; } = 15;
        public decimal MuscleMassMax { get; set; } = 100;
        public decimal MuscleMassChange { get; set; } = 3;

        // Pulse (bpm): Low below PulseLow, High above PulseHigh
        public int PulseLow { get; set; } = 60;
        public int PulseHigh { get; set; } = 100;

        // Blood pressure (mmHg): High at or above the High limits, Low below the Low limits
        public int SystolicHigh { get; set; } = 140;
        public int DiastolicHigh { get; set; } = 90;
        public int SystolicLow { get; set; } = 90;
        public int DiastolicLow { get; set; } = 60;

        public VitalStatus PulseStatus(int? pulse)
        {
            if (!pulse.HasValue) return VitalStatus.Unknown;
            if (pulse > PulseHigh) return VitalStatus.High;
            if (pulse < PulseLow) return VitalStatus.Low;
            return VitalStatus.Normal;
        }

        public VitalStatus BloodPressureStatus(int? systolic, int? diastolic)
        {
            if (!systolic.HasValue && !diastolic.HasValue) return VitalStatus.Unknown;
            if (systolic >= SystolicHigh || diastolic >= DiastolicHigh) return VitalStatus.High;
            if (systolic < SystolicLow || diastolic < DiastolicLow) return VitalStatus.Low;
            return VitalStatus.Normal;
        }

        public bool IsWeightJump(double weightChange) => Math.Abs(weightChange) > WeightChangeKg;

        public bool IsVisceralFatOutOfRange(decimal? value) =>
            value.HasValue && (value < VisceralFatMin || value > VisceralFatMax);

        public bool IsMuscleMassOutOfRange(decimal? value) =>
            value.HasValue && (value < MuscleMassMin || value > MuscleMassMax);

        public bool IsVisceralFatJump(decimal? change) => change.HasValue && Math.Abs(change.Value) > VisceralFatChange;

        public bool IsMuscleMassJump(decimal? change) => change.HasValue && Math.Abs(change.Value) > MuscleMassChange;
    }
}
