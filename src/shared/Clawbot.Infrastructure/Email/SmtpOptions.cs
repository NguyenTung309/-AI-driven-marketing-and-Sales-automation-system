namespace Clawbot.Infrastructure.Email;

// Config module for outbound SMTP (external service). Bind from "Email:Smtp".
// Config-gated: when Host is empty the sender no-ops.
public sealed class SmtpOptions
{
    public const string SectionName = "Email:Smtp";

    public string? Host { get; init; }
    public int Port { get; init; } = 587;
    public string From { get; init; } = "no-reply@hoc-ba.edu.vn";
    public string? User { get; init; }
    public string? Password { get; init; }
    public bool UseSsl { get; init; } = true;
}
