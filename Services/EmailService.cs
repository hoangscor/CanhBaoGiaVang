using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


using System.Net;
using System.Net.Mail;
using System.Threading;
using GoldPriceAlertWinForms.Models;

namespace GoldPriceAlertWinForms.Services
{
    public sealed class EmailService
    {
        public Task SendGmailAsync(EmailSettings settings, string subject, string body, CancellationToken ct)
        {
            if (!settings.Enabled) throw new InvalidOperationException("Email disabled in settings.json.");
            if (string.IsNullOrWhiteSpace(settings.FromAddress)) throw new InvalidOperationException("FromAddress empty.");
            if (string.IsNullOrWhiteSpace(settings.ToAddresses)) throw new InvalidOperationException("ToAddresses empty.");
            if (string.IsNullOrWhiteSpace(settings.AppPassword)) throw new InvalidOperationException("AppPassword empty.");

            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                var from = new MailAddress(settings.FromAddress, settings.FromDisplayName ?? settings.FromAddress);

                using var message = new MailMessage
                {
                    From = from,
                    Subject = subject ?? "",
                    Body = body ?? "",
                    IsBodyHtml = false
                };

                foreach (var to in SplitEmails(settings.ToAddresses))
                    message.To.Add(to);

                using var smtp = new SmtpClient
                {
                    Host = settings.SmtpHost,
                    Port = settings.SmtpPort,
                    EnableSsl = settings.EnableSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(from.Address, settings.AppPassword)
                };

                smtp.Send(message);
            }, ct);
        }

        private static IEnumerable<MailAddress> SplitEmails(string csv)
        {
            var parts = csv.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                var s = p.Trim();
                if (!string.IsNullOrWhiteSpace(s))
                    yield return new MailAddress(s);
            }
        }
    }
}
