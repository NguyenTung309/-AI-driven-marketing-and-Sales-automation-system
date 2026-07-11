using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Leads;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawbot.Infrastructure.Jobs;

// Review-gate P5: template drip là text tĩnh duyệt-1-lần (QĐ6), NHƯNG {lead_name} interpolate từ tên
// contact (dữ liệu khách tự đặt) — nên bản đã render phải qua toxicity trước khi rời hệ thống.
public sealed partial class DripSequenceJob(
    AppDbContext db,
    IChannelAdapter adapter,
    IClock clock,
    ILogger<DripSequenceJob> logger,
    IToxicityFilter? toxicity = null,
    IOptions<ToxicityOptions>? toxicityOptions = null)
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        var dueEnrollments = await db.Set<DripEnrollment>()
            .IgnoreQueryFilters()
            .Where(e => e.Status == "active" && e.NextSendAt <= now)
            .OrderBy(e => e.NextSendAt)
            .Take(50)
            .ToListAsync(ct);

        if (dueEnrollments.Count == 0)
        {
            LogSkipped(logger, "no due enrollments");
            return;
        }

        LogProcessing(logger, dueEnrollments.Count);

        foreach (var enrollment in dueEnrollments)
        {
            try
            {
                var steps = await db.Set<DripSequenceStep>()
                    .Where(s => s.SequenceId == enrollment.SequenceId)
                    .OrderBy(s => s.StepOrder)
                    .ToListAsync(ct);

                var currentStepIdx = enrollment.CurrentStep;
                if (currentStepIdx >= steps.Count)
                {
                    enrollment.Complete(now);
                    continue;
                }

                var step = steps[currentStepIdx];
                var lead = await db.Leads.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(l => l.Id == enrollment.LeadId, ct);

                if (lead is null)
                {
                    enrollment.Cancel();
                    continue;
                }

                var contact = lead.ContactId.HasValue
                    ? await db.Contacts.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(c => c.Id == lead.ContactId.Value, ct)
                    : null;

                var conversation = lead.ContactId.HasValue
                    ? await db.Conversations.IgnoreQueryFilters()
                        .Where(c => c.TenantId == lead.TenantId
                            && c.ContactId == lead.ContactId.Value
                            && c.DeletedAt == null
                            && c.Status != "resolved")
                        .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
                        .FirstOrDefaultAsync(ct)
                    : null;
                if (conversation is null)
                {
                    enrollment.Cancel();
                    continue;
                }

                // Review-gate P3: sale đang cầm hội thoại (handover/manual) → drip đứng yên, KHÔNG cancel —
                // enrollment giữ nguyên và tự thử lại lần chạy sau; AI bật lại thì sequence tiếp tục.
                if (!conversation.AiAutoReplyEnabled)
                {
                    LogHeldManualMode(logger, enrollment.Id, conversation.Id);
                    continue;
                }

                // Build personalized message
                var body = step.TemplateBody
                    .Replace("{lead_name}", contact?.DisplayName ?? "bạn", StringComparison.Ordinal);

                // P5: bản render (template + tên khách) toxic → hủy enrollment (lỗi cấu hình template
                // hoặc tên contact bẩn — retry vô ích), log error cho người sửa. Fail-closed.
                if (toxicity is not null
                    && await toxicity.IsBlockedAsync(body, toxicityOptions?.Value.OutboundBlockThreshold ?? 0.8f, ct).ConfigureAwait(false))
                {
                    LogToxicBlocked(logger, enrollment.Id, conversation.Id);
                    enrollment.Cancel();
                    continue;
                }

                // Send via channel adapter
                LogSending(logger, enrollment.Id, step.Channel, currentStepIdx);
                if (!string.Equals(step.Channel, adapter.Name, StringComparison.OrdinalIgnoreCase))
                {
                    enrollment.Cancel();
                    continue;
                }

                await adapter.SendAsync(conversation.ExternalThreadId, body, ct).ConfigureAwait(false);
                conversation.AppendMessage("out", "agent", body, "text", now);

                // Advance to next step
                var nextStepIdx = currentStepIdx + 1;
                if (nextStepIdx >= steps.Count)
                {
                    enrollment.Complete(now);
                }
                else
                {
                    var nextStep = steps[nextStepIdx];
                    enrollment.Advance(nextStepIdx, now.AddHours(nextStep.DelayHours));
                }
            }
            catch (Exception ex)
            {
                LogError(logger, ex, enrollment.Id);
            }
        }

        await db.SaveChangesAsync(ct);
        LogCompleted(logger, dueEnrollments.Count);
    }

    [LoggerMessage(EventId = 11001, Level = LogLevel.Debug,
        Message = "DripSequence job skipped: {Reason}")]
    private static partial void LogSkipped(ILogger logger, string reason);

    [LoggerMessage(EventId = 11002, Level = LogLevel.Information,
        Message = "DripSequence job processing {Count} due enrollments")]
    private static partial void LogProcessing(ILogger logger, int count);

    [LoggerMessage(EventId = 11003, Level = LogLevel.Information,
        Message = "DripSequence job completed: {Count} enrollments processed")]
    private static partial void LogCompleted(ILogger logger, int count);

    [LoggerMessage(EventId = 11004, Level = LogLevel.Information,
        Message = "DripSequence sending enrollment {Id} via {Channel} step {Step}")]
    private static partial void LogSending(ILogger logger, Guid id, string channel, int step);

    [LoggerMessage(EventId = 11005, Level = LogLevel.Error,
        Message = "DripSequence error for enrollment {Id}")]
    private static partial void LogError(ILogger logger, Exception ex, Guid id);

    [LoggerMessage(EventId = 11006, Level = LogLevel.Information,
        Message = "DripSequence held enrollment {Id}: conversation {ConversationId} is in manual mode (AiAutoReplyEnabled=false)")]
    private static partial void LogHeldManualMode(ILogger logger, Guid id, Guid conversationId);

    [LoggerMessage(EventId = 11007, Level = LogLevel.Error,
        Message = "DripSequence blocked toxic rendered message for enrollment {Id} conversation {ConversationId} — enrollment cancelled, fix the template/contact name")]
    private static partial void LogToxicBlocked(ILogger logger, Guid id, Guid conversationId);
}
