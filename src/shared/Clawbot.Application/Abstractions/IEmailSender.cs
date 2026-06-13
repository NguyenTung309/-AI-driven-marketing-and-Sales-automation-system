namespace Clawbot.Application.Abstractions;

/// <summary>Sends transactional email (password reset, onboarding). Config-gated; no-op when SMTP unset.</summary>
public interface IEmailSender
{
    Task SendAsync(string recipient, string subject, string body, CancellationToken ct = default);
}
