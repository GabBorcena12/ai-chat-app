using System.Net;
using System.Net.Mail;

namespace AIChatApp.API.Services.Authentication
{
    /// <summary>
    /// Sends account-related email through the configured SMTP server.
    /// Keep SMTP credentials in configuration providers and let callers decide which account workflow requires a message.
    /// </summary>
    public class EmailService
    {
        private readonly IConfiguration _config;

        public EmailService(IConfiguration config)
        {
            _config = config;
        }

        public async Task<bool> TestAccountAsync()
        {
            try
            {
                var settings = _config.GetSection("EmailSettings");

                using var client = new SmtpClient(settings["SmtpServer"], int.Parse(settings["Port"]))
                {
                    Credentials = new NetworkCredential(settings["Username"], settings["AppPassword"]),
                    EnableSsl = true
                };

                // Use "EHLO" via SendMailAsync with a test message to self
                using var message = new MailMessage(settings["From"], settings["From"], "Test Email", "This is a test email to verify SMTP login.");

                await client.SendMailAsync(message);

                Console.WriteLine("✅ SMTP login successful and test email sent!");
                return true;
            }
            catch (SmtpException ex)
            {
                Console.WriteLine($"❌ SMTP login failed: {ex.Message}");
                return false;
            }
        }

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            var settings = _config.GetSection("EmailSettings");

            var smtpClient = new SmtpClient(settings["SmtpServer"])
            {
                Port = int.Parse(settings["Port"]),
                Credentials = new NetworkCredential(
                    settings["Username"],
                    settings["AppPassword"]
                ),
                EnableSsl = true
            };

            var mail = new MailMessage
            {
                From = new MailAddress(settings["From"]),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };
            mail.To.Add(toEmail);
            await smtpClient.SendMailAsync(mail);
        }
    }
}
