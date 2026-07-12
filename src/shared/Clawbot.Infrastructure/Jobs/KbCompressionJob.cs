using System.Text.Json;
using Clawbot.Agents.Core.Content;
using Clawbot.Agents.Core.Learning;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.KnowledgeBase;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Notifications;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

// ai-self-learning-memory 3.2: nén KB weekly — tìm cặp module trùng lắp, sinh kb_suggestions op=merge
// đi ĐÚNG pipeline chờ-duyệt của Phase 1. Merge LUÔN chờ người (không auto): gộp nhóm là thay đổi lớn,
// người duyệt xong còn phải tự lưu trữ nhóm nguồn.
public sealed partial class KbCompressionJob(
    AppDbContext db,
    KnowledgeDistiller distiller,
    ContentReviewer reviewer,
    IPiiRedactor pii,
    INotificationPublisher publisher,
    IClock clock,
    ILogger<KbCompressionJob> logger)
{
    private const int ModuleExcerptChars = 500;
    private const int MaxMergesPerTenant = 5;

    [DisableConcurrentExecution(timeoutInSeconds: 900)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var tenants = await db.Tenants.Where(t => t.IsActive).Select(t => t.Id).ToListAsync(ct).ConfigureAwait(false);
        foreach (var tenantId in tenants)
        {
            try
            {
                await RunForTenantAsync(tenantId, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogTenantFailed(logger, ex, tenantId);
            }
        }
    }

    public async Task RunForTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        // Catalog kèm nội dung deployed ĐẦY ĐỦ (merge cần full text, không phải excerpt).
        var modules = await db.KbModules.IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.Status == "active" && m.DeletedAt == null)
            .Select(m => new { m.Id, m.Code, m.Name })
            .ToListAsync(ct).ConfigureAwait(false);
        if (modules.Count < 2) return;

        var moduleIds = modules.Select(m => m.Id).ToList();
        var contents = (await db.KbVersions.IgnoreQueryFilters()
            .Where(v => moduleIds.Contains(v.KbModuleId) && v.Status == "deployed")
            .Select(v => new { v.KbModuleId, v.Version, v.ContentMd })
            .ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(v => v.KbModuleId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.Version).First().ContentMd);

        var catalog = modules
            .Where(m => contents.ContainsKey(m.Id)) // chỉ so module đã deploy
            .Select(m =>
            {
                var content = contents[m.Id];
                return new ExistingKbModule(m.Id, m.Code, m.Name,
                    content.Length > ModuleExcerptChars ? content[..ModuleExcerptChars] : content);
            })
            .ToList();
        if (catalog.Count < 2) return;

        var candidates = await distiller.ProposeMergesAsync(tenantId, catalog, ct).ConfigureAwait(false);
        if (candidates is null || candidates.Count == 0) return;

        var created = 0;
        foreach (var candidate in candidates.Take(MaxMergesPerTenant))
        {
            try
            {
                if (await CreateMergeSuggestionAsync(tenantId, candidate, catalog, contents, now, ct).ConfigureAwait(false))
                    created++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogCandidateFailed(logger, ex, tenantId);
            }
        }

        if (created > 0)
        {
            await publisher.PublishAsync(new NotificationRequest(
                tenantId, null, "kb_suggestion_pending",
                "Đề xuất gộp nhóm tri thức trùng lắp",
                Severity: "info",
                Body: $"{created} đề xuất gộp nhóm tri thức đang chờ duyệt trong Kho tri thức. Duyệt xong nhớ lưu trữ nhóm nguồn.",
                Link: "/kb"), ct).ConfigureAwait(false);
        }

        LogCompleted(logger, tenantId, created);
    }

    private async Task<bool> CreateMergeSuggestionAsync(
        Guid tenantId,
        KbMergeCandidate candidate,
        IReadOnlyList<ExistingKbModule> catalog,
        Dictionary<Guid, string> contents,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var target = catalog.First(m => m.Id == candidate.TargetModuleId);
        var source = catalog.First(m => m.Id == candidate.SourceModuleId);

        // Idempotent theo cặp code (không phụ thuộc chiều gộp).
        var pairKey = string.Join(" ", new[] { target.Code, source.Code }.OrderBy(c => c, StringComparer.Ordinal));
        var hash = KnowledgeDistiller.ComputeDedupHash($"merge {pairKey}");
        var exists = await db.KbSuggestions.IgnoreQueryFilters()
            .AnyAsync(s => s.TenantId == tenantId && s.DedupHash == hash, ct).ConfigureAwait(false);
        if (exists) return false;

        var draft = await distiller.MergeModulesAsync(
            tenantId, target.Name, contents[target.Id], source.Name, contents[source.Id], ct).ConfigureAwait(false);
        if (draft is null) return false;

        // Text derived qua LLM (title/contentMd) đều redact trước persist, đồng bộ với KnowledgeDistillationJob.
        var title = (await pii.RedactAsync(draft.Title, ct).ConfigureAwait(false)).RedactedText;
        var contentMd = (await pii.RedactAsync(draft.ContentMd, ct).ConfigureAwait(false)).RedactedText;
        var rationaleRaw = $"{candidate.Reason} Gộp nhóm \"{source.Name}\" vào \"{target.Name}\" — duyệt xong cần lưu trữ nhóm nguồn thủ công.";
        var rationale = (await pii.RedactAsync(rationaleRaw, ct).ConfigureAwait(false)).RedactedText;
        var evidenceJson = JsonSerializer.Serialize(new[]
        {
            new { conversationId = (Guid?)null, snippetRedacted = $"Nguồn: {source.Code} — {source.Name}", signal = "kb_compression" },
            new { conversationId = (Guid?)null, snippetRedacted = $"Đích: {target.Code} — {target.Name}", signal = "kb_compression" },
        });

        var suggestion = KbSuggestion.Create(
            tenantId, KbSuggestion.OpMerge, target.Id,
            title, contentMd, rationale, evidenceJson, hash, now);

        var verdict = await reviewer.ReviewKbSuggestionAsync(tenantId, title, contentMd, rationale, ct).ConfigureAwait(false);
        suggestion.RecordReview(verdict.Verdict, verdict.Reason);
        // KHÔNG đo accuracy, KHÔNG auto-approve: merge luôn chờ người (accuracy NULL giữ rail đóng).

        db.KbSuggestions.Add(suggestion);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    [LoggerMessage(EventId = 12501, Level = LogLevel.Error, Message = "KbCompression failed for tenant {TenantId}")]
    private static partial void LogTenantFailed(ILogger logger, Exception ex, Guid tenantId);

    [LoggerMessage(EventId = 12502, Level = LogLevel.Warning, Message = "KbCompression merge candidate skipped for tenant {TenantId}")]
    private static partial void LogCandidateFailed(ILogger logger, Exception ex, Guid tenantId);

    [LoggerMessage(EventId = 12503, Level = LogLevel.Information, Message = "KbCompression tenant {TenantId}: {Created} merge suggestions created")]
    private static partial void LogCompleted(ILogger logger, Guid tenantId, int created);
}
