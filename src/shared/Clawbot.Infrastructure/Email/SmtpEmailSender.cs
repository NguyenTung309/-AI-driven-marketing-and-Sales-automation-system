using System.Net;
using System.Net.Mail;
using Clawbot.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawbot.Infrastructure.Email;

/// <summary>
/// SMTP email via BCL <see cref="SmtpClient"/> (no extra NuGet → clears the audit gate).
/// Config-gated via <see cref="SmtpOptions"/>: if Host is unset it logs + no-ops (dev stays green).
/// </summary>
public sealed partial class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly SmtpOptions _opts = options.Value;

    public async Task SendAsync(string recipient, string subject, string body, CancellationToken ct = default) =>
        await SendAsync(recipient, subject, body, Array.Empty<EmailAttachment>(), ct).ConfigureAwait(false);

    public async Task SendAsync(
        string recipient,
        string subject,
        string body,
        IReadOnlyList<EmailAttachment> attachments,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.Host))
        {
            LogSkipped(logger, recipient, subject);
            return;
        }

        using var message = new MailMessage(_opts.From, recipient, subject, body);
        foreach (var att in attachments ?? Array.Empty<EmailAttachment>())
        {
            var stream = new MemoryStream(att.Content, writable: false);
            message.Attachments.Add(new Attachment(stream, att.FileName, att.ContentType));
        }

        using var client = new SmtpClient(_opts.Host, _opts.Port);
        if (!string.IsNullOrWhiteSpace(_opts.User))
            client.Credentials = new NetworkCredential(_opts.User, _opts.Password);
        client.EnableSsl = _opts.UseSsl;

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
