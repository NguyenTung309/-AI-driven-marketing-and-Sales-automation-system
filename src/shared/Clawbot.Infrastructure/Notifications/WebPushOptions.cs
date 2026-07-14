namespace Clawbot.Infrastructure.Notifications;

/// <summary>
/// VAPID keys cho Web Push. PrivateKey là secret — nạp qua env/secret manager, KHÔNG commit.
/// Thiếu key = tắt web push (feed + toast vẫn chạy).
/// </summary>
public sealed class WebPushOptions
{
    public const string SectionName = "WebPush";

    public string PublicKey { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;
    public string Subject { get; set; } = "mailto:admin@clawbot.local";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);
}
