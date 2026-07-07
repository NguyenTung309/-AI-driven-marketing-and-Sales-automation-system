using Clawbot.Domain.Common;

namespace Clawbot.Domain.Channels;

public sealed class Inbox : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Platform { get; private set; } = string.Empty;
    public string ExternalPageId { get; private set; } = string.Empty;
    public string? AvatarUrl { get; private set; }
    public string? EncryptedAccessToken { get; private set; }
    public string? SenderId { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private Inbox() { }

    public void UpdateName(string name, DateTimeOffset at)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        UpdatedAt = at;
    }

    public static Inbox Create(Guid tenantId, string name, string platform, string externalPageId)
    {
        var now = DateTimeOffset.UtcNow;
        return new Inbox
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Platform = platform,
            ExternalPageId = externalPageId,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void SetSenderId(string? senderId)
    {
        SenderId = senderId;
    }

    public void SetAccessToken(string encryptedToken, DateTimeOffset at)
    {
        EncryptedAccessToken = encryptedToken;
        UpdatedAt = at;
    }
}
