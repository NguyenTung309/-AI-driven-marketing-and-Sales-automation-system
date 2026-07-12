using Clawbot.Agents.Contracts.Ads;
using Clawbot.Agents.Core.Ads;
using Clawbot.Domain.Ads;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Notifications;
using Clawbot.SharedKernel.Time;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CoreAds = Clawbot.Agents.Core.Ads;

namespace Clawbot.AgentService.Services;

public sealed partial class AdsAgentGrpcService(
    CoreAds.AdsAgent agent,
    AppDbContext db,
    INotificationPublisher publisher,
    IClock clock,
    ILogger<AdsAgentGrpcService> logger) : Clawbot.Agents.Contracts.Ads.AdsAgent.AdsAgentBase
{
    private readonly CoreAds.AdsAgent _agent = agent;
    private readonly AppDbContext _db = db;
    private readonly INotificationPublisher _publisher = publisher;
    private readonly IClock _clock = clock;
    private readonly ILogger<AdsAgentGrpcService> _logger = logger;

    public override async Task<AdsEvaluateResponse> Evaluate(AdsEvaluateRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenantId = ParseTenantId(request.TenantId);
        if (!Guid.TryParse(request.CampaignId, out var campaignId) || campaignId == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "campaign_id required"));

        var campaign = await _db.AdsCampaigns.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == campaignId, context.CancellationToken)
            .ConfigureAwait(false)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "campaign not found"));

        var rules = await _db.AdsRules.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.IsActive
                && string.Equals(r.Platform, campaign.Platform, StringComparison.OrdinalIgnoreCase))
            .ToListAsync(context.CancellationToken).ConfigureAwait(false);

        var last3Days = await _db.AdsMetricsDailies.IgnoreQueryFilters()
            .Where(m => m.CampaignId == campaignId)
            .OrderByDescending(m => m.MetricDate)
            .Take(3)
            .Select(m => new AdsMetricSnapshot(
                m.Cpl ?? 0, m.Frequency ?? 0, m.Ctr ?? 0, m.Spend ?? 0, campaign.DailyBudget ?? 0))
            .ToListAsync(context.CancellationToken).ConfigureAwait(false);

        var nowGmt7 = _clock.UtcNow;
        var decisions = await _agent.EvaluateCampaignAsync(
            campaign, rules, last3Days, null, nowGmt7, context.CancellationToken).ConfigureAwait(false);

        var executed = new List<AdsActionExecuted>();
        foreach (var decision in decisions)
        {
            var action = AdsAction.Create(
                tenantId, campaignId, decision.RuleId, decision.Action, decision.Note, _clock.UtcNow);
            _db.AdsActions.Add(action);

            // Ads-1: proactive budget alert is not a campaign action — notify MKT Lead/Admin and skip the connector.
            if (decision.Action == "alert")
            {
                await _publisher.PublishAsync(new NotificationRequest(
                    tenantId, null, "ads_budget", "Ngân sách quảng cáo đạt ngưỡng (90%)",
                    Severity: "warning",
                    Body: $"Chiến dịch {campaignId} đã chi {decision.Note}. Kiểm tra Quảng cáo.",
                    Link: "/ads"), context.CancellationToken).ConfigureAwait(false);
                executed.Add(new AdsActionExecuted
                {
                    RuleId = string.Empty,
                    ActionTaken = decision.Action,
                    Note = decision.Note,
                });
                LogActionExecuted(_logger, tenantId, campaignId, decision.Action, decision.Note);
                continue;
            }

            var applied = await _agent.ApplyActionAsync(
                tenantId, campaign.Platform, campaign.ExternalCampaignId, decision.Action, null, context.CancellationToken).ConfigureAwait(false);

            if (applied && decision.Action is "pause" or "scale_up" or "scale_down")
            {
                if (decision.Action == "pause")
                    campaign.Pause(_clock.UtcNow);
                else if (decision.Action == "scale_up" && campaign.DailyBudget.HasValue)
                    campaign.ScaleBudget(campaign.DailyBudget.Value * 1.2m, _clock.UtcNow);
                else if (decision.Action == "scale_down" && campaign.DailyBudget.HasValue)
                    campaign.ScaleBudget(campaign.DailyBudget.Value * 0.8m, _clock.UtcNow);
            }

            executed.Add(new AdsActionExecuted
            {
                RuleId = decision.RuleId?.ToString() ?? string.Empty,
                ActionTaken = decision.Action,
                Note = decision.Note,
            });

            LogActionExecuted(_logger, tenantId, campaignId, decision.Action, decision.Note);
        }

        await _db.SaveChangesAsync(context.CancellationToken).ConfigureAwait(false);

        var response = new AdsEvaluateResponse();
        response.Actions.AddRange(executed);
        return response;
    }

    public override async Task<AdsLookalikeResponse> BuildLookalike(AdsLookalikeRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenantId = ParseTenantId(request.TenantId);
        if (request.SeedContactKeys.Count == 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "seed_contact_keys required"));

        var audienceId = await _agent.BuildLookalikeAsync(
            tenantId, request.Platform, request.SeedContactKeys.ToList(), context.CancellationToken).ConfigureAwait(false);

        return new AdsLookalikeResponse
        {
            AudienceId = audienceId ?? string.Empty,
            Created = audienceId is not null,
        };
    }

    public override async Task<AdsRemarketResponse> Remarket(AdsRemarketRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenantId = ParseTenantId(request.TenantId);
        if (request.ContactKeys.Count == 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "contact_keys required"));

        var success = await _agent.BuildRemarketingAsync(
            tenantId, request.Platform, request.AudienceName, request.ContactKeys.ToList(), context.CancellationToken).ConfigureAwait(false);

        return new AdsRemarketResponse { Success = success };
    }

    public override async Task<AdsSignalResponse> HandleSignal(AdsSignalRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenantId = ParseTenantId(request.TenantId);

        if (request.SignalType == "budget_threshold")
        {
            LogBudgetAlert(_logger, tenantId, request.CampaignId, request.PayloadJson);
            await _publisher.PublishAsync(new NotificationRequest(
                tenantId, null, "ads_budget", "Ngân sách quảng cáo đạt ngưỡng (90%)",
                Severity: "warning",
                Body: $"Chiến dịch {request.CampaignId} đã chạm ngưỡng ngân sách — kiểm tra Quảng cáo.",
                Link: "/ads"), context.CancellationToken).ConfigureAwait(false);
            return new AdsSignalResponse { Handled = true, ActionTaken = "alert" };
        }

        return new AdsSignalResponse { Handled = false, Error = "unknown signal type" };
    }

    private static Guid ParseTenantId(string tenantId)
    {
        if (!Guid.TryParse(tenantId, out var parsed) || parsed == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "tenant_id required"));
        return parsed;
    }

    [LoggerMessage(EventId = 5301, Level = LogLevel.Information, Message = "Ads action executed for tenant {TenantId} campaign {CampaignId}: {Action} ({Note})")]
    private static partial void LogActionExecuted(ILogger logger, Guid tenantId, Guid campaignId, string action, string note);

    [LoggerMessage(EventId = 5302, Level = LogLevel.Warning, Message = "Budget alert for tenant {TenantId} campaign {CampaignId}: {Payload}")]
    private static partial void LogBudgetAlert(ILogger logger, Guid tenantId, string campaignId, string payload);
}
