using Clawbot.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class OutOfHoursAutoReplyJob(
    AppDbContext db,
    ILogger<OutOfHoursAutoReplyJob> logger)
{
    private static readonly TimeOnly WorkStart = new(8, 0);
    private static readonly TimeOnly WorkEnd = new(22, 0);
    private static readonly TimeSpan Gmt7Offset = TimeSpan.FromHours(7);

    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var localTime = TimeOnly.FromDateTime(now.ToOffset(Gmt7Offset).DateTime);

        if (localTime >= WorkStart && localTime <= WorkEnd)
        {
            LogSkipped(logger, "within business hours");
            return;
        }

        var recentThreshold = now.AddMinutes(-5);

        var staleConversations = await db.Conversations
            .IgnoreQueryFilters()
            .Where(c => c.Status == "open"
                && c.LastMessageAt != null
                && c.LastMessageAt < recentThreshold
                && c.LastMessageAt > now.AddHours(-2))
            .Select(c => new { c.Id, c.TenantId })
            .Take(50)
            .ToListAsync(ct);

        if (staleConversations.Count == 0)
        {
            LogSkipped(logger, "no stale conversations");
            return;
        }

        LogProcessing(logger, staleConversations.Count);

        foreach (var convId in staleConversations.Select(c => c.Id).Distinct())
        {
            var hasSystemReply = await db.Messages
                .IgnoreQueryFilters()
                .Where(m => m.ConversationId == convId
                    && m.SenderType == "system"
                    && m.SentAt > now.AddHours(-2))
                .AnyAsync(ct);

            if (hasSystemReply) continue;

            var conv = await db.Conversations.IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == convId, ct);
            if (conv is null) continue;

            conv.AppendMessage("out", "system",
                "Cảm ơn bạn đã liên hệ! Hiện tại ngoài giờ làm việc (8:00-22:00). " +
                "Chúng tôi sẽ phản hồi trong giờ làm việc tiếp theo.",
                "text", now);
        }

        await db.SaveChangesAsync(ct);
        LogCompleted(logger, staleConversations.Count);
    }

    [LoggerMessage(EventId = 10001, Level = LogLevel.Debug,
        Message = "OutOfHours job skipped: {Reason}")]
    private static partial void LogSkipped(ILogger logger, string reason);

    [LoggerMessage(EventId = 10002, Level = LogLevel.Information,
        Message = "OutOfHours job processing {Count} stale conversations")]
    private static partial void LogProcessing(ILogger logger, int count);

    [LoggerMessage(EventId = 10003, Level = LogLevel.Information,
        Message = "OutOfHours job completed: {Count} conversations handled")]
    private static partial void LogCompleted(ILogger logger, int count);
}
