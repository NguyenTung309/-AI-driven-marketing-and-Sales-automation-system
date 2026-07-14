using System.Text.Json;
using Clawbot.Agents.Contracts.SaleAssist;
using Clawbot.Api.Contracts.SaleAssist;
using Clawbot.SharedKernel.Jobs;

namespace Clawbot.Api.Jobs;

public sealed record SaleAssistConversationJobPayload(Guid ConversationId);

// 3 việc trợ lý sale đều là LLM: soạn nháp, tóm tắt hội thoại, gợi ý upsell.
// NotifyOnSuccess=false: sale đang mở hội thoại và nhìn màn hình chờ — kết quả đổ thẳng vào panel,
// rung chuông mỗi lần bấm là spam. Việc vẫn hiện trong "Việc đang chạy" và lỗi thì vẫn báo.

public sealed class SaleAssistDraftJobHandler(SaleAssistAgent.SaleAssistAgentClient grpc) : IJobHandler
{
    public const string JobType = "saleassist.draft";

    public string Type => JobType;

    public bool NotifyOnSuccess => false;

    public async Task<JobResult> RunAsync(JobContext ctx, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<SaleAssistConversationJobPayload>(ctx.PayloadJson)
            ?? throw new InvalidOperationException("Thiếu hội thoại cần soạn câu trả lời.");

        var resp = await grpc.DraftAsync(new DraftRequest
        {
            TenantId = ctx.TenantId.ToString(),
            ConversationId = payload.ConversationId.ToString(),
            SaleUserId = ctx.UserId?.ToString() ?? string.Empty,
        }, cancellationToken: ct).ResponseAsync.ConfigureAwait(false);

        var result = new SaleAssistDraftResponse(resp.DraftText, resp.SuggestedAction, resp.LeadScore, 0);
        return new JobResult($"/inbox?conversation={payload.ConversationId}", JsonSerializer.Serialize(result));
    }
}

public sealed class SaleAssistSummaryJobHandler(SaleAssistAgent.SaleAssistAgentClient grpc) : IJobHandler
{
    public const string JobType = "saleassist.summary";

    public string Type => JobType;

    public bool NotifyOnSuccess => false;

    public async Task<JobResult> RunAsync(JobContext ctx, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<SaleAssistConversationJobPayload>(ctx.PayloadJson)
            ?? throw new InvalidOperationException("Thiếu hội thoại cần tóm tắt.");

        var resp = await grpc.SummarizeAsync(new SummarizeRequest
        {
            TenantId = ctx.TenantId.ToString(),
            ConversationId = payload.ConversationId.ToString(),
        }, cancellationToken: ct).ResponseAsync.ConfigureAwait(false);

        var result = new SaleAssistSummaryResponse(resp.Summary, 0);
        return new JobResult($"/inbox?conversation={payload.ConversationId}", JsonSerializer.Serialize(result));
    }
}

public sealed class SaleAssistUpsellJobHandler(SaleAssistAgent.SaleAssistAgentClient grpc) : IJobHandler
{
    public const string JobType = "saleassist.upsell";

    public string Type => JobType;

    public bool NotifyOnSuccess => false;

    public async Task<JobResult> RunAsync(JobContext ctx, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<SaleAssistConversationJobPayload>(ctx.PayloadJson)
            ?? throw new InvalidOperationException("Thiếu hội thoại cần gợi ý upsell.");

        var resp = await grpc.UpsellAsync(new UpsellRequest
        {
            TenantId = ctx.TenantId.ToString(),
            ConversationId = payload.ConversationId.ToString(),
        }, cancellationToken: ct).ResponseAsync.ConfigureAwait(false);

        var result = new SaleAssistUpsellResponse(resp.Eligible, resp.Suggestion, resp.Reason, resp.LeadScore);
        return new JobResult($"/inbox?conversation={payload.ConversationId}", JsonSerializer.Serialize(result));
    }
}
