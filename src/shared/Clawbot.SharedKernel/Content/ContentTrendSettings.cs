namespace Clawbot.SharedKernel.Content;

/// <summary>
/// Per-tenant trend-scan configuration, stored as one encrypted JSON blob in social_credentials
/// (provider = <see cref="CredentialProvider"/>). Null members mean "use the appsettings default".
/// </summary>
public sealed record ContentTrendSourceSetting(bool? Enabled = null, string? ApiKey = null, string? Url = null);

public sealed record ContentTrendSettings(
    string? Geo = null,
    ContentTrendSourceSetting? Google = null,
    ContentTrendSourceSetting? YouTube = null,
    ContentTrendSourceSetting? TikTok = null)
{
    public const string CredentialProvider = "trends";

    // AgentScheduleRunner routes schedules whose GoalTemplate equals this marker to the direct
    // trend-scan path (no LLM orchestration).
    public const string ScheduleGoalMarker = "[trend-scan]";

    public static readonly string[] AllowedScheduleCadences = ["daily", "weekly", "monthly"];
}
