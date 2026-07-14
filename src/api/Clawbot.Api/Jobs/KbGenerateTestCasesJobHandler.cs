using System.Text.Json;
using Clawbot.Api.Services;
using Clawbot.SharedKernel.Jobs;

namespace Clawbot.Api.Jobs;

// Count=null -> tự tính số case theo độ dài tài liệu (phủ tối đa); có giá trị -> tôn trọng số người dùng chọn.
public sealed record KbGenerateTestCasesJobPayload(Guid ModuleId, int? Count);

// Sinh bộ test case từ nội dung KB bằng LLM — dùng chung KbTestingOrchestrator để bỏ trùng + tự tính số câu.
internal sealed class KbGenerateTestCasesJobHandler(KbTestingOrchestrator orchestrator) : IJobHandler
{
    public const string JobType = "kb.test-cases-generate";

    public string Type => JobType;

    public async Task<JobResult> RunAsync(JobContext ctx, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<KbGenerateTestCasesJobPayload>(ctx.PayloadJson)
            ?? throw new InvalidOperationException("Thiếu KB module cho việc sinh test case.");

        var result = await orchestrator.GenerateAndSaveAsync(
            ctx.TenantId, payload.ModuleId, payload.Count, KbTestingOrchestrator.ManualMaxCases, ct)
            .ConfigureAwait(false);
        if (result is null)
            throw new InvalidOperationException("Module chưa có nội dung để sinh test case.");
        if (result.Generated == 0)
            throw new InvalidOperationException("Agent không sinh được test case nào.");

        return new JobResult(
            $"/kb?module={payload.ModuleId}",
            $"Đã sinh {result.Added} test case mới (bỏ {result.Generated - result.Added} câu trùng).");
    }
}
