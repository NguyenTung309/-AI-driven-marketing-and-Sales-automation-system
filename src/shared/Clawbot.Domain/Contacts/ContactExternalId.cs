using Clawbot.Domain.Common;

namespace Clawbot.Domain.Contacts;

public sealed class ContactExternalId : Entity<Guid>
{
    public Guid ContactId { get; private set; }
    public string Platform { get; private set; } = string.Empty;
    public string ExternalId { get; private set; } = string.Empty;
    public DateTimeOffset FirstSeenAt { get; private set; }

    private ContactExternalId() { }

    public static ContactExternalId Create(Guid contactId, string platform, string externalId, DateTimeOffset firstSeenAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            ContactId = contactId,
            Platform = platform,
            ExternalId = externalId,
            FirstSeenAt = firstSeenAt,
        };
}
