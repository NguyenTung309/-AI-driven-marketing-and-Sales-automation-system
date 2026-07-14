using Clawbot.Domain.KnowledgeBase;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Services;

internal sealed record KbCaseGenResult(int Generated, int Added);

internal sealed record KbAccuracyOutcome(decimal Score, int Passed, int Total, int Version);

// Điều phối phần "kiểm thử KB" tái dùng được: sinh test case + chấm điểm cho 1 module.
// Bản khoan dung (trả null/kết quả thay vì ném lỗi khi thiếu dữ liệu) để luồng nạp-file tự chạy nối tiếp
// mà không làm hỏng cả job. Handler chạy tay (KbTestJobHandler/KbGenerateTestCasesJobHandler) vẫn giữ
// nhánh nghiêm ngặt riêng để báo lỗi rõ khi người dùng chủ động bấm.
internal sealed class KbTestingOrchestrator(AppDbContext db, KbTestRunnerService runner, IClock clock)
{
    private readonly AppDbContext _db = db;
    private readonly KbTestRunnerService _runner = runner;
    private readonly IClock _clock = clock;

    // ponytail: ~1 case / 1k ký tự để bộ test bám theo độ dài tài liệu (phủ càng nhiều dữ kiện càng tốt).
    private const int CharsPerCase = 1000;
    private const int MinCases = 8;

    // Trần số case tách theo ngữ cảnh: nạp-file hàng loạt giữ vừa phải để không nổ chi phí LLM;
    // bấm tay "Tự động tạo từ tài liệu" cho phủ tối đa vì đó là hành động chủ động của người dùng.
    public const int AutoUploadMaxCases = 20;
    public const int ManualMaxCases = 40;

    public static int ScaleCaseCount(int contentLength, int maxCases) =>
        Math.Clamp((int)Math.Ceiling(contentLength / (double)CharsPerCase), MinCases, maxCases);

    // Sinh test case từ nội dung bản mới nhất của module rồi lưu (bỏ câu trùng).
    // count=null -> tự tính theo độ dài nội dung (kẹp trần maxAutoCases). Trả null nếu module chưa có nội dung.
    public async Task<KbCaseGenResult?> GenerateAndSaveAsync(
        Guid tenantId, Guid moduleId, int? count, int maxAutoCases, CancellationToken ct)
    {
        var content = await _db.KbVersions.IgnoreQueryFilters()
            .Where(v => v.KbModuleId == moduleId)
            .OrderByDescending(v => v.Version)
            .Select(v => v.ContentMd)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content)) return null;

        var target = count ?? ScaleCaseCount(content.Length, maxAutoCases);
        var generated = await _runner.GenerateCasesAsync(tenantId, content, target, ct).ConfigureAwait(false);
        if (generated.Count == 0) return new KbCaseGenResult(0, 0);

        var existing = await _db.KbTestCases.IgnoreQueryFilters()
            .Where(t => t.KbModuleId == moduleId)
            .Select(t => t.Question)
            .ToListAsync(ct).ConfigureAwait(false);
        var seen = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var g in generated)
        {
            if (!seen.Add(g.Question)) continue;
            _db.KbTestCases.Add(KbTestCase.Create(moduleId, g.Question, g.ExpectedAnswer, _clock.UtcNow));
            added++;
        }
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new KbCaseGenResult(generated.Count, added);
    }

    // Chạy toàn bộ test case active trên bản deployed rồi ghi điểm.
    // Trả null nếu module chưa có bản deployed hoặc chưa có test case nào.
    public async Task<KbAccuracyOutcome?> RunAndRecordAsync(
        Guid tenantId, string moduleCode, Guid moduleId, CancellationToken ct)
    {
        var deployed = await _db.KbVersions.IgnoreQueryFilters()
            .Where(v => v.KbModuleId == moduleId && v.Status == "deployed")
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (deployed is null) return null;

        var cases = await _db.KbTestCases.IgnoreQueryFilters()
            .Where(t => t.KbModuleId == moduleId && t.IsActive)
            .ToListAsync(ct).ConfigureAwait(false);
        if (cases.Count == 0) return null;

        var passed = 0;
        foreach (var testCase in cases)
        {
            ct.ThrowIfCancellationRequested();
            var result = await _runner.EvaluateAsync(tenantId, moduleCode, testCase, ct).ConfigureAwait(false);
            if (result.Passed) passed++;
        }

        var score = decimal.Round(100m * passed / cases.Count, 2);
        deployed.RecordAccuracy(score);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new KbAccuracyOutcome(score, passed, cases.Count, deployed.Version);
    }
}
