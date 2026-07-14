using System.Text.Json;
using Clawbot.Api.Services;
using Clawbot.Domain.KnowledgeBase;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Jobs;

public sealed record KbTestJobPayload(Guid ModuleId);

// Chạy bộ test độ chính xác của 1 KB module: mỗi test case là 1 lượt hỏi agent — chục case là vài phút.
internal sealed class KbTestJobHandler(AppDbContext db, KbTestRunnerService testRunner) : IJobHandler
{
    public const string JobType = "kb.test";

    public string Type => JobType;

    public async Task<JobResult> RunAsync(JobContext ctx, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<KbTestJobPayload>(ctx.PayloadJson)
            ?? throw new InvalidOperationException("Thiếu KB module cho việc chạy test.");

        var module = await db.KbModules.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == payload.ModuleId && m.TenantId == ctx.TenantId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Không tìm thấy KB module.");

        var deployedVersion = await db.KbVersions.IgnoreQueryFilters()
            .Where(v => v.KbModuleId == module.Id && v.Status == "deployed")
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Module chưa có phiên bản nào được triển khai.");

        var cases = await db.KbTestCases.IgnoreQueryFilters()
            .Where(t => t.KbModuleId == module.Id && t.IsActive)
            .ToListAsync(ct).ConfigureAwait(false);
        if (cases.Count == 0) throw new InvalidOperationException("Module chưa có test case nào.");

        var passed = 0;
        for (var i = 0; i < cases.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            await ctx.Progress.ReportAsync(i * 100 / cases.Count, $"Chạy case {i + 1}/{cases.Count}", ct)
                .ConfigureAwait(false);

            var result = await testRunner.EvaluateAsync(ctx.TenantId, module.Code, cases[i], ct).ConfigureAwait(false);
            if (result.Passed) passed++;
        }

        var score = decimal.Round(100m * passed / cases.Count, 2);
        deployedVersion.RecordAccuracy(score);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new JobResult(
            $"/kb?module={module.Id}",
            $"Độ chính xác {score}% ({passed}/{cases.Count} case đạt) trên phiên bản v{deployedVersion.Version}.");
    }
}
