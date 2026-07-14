using System.Text.Json;
using Clawbot.Agents.Core.Docs;
using Clawbot.Agents.Core.Kb;
using Clawbot.Api.Contracts.KnowledgeBase;
using Clawbot.Api.Endpoints;
using Clawbot.Api.Services;
using Clawbot.Domain.KnowledgeBase;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Jobs;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Jobs;

/// <param name="StorageKey">Key trên object storage — file thô nằm ở đó, KHÔNG nằm trong payload job.</param>
public sealed record KbStagedUpload(string StorageKey, string DisplayName, string ExtractFileName);

public sealed record KbClassifyUploadJobPayload(IReadOnlyList<KbStagedUpload> Files, bool AutoDeploy);

// Nạp tri thức từ file tải lên: mỗi file là 1 lượt LLM phân loại + tạo version + deploy/embed.
// Lỗi 1 file không chặn các file còn lại (giữ nguyên hành vi cũ).
internal sealed class KbClassifyUploadJobHandler(
    AppDbContext db,
    IClock clock,
    IDocumentTextExtractor extractor,
    IDocumentStorage storage,
    KbAutoClassifyService classifier,
    KbDeployService deployService,
    ILoggerFactory loggerFactory) : IJobHandler
{
    public const string JobType = "kb.classify-upload";

    public string Type => JobType;

    public async Task<JobResult> RunAsync(JobContext ctx, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<KbClassifyUploadJobPayload>(ctx.PayloadJson)
            ?? throw new InvalidOperationException("Thiếu danh sách tệp cần nạp.");

        var modules = await db.KbModules.IgnoreQueryFilters()
            .Where(m => m.TenantId == ctx.TenantId && m.DeletedAt == null)
            .ToListAsync(ct).ConfigureAwait(false);
        var logger = loggerFactory.CreateLogger("KbClassifyUploadJob");

        var results = new List<KbClassifiedFileDto>(payload.Files.Count);
        for (var i = 0; i < payload.Files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = payload.Files[i];
            await ctx.Progress
                .ReportAsync(i * 100 / payload.Files.Count, $"Đang xử lý {file.DisplayName} ({i + 1}/{payload.Files.Count})", ct)
                .ConfigureAwait(false);

            var content = await storage.ReadAsync(file.StorageKey, ct).ConfigureAwait(false);
            results.Add(await KbEndpoints.ClassifyOneAsync(
                content, file.DisplayName, file.ExtractFileName, payload.AutoDeploy,
                ctx.TenantId, modules, db, clock, extractor, classifier, deployService, logger, ct)
                .ConfigureAwait(false));
        }

        var ok = results.Count(r => r.Success);
        var failures = results.Where(r => !r.Success).ToList();
        if (failures.Count == 0)
            return new JobResult("/kb", $"Đã nạp {ok} tệp vào kho tri thức.");

        // Liệt kê tệp lỗi ngay trong tóm tắt — trước đây bảng kết quả trong modal lo việc này.
        var detail = string.Join('\n', failures.Select(f => $"- {f.FileName}: {f.Error}"));
        return new JobResult("/kb", $"Đã nạp {ok} tệp, {failures.Count} tệp lỗi:\n{detail}");
    }
}
