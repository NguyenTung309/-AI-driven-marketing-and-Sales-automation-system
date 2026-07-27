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
        // Out-of-order: không lùi LastActivityAt; message cũ không được reactivated lost.
        var isStale = LastActivityAt is { } prev && prev > at;
        if (isStale)
            at = LastActivityAt!.Value;

        var previousScore = Score;
        var previousStage = Stage;
        Score = Math.Max(0, Score + delta);

        var isTerminal = previousStage is "customer" or "lost";
        var isReactivating = previousStage == "lost" && delta > 0 && !isStale;
        if (!isTerminal || isReactivating)
            Stage = PipelineStageFromScore(Score);

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

        if (isReactivating)
        {
            AddStageChangeActivity(previousStage, Stage, reason, at, byUserId: null, "reactivated");
            Raise(new Events.LeadReactivated(TenantId, Id, OwnerUserId, Score, at));
        }

        // Raise on upward stage transition only (consumed by Lead-2 auto-assign+notify / Lead-3 drip-enroll).
        if (!isReactivating && Stage == "hot" && previousStage != "hot")
            Raise(new Events.LeadBecameHot(TenantId, Id, OwnerUserId, Score, at));
        else if (!isReactivating && Stage == "warm" && previousStage == "cold")
            Raise(new Events.LeadBecameWarm(TenantId, Id, Score, at));
    }

    public void MarkCustomer(
        string reason,
        DateTimeOffset at,
        Guid? byUserId = null,
        string trigger = "manual")
    {
        if (Stage == "customer")
            return;

        var previousStage = Stage;
        Stage = "customer";
        LastActivityAt = at;
        AddStageChangeActivity(previousStage, Stage, reason, at, byUserId, trigger);
        Raise(new Events.LeadBecameCustomer(TenantId, Id, OwnerUserId, Score, at));
    }

    public void MarkLost(
        string reason,
        DateTimeOffset at,
        Guid? byUserId = null,
        string trigger = "manual")
    {
        if (Stage == "lost")
            return;

        var previousStage = Stage;
        Stage = "lost";
        AddStageChangeActivity(previousStage, Stage, reason, at, byUserId, trigger);
    }

    public void ReopenStage(string reason, DateTimeOffset at, Guid? byUserId)
    {
        if (Stage is not ("customer" or "lost"))
            return;

        var previousStage = Stage;
        Stage = PipelineStageFromScore(Score);
        LastActivityAt = at;
        AddStageChangeActivity(previousStage, Stage, reason, at, byUserId, "manual");
    }

    public bool ReactivateFromInbound(DateTimeOffset at)
    {
        if (Stage != "lost")
            return false;
        // Message cũ / out-of-order không được hồi sinh và không kéo LastActivityAt lùi.
        if (LastActivityAt is { } prev && prev >= at)
            return false;

        var previousStage = Stage;
        Stage = PipelineStageFromScore(Score);
        LastActivityAt = at;
        AddStageChangeActivity(previousStage, Stage, "customer_inbound", at, byUserId: null, "reactivated");
        Raise(new Events.LeadReactivated(TenantId, Id, OwnerUserId, Score, at));
        return true;
    }

    /// <summary>
    /// Tin inbound (kể cả không match scoring rule) vẫn reset đồng hồ im lặng —
    /// tránh auto-lost khi khách nhắn "ok" / "dạ" không sinh delta.
    /// lost → ReactivateFromInbound; customer/pipeline → chỉ bump LastActivityAt.
    /// </summary>
    public bool TouchInboundActivity(DateTimeOffset at)
    {
        if (Stage == "lost")
            return ReactivateFromInbound(at);

        if (LastActivityAt is { } prev && prev >= at)
            return false;

        LastActivityAt = at;
        return true;
    }

    public void Assign(Guid userId) => OwnerUserId = userId;

    private void AddStageChangeActivity(
        string previousStage,
        string newStage,
        string reason,
        DateTimeOffset at,
        Guid? byUserId,
        string trigger)
    {
        var metaJson = JsonSerializer.Serialize(new
        {
            previousStage,
            newStage,
            byUserId,
            trigger,
        });
        _activities.Add(LeadActivity.Create(TenantId, Id, "stage_change", reason, at, metaJson));
    }

    private static string PipelineStageFromScore(int score) => score switch
    {
        >= 70 => "hot",
        >= 30 => "warm",
        _ => "cold",
    };
}
