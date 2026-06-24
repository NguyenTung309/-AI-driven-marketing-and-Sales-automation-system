using Clawbot.Domain.Common;

namespace Clawbot.Domain.Agents;

public sealed class AgentA2AMessage : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid? FromAgentDefinitionId { get; private set; }
    public Guid ToAgentDefinitionId { get; private set; }
    public string TaskId { get; private set; } = string.Empty;
    public string Intent { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = "{}";
    public string Status { get; private set; } = "pending";
    public string? Error { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    private AgentA2AMessage() { }

    public static AgentA2AMessage Send(
        Guid tenantId,
        Guid sessionId,
        Guid? fromAgentDefinitionId,
        Guid toAgentDefinitionId,
        string taskId,
        string intent,
        string payloadJson,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SessionId = sessionId,
            FromAgentDefinitionId = fromAgentDefinitionId,
            ToAgentDefinitionId = toAgentDefinitionId,
            TaskId = taskId.Trim(),
            Intent = intent.Trim().ToLowerInvariant(),
            PayloadJson = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson,
            CreatedAt = createdAt,
        };

    public void Claim(DateTimeOffset at)
    {
        if (Status != "pending")
            throw new InvalidOperationException("Only pending A2A messages can be claimed.");

        Status = "processing";
        ProcessedAt = at;
        Error = null;
    }

    public void Complete(string payloadJson, DateTimeOffset at)
    {
        if (Status != "processing")
            throw new InvalidOperationException("Only processing A2A messages can be completed.");

        Status = "completed";
        PayloadJson = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson;
        ProcessedAt = at;
        Error = null;
    }

    public void Fail(string error, DateTimeOffset at)
    {
        if (Status is "completed" or "cancelled")
            throw new InvalidOperationException("Completed or cancelled A2A messages cannot fail.");

        Status = "failed";
        ProcessedAt = at;
        Error = error.Trim();
    }

    public void Cancel(DateTimeOffset at)
    {
        if (Status == "completed")
            throw new InvalidOperationException("Completed A2A messages cannot be cancelled.");

        Status = "cancelled";
        ProcessedAt = at;
    }
}
