using Clawbot.Domain.Notifications;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Notifications;

/// <summary>
/// Quyết định 1 thông báo có vào feed / có đẩy push cho 1 user hay không.
///
/// Mặc định (khi user chưa chỉnh gì):
/// - Vào feed: tất cả.
/// - Đẩy push: mọi thứ TRỪ nhóm việc máy móc lặp lại (đổi giá thầu, auto-reply, drip, học trí nhớ) —
///   chúng vẫn nằm trong feed, chỉ không rung chuông.
///
/// Chốt cứng: severity=warning (job fail, token hỏng) LUÔN push, bỏ qua preferences.
/// Tắt được cái đó thì AI hỏng mà không ai biết — đúng thứ hệ thống này phải tránh nhất.
/// </summary>
public static class NotificationDeliveryPolicy
{
    private static readonly HashSet<string> QuietByDefault = new(StringComparer.OrdinalIgnoreCase)
    {
        "ads_daypart",
        "ads_creative_rotation",
        "ads_remarketing",
        "drip_sent",
        "comment_auto_reply",
        "contact_memory_learned",
        "agent_memory_learned",
    };

    public static bool IsAlwaysPushed(string severity) =>
        severity is "warning" or "error" or "critical";

    public static bool DefaultPush(string type) => !QuietByDefault.Contains(type);

    public static async Task<NotificationPreference?> FindAsync(
        AppDbContext db, Guid tenantId, Guid userId, string type, CancellationToken ct) =>
        await db.NotificationPreferences.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.TenantId == tenantId && p.UserId == userId && (p.Type == type || p.Type == "*"),
                ct)
            .ConfigureAwait(false);

    public static bool ShouldPush(NotificationPreference? preference, string type, string severity)
    {
        if (IsAlwaysPushed(severity)) return true;
        return preference?.Push ?? DefaultPush(type);
    }

    public static bool ShouldShowInApp(NotificationPreference? preference, string severity)
    {
        if (IsAlwaysPushed(severity)) return true;
        return preference?.InApp ?? true;
    }
}
