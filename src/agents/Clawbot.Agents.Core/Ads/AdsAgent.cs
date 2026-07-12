using Clawbot.Domain.Ads;

namespace Clawbot.Agents.Core.Ads;

public sealed record AdsMetricSnapshot(
    decimal Cpl,
    decimal Frequency,
    decimal Ctr,
    decimal Spend,
    decimal DailyBudget);

public sealed record AdsDecision(
    Guid? RuleId,
    string Metric,
    string Action,
    string Note);

public interface IAdsPlatformConnector
{
    string Platform { get; }
    Task<AdsMetricSnapshot?> FetchMetricsAsync(Guid tenantId, string externalCampaignId, CancellationToken ct = default);
    Task<bool> ApplyActionAsync(Guid tenantId, string externalCampaignId, string action, decimal? newBudget, CancellationToken ct = default);
    Task<string?> BuildLookalikeAsync(Guid tenantId, IReadOnlyList<string> seedContactKeys, CancellationToken ct = default);
    Task<bool> BuildRemarketingAsync(Guid tenantId, string audienceName, IReadOnlyList<string> contactKeys, CancellationToken ct = default);
}

public interface IAdsConnectorResolver
{
    IAdsPlatformConnector? Resolve(string platform);
}

public sealed class AdsAgent(IAdsConnectorResolver connectorResolver)
{
    private readonly IAdsConnectorResolver _connectorResolver = connectorResolver;

    public async Task<IReadOnlyList<AdsDecision>> EvaluateCampaignAsync(
        AdsCampaign campaign,
        IReadOnlyList<AdsRule> rules,
        IReadOnlyList<AdsMetricSnapshot> last3Days,
        DateTimeOffset? lastScaledAt,
        DateTimeOffset nowGmt7,
        CancellationToken ct = default)
    {
        var connector = _connectorResolver.Resolve(campaign.Platform);
        if (connector is null)
            return [];

        var snapshot = await connector.FetchMetricsAsync(campaign.TenantId, campaign.ExternalCampaignId, ct).ConfigureAwait(false);
        if (snapshot is null)
            return [];

        return AdsRuleEngine.Evaluate(
            snapshot,
            campaign.TargetCpl,
            last3Days,
            lastScaledAt,
            nowGmt7,
            rules);
    }

    public async Task<bool> ApplyActionAsync(
        Guid tenantId,
        string platform,
        string externalCampaignId,
        string action,
        decimal? newBudget,
        CancellationToken ct = default)
    {
        var connector = _connectorResolver.Resolve(platform);
        if (connector is null)
            return false;

        return await connector.ApplyActionAsync(tenantId, externalCampaignId, action, newBudget, ct).ConfigureAwait(false);
    }

    public async Task<string?> BuildLookalikeAsync(
        Guid tenantId,
        string platform,
        IReadOnlyList<string> seedContactKeys,
        CancellationToken ct = default)
    {
        var connector = _connectorResolver.Resolve(platform);
        if (connector is null)
            return null;

        return await connector.BuildLookalikeAsync(tenantId, seedContactKeys, ct).ConfigureAwait(false);
    }

    public async Task<bool> BuildRemarketingAsync(
        Guid tenantId,
        string platform,
        string audienceName,
        IReadOnlyList<string> contactKeys,
        CancellationToken ct = default)
    {
        var connector = _connectorResolver.Resolve(platform);
        if (connector is null)
            return false;

        return await connector.BuildRemarketingAsync(tenantId, audienceName, contactKeys, ct).ConfigureAwait(false);
    }
}
