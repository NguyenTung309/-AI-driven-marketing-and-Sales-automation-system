using Clawbot.Domain.Common;

namespace Clawbot.Domain.Leads;

public sealed class Lead : AggregateRoot<Guid>, ITenantOwned
{
    private readonly List<LeadActivity> _activities = new();

    public Guid TenantId { get; private set; }
    public Guid? ContactId { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public int Score { get; private set; }
    public string Stage { get; private set; } = "cold";    // cold|warm|hot|customer|lost
    public string? SourcePlatform { get; private set; }
    public DateTimeOffset? LastActivityAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public IReadOnlyCollection<LeadActivity> Activities => _activities.AsReadOnly();

    private Lead() { }

    public static Lead Create(Guid tenantId, Guid contactId, string sourcePlatform, DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ContactId = contactId,
            SourcePlatform = sourcePlatform,
            CreatedAt = createdAt,
        };

    public void AdjustScore(int delta, string reason, DateTimeOffset at)
    {
        Score = Math.Max(0, Score + delta);
        Stage = Score switch
        {
            >= 70 => "hot",
            >= 30 => "warm",
            _     => "cold",
        };
        _activities.Add(LeadActivity.Create(TenantId, Id, "score_adjust", reason, at));
        LastActivityAt = at;
    }

    public void Assign(Guid userId) => OwnerUserId = userId;
}
