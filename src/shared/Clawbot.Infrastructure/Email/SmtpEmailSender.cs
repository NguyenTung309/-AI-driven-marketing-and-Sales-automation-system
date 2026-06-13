using System.Net;
using System.Net.Mail;
using Clawbot.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Email;

/// <summary>
/// SMTP email via BCL <see cref="SmtpClient"/> (no extra NuGet → clears the audit gate).
/// Config-gated: if <c>Email:Smtp:Host</c> is unset it logs + no-ops (dev stays green).
/// </summary>
public sealed partial class SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(string recipient, string subject, string body, CancellationToken ct = default)
    {
        var host = config["Email:Smtp:Host"];
        if (string.IsNullOrWhiteSpace(host))
        {
            LogSkipped(logger, recipient, subject);
            return;
        }

        var port = int.TryParse(config["Email:Smtp:Port"], out var p) ? p : 587;
        var from = config["Email:Smtp:From"] ?? "no-reply@hoc-ba.edu.vn";

        using var message = new MailMessage(from, recipient, subject, body);
        using var client = new SmtpClient(host, port);
        var user = config["Email:Smtp:User"];
        if (!string.IsNullOrWhiteSpace(user))
            client.Credentials = new NetworkCredential(user, config["Email:Smtp:Password"]);
        client.EnableSsl = !string.Equals(config["Email:Smtp:UseSsl"], "false", StringComparison.OrdinalIgnoreCase);

        await client.SendMailAsync(message, ct).ConfigureAwait(false);
        LogSent(logger, recipient, subject);
    }

    [LoggerMessage(EventId = 2101, Level = LogLevel.Information,
        Message = "Email skipped (SMTP not configured): recipient={Recipient} subject={Subject}")]
    private static partial void LogSkipped(ILogger logger, string recipient, string subject);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Information,
        Message = "Email sent: recipient={Recipient} subject={Subject}")]
    private static partial void LogSent(ILogger logger, string recipient, string subject);
}
