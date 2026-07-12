using Clawbot.Domain.Common;

namespace Clawbot.Domain.Agents;

// ai-self-learning-memory Lớp 3: bài học nghiệp vụ tích lũy theo TỪNG agent (scope agent_code,
// không theo khách). Dùng đầu tiên cho reviewer-agent: "lỗi content hay gặp" nạp vào persona khi chấm.
// Bất biến như ContactMemory: sửa/xóa = supersede, không update-in-place.
public sealed class AgentMemory : Entity<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string AgentCode { get; private set; } = string.Empty;
    public string Fact { get; private set; } = string.Empty;
    public string Category { get; private set; } = "mistake";
    public decimal Confidence { get; private set; }
    public bool IsActive { get; private set; } = true;
    public Guid? SupersededById { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private AgentMemory() { }

    public static AgentMemory Create(
        Guid tenantId,
        string agentCode,
        string fact,
        string category,
        decimal confidence,
        DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(agentCode))
            throw new ArgumentException("agent_code_required", nameof(agentCode));
        if (string.IsNullOrWhiteSpace(fact))
            throw new ArgumentException("fact_required", nameof(fact));
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("category_required", nameof(category));

        return new AgentMemory
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AgentCode = agentCode.Trim(),
            Fact = fact.Trim(),
            Category = category.Trim(),
            Confidence = Math.Clamp(confidence, 0m, 1m),
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };
    }

    public void Supersede(Guid? supersededById, DateTimeOffset at)
    {
        if (!IsActive)
            throw new InvalidOperationException("memory_already_superseded");
        IsActive = false;
        SupersededById = supersededById;
        UpdatedAt = at;
    }
}
