using Clawbot.Domain.Jobs;
using Clawbot.Infrastructure.Jobs;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;
using JobEntity = Clawbot.Domain.Jobs.BackgroundJob;

namespace Clawbot.Infrastructure.Tests.Jobs;

public sealed class HangfireJobLauncherTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 8, 0, 0, TimeSpan.Zero);

    private static HangfireJobLauncher Build(TestAppDb fx)
    {
        var hangfire = Substitute.For<IBackgroundJobClient>();
        hangfire.Create(Arg.Any<Job>(), Arg.Any<IState>()).Returns("hf-1");

        var tenants = Substitute.For<ITenantAccessor>();
        var ctx = new TenantContext(fx.TenantId, "demo");
        tenants.Current.Returns(ctx);
        tenants.Require().Returns(ctx);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        return new HangfireJobLauncher(fx.Db, hangfire, tenants, clock);
    }

    [Fact]
    public async Task Launch_reuses_job_that_is_still_running_for_same_idempotency_key()
    {
        using var fx = new TestAppDb();
        var launcher = Build(fx);

        var first = await launcher.LaunchAsync("content.trends-scan", "Quét tuần", null, null, "trends:2026-W29");
        var second = await launcher.LaunchAsync("content.trends-scan", "Quét tuần", null, null, "trends:2026-W29");

        second.Should().Be(first, "job cùng khoá đang chờ/chạy thì bấm lần 2 không tạo job mới");
        (await fx.Db.BackgroundJobs.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Launch_creates_new_job_when_previous_one_with_same_key_finished()
    {
        using var fx = new TestAppDb();
        var launcher = Build(fx);

        var first = await launcher.LaunchAsync("content.trends-scan", "Quét tuần", null, null, "trends:2026-W29");

        var done = await fx.Db.BackgroundJobs.IgnoreQueryFilters().FirstAsync(j => j.Id == first);
        done.MarkRunning(Now);
        done.MarkSucceeded("/content", "xong", Now);
        await fx.Db.SaveChangesAsync();

        // Quét lại cùng tuần PHẢI chạy lại: trả về job cũ là user tưởng hệ thống không làm gì.
        var second = await launcher.LaunchAsync("content.trends-scan", "Quét tuần", null, null, "trends:2026-W29");

        second.Should().NotBe(first);
        (await fx.Db.BackgroundJobs.IgnoreQueryFilters().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Launch_keeps_payload_intact_for_the_handler()
    {
        using var fx = new TestAppDb();
        var launcher = Build(fx);

        // Payload là input của user (brief có hotline) — redact ở đây thì bài viết sinh ra mang số bị che.
        var jobId = await launcher.LaunchAsync(
            "content.generate", "Sinh bài", new { Brief = "Gọi hotline 0909123456 để đăng ký" });

        var job = await fx.Db.BackgroundJobs.IgnoreQueryFilters().AsNoTracking().FirstAsync(j => j.Id == jobId);
        job.PayloadJson.Should().Contain("0909123456");
        job.Status.Should().Be(BackgroundJobStatuses.Queued);
    }
}
