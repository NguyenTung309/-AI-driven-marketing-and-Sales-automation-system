using System.Text.Json;
using Clawbot.Agents.Contracts.Content;
using Clawbot.SharedKernel.Jobs;

namespace Clawbot.Api.Jobs;

/// <summary>Input đã được endpoint resolve xong (brief lấy từ DB, platform đã chuẩn hoá) — handler chỉ gọi agent.</summary>
public sealed record ContentGenerateJobPayload(Guid? BriefId, string Platform, string Brief);

// Sinh nội dung bằng ContentAgent. Trước đây chạy trong HTTP request (5-30s treo màn hình).
public sealed class ContentGenerateJobHandler(ContentAgent.ContentAgentClient grpc) : IJobHandler
{
    public const string JobType = "content.generate";

    public string Type => JobType;

    public async Task<JobResult> RunAsync(JobContext ctx, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<ContentGenerateJobPayload>(ctx.PayloadJson)
            ?? throw new InvalidOperationException("Thiếu dữ liệu đầu vào cho việc sinh nội dung.");

        var resp = await grpc.GenerateAsync(new ContentRequest
        {
            TenantId = ctx.TenantId.ToString(),
            BriefId = payload.BriefId?.ToString() ?? string.Empty,
            Channel = payload.Platform,
            Brief = payload.Brief,
        }, cancellationToken: ct).ResponseAsync.ConfigureAwait(false);

        return new JobResult(
            $"/content?tab=queue&itemId={resp.ContentId}",
            "Đã sinh nội dung, đang chờ duyệt trong hàng đợi.");
    }
}
