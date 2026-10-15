using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using DEGMA.Models;
using Microsoft.Extensions.Options;

namespace DEGMA.Services
{
    public class EmailService
    {
        private readonly EmailSettings _settings;

        public EmailService(IOptions<EmailSettings> settings)
        {
            _settings = settings.Value;
        }

        public async Task SendReportAsync(
            string recipientEmail,
            string recipientName,
            string subject,
            string body,
            byte[] pdfAttachment,
            string attachmentFilename)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_settings.SenderName, _settings.SenderEmail));
            message.To.Add(new MailboxAddress(recipientName, recipientEmail));
            message.Subject = subject;

            var builder = new BodyBuilder { TextBody = body };
            builder.Attachments.Add(attachmentFilename, pdfAttachment,
                new ContentType("application", "pdf"));

            message.Body = builder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort,
                SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(_settings.SmtpUser, _settings.SmtpPassword);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }
}