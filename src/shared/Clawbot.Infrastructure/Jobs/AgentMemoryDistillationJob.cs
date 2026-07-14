using Clawbot.Agents.Core.Learning;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Agents;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Clawbot.SharedKernel.Notifications;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

// ai-self-learning-memory Lớp 3: đêm rút "lỗi hay gặp" từ lý do reject content 24h qua,
// memory-ops vào agent_memories (scope reviewer-agent) — reviewer chấm ngày càng bắt lỗi nhanh hơn.
public sealed partial class AgentMemoryDistillationJob(
    AppDbContext db,
    AgentMistakeExtractor extractor,
    IPiiRedactor pii,
    IClock clock,
    INotificationPublisher publisher,
    ILogger<AgentMemoryDistillationJob> logger)
{
    private const string ReviewerAgentCode = "reviewer-agent";
    private const int MaxReasonsPerTenant = 50;

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var since = now.AddHours(-24);

        var tenantIds = await db.ContentItems.IgnoreQueryFilters()
            .Where(i => i.DeletedAt == null && i.Status == "rejected"
                && i.RejectedReason != null && i.UpdatedAt >= since)
            .Select(i => i.TenantId)
            .Distinct()
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var tenantId in tenantIds)
        {
            try
            {
                await RunForTenantAsync(tenantId, since, now, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogTenantFailed(logger, ex, tenantId);
            }
        }
    }

    public async Task RunForTenantAsync(Guid tenantId, DateTimeOffset since, DateTimeOffset now, CancellationToken ct = default)
    {
        var reasons = await db.ContentItems.IgnoreQueryFilters()
            .Where(i => i.TenantId == tenantId && i.DeletedAt == null && i.Status == "rejected"
                && i.RejectedReason != null && i.UpdatedAt >= since)
            .OrderByDescending(i => i.UpdatedAt)
            .Take(MaxReasonsPerTenant)
            .Select(i => i.RejectedReason!)
            .ToListAsync(ct).ConfigureAwait(false);
        if (reasons.Count == 0) return;

        var existing = await db.AgentMemories.IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.AgentCode == ReviewerAgentCode && m.IsActive)
            .OrderByDescending(m => m.UpdatedAt)
            .ToListAsync(ct).ConfigureAwait(false);
        var existingLessons = existing
            .Select(m => new ContactFact(m.Id, m.Fact, m.Category, m.Confidence))
            .ToList();

        var ops = await extractor.ExtractAsync(tenantId, ReviewerAgentCode, reasons, existingLessons, ct).ConfigureAwait(false);
        if (ops is null)
        {
            LogExtractionFailed(logger, tenantId);
            return; // LLM chịu thua sau self-repair — bỏ lượt này, đêm sau dữ liệu vẫn còn cửa sổ khác
        }

        var byId = existing.ToDictionary(m => m.Id);
        foreach (var op in ops)
        {
            switch (op.Op)
            {
                case "add":
                {
                    var fact = await RedactAsync(op.Fact!, ct).ConfigureAwait(false);
                    db.AgentMemories.Add(AgentMemory.Create(tenantId, ReviewerAgentCode, fact, op.Category!, op.Confidence ?? 0.7m, now));
                    break;
                }
                case "update":
                {
                    var fact = await RedactAsync(op.Fact!, ct).ConfigureAwait(false);
                    var replacement = AgentMemory.Create(tenantId, ReviewerAgentCode, fact, op.Category!, op.Confidence ?? 0.7m, now);
                    db.AgentMemories.Add(replacement);
                    if (byId.TryGetValue(op.FactId!.Value, out var old) && old.IsActive)
                        old.Supersede(replacement.Id, now);
                    break;
                }
                case "delete":
                {
                    if (byId.TryGetValue(op.FactId!.Value, out var old) && old.IsActive)
                        old.Supersede(null, now);
                    break;
                }
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        LogCompleted(logger, tenantId, ops.Count);
        // AI tự học: người phải thấy nó học được gì, không thì "tự học" là hộp đen.
        await publisher.PublishAsync(new NotificationRequest(
            tenantId,
            UserId: null,
            Type: "agent_memory_learned",
            Title: "Agent đã rút ra bài học mới",
            Severity: "info",
            Body: $"Từ lý do từ chối nội dung: {ops.Count} bài học cho reviewer-agent.",
            Link: "/agents",
            GroupKey: "agent.memory.learned"), ct).ConfigureAwait(false);
    }

    private async Task<string> RedactAsync(string text, CancellationToken ct) =>
        (await pii.RedactAsync(text, ct).ConfigureAwait(false)).RedactedText;

    [LoggerMessage(EventId = 12401, Level = LogLevel.Error, Message = "AgentMemoryDistillation failed for tenant {TenantId}")]
    private static partial void LogTenantFailed(ILogger logger, Exception ex, Guid tenantId);

    [LoggerMessage(EventId = 12402, Level = LogLevel.Warning, Message = "AgentMemoryDistillation extraction gave up for tenant {TenantId}")]
    private static partial void LogExtractionFailed(ILogger logger, Guid tenantId);

    [LoggerMessage(EventId = 12403, Level = LogLevel.Information, Message = "AgentMemoryDistillation tenant {TenantId}: {OpCount} memory ops applied")]
    private static partial void LogCompleted(ILogger logger, Guid tenantId, int opCount);
}
