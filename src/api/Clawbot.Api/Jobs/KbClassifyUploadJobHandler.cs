using System.Text.Json;
using Clawbot.Agents.Core.Chat;
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

public sealed record KbClassifyUploadJobPayload(IReadOnlyList<KbStagedUpload> Files, bool AutoDeploy, bool AutoTest);

// Nạp tri thức từ file tải lên: mỗi file là 1 lượt LLM phân loại + tạo version + deploy/embed.
// Lỗi 1 file không chặn các file còn lại (giữ nguyên hành vi cũ).
internal sealed partial class KbClassifyUploadJobHandler(
    AppDbContext db,
    IClock clock,
    IDocumentTextExtractor extractor,
    IDocumentStorage storage,
    KbAutoClassifyService classifier,
    KbDeployService deployService,
    KbTestingOrchestrator testing,
    ILoggerFactory loggerFactory) : IJobHandler
{
    public const string JobType = "kb.classify-upload";

    // Chia thanh tiến trình: nạp+phân loại 0->60%, tự kiểm thử 60->100% (khi bật AutoTest).
    private const int ClassifyProgressCap = 60;

    public string Type => JobType;

    public async Task<JobResult> RunAsync(JobContext ctx, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<KbClassifyUploadJobPayload>(ctx.PayloadJson)
            ?? throw new InvalidOperationException("Thiếu danh sách tệp cần nạp.");

        var modules = await db.KbModules.IgnoreQueryFilters()
            .Where(m => m.TenantId == ctx.TenantId && m.DeletedAt == null)
            .ToListAsync(ct).ConfigureAwait(false);
        var logger = loggerFactory.CreateLogger("KbClassifyUploadJob");
        var classifyCap = payload.AutoTest ? ClassifyProgressCap : 100;

        var results = new List<KbClassifiedFileDto>(payload.Files.Count);
        for (var i = 0; i < payload.Files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = payload.Files[i];
            await ctx.Progress
                .ReportAsync(i * classifyCap / payload.Files.Count, $"Đang xử lý {file.DisplayName} ({i + 1}/{payload.Files.Count})", ct)
                .ConfigureAwait(false);

            var content = await storage.ReadAsync(file.StorageKey, ct).ConfigureAwait(false);
            results.Add(await KbEndpoints.ClassifyOneAsync(
                content, file.DisplayName, file.ExtractFileName, payload.AutoDeploy,
                ctx.TenantId, modules, db, clock, extractor, classifier, deployService, logger, ct)
                .ConfigureAwait(false));
        }

        var testNotes = payload.AutoTest
            ? await AutoTestAsync(ctx, results, logger, ct).ConfigureAwait(false)
            : [];

        var ok = results.Count(r => r.Success);
        var failures = results.Where(r => !r.Success).ToList();
        var summary = failures.Count == 0
            ? $"Đã nạp {ok} tệp vào kho tri thức."
            // Liệt kê tệp lỗi ngay trong tóm tắt — trước đây bảng kết quả trong modal lo việc này.
            : $"Đã nạp {ok} tệp, {failures.Count} tệp lỗi:\n{string.Join('\n', failures.Select(f => $"- {f.FileName}: {f.Error}"))}";
        if (testNotes.Count > 0)
            summary += "\n\nKiểm thử độ chính xác:\n" + string.Join('\n', testNotes);

        return new JobResult("/kb", summary);
    }

    // Sau khi phân loại: mỗi module vừa động tới -> tự sinh test case (phủ theo độ dài tài liệu),
    // rồi chấm độ chính xác nếu đã có bản deployed. Lỗi 1 module không chặn các module khác.
    private async Task<List<string>> AutoTestAsync(
        JobContext ctx, IReadOnlyList<KbClassifiedFileDto> results, ILogger logger, CancellationToken ct)
    {
        var touched = results
            .Where(r => r.Success && r.ModuleId is not null)
            .GroupBy(r => r.ModuleId!.Value)
            .Select(g => (Id: g.Key, Code: g.First().ModuleCode!, Name: g.First().ModuleName!, Deployed: g.Any(x => x.Deployed)))
            .ToList();

        var notes = new List<string>(touched.Count);
        for (var i = 0; i < touched.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var m = touched[i];
            var basePct = ClassifyProgressCap + i * (100 - ClassifyProgressCap) / Math.Max(1, touched.Count);
            await ctx.Progress.ReportAsync(basePct, $"Sinh câu kiểm thử cho {m.Name} ({i + 1}/{touched.Count})", ct)
                .ConfigureAwait(false);

            try
            {
                var gen = await testing.GenerateAndSaveAsync(
                    ctx.TenantId, m.Id, null, KbTestingOrchestrator.AutoUploadMaxCases, ct).ConfigureAwait(false);
                var added = gen?.Added ?? 0;

                if (!m.Deployed)
                {
                    notes.Add($"- {m.Name}: sinh {added} câu (chưa deploy nên chưa chấm điểm)");
                    continue;
                }

                await ctx.Progress.ReportAsync(basePct, $"Chấm độ chính xác {m.Name}", ct).ConfigureAwait(false);
                var outcome = await testing.RunAndRecordAsync(ctx.TenantId, m.Code, m.Id, ct).ConfigureAwait(false);
                notes.Add(outcome is not null
                    ? $"- {m.Name}: {outcome.Score}% ({outcome.Passed}/{outcome.Total} câu đạt) trên v{outcome.Version}"
                    : $"- {m.Name}: sinh {added} câu, chưa chấm được điểm");
            }
            catch (LlmConfigNotConfiguredException)
            {
                notes.Add($"- {m.Name}: chưa cấu hình LLM cho việc kiểm thử");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Không nuốt lỗi: ghi log đầy đủ + nêu loại lỗi thật trong tóm tắt để chẩn đoán được.
                LogAutoTestFailed(logger, m.Id, ex);
                var reason = (ex.Message ?? ex.GetType().Name).Split('\n')[0].Trim();
                if (reason.Length > 140) reason = string.Concat(reason.AsSpan(0, 140), "…");
                notes.Add($"- {m.Name}: kiểm thử lỗi ({ex.GetType().Name}: {reason})");
            }
        }

        return notes;
    }

    [LoggerMessage(EventId = 5310, Level = LogLevel.Warning, Message = "KB auto-test failed for module {ModuleId}")]
    private static partial void LogAutoTestFailed(ILogger logger, Guid moduleId, Exception ex);
}
