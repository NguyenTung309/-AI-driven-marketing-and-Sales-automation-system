using System.Text.Encodings.Web;
using System.Text.Json;
using Clawbot.Agents.Contracts.SaleAssist;
using Clawbot.Api.Contracts.SaleAssist;
using Clawbot.Domain.SaleAssist;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Jobs;

public sealed record SaleAssistConversationJobPayload(Guid ConversationId);

// Job handler nào nhét kết quả JSON vào ResultSummary để FE JSON.parse (đọc field camelCase: draftText, reply...)
// PHẢI serialize bằng options này. Default JsonSerializer ra PascalCase (DraftText) -> FE đọc undefined -> vỡ panel.
// UnsafeRelaxedJsonEscaping giữ tiếng Việt không bị escape \uXXXX.
internal static class JobResultJson
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

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
        return new JobResult($"/inbox?conversation={payload.ConversationId}", JsonSerializer.Serialize(result, JobResultJson.Web));
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
        return new JobResult($"/inbox?conversation={payload.ConversationId}", JsonSerializer.Serialize(result, JobResultJson.Web));
    }
}

public sealed class SaleAssistUpsellJobHandler(SaleAssistAgent.SaleAssistAgentClient grpc, AppDbContext db) : IJobHandler
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

        // Ghi cache theo hội thoại: danh sách "Lead tiềm năng khác" và nút "Xem gợi ý bán thêm"
        // đọc lại kết quả này miễn phí cho đến khi hội thoại có tin nhắn mới.
        // Job scope không có ambient tenant -> IgnoreQueryFilters + filter ctx.TenantId tay.
        var sourceLastMessageAt = await db.Conversations.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Id == payload.ConversationId && c.TenantId == ctx.TenantId)
            .Select(c => c.LastMessageAt ?? c.CreatedAt)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        var cached = await db.UpsellSuggestionCaches.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == ctx.TenantId && x.ConversationId == payload.ConversationId, ct)
            .ConfigureAwait(false);
        if (cached is null)
        {
            db.UpsellSuggestionCaches.Add(UpsellSuggestionCache.Create(
                ctx.TenantId, payload.ConversationId,
                resp.Eligible, resp.Suggestion, resp.Reason, resp.LeadScore,
                DateTimeOffset.UtcNow, sourceLastMessageAt));
        }
        else
        {
            cached.Update(resp.Eligible, resp.Suggestion, resp.Reason, resp.LeadScore,
                DateTimeOffset.UtcNow, sourceLastMessageAt);
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var result = new SaleAssistUpsellResponse(resp.Eligible, resp.Suggestion, resp.Reason, resp.LeadScore);
        return new JobResult($"/inbox?conversation={payload.ConversationId}", JsonSerializer.Serialize(result, JobResultJson.Web));
    }
}
