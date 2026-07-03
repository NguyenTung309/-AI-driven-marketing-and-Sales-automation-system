namespace Clawbot.SharedKernel.Orchestration;

/// <summary>
/// Event keys that can trigger an AgentSchedule (TriggerType = "event"). Every key listed here
/// MUST have a dispatcher call site (ScheduleEventDispatcher.FireAsync) — do not add keys that
/// nothing fires, the FE offers this list to users.
/// </summary>
public static class ScheduleEventKeys
{
    /// <summary>Fired after a tenant trend scan persists its briefs (TrendScanService).</summary>
    public const string TrendsScanned = "content.trends.scanned";

    /// <summary>Fired when a lead crosses into 'hot' (LeadBecameHotConsumer).</summary>
    public const string LeadBecameHot = "lead.became_hot";

    /// <summary>Fired when a scheduled publish exhausts its retries (ContentPublishJob).</summary>
    public const string ContentPublishFailed = "content.publish.failed";

    public static readonly string[] All = [TrendsScanned, LeadBecameHot, ContentPublishFailed];
}
