using System.Collections.Concurrent;
using Clawbot.Infrastructure.Messaging;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Clawbot.Infrastructure.Tests;

public sealed class AiReplyDebouncerTests
{
    private sealed class CountingResumer : IAiAutoReplyResumer
    {
        public ConcurrentBag<(Guid TenantId, Guid ConversationId)> Calls { get; } = new();

        public Task<bool> ReplyToHangingCustomerMessageAsync(Guid tenantId, Guid conversationId, CancellationToken ct)
        {
            Calls.Add((tenantId, conversationId));
            return Task.FromResult(true);
        }
    }

    private static (AiReplyDebouncer Debouncer, CountingResumer Resumer) Create(int debounceSeconds)
    {
        var resumer = new CountingResumer();
        var services = new ServiceCollection();
        services.AddScoped<IAiAutoReplyResumer>(_ => resumer);
        var provider = services.BuildServiceProvider();
        var debouncer = new AiReplyDebouncer(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AiAutoReplyOptions { DebounceSeconds = debounceSeconds }),
            NullLogger<AiReplyDebouncer>.Instance);
        return (debouncer, resumer);
    }

    private static async Task WaitForCallsAsync(CountingResumer resumer, int expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (resumer.Calls.Count < expected && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task RapidSchedulesForSameConversationFireOnce()
    {
        var (debouncer, resumer) = Create(debounceSeconds: 1);
        var tenantId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();

        debouncer.Schedule(tenantId, conversationId);
        debouncer.Schedule(tenantId, conversationId);
        debouncer.Schedule(tenantId, conversationId);

        await WaitForCallsAsync(resumer, expected: 1, TimeSpan.FromSeconds(5));
        // Chờ thêm quá 1 cửa sổ nữa để chắc không có lần fire thứ hai từ các timer bị thay.
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        resumer.Calls.Should().ContainSingle().Which.Should().Be((tenantId, conversationId));
    }

    [Fact]
    public async Task DifferentConversationsFireIndependently()
    {
        var (debouncer, resumer) = Create(debounceSeconds: 1);
        var tenantId = Guid.NewGuid();
        var convA = Guid.NewGuid();
        var convB = Guid.NewGuid();

        debouncer.Schedule(tenantId, convA);
        debouncer.Schedule(tenantId, convB);

        await WaitForCallsAsync(resumer, expected: 2, TimeSpan.FromSeconds(5));

        resumer.Calls.Should().HaveCount(2);
        resumer.Calls.Should().Contain((tenantId, convA));
        resumer.Calls.Should().Contain((tenantId, convB));
    }

    [Fact]
    public async Task ZeroDebounceRunsImmediatelyPerCall()
    {
        var (debouncer, resumer) = Create(debounceSeconds: 0);
        var tenantId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();

        debouncer.Schedule(tenantId, conversationId);
        debouncer.Schedule(tenantId, conversationId);

        await WaitForCallsAsync(resumer, expected: 2, TimeSpan.FromSeconds(5));

        // Debounce tắt -> mỗi Schedule là một lần chạy ngay, hành vi cũ.
        resumer.Calls.Should().HaveCount(2);
    }

    [Fact]
    public async Task ReschedulingAfterFireStartsNewWindow()
    {
        var (debouncer, resumer) = Create(debounceSeconds: 1);
        var tenantId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();

        debouncer.Schedule(tenantId, conversationId);
        await WaitForCallsAsync(resumer, expected: 1, TimeSpan.FromSeconds(5));

        debouncer.Schedule(tenantId, conversationId);
        await WaitForCallsAsync(resumer, expected: 2, TimeSpan.FromSeconds(5));

        resumer.Calls.Should().HaveCount(2);
    }
}
