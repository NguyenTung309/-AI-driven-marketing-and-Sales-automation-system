using Clawbot.Domain.Notifications;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Notifications;

/// <summary>
/// Gom nhóm kiểu Facebook, dùng chung cho cả 2 publisher (API SignalR + AgentService Redis bridge).
/// Cộng dồn bằng 1 câu UPDATE rồi mới insert nếu không có dòng nào khớp — đọc-rồi-ghi sẽ đẻ 2 dòng
/// khi 2 sự kiện cùng nhóm về song song.
/// </summary>
public static class NotificationGrouping
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    public static async Task<Notification> UpsertAsync(
        AppDbContext db,
        NotificationRequest request,
        DateTimeOffset now,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.GroupKey))
            return await InsertAsync(db, request, now, ct).ConfigureAwait(false);

        var since = now - Window;
        var group = db.Notifications.IgnoreQueryFilters().Where(n =>
            n.TenantId == request.TenantId
            && n.UserId == request.UserId
            && n.GroupKey == request.GroupKey
            && !n.IsRead
            && n.CreatedAt >= since);

        var bumped = await group
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(n => n.OccurrenceCount, n => n.OccurrenceCount + 1)
                    .SetProperty(n => n.LastOccurredAt, now)
                    .SetProperty(n => n.Body, request.Body),
                ct)
            .ConfigureAwait(false);

        if (bumped == 0)
            return await InsertAsync(db, request, now, ct).ConfigureAwait(false);

        // Lấy lại dòng vừa cộng dồn để realtime push mang đúng id + số đếm.
        // AsNoTracking bắt buộc: ExecuteUpdate ghi thẳng DB, bản đang tracking (nếu có) vẫn giữ số đếm cũ.
        return await group.AsNoTracking()
            .OrderByDescending(n => n.CreatedAt)
            .FirstAsync(ct)
            .ConfigureAwait(false);
    }

    private static async Task<Notification> InsertAsync(
        AppDbContext db, NotificationRequest request, DateTimeOffset now, CancellationToken ct)
    {
        var notification = Notification.Create(
            request.TenantId, request.UserId, request.Type, request.Title,
            now, request.Severity, request.Body, request.Link, request.GroupKey);

        db.Notifications.Add(notification);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return notification;
    }
}
