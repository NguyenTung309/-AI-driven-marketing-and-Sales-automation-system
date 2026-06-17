using Clawbot.Infrastructure.Ads;
using FluentAssertions;

namespace Clawbot.Infrastructure.Tests.Ads;

public sealed class AdsPlatformThrottleTests
{
    [Fact]
    public async Task RunAsync_serializes_operations_for_the_same_platform()
    {
        using var throttle = new AdsPlatformThrottle();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = false;

        var first = throttle.RunAsync("meta", async ct =>
        {
            firstEntered.SetResult();
            await releaseFirst.Task.WaitAsync(ct);
            return 1;
        });
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var second = throttle.RunAsync("META", _ =>
        {
            secondStarted = true;
            return Task.FromResult(2);
        });

        await Task.Delay(50);
        secondStarted.Should().BeFalse();

        releaseFirst.SetResult();
        (await first).Should().Be(1);
        (await second).Should().Be(2);
        secondStarted.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_allows_different_platforms_to_run_independently()
    {
        using var throttle = new AdsPlatformThrottle();
        var metaEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseMeta = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var meta = throttle.RunAsync("meta", async ct =>
        {
            metaEntered.SetResult();
            await releaseMeta.Task.WaitAsync(ct);
            return 1;
        });
        await metaEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var tiktokResult = await throttle.RunAsync("tiktok", _ => Task.FromResult(2));

        tiktokResult.Should().Be(2);
        releaseMeta.SetResult();
        (await meta).Should().Be(1);
    }
}
