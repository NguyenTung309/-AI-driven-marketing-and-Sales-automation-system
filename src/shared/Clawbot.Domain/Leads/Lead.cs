using Clawbot.Domain.Common;
using System.Text.Json;

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

    public static Lead Create(Guid tenantId, Guid contactId, string sourcePlatform, DateTimeOffset createdAt)
    {
        var lead = new Lead
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ContactId = contactId,
            SourcePlatform = sourcePlatform,
            CreatedAt = createdAt,
        };
        lead.Raise(new Events.LeadCreated(tenantId, lead.Id, contactId, sourcePlatform, createdAt));
        return lead;
    }

    public void AdjustScore(int delta, string reason, DateTimeOffset at)
    {
        var previousScore = Score;
        var previousStage = Stage;
        Score = Math.Max(0, Score + delta);
        Stage = Score switch
        {
            >= 70 => "hot",
            >= 30 => "warm",
            _     => "cold",
        };

        var metaJson = JsonSerializer.Serialize(new
        {
            previousScore,
            newScore = Score,
            delta = Score - previousScore,
            requestedDelta = delta,
            previousStage,
            newStage = Stage,
            reason,
        });
        _activities.Add(LeadActivity.Create(TenantId, Id, "score_adjust", reason, at, metaJson));
        LastActivityAt = at;

        // Raise on upward stage transition only (consumed by Lead-2 auto-assign+notify / Lead-3 drip-enroll).
        if (Stage == "hot" && previousStage != "hot")
            Raise(new Events.LeadBecameHot(TenantId, Id, OwnerUserId, Score, at));
        else if (Stage == "warm" && previousStage == "cold")
            Raise(new Events.LeadBecameWarm(TenantId, Id, Score, at));
    }

    public void Assign(Guid userId) => OwnerUserId = userId;
}
