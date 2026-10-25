using System.Net;
using System.Net.Mail;

namespace QLDACNTTNhom10.Services
{
    public class EmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration config, ILogger<EmailService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
        {
            bool enableRealEmail = _config.GetValue<bool>("SmtpSettings:EnableRealEmail");

            // Chế độ giả lập (Sandbox) giúp test nhanh không cần tài khoản SMTP thật
            if (!enableRealEmail)
            {
                _logger.LogInformation($"[MOCK EMAIL] To: {toEmail} | Subject: {subject}");
                return;
            }

            string host = _config["SmtpSettings:Host"]!;
            int port = _config.GetValue<int>("SmtpSettings:Port");
            string senderName = _config["SmtpSettings:SenderName"]!;
            string senderEmail = _config["SmtpSettings:SenderEmail"]!;
            string password = _config["SmtpSettings:AppPassword"]!;

            using var client = new SmtpClient(host, port)
            {
                Credentials = new NetworkCredential(senderEmail, password),
                EnableSsl = true
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(senderEmail, senderName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            mailMessage.To.Add(toEmail);

            await client.SendMailAsync(mailMessage);
            _logger.LogInformation($"[SMTP SENT] Đã gửi email thành công tới {toEmail}");
        }
    }
}