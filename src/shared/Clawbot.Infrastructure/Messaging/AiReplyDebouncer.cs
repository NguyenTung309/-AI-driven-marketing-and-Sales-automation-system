using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawbot.Infrastructure.Messaging;

public sealed class AiAutoReplyOptions
{
    public const string SectionName = "AiAutoReply";

    // Cửa sổ gom tin: khách nhắn tiếp trong khoảng này thì reset đồng hồ, hết cửa sổ mới trả lời
    // một lần cho cả chuỗi. <= 0 để tắt debounce (trả lời ngay từng tin như trước).
    public int DebounceSeconds { get; set; } = 6;
}

public interface IAiReplyDebouncer
{
    void Schedule(Guid tenantId, Guid conversationId);
}

// Gom tin khách nhắn liên tiếp: mỗi tin inbound chỉ "đặt/reset đồng hồ" cho hội thoại; hết cửa sổ
// debounce mới gọi IAiAutoReplyResumer trả lời MỘT lần cho cả khối tin treo (nó tự đọc khối từ DB).
// In-memory theo host là chấp nhận được: fire chỉ là trigger, trạng thái thật nằm ở DB — host khác
// fire trùng thì guard "tin cuối phải là in" trong resumer chặn reply đúp; restart mất timer thì tin
// treo tới khi khách nhắn tiếp (tương đương mất reply giữa lúc generate ở luồng cũ).
public sealed partial class AiReplyDebouncer(
    IServiceScopeFactory scopeFactory,
    IOptions<AiAutoReplyOptions> options,
    ILogger<AiReplyDebouncer> logger) : IAiReplyDebouncer, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IOptions<AiAutoReplyOptions> _options = options;
    private readonly ILogger<AiReplyDebouncer> _logger = logger;
    private readonly ConcurrentDictionary<(Guid TenantId, Guid ConversationId), CancellationTokenSource> _pending = new();
    private bool _disposed;

    public void Schedule(Guid tenantId, Guid conversationId)
    {
        var delay = TimeSpan.FromSeconds(_options.Value.DebounceSeconds);
        if (delay <= TimeSpan.Zero)
        {
            // Thoát hiểm: debounce tắt -> chạy ngay, không qua timer.
            _ = RunAsync(tenantId, conversationId, CancellationToken.None);
            return;
        }

        var key = (tenantId, conversationId);
        var cts = new CancellationTokenSource();
        // Sliding window: tin mới thay CTS cũ và cancel nó — chỉ timer của tin cuối cùng sống sót.
        CancellationTokenSource? previous = null;
        _pending.AddOrUpdate(key, _ => cts, (_, old) =>
        {
            previous = old;
            return cts;
        });
        if (previous is not null)
        {
            try
            {
                previous.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Timer cũ vừa tự kết thúc và dispose CTS của nó — không sao, entry đã bị thay.
            }
        }

        _ = DelayedRunAsync(key, cts);
    }

    private async Task DelayedRunAsync((Guid TenantId, Guid ConversationId) key, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_options.Value.DebounceSeconds), cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Bị tin mới hơn thay thế (hoặc Dispose) — timer của tin đó sẽ lo phần trả lời.
            return;
        }
        finally
        {
            // Chỉ gỡ entry nếu vẫn là CTS của mình; tin mới hơn đã thay entry thì để nguyên.
            _pending.TryRemove(new KeyValuePair<(Guid, Guid), CancellationTokenSource>(key, cts));
            cts.Dispose();
        }

        await RunAsync(key.TenantId, key.ConversationId, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task RunAsync(Guid tenantId, Guid conversationId, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var resumer = scope.ServiceProvider.GetRequiredService<IAiAutoReplyResumer>();
            await resumer.ReplyToHangingCustomerMessageAsync(tenantId, conversationId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogDebouncedReplyFailed(_logger, ex, conversationId);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var cts in _pending.Values)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    [LoggerMessage(EventId = 9130, Level = LogLevel.Warning,
        Message = "Debounced AI reply failed for conversation {ConversationId}")]
    private static partial void LogDebouncedReplyFailed(ILogger logger, Exception ex, Guid conversationId);
}
