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

    private Notification() { }

    public static Notification Create(
        Guid tenantId,
        Guid? userId,
        string type,
        string title,
        DateTimeOffset createdAt,
        string severity = "info",
        string? body = null,
        string? link = null) =>
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
        };

    public void MarkRead(DateTimeOffset at)
    {
        if (IsRead) return;
        IsRead = true;
        ReadAt = at;
    }
}
