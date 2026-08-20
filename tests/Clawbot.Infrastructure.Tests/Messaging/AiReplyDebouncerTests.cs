using Clawbot.Infrastructure.Messaging;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Clawbot.Infrastructure.Tests.Messaging;

// Gom tin khách nhắn liên tiếp: debounce tắt -> trả lời ngay; debounce bật -> chỉ timer của tin cuối sống sót.
public sealed class AiReplyDebouncerTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Conversation = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static (AiReplyDebouncer Debouncer, IAiAutoReplyResumer Resumer) Build(int debounceSeconds)
    {
        var resumer = Substitute.For<IAiAutoReplyResumer>();
        resumer.ReplyToHangingCustomerMessageAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var services = new ServiceCollection();
        services.AddSingleton(resumer);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var options = Options.Create(new AiAutoReplyOptions { DebounceSeconds = debounceSeconds });
        var debouncer = new AiReplyDebouncer(scopeFactory, options, NullLogger<AiReplyDebouncer>.Instance);
        return (debouncer, resumer);
    }

    [Fact]
    public async Task Schedule_DebounceDisabled_FiresImmediately()
    {
        var (debouncer, resumer) = Build(0);

        debouncer.Schedule(Tenant, Conversation);

        // Fire là fire-and-forget; chờ ngắn để task chạy xong.
        await WaitForCallAsync(resumer);
        await resumer.Received(1).ReplyToHangingCustomerMessageAsync(Tenant, Conversation, Arg.Any<CancellationToken>());
        debouncer.Dispose();
    }

    [Fact]
    public async Task Schedule_DebounceWindow_FiresOnceAfterDelay()
    {
        var (debouncer, resumer) = Build(1);

        debouncer.Schedule(Tenant, Conversation);

        // Trước khi hết cửa sổ, chưa được gọi.
        await Task.Delay(200);
        await resumer.DidNotReceive().ReplyToHangingCustomerMessageAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        await WaitForCallAsync(resumer, timeoutMs: 3000);
        await resumer.Received(1).ReplyToHangingCustomerMessageAsync(Tenant, Conversation, Arg.Any<CancellationToken>());
        debouncer.Dispose();
    }

    [Fact]
    public async Task Schedule_RepeatedWithinWindow_OnlyLastTimerFires()
    {
        var (debouncer, resumer) = Build(1);

        // Ba tin liên tiếp -> chỉ tin cuối trả lời một lần.
        debouncer.Schedule(Tenant, Conversation);
        debouncer.Schedule(Tenant, Conversation);
        debouncer.Schedule(Tenant, Conversation);

        await WaitForCallAsync(resumer, timeoutMs: 3000);
        await Task.Delay(300); // để timer bị hủy (nếu có) kịp thể hiện
        await resumer.Received(1).ReplyToHangingCustomerMessageAsync(Tenant, Conversation, Arg.Any<CancellationToken>());
        debouncer.Dispose();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var (debouncer, _) = Build(30);
        debouncer.Schedule(Tenant, Conversation);

        debouncer.Dispose();
        var act = () => debouncer.Dispose();
        act.Should().NotThrow();
    }

    private static async Task WaitForCallAsync(IAiAutoReplyResumer resumer, int timeoutMs = 1000)
    {
        var deadline = timeoutMs;
        while (deadline > 0)
        {
            if (resumer.ReceivedCalls().Any())
                return;
            await Task.Delay(50);
            deadline -= 50;
        }
    }
}
