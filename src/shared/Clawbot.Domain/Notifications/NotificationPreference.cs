using Clawbot.Domain.Common;

namespace Clawbot.Domain.Notifications;

/// <summary>
/// User bật/tắt từng loại thông báo. Không có dòng = dùng mặc định trong code.
/// Cảnh báo lỗi (severity=warning) luôn được đẩy, không đọc bảng này — tắt được là AI hỏng mà không ai biết.
/// </summary>
public sealed class NotificationPreference : Entity<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public bool InApp { get; private set; } = true;
    public bool Push { get; private set; } = true;
    public bool Email { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private NotificationPreference() { }

    public static NotificationPreference Create(
        Guid tenantId, Guid userId, string type, bool inApp, bool push, bool email, DateTimeOffset at) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            Type = type,
            InApp = inApp,
            Push = push,
            Email = email,
            UpdatedAt = at,
        };

    public void Update(bool inApp, bool push, bool email, DateTimeOffset at)
    {
        InApp = inApp;
        Push = push;
        Email = email;
        UpdatedAt = at;
    }
}

/// <summary>Web Push endpoint của 1 trình duyệt. Push service trả 404/410 = hết hạn, xoá dòng.</summary>
public sealed class PushSubscription : Entity<Guid>, ITenantOwned, IAuditExempt
{
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public string Endpoint { get; private set; } = string.Empty;
    public string P256dh { get; private set; } = string.Empty;
    public string Auth { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    private PushSubscription() { }

    public static PushSubscription Create(
        Guid tenantId, Guid userId, string endpoint, string p256dh, string auth, DateTimeOffset at) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            Endpoint = endpoint,
            P256dh = p256dh,
            Auth = auth,
            CreatedAt = at,
        };
}
