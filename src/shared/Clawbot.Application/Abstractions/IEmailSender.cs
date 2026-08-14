namespace Clawbot.Application.Abstractions;

/// <summary>Sends transactional email (password reset, onboarding). Config-gated; no-op when SMTP unset.</summary>
public interface IEmailSender
{
    Task SendAsync(string recipient, string subject, string body, CancellationToken ct = default);

    /// <summary>Sends an email with optional file attachments. Default impl drops attachments for compatibility.</summary>
    Task SendAsync(
        string recipient,
        string subject,
        string body,
        IReadOnlyList<EmailAttachment> attachments,
        CancellationToken ct = default) =>
        SendAsync(recipient, subject, body, ct);
}

/// <summary>In-memory representation of a file attached to an outgoing email.</summary>
public sealed record EmailAttachment(string FileName, byte[] Content, string ContentType);
