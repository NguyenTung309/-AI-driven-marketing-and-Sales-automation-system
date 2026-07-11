using Clawbot.Domain.Common;

namespace Clawbot.Domain.Contacts;

// 1 fact AI ghi nhớ về khách (ai-self-learning-memory Lớp 2). Bất biến: memory-ops UPDATE/DELETE
// không sửa đè mà hạ is_active + trỏ bản thay thế — giữ vết "AI nhớ nhầm từ đâu".
// Fact PHẢI được PII-redact trước khi vào entity (giữ nghiệp vụ: trình độ, ca học, trạng thái cọc).
public sealed class ContactMemory : Entity<Guid>, ITenantOwned
{
    public const string CategoryProfile = "profile";
    public const string CategoryPreference = "preference";
    public const string CategoryCommitment = "commitment";
    public const string CategoryHistory = "history";

    public Guid TenantId { get; private set; }
    public Guid ContactId { get; private set; }
    public string Fact { get; private set; } = string.Empty;
    public string Category { get; private set; } = CategoryProfile;
    public decimal Confidence { get; private set; }
    public Guid? SourceConversationId { get; private set; }
    public bool IsActive { get; private set; } = true;
    public Guid? SupersededById { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ContactMemory() { }

    public static ContactMemory Create(
        Guid tenantId,
        Guid contactId,
        string fact,
        string category,
        decimal confidence,
        Guid? sourceConversationId,
        DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(fact))
            throw new ArgumentException("fact_required", nameof(fact));
        if (category is not (CategoryProfile or CategoryPreference or CategoryCommitment or CategoryHistory))
            throw new ArgumentException($"invalid_category:{category}", nameof(category));

        return new ContactMemory
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ContactId = contactId,
            Fact = fact.Trim(),
            Category = category,
            Confidence = Math.Clamp(confidence, 0m, 1m),
            SourceConversationId = sourceConversationId,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };
    }

    // UPDATE của memory-ops: bản cũ hạ cờ + trỏ bản mới. DELETE: supersededById null (hạ cờ không thay thế).
    public void Supersede(Guid? supersededById, DateTimeOffset at)
    {
        if (!IsActive)
            throw new InvalidOperationException("memory_already_superseded");
        IsActive = false;
        SupersededById = supersededById;
        UpdatedAt = at;
    }
}
