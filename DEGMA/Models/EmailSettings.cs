namespace DEGMA.Models
{
    public class EmailSettings
    {
        public string SenderEmail { get; set; } = "";
        public string SenderName { get; set; } = "";
        public string SmtpHost { get; set; } = "";
        public int SmtpPort { get; set; } = 587;
        public string SmtpUser { get; set; } = "";
        public string SmtpPassword { get; set; } = "";
    }
}