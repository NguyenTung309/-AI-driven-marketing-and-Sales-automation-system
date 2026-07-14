using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Jobs;
using Clawbot.Infrastructure.Jobs;
using Clawbot.SharedKernel.Jobs;
using Clawbot.SharedKernel.Notifications;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using JobEntity = Clawbot.Domain.Jobs.BackgroundJob;

namespace Clawbot.Infrastructure.Tests.Jobs;

// Nền "chạy ngầm — thông báo — click xem trạng thái": JobRunner là chỗ DUY NHẤT bắn thông báo,
// nên test ở đây khoá 2 điều: trạng thái cuối đúng, và thông báo bắn đúng 1 lần với link click được.
public sealed class JobRunnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 9, 0, 0, TimeSpan.Zero);

    private sealed class StubHandler(string type, Func<JobContext, Task<JobResult>> run) : IJobHandler
    {
        public string Type => type;

        public Task<JobResult> RunAsync(JobContext ctx, CancellationToken ct) => run(ctx);
    }

    private static (JobRunner Runner, INotificationPublisher Publisher) Build(
        TestAppDb fx, params IJobHandler[] handlers)
    {
        var publisher = Substitute.For<INotificationPublisher>();
        var realtime = Substitute.For<IJobRealtime>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        var pii = Substitute.For<IPiiRedactor>();
        pii.RedactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new RedactionResult(call.Arg<string>(), Array.Empty<PiiSpan>())));

        var runner = new JobRunner(fx.Db, handlers, publisher, realtime, pii, clock, NullLogger<JobRunner>.Instance);
        return (runner, publisher);
    }

    private static JobEntity QueueJob(TestAppDb fx, string type = "test.job")
    {
        var job = JobEntity.Queue(fx.TenantId, Guid.NewGuid(), type, "Sinh bài đăng", "{}", Now);
        fx.Db.BackgroundJobs.Add(job);
        fx.Db.SaveChanges();
        return job;
    }

    [Fact]
    public async Task RunAsync_marks_succeeded_and_notifies_with_result_link()
    {
        using var fx = new TestAppDb();
        var job = QueueJob(fx);
        var handler = new StubHandler("test.job", _ => Task.FromResult(new JobResult("/content?itemId=42", "Đã sinh 1 bài")));
        var (runner, publisher) = Build(fx, handler);

        await runner.RunAsync(job.Id, perform: null, CancellationToken.None);

        job.Status.Should().Be(BackgroundJobStatuses.Succeeded);
        job.Progress.Should().Be(100);
        job.ResultLink.Should().Be("/content?itemId=42");
        await publisher.Received(1).PublishAsync(
            Arg.Is<NotificationRequest>(r => r.Type == "job_succeeded" && r.Link == "/content?itemId=42"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_falls_back_to_job_center_link_when_handler_has_no_result_page()
    {
        using var fx = new TestAppDb();
        var job = QueueJob(fx);
        var (runner, publisher) = Build(fx, new StubHandler("test.job", _ => Task.FromResult(new JobResult())));

        await runner.RunAsync(job.Id, perform: null, CancellationToken.None);

        await publisher.Received(1).PublishAsync(
            Arg.Is<NotificationRequest>(r => r.Link == $"/agents?job={job.Id}"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_marks_failed_and_notifies_warning_on_final_attempt()
    {
        using var fx = new TestAppDb();
        var job = QueueJob(fx);
        var handler = new StubHandler("test.job", _ => throw new InvalidOperationException("LLM timeout"));
        var (runner, publisher) = Build(fx, handler);

        // perform=null => coi như lần retry cuối: phải báo user, không nuốt lỗi.
        await runner.RunAsync(job.Id, perform: null, CancellationToken.None);

        job.Status.Should().Be(BackgroundJobStatuses.Failed);
        job.Error.Should().Contain("LLM timeout");
        await publisher.Received(1).PublishAsync(
            Arg.Is<NotificationRequest>(r => r.Type == "job_failed" && r.Severity == "warning"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_fails_when_no_handler_registered_for_type()
    {
        using var fx = new TestAppDb();
        var job = QueueJob(fx, "unknown.type");
        var (runner, publisher) = Build(fx, new StubHandler("test.job", _ => Task.FromResult(new JobResult())));

        await runner.RunAsync(job.Id, perform: null, CancellationToken.None);

        job.Status.Should().Be(BackgroundJobStatuses.Failed);
        await publisher.Received(1).PublishAsync(
            Arg.Is<NotificationRequest>(r => r.Type == "job_failed"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_skips_cancelled_job_without_running_handler()
    {
        using var fx = new TestAppDb();
        var job = QueueJob(fx);
        job.RequestCancel(Now); // queued -> cancelled ngay
        await fx.Db.SaveChangesAsync();

        var ran = false;
        var handler = new StubHandler("test.job", _ =>
        {
            ran = true;
            return Task.FromResult(new JobResult());
        });
        var (runner, publisher) = Build(fx, handler);

        await runner.RunAsync(job.Id, perform: null, CancellationToken.None);

        ran.Should().BeFalse();
        job.Status.Should().Be(BackgroundJobStatuses.Cancelled);
        await publisher.DidNotReceive().PublishAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_does_not_rerun_a_finished_job()
    {
        using var fx = new TestAppDb();
        var job = QueueJob(fx);
        job.MarkRunning(Now);
        job.MarkSucceeded("/content", "xong", Now);
        await fx.Db.SaveChangesAsync();

        var runs = 0;
        var handler = new StubHandler("test.job", _ =>
        {
            runs++;
            return Task.FromResult(new JobResult());
        });
        var (runner, publisher) = Build(fx, handler);

        await runner.RunAsync(job.Id, perform: null, CancellationToken.None);

        runs.Should().Be(0);
        await publisher.DidNotReceive().PublishAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Requeue_only_allowed_from_failed_or_cancelled()
    {
        var job = JobEntity.Queue(Guid.NewGuid(), null, "test.job", "Việc", "{}", Now);
        job.MarkRunning(Now);

        job.Requeue().Should().BeFalse("job đang chạy không được đá về hàng đợi");

        job.MarkFailed("lỗi", Now);
        job.Requeue().Should().BeTrue();
        job.Status.Should().Be(BackgroundJobStatuses.Queued);
        job.Error.Should().BeNull();
        job.FinishedAt.Should().BeNull();
    }
}
