using System.Text.Json;
using Clawbot.Agents.Contracts.Ads;
using Clawbot.SharedKernel.Jobs;

namespace Clawbot.Api.Jobs;

public sealed record AdsEvaluateJobPayload(Guid CampaignId, string Platform);

public sealed record AdsLookalikeJobPayload(string Platform);

// Đánh giá campaign theo rule + hành động của AdsAgent (5-60s).
public sealed class AdsEvaluateJobHandler(AdsAgent.AdsAgentClient client) : IJobHandler
{
    public const string JobType = "ads.evaluate";

    public string Type => JobType;

    public async Task<JobResult> RunAsync(JobContext ctx, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<AdsEvaluateJobPayload>(ctx.PayloadJson)
            ?? throw new InvalidOperationException("Thiếu campaign cho việc đánh giá quảng cáo.");

        var response = await client.EvaluateAsync(new AdsEvaluateRequest
        {
            TenantId = ctx.TenantId.ToString(),
            Platform = payload.Platform,
            CampaignId = payload.CampaignId.ToString(),
        }, cancellationToken: ct).ConfigureAwait(false);

        var actions = response.Actions.Count;
        return new JobResult(
            "/ads",
            actions == 0
                ? "Đã đánh giá campaign: không có hành động nào cần làm."
                : $"Đã đánh giá campaign và thực hiện {actions} hành động.");
    }
}

// Dựng tệp lookalike từ seed contact.
public sealed class AdsLookalikeJobHandler(AdsAgent.AdsAgentClient client) : IJobHandler
{
    public const string JobType = "ads.lookalike";

    public string Type => JobType;

    public async Task<JobResult> RunAsync(JobContext ctx, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<AdsLookalikeJobPayload>(ctx.PayloadJson)
            ?? throw new InvalidOperationException("Thiếu nền tảng cho việc dựng tệp lookalike.");

        var response = await client.BuildLookalikeAsync(new AdsLookalikeRequest
        {
            TenantId = ctx.TenantId.ToString(),
            Platform = payload.Platform,
        }, cancellationToken: ct).ConfigureAwait(false);

        return new JobResult(
            "/ads",
            response.Created
                ? $"Đã tạo tệp lookalike {response.AudienceId}."
                : "Nền tảng chưa trả về tệp lookalike nào.");
    }
}
