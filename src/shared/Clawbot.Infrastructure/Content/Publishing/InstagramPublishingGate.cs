namespace Clawbot.Infrastructure.Content.Publishing;

/// <summary>
/// Temporary fail-closed gate for scheduled Instagram attempts until snapshots include a public provider media URL.
/// Direct native publishing bypasses this gate; scheduler and publish-job checks must remain until media resolution lands.
/// </summary>
public static class InstagramPublishingGate
{
    public const string ErrorCode = "instagram_not_configured";

    public static bool IsBlocked(string? platform) =>
        string.Equals(platform?.Trim(), "instagram", StringComparison.OrdinalIgnoreCase);
}
