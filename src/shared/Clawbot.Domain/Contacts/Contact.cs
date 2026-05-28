using Clawbot.Domain.Common;

namespace Clawbot.Domain.Contacts;

public sealed class Contact : AggregateRoot<Guid>, ITenantOwned
{
    private readonly List<ContactExternalId> _externalIds = new();

    public Guid TenantId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string Locale { get; private set; } = "vi-VN";
    public int LifetimeScore { get; private set; }
    public string LifecycleStage { get; private set; } = "visitor";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public IReadOnlyCollection<ContactExternalId> ExternalIds => _externalIds.AsReadOnly();

    private Contact() { }

    public static Contact Create(Guid tenantId, string displayName, DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DisplayName = displayName,
            CreatedAt = createdAt,
        };

    public void LinkExternalId(string platform, string externalId, DateTimeOffset seenAt)
    {
        if (_externalIds.Any(x => x.Platform == platform && x.ExternalId == externalId)) return;
        _externalIds.Add(ContactExternalId.Create(Id, platform, externalId, seenAt));
    }
}
