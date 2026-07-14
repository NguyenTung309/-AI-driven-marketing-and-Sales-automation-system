using Clawbot.Domain.Common;

namespace Clawbot.Domain.Notifications;

/// <summary>
/// Persisted alert for the notification center. <see cref="UserId"/> null = tenant broadcast.
/// </summary>
public sealed class Notification : Entity<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Severity { get; private set; } = "info";
    public string Title { get; private set; } = string.Empty;
    public string? Body { get; private set; }
    public string? Link { get; private set; }
    public bool IsRead { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // Gom nhóm kiểu Facebook: sự kiện cùng GroupKey chưa đọc thì cộng dồn vào 1 dòng thay vì đẻ dòng mới.
    public string? GroupKey { get; private set; }
    public int OccurrenceCount { get; private set; } = 1;
    public DateTimeOffset? LastOccurredAt { get; private set; }

    /// <summary>Đã gửi email dự phòng (cảnh báo lỗi chưa ai đọc) — chặn gửi lặp.</summary>
    public DateTimeOffset? EmailSentAt { get; private set; }

    private Notification() { }

    public static Notification Create(
        Guid tenantId,
        Guid? userId,
        string type,
        string title,
        DateTimeOffset createdAt,
        string severity = "info",
        string? body = null,
        string? link = null,
        string? groupKey = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            Type = type,
            Severity = severity,
            Title = title,
            Body = body,
            Link = link,
            IsRead = false,
            CreatedAt = createdAt,
            GroupKey = groupKey,
            OccurrenceCount = 1,
            LastOccurredAt = createdAt,
        };

    /// <summary>Cùng nhóm, chưa đọc: cộng dồn thay vì tạo dòng mới. Body lấy theo sự kiện mới nhất.</summary>
    public void Bump(DateTimeOffset at, string? body)
    {
        OccurrenceCount++;
        LastOccurredAt = at;
        if (!string.IsNullOrEmpty(body)) Body = body;
    }

    public void MarkEmailSent(DateTimeOffset at) => EmailSentAt = at;

    public void MarkRead(DateTimeOffset at)
    {
        if (IsRead) return;
        IsRead = true;
        ReadAt = at;
    }
}
