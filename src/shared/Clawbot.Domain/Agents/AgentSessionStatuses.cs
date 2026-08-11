namespace Clawbot.Domain.Agents;

public static class AgentSessionStatuses
{
    public const string Draft = "draft";
    public const string PendingApproval = "pending_approval";
    public const string Running = "running";
    public const string PauseRequested = "pause_requested";
    public const string Paused = "paused";
    public const string Cancelling = "cancelling";
    public const string Failing = "failing";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}
