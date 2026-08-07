using Clawbot.Domain.Common;

namespace Clawbot.Domain.SaleAssist;

// Cache kết quả "Gợi ý upsell" theo hội thoại: sinh 1 lần bằng Claude (qua background job),
// đọc lại miễn phí cho đến khi hội thoại có tin nhắn mới hơn SourceLastMessageAt.
public sealed class UpsellSuggestionCache : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid ConversationId { get; private set; }
    public bool Eligible { get; private set; }
    public string Suggestion { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public int LeadScore { get; private set; }
    public DateTimeOffset GeneratedAt { get; private set; }
    // LastMessageAt ?? CreatedAt của hội thoại tại thời điểm sinh — mốc xét staleness.
    public DateTimeOffset SourceLastMessageAt { get; private set; }

    private UpsellSuggestionCache() { }

    public static UpsellSuggestionCache Create(
        Guid tenantId,
        Guid conversationId,
        bool eligible,
        string suggestion,
        string reason,
        int leadScore,
        DateTimeOffset generatedAt,
        DateTimeOffset sourceLastMessageAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ConversationId = conversationId,
            Eligible = eligible,
            Suggestion = suggestion,
            Reason = reason,
            LeadScore = leadScore,
            GeneratedAt = generatedAt,
            SourceLastMessageAt = sourceLastMessageAt,
        };

    public void Update(
        bool eligible,
        string suggestion,
        string reason,
        int leadScore,
        DateTimeOffset generatedAt,
        DateTimeOffset sourceLastMessageAt)
    {
        Eligible = eligible;
        Suggestion = suggestion;
        Reason = reason;
        LeadScore = leadScore;
        GeneratedAt = generatedAt;
        SourceLastMessageAt = sourceLastMessageAt;
    }
}
