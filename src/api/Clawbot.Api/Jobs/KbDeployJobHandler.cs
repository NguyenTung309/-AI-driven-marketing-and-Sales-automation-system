using System.Text.Json;
using Clawbot.Agents.Core.Kb;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Jobs;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Jobs;

public sealed record KbDeployJobPayload(Guid ModuleId, Guid VersionId, bool IsRollback = false);

// Phát hành/re-embed KB chạy ngầm: KB lớn + embedding thật là hàng chục lời gọi API — không giữ HTTP
// request. Embed lên vector store TRƯỚC, thành công mới archive bản cũ + đánh dấu deployed (thứ tự
// ngược lại tạo "bản deployed ma" không có vector — kb-deployed-ghost-zero-accuracy).
internal sealed class KbDeployJobHandler(AppDbContext db, KbDeployService deployService, IClock clock) : IJobHandler
{
    public const string JobType = "kb.deploy";

    public string Type => JobType;

    public async Task<JobResult> RunAsync(JobContext ctx, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<KbDeployJobPayload>(ctx.PayloadJson)
            ?? throw new InvalidOperationException("Thiếu thông tin phiên bản KB cần phát hành.");

        var module = await db.KbModules.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == payload.ModuleId && m.TenantId == ctx.TenantId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Không tìm thấy KB module.");

        // Tracked: EmbedAndUpsertAsync set EmbeddingJson + Deploy() đổi status — phải persist được.
        var target = await db.KbVersions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.Id == payload.VersionId && v.KbModuleId == module.Id, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Không tìm thấy phiên bản KB.");

        await ctx.Progress.ReportAsync(10, "Đang embed nội dung lên vector store...", ct).ConfigureAwait(false);
        var chunkCount = await deployService.EmbedAndUpsertAsync(target, module.Code, ctx.TenantId, ct).ConfigureAwait(false);

        await ctx.Progress.ReportAsync(85, "Đang kích hoạt phiên bản...", ct).ConfigureAwait(false);
        var previous = await db.KbVersions.IgnoreQueryFilters()
            .Where(v => v.KbModuleId == module.Id && v.Status == "deployed" && v.Id != target.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var prev in previous)
            db.Entry(prev).Property("Status").CurrentValue = "archived";
        target.Deploy(clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var action = payload.IsRollback ? "Đã khôi phục" : "Đã phát hành";
        return new JobResult(
            $"/kb?module={module.Id}",
            $"{action} {module.Code} v{target.Version} — {chunkCount} đoạn đã embed lên vector store.");
    }
}
