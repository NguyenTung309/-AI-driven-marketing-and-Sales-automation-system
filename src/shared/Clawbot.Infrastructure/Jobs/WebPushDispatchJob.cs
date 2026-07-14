using System.Net;
using System.Text.Json;
using Clawbot.Infrastructure.Notifications;
using Clawbot.Infrastructure.Persistence;
using Hangfire;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DomainPushSubscription = Clawbot.Domain.Notifications.PushSubscription;

namespace Clawbot.Infrastructure.Jobs;

/// <summary>
/// Đẩy Web Push cho 1 thông báo đã persist: user đóng tab vẫn nhận được.
/// Chạy trong Hangfire (không giữ request), 1 job / 1 thông báo.
/// </summary>
[Queue("default")]
[AutomaticRetry(Attempts = 2)]
public sealed partial class WebPushDispatchJob(
    AppDbContext db,
    PushServiceClient pushClient,
    IOptions<WebPushOptions> options,
    ILogger<WebPushDispatchJob> logger)
{
    public async Task SendAsync(Guid notificationId, CancellationToken ct = default)
    {
        var opts = options.Value;
        if (!opts.IsConfigured) return;

        // PushServiceClient là typed HttpClient (transient — mỗi job 1 instance), nên gán auth ở đây an toàn.
        // KHÔNG đăng ký sẵn trong DI: bọc AddScoped quanh chính nó gây resolve đệ quy vô hạn.
        pushClient.DefaultAuthentication = new VapidAuthentication(opts.PublicKey, opts.PrivateKey)
        {
            Subject = opts.Subject,
        };

        var notification = await db.Notifications.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == notificationId, ct).ConfigureAwait(false);
        if (notification is null) return;

        // Thông báo broadcast (UserId null) đẩy cho mọi user của tenant; có UserId thì chỉ người đó.
        var subscriptions = await db.PushSubscriptions.IgnoreQueryFilters()
            .Where(s => s.TenantId == notification.TenantId
                && (notification.UserId == null || s.UserId == notification.UserId))
            .ToListAsync(ct).ConfigureAwait(false);
        if (subscriptions.Count == 0) return;

        var payload = JsonSerializer.Serialize(new
        {
            title = notification.Title,
            body = notification.Body ?? string.Empty,
            url = notification.Link ?? "/notifications",
            id = notification.Id,
        });

        foreach (var group in subscriptions.GroupBy(s => s.UserId))
        {
            var preference = await NotificationDeliveryPolicy
                .FindAsync(db, notification.TenantId, group.Key, notification.Type, ct)
                .ConfigureAwait(false);
            if (!NotificationDeliveryPolicy.ShouldPush(preference, notification.Type, notification.Severity))
                continue;

            foreach (var subscription in group)
                await SendOneAsync(subscription, payload, ct).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task SendOneAsync(DomainPushSubscription subscription, string payload, CancellationToken ct)
    {
        var target = new Lib.Net.Http.WebPush.PushSubscription
        {
            Endpoint = subscription.Endpoint,
            Keys = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["p256dh"] = subscription.P256dh,
                ["auth"] = subscription.Auth,
            },
        };

        try
        {
            await pushClient.RequestPushMessageDeliveryAsync(target, new PushMessage(payload), ct).ConfigureAwait(false);
        }
        catch (PushServiceClientException ex)
            when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            // Trình duyệt đã gỡ đăng ký / hết hạn: xoá, không retry.
            db.PushSubscriptions.Remove(subscription);
            LogSubscriptionExpired(logger, subscription.Endpoint);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Push hỏng không được làm hỏng luồng thông báo: feed + chuông vẫn có.
            LogPushFailed(logger, ex, subscription.UserId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Push subscription expired, removed: {Endpoint}")]
    private static partial void LogSubscriptionExpired(ILogger logger, string endpoint);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Web push failed for user {UserId}")]
    private static partial void LogPushFailed(ILogger logger, Exception ex, Guid userId);
}
