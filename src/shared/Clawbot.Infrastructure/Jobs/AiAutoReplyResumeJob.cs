using Clawbot.Infrastructure.Messaging;
using Clawbot.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

// Sale gửi tay -> AI tạm nhường, đặt AiAutoReplyResumeAt. Consumer chỉ bật lại khi khách NHẮN TIẾP;
// job này lo case khách im luôn: hết cửa sổ nhường thì tự bật lại AI và trả lời tin khách đang treo.
// Scope job không có tenant -> IgnoreQueryFilters + truyền tenantId tường minh (xem hangfire-job-scope-has-no-tenant).
public sealed partial class AiAutoReplyResumeJob(
    AppDbContext db,
    IAiAutoReplyResumer resumer,
    ILogger<AiAutoReplyResumeJob> logger)
{
    private const int BatchLimit = 50;

    private readonly AppDbContext _db = db;
    private readonly IAiAutoReplyResumer _resumer = resumer;
    private readonly ILogger<AiAutoReplyResumeJob> _logger = logger;

    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        var due = await _db.Conversations
            .IgnoreQueryFilters()
            .Where(c => !c.AiAutoReplyEnabled
                && c.AiAutoReplyResumeAt != null
                && c.AiAutoReplyResumeAt <= now
                && c.Status == "open"
                && c.DeletedAt == null)
            .OrderBy(c => c.AiAutoReplyResumeAt)
            .Select(c => new { c.Id, c.TenantId })
            .Take(BatchLimit)
            .ToListAsync(ct).ConfigureAwait(false);

        if (due.Count == 0)
            return;

        LogResuming(_logger, due.Count);

        foreach (var item in due)
        {
            // Tracked (không AsNoTracking): TryResumeAiAutoReply -> SaveChanges bật lại cờ trước khi trả lời.
            var conv = await _db.Conversations
                .IgnoreQueryFilters()
                .Where(c => c.Id == item.Id && c.TenantId == item.TenantId)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (conv is null || !conv.TryResumeAiAutoReply(now))
                continue;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            await _resumer.ReplyToHangingCustomerMessageAsync(item.TenantId, item.Id, ct).ConfigureAwait(false);
        }
    }

    [LoggerMessage(EventId = 12010, Level = LogLevel.Information,
        Message = "AiAutoReplyResumeJob resuming {Count} conversations past their handover pause window")]
    private static partial void LogResuming(ILogger logger, int count);
}
