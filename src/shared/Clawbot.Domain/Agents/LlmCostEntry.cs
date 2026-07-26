using Clawbot.Domain.Common;

namespace Clawbot.Domain.Agents;

/// <summary>One persisted LLM-call cost row (claude_cost_ledger). Feeds the agent-cost report.</summary>
public sealed class LlmCostEntry : Entity<Guid>, ITenantOwned
{
    public const string ReservationAgentCode = "__cost_reservation__";
    public const string ReservationModel = "reserved-budget";

    public Guid TenantId { get; private set; }
    public string AgentCode { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public decimal Usd { get; private set; }
    // Phiên điều phối sinh ra chi phí này (null cho gọi LLM ngoài run: chat, content thủ công...).
    public Guid? SessionId { get; private set; }

    // true = provider không trả usage nên token/cost do hệ thống đếm cục bộ. Số này THẤP HƠN hóa đơn
    // thật (không thấy reasoning token) -> báo cáo phải tách riêng và gắn nhãn, không trộn làm số thật.
    public bool IsEstimated { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private LlmCostEntry() { }

    public static LlmCostEntry Create(
        Guid tenantId,
        string agentCode,
        string model,
        int inputTokens,
        int outputTokens,
        decimal usd,
        DateTimeOffset createdAt,
        Guid? sessionId = null,
        bool isEstimated = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AgentCode = agentCode,
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            Usd = usd,
            SessionId = sessionId,
            IsEstimated = isEstimated,
            CreatedAt = createdAt,
        };

    public static LlmCostEntry CreateReservation(Guid tenantId, Guid reservationId, decimal usd, DateTimeOffset createdAt) =>
        new()
        {
            Id = reservationId,
            TenantId = tenantId,
            AgentCode = ReservationAgentCode,
            Model = ReservationModel,
            InputTokens = 0,
            OutputTokens = 0,
            Usd = Math.Max(0m, usd),
            CreatedAt = createdAt,
        };

    public void ReleaseReservation()
    {
        if (!string.Equals(AgentCode, ReservationAgentCode, StringComparison.Ordinal))
            throw new InvalidOperationException("Only cost reservation rows can be released.");

        Usd = 0m;
    }

    public void ApplyReservation(
        string agentCode,
        string model,
        int inputTokens,
        int outputTokens,
        decimal usd,
        Guid? sessionId = null,
        bool isEstimated = false)
    {
        if (!string.Equals(AgentCode, ReservationAgentCode, StringComparison.Ordinal))
            throw new InvalidOperationException("Only cost reservation rows can be applied.");

        AgentCode = agentCode;
        Model = model;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        Usd = Math.Max(0m, usd);
        SessionId = sessionId;
        IsEstimated = isEstimated;
    }
}
