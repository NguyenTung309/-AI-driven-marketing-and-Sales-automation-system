using System.Text.Json;
using Clawbot.Api.Services;
using Clawbot.Domain.KnowledgeBase;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Jobs;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Jobs;

public sealed record KbGenerateTestCasesJobPayload(Guid ModuleId, int Count);

// Sinh bộ test case từ nội dung KB bằng LLM — bỏ trùng câu hỏi đã có.
internal sealed class KbGenerateTestCasesJobHandler(
    AppDbContext db,
    KbTestRunnerService testRunner,
    IClock clock) : IJobHandler
{
    public const string JobType = "kb.test-cases-generate";

    public string Type => JobType;

    public async Task<JobResult> RunAsync(JobContext ctx, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<KbGenerateTestCasesJobPayload>(ctx.PayloadJson)
            ?? throw new InvalidOperationException("Thiếu KB module cho việc sinh test case.");

        var latest = await db.KbVersions.IgnoreQueryFilters()
            .Where(v => v.KbModuleId == payload.ModuleId)
            .OrderByDescending(v => v.Version)
            .Select(v => v.ContentMd)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(latest))
            throw new InvalidOperationException("Module chưa có nội dung để sinh test case.");

        var generated = await testRunner.GenerateCasesAsync(ctx.TenantId, latest, payload.Count, ct).ConfigureAwait(false);
        if (generated.Count == 0)
            throw new InvalidOperationException("Agent không sinh được test case nào.");

        var existing = await db.KbTestCases.IgnoreQueryFilters()
            .Where(t => t.KbModuleId == payload.ModuleId)
            .Select(t => t.Question)
            .ToListAsync(ct).ConfigureAwait(false);
        var seen = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var g in generated)
        {
            if (!seen.Add(g.Question)) continue;
            db.KbTestCases.Add(KbTestCase.Create(payload.ModuleId, g.Question, g.ExpectedAnswer, clock.UtcNow));
            added++;
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new JobResult(
            $"/kb?module={payload.ModuleId}",
            $"Đã sinh {added} test case mới (bỏ {generated.Count - added} câu trùng).");
    }
}
