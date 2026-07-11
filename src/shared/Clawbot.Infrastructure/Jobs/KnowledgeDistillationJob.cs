using System.Text.Json;
using Clawbot.Agents.Core.Content;
using Clawbot.Agents.Core.Learning;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.KnowledgeBase;
using Clawbot.Infrastructure.Learning;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Notifications;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawbot.Infrastructure.Jobs;

// ai-self-learning-memory Lớp 1: đêm mine 3 nguồn tín hiệu (AI trượt, sale trả lời tay, câu hỏi lặp),
// LLM chưng cất + memory-ops đối chiếu KB, ghi kb_suggestions. Gate 2 chế độ: tenant auto (mặc định)
// chỉ tự deploy khi rail đạt (verdict approve + accuracy không giảm); còn lại chờ người.
// Per-item try/catch — 1 cụm lỗi không giết cả batch; parse fail = skip, KHÔNG ghi dữ liệu đoán.
public sealed partial class KnowledgeDistillationJob(
    AppDbContext db,
    KnowledgeDistiller distiller,
    ContentReviewer reviewer,
    KbSuggestionAccuracyEvaluator accuracyEvaluator,
    KbSuggestionMaterializer materializer,
    IPiiRedactor pii,
    INotificationPublisher publisher,
    IClock clock,
    IOptions<LearningOptions> options,
    ILogger<KnowledgeDistillationJob> logger)
{
    private sealed record MinedCluster(List<DistillSignal> Signals, List<EvidenceItem> Evidence);

    private sealed record EvidenceItem(Guid ConversationId, string SnippetRedacted, string Signal);

    private const int SnippetMaxChars = 200;
    private const int ModuleExcerptChars = 500;

    [DisableConcurrentExecution(timeoutInSeconds: 900)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var tenants = await db.Tenants
            .Where(t => t.IsActive)
            .Select(t => new { t.Id, t.RequireKbHumanReview })
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var tenant in tenants)
        {
            try
            {
                await RunForTenantAsync(tenant.Id, tenant.RequireKbHumanReview, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogTenantFailed(logger, ex, tenant.Id);
            }
        }
    }

    public async Task RunForTenantAsync(Guid tenantId, bool requireHumanReview, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var clusters = await MineAsync(tenantId, now, ct).ConfigureAwait(false);
        if (clusters.Count == 0)
        {
            LogNoSignals(logger, tenantId);
            return;
        }

        var modules = await LoadModuleCatalogAsync(tenantId, ct).ConfigureAwait(false);
        var autoApproved = 0;
        var pending = 0;

        foreach (var cluster in clusters.Take(options.Value.MaxConversationsPerRun))
        {
            try
            {
                if (await ProcessClusterAsync(tenantId, requireHumanReview, cluster, modules, now, ct).ConfigureAwait(false) is { } wasAuto)
                {
                    if (wasAuto) autoApproved++; else pending++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogClusterFailed(logger, ex, tenantId);
            }
        }

        if (autoApproved > 0)
        {
            await publisher.PublishAsync(new NotificationRequest(
                tenantId, null, "kb_suggestion_auto_approved",
                "AI đã tự duyệt tri thức mới",
                Severity: "info",
                Body: $"{autoApproved} tri thức mới đạt chuẩn (reviewer duyệt + accuracy không giảm) và đã được đưa vào kho. Xem lại trong Kho tri thức.",
                Link: "/kb"), ct).ConfigureAwait(false);
        }

        if (pending > 0)
        {
            await publisher.PublishAsync(new NotificationRequest(
                tenantId, null, "kb_suggestion_pending",
                "Tri thức mới chờ duyệt",
                Severity: "info",
                Body: $"{pending} tri thức mới từ hội thoại thật đang chờ duyệt trong Kho tri thức.",
                Link: "/kb"), ct).ConfigureAwait(false);
        }

        LogCompleted(logger, tenantId, autoApproved, pending);
    }

    // null = cụm bị skip (trùng, noop, LLM chịu thua); true = auto-approved; false = pending.
    private async Task<bool?> ProcessClusterAsync(
        Guid tenantId,
        bool requireHumanReview,
        MinedCluster cluster,
        IReadOnlyList<ExistingKbModule> modules,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var draft = await distiller.DistillAsync(tenantId, cluster.Signals, ct).ConfigureAwait(false);
        if (draft is null) return null;

        var hash = KnowledgeDistiller.ComputeDedupHash(draft.NormalizedQuestion);
        var exists = await db.KbSuggestions.IgnoreQueryFilters()
            .AnyAsync(s => s.TenantId == tenantId && s.DedupHash == hash, ct).ConfigureAwait(false);
        if (exists) return null;

        var consolidation = await distiller.ConsolidateAsync(tenantId, draft, modules, ct).ConfigureAwait(false);
        if (consolidation is null || consolidation.Op == "noop") return null;

        // Belt-and-braces: input đã redact từ ingest, nhưng text derived vẫn qua redactor lần cuối trước persist.
        var title = await RedactAsync(draft.Title, ct).ConfigureAwait(false);
        var contentMd = await RedactAsync(
            string.IsNullOrWhiteSpace(consolidation.MergedContentMd) ? draft.ContentMd : consolidation.MergedContentMd,
            ct).ConfigureAwait(false);
        var rationale = await RedactAsync(draft.Rationale, ct).ConfigureAwait(false);
        var evidenceJson = JsonSerializer.Serialize(cluster.Evidence.Select(e => new
        {
            conversationId = e.ConversationId,
            snippetRedacted = e.SnippetRedacted,
            signal = e.Signal,
        }));

        var suggestion = KbSuggestion.Create(
            tenantId, consolidation.Op,
            consolidation.Op == KbSuggestion.OpAdd ? null : consolidation.TargetModuleId,
            title, contentMd, rationale, evidenceJson, hash, now);

        var evidenceText = string.Join("\n", cluster.Evidence.Select(e => $"- [{e.Signal}] {e.SnippetRedacted}"));
        var verdict = await reviewer.ReviewKbSuggestionAsync(tenantId, title, contentMd, evidenceText, ct).ConfigureAwait(false);
        suggestion.RecordReview(verdict.Verdict, verdict.Reason);

        if (suggestion.TargetKbModuleId is { } moduleId)
        {
            var module = modules.First(m => m.Id == moduleId);
            var cases = await db.KbTestCases.IgnoreQueryFilters()
                .Where(c => c.KbModuleId == moduleId)
                .Select(c => new KbAccuracyCase(c.Question, c.ExpectedAnswer))
                .ToListAsync(ct).ConfigureAwait(false);
            var accuracy = await accuracyEvaluator.EvaluateAsync(tenantId, module.Code, cases, contentMd, ct).ConfigureAwait(false);
            suggestion.RecordAccuracy(accuracy.Before, accuracy.After);
        }

        db.KbSuggestions.Add(suggestion);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (!requireHumanReview && suggestion.IsAutoApprovable)
        {
            suggestion.Approve(now, decidedBy: null, KbSuggestion.ApprovalModeAuto);
            await materializer.MaterializeAsync(suggestion, ct).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    private async Task<string> RedactAsync(string text, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(text) ? text : (await pii.RedactAsync(text, ct).ConfigureAwait(false)).RedactedText;

    // 2 query phẳng thay vì APPLY (SQLite trong test không hỗ trợ; bản deployed mỗi module cũng chỉ có 1).
    private async Task<IReadOnlyList<ExistingKbModule>> LoadModuleCatalogAsync(Guid tenantId, CancellationToken ct)
    {
        var modules = await db.KbModules.IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.Status == "active" && m.DeletedAt == null)
            .Select(m => new { m.Id, m.Code, m.Name })
            .ToListAsync(ct).ConfigureAwait(false);
        if (modules.Count == 0) return [];

        var moduleIds = modules.Select(m => m.Id).ToList();
        var deployedContent = (await db.KbVersions.IgnoreQueryFilters()
            .Where(v => moduleIds.Contains(v.KbModuleId) && v.Status == "deployed")
            .Select(v => new { v.KbModuleId, v.Version, v.ContentMd })
            .ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(v => v.KbModuleId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.Version).First().ContentMd);

        return modules
            .Select(m =>
            {
                var content = deployedContent.GetValueOrDefault(m.Id) ?? string.Empty;
                return new ExistingKbModule(
                    m.Id, m.Code, m.Name,
                    content.Length > ModuleExcerptChars ? content[..ModuleExcerptChars] : content);
            })
            .ToList();
    }

    // Mine 3 nguồn. Message.Content đã redact từ lúc ingest — dùng thẳng làm signal/snippet.
    private async Task<List<MinedCluster>> MineAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        var opts = options.Value;
        var since = now.AddHours(-opts.SignalWindowHours);
        var cap = opts.MaxConversationsPerRun;
        var clusters = new List<MinedCluster>();
        var minedConversations = new HashSet<Guid>();

        // Nguồn 1: hội thoại escalated — AI bó tay, sale (nếu có) đã trả lời chuẩn.
        var escalated = await db.Conversations.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && c.Status == "escalated"
                && c.LastMessageAt != null && c.LastMessageAt >= since && c.DeletedAt == null)
            .Select(c => c.Id)
            .Take(cap)
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var conversationId in escalated)
        {
            var msgs = await db.Messages.IgnoreQueryFilters()
                .Where(m => m.ConversationId == conversationId)
                .OrderByDescending(m => m.SentAt)
                .Take(20)
                .Select(m => new { m.Direction, m.SenderType, m.Content, m.SentAt })
                .ToListAsync(ct).ConfigureAwait(false);

            var question = msgs.FirstOrDefault(m => m.Direction == "in");
            if (question is null || string.IsNullOrWhiteSpace(question.Content)) continue;
            var aiAnswer = msgs.FirstOrDefault(m => m.Direction == "out" && (m.SenderType == "agent" || m.SenderType == "bot"));
            var saleAnswer = msgs.FirstOrDefault(m => m.Direction == "out" && m.SenderType == "user" && m.SentAt > question.SentAt);

            minedConversations.Add(conversationId);
            clusters.Add(new MinedCluster(
                [new DistillSignal("ai_failed", question.Content, aiAnswer?.Content, saleAnswer?.Content)],
                [new EvidenceItem(conversationId, Snippet(question.Content), "ai_failed")]));
        }

        // Nguồn 2: sale trả lời tay — cặp (khách hỏi, sale đáp) trong cửa sổ.
        var saleReplies = await db.Messages.IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.Direction == "out" && m.SenderType == "user" && m.SentAt >= since)
            .OrderByDescending(m => m.SentAt)
            .Select(m => new { m.ConversationId, m.Content, m.SentAt })
            .Take(cap * 2)
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var reply in saleReplies.DistinctBy(r => r.ConversationId))
        {
            if (!minedConversations.Add(reply.ConversationId)) continue;
            var question = await db.Messages.IgnoreQueryFilters()
                .Where(m => m.ConversationId == reply.ConversationId && m.Direction == "in" && m.SentAt < reply.SentAt)
                .OrderByDescending(m => m.SentAt)
                .Select(m => m.Content)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(question) || string.IsNullOrWhiteSpace(reply.Content)) continue;

            clusters.Add(new MinedCluster(
                [new DistillSignal("sale_answered", question, null, reply.Content)],
                [new EvidenceItem(reply.ConversationId, Snippet(question), "sale_answered")]));
        }

        // Nguồn 3: câu hỏi lặp >= N lần trong cửa sổ dài (group theo hash chuẩn hóa, tính trong bộ nhớ).
        var repeatedSince = now.AddDays(-opts.RepeatedQuestionWindowDays);
        var inbound = await db.Messages.IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.Direction == "in" && m.SentAt >= repeatedSince
                && m.Content != null && m.Content != "")
            .OrderByDescending(m => m.SentAt)
            .Select(m => new { m.ConversationId, m.Content })
            .Take(2000)
            .ToListAsync(ct).ConfigureAwait(false);

        var repeatedGroups = inbound
            .GroupBy(m => KnowledgeDistiller.ComputeDedupHash(m.Content))
            .Where(g => g.Count() >= opts.RepeatedQuestionThreshold)
            .Take(cap);

        foreach (var group in repeatedGroups)
        {
            var sample = group.First();
            clusters.Add(new MinedCluster(
                [new DistillSignal("repeated_question", sample.Content, null, null)],
                group.Take(3).Select(m => new EvidenceItem(m.ConversationId, Snippet(m.Content), "repeated_question")).ToList()));
        }

        return clusters;
    }

    private static string Snippet(string text) =>
        text.Length <= SnippetMaxChars ? text : text[..SnippetMaxChars];

    [LoggerMessage(EventId = 12201, Level = LogLevel.Error, Message = "KnowledgeDistillation failed for tenant {TenantId}")]
    private static partial void LogTenantFailed(ILogger logger, Exception ex, Guid tenantId);

    [LoggerMessage(EventId = 12202, Level = LogLevel.Debug, Message = "KnowledgeDistillation: no signals for tenant {TenantId}")]
    private static partial void LogNoSignals(ILogger logger, Guid tenantId);

    [LoggerMessage(EventId = 12203, Level = LogLevel.Warning, Message = "KnowledgeDistillation: cluster skipped for tenant {TenantId}")]
    private static partial void LogClusterFailed(ILogger logger, Exception ex, Guid tenantId);

    [LoggerMessage(EventId = 12204, Level = LogLevel.Information, Message = "KnowledgeDistillation tenant {TenantId}: {AutoApproved} auto-approved, {Pending} pending review")]
    private static partial void LogCompleted(ILogger logger, Guid tenantId, int autoApproved, int pending);
}
