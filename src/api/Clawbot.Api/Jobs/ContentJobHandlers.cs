using System.Text.Json;
using Clawbot.Agents.Contracts.Content;
using Clawbot.Agents.Contracts.Research;
using Clawbot.Api.Contracts.Content;
using Clawbot.Api.Services;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Time;
using Clawbot.SharedKernel.Jobs;

namespace Clawbot.Api.Jobs;

public sealed record ContentRepurposeJobPayload(Guid ContentItemId, IReadOnlyList<string> TargetPlatforms);

public sealed record ContentTrendScanJobPayload(string WeekOf);

public sealed record ContentImagePromptJobPayload(GenerateImagePromptRequest Request);

public sealed record ContentRegenerateHookJobPayload(Guid ContentItemId, int HookIndex);

// Chuyển thể bài sang các nền tảng khác — mỗi nền tảng là 1 lượt gọi agent.
public sealed class ContentRepurposeJobHandler(ContentAgent.ContentAgentClient grpc) : IJobHandler
{
    public const string JobType = "content.repurpose";

    public string Type => JobType;

    public async Task<JobResult> RunAsync(JobContext ctx, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<ContentRepurposeJobPayload>(ctx.PayloadJson)
            ?? throw new InvalidOperationException("Thiếu bài gốc cho việc chuyển thể.");

        var req = new RepurposeRequest
        {
            TenantId = ctx.TenantId.ToString(),
            ContentId = payload.ContentItemId.ToString(),
        };
        req.TargetChannels.AddRange(payload.TargetPlatforms);

        var resp = await grpc.RepurposeAsync(req, cancellationToken: ct).ResponseAsync.ConfigureAwait(false);

        return new JobResult(
            "/content?tab=queue",
            $"Đã chuyển thể thành {resp.Variants.Count} biến thể, đang chờ duyệt.");
    }
}

// Quét xu hướng tuần (research agent + web search) — 30s đến vài phút.
public sealed class ContentTrendScanJobHandler(
    ResearchAgent.ResearchAgentClient grpc,
    IContentNotifier notifier,
    IClock clock) : IJobHandler
{
    public const string JobType = "content.trends-scan";

    public string Type => JobType;

    public async Task<JobResult> RunAsync(JobContext ctx, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<ContentTrendScanJobPayload>(ctx.PayloadJson)
            ?? throw new InvalidOperationException("Thiếu tuần cần quét.");

        var response = await grpc.WeeklyTrendsAsync(new TrendRequest
        {
            TenantId = ctx.TenantId.ToString(),
            WeekOf = payload.WeekOf,
        }, cancellationToken: ct).ResponseAsync.ConfigureAwait(false);

        // Giữ event realtime cho màn nội dung đang mở; thông báo bền do JobRunner lo.
        await notifier.NotifyTrendScanAsync(
            ctx.TenantId,
            new ContentTrendScanEvent(ctx.TenantId, response.Trends.Count, clock.UtcNow),
            ct).ConfigureAwait(false);

        return new JobResult(
            "/content?tab=trends",
            $"Đã quét xong tuần {payload.WeekOf}: {response.Trends.Count} xu hướng.");
    }
}

// Sinh prompt ảnh cho bài đăng.
public sealed class ContentImagePromptJobHandler(ContentImagePromptService service) : IJobHandler
{
    public const string JobType = "content.image-prompt";

    public string Type => JobType;

    public async Task<JobResult> RunAsync(JobContext ctx, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<ContentImagePromptJobPayload>(ctx.PayloadJson)
            ?? throw new InvalidOperationException("Thiếu dữ liệu cho việc sinh prompt ảnh.");

        var result = await service.GenerateAsync(ctx.TenantId, payload.Request, ct).ConfigureAwait(false);

        return new JobResult("/content?tab=queue", result.Prompt);
    }
}

// Đổi hook (P5, §4.5): chạy lại L3+L4 với hook marketer chọn, sửa bài tại chỗ (revision mới + chờ review lại).
public sealed class ContentRegenerateHookJobHandler(ContentAgent.ContentAgentClient grpc) : IJobHandler
{
    public const string JobType = "content.regenerate-hook";

    public string Type => JobType;

    public async Task<JobResult> RunAsync(JobContext ctx, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<ContentRegenerateHookJobPayload>(ctx.PayloadJson)
            ?? throw new InvalidOperationException("Thiếu bài hoặc hook cho việc đổi hook.");

        var resp = await grpc.RegenerateHookAsync(new RegenerateHookRequest
        {
            TenantId = ctx.TenantId.ToString(),
            ContentId = payload.ContentItemId.ToString(),
            HookIndex = payload.HookIndex,
        }, cancellationToken: ct).ResponseAsync.ConfigureAwait(false);

        return new JobResult(
            $"/content?tab=queue&itemId={resp.ContentId}",
            "Đã đổi hook, bài chờ duyệt lại.");
    }
}
