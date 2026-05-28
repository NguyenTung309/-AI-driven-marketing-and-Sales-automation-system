using Clawbot.Domain.Common;

namespace Clawbot.Domain.SaleAssist;

public sealed class QuickReplyTemplate : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string? Category { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public string? Platforms { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private QuickReplyTemplate() { }

    public static QuickReplyTemplate Create(Guid tenantId, string code, string body, DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = code,
            Body = body,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };
}
