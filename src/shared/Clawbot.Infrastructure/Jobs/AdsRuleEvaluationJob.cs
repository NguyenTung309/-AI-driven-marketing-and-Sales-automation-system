using Clawbot.Agents.Core.Ads;
using Clawbot.Domain.Ads;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class AdsRuleEvaluationJob(
    AppDbContext db,
    AdsAgent agent,
    IClock clock,
    ILogger<AdsRuleEvaluationJob> logger)
{
    private readonly AppDbContext _db = db;
    private readonly AdsAgent _agent = agent;
    private readonly IClock _clock = clock;
    private readonly ILogger<AdsRuleEvaluationJob> _logger = logger;

    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var tenantCampaigns = await _db.AdsCampaigns.IgnoreQueryFilters()
            .Where(c => c.Status != null && c.Status != "PAUSED" && !c.DaypartPaused)
            .GroupBy(c => c.TenantId)
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var group in tenantCampaigns)
        {
            var tenantId = group.Key;
            var rules = await _db.AdsRules.IgnoreQueryFilters()
                .Where(r => r.TenantId == tenantId && r.IsActive)
                .ToListAsync(ct).ConfigureAwait(false);

            foreach (var campaign in group)
            {
                try
                {
                    var last3Days = await _db.AdsMetricsDailies.IgnoreQueryFilters()
                        .Where(m => m.CampaignId == campaign.Id)
                        .OrderByDescending(m => m.MetricDate)
                        .Take(3)
                        .Select(m => new AdsMetricSnapshot(m.Cpl ?? 0, m.Frequency ?? 0, m.Ctr ?? 0, m.Spend ?? 0, campaign.DailyBudget ?? 0))
                        .ToListAsync(ct).ConfigureAwait(false);

                    var decisions = await _agent.EvaluateCampaignAsync(
                        campaign, rules, last3Days, null, now, ct).ConfigureAwait(false);

                    foreach (var decision in decisions)
                    {
                        var action = AdsAction.Create(tenantId, campaign.Id, decision.RuleId, decision.Action, decision.Note, now);
                        _db.AdsActions.Add(action);

                        await _agent.ApplyActionAsync(campaign.Platform, campaign.ExternalCampaignId, decision.Action, null, ct).ConfigureAwait(false);

                        if (decision.Action == "pause")
                            campaign.Pause(now);
                        else if (decision.Action == "scale_up" && campaign.DailyBudget.HasValue)
                            campaign.ScaleBudget(campaign.DailyBudget.Value * 1.2m, now);
                        else if (decision.Action == "scale_down" && campaign.DailyBudget.HasValue)
                            campaign.ScaleBudget(campaign.DailyBudget.Value * 0.8m, now);

                        LogActionApplied(_logger, tenantId, campaign.Id, decision.Action);
                    }

                    await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogCampaignEvalFailed(_logger, tenantId, campaign.Id, ex.Message, ex);
                }
            }
        }
    }

    [LoggerMessage(EventId = 5501, Level = LogLevel.Information, Message = "Ads action applied for tenant {TenantId} campaign {CampaignId}: {Action}")]
    private static partial void LogActionApplied(ILogger logger, Guid tenantId, Guid campaignId, string action);

    [LoggerMessage(EventId = 5502, Level = LogLevel.Warning, Message = "Ads campaign evaluation failed for tenant {TenantId} campaign {CampaignId}: {Reason}")]
    private static partial void LogCampaignEvalFailed(ILogger logger, Guid tenantId, Guid campaignId, string reason, Exception exception);
}
