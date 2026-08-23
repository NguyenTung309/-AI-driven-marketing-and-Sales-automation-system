using Clawbot.Domain.Jobs;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Jobs;

public sealed class BackgroundJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static BackgroundJob CreateQueued() =>
        BackgroundJob.Queue(TenantId, UserId, "content_generate", "Generate post", "{\"topic\":\"ai\"}", Now, "idem-1");

    // ── Queue ─────────────────────────────────────────────────────────

    [Fact]
    public void Queue_SetsInitialDefaults()
    {
        var job = CreateQueued();

        job.TenantId.Should().Be(TenantId);
        job.UserId.Should().Be(UserId);
        job.Type.Should().Be("content_generate");
        job.Title.Should().Be("Generate post");
        job.Status.Should().Be(BackgroundJobStatuses.Queued);
        job.Progress.Should().Be(0);
        job.PayloadJson.Should().Be("{\"topic\":\"ai\"}");
        job.IdempotencyKey.Should().Be("idem-1");
        job.CreatedAt.Should().Be(Now);
        job.StartedAt.Should().BeNull();
        job.FinishedAt.Should().BeNull();
        job.CancelRequested.Should().BeFalse();
    }

    [Fact]
    public void Queue_AllowsNullUserIdAndIdempotencyKey()
    {
        var job = BackgroundJob.Queue(TenantId, null, "kb_test", "Test KB", null, Now);

        job.UserId.Should().BeNull();
        job.IdempotencyKey.Should().BeNull();
        job.PayloadJson.Should().BeNull();
    }

    // ── AttachHangfireJob ─────────────────────────────────────────────

    [Fact]
    public void AttachHangfireJob_SetsHangfireJobId()
    {
        var job = CreateQueued();

        job.AttachHangfireJob("hf-123");

        job.HangfireJobId.Should().Be("hf-123");
    }

    // ── MarkRunning ───────────────────────────────────────────────────

    [Fact]
    public void MarkRunning_TransitionsFromQueued()
    {
        var job = CreateQueued();

        job.MarkRunning(Now.AddMinutes(1));

        job.Status.Should().Be(BackgroundJobStatuses.Running);
        job.StartedAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void MarkRunning_PreservesOriginalStartedAt()
    {
        var job = CreateQueued();
        job.MarkRunning(Now.AddMinutes(1));

        job.MarkRunning(Now.AddMinutes(5));

        job.StartedAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void MarkRunning_NoOpWhenTerminal()
    {
        var job = CreateQueued();
        job.MarkRunning(Now);
        job.MarkSucceeded(null, null, Now.AddMinutes(1));

        job.MarkRunning(Now.AddMinutes(5));

        job.Status.Should().Be(BackgroundJobStatuses.Succeeded);
    }

    // ── ReportProgress ────────────────────────────────────────────────

    [Fact]
    public void ReportProgress_UpdatesPercentAndNote()
    {
        var job = CreateQueued();
        job.MarkRunning(Now);

        job.ReportProgress(50, "halfway done");

        job.Progress.Should().Be(50);
        job.ProgressNote.Should().Be("halfway done");
    }

    [Fact]
    public void ReportProgress_ClampsToZeroAndHundred()
    {
        var job = CreateQueued();
        job.MarkRunning(Now);

        job.ReportProgress(-10, null);
        job.Progress.Should().Be(0);

        job.ReportProgress(200, null);
        job.Progress.Should().Be(100);
    }

    [Fact]
    public void ReportProgress_NoOpWhenTerminal()
    {
        var job = CreateQueued();
        job.MarkRunning(Now);
        job.MarkFailed("err", Now.AddMinutes(1));

        job.ReportProgress(99, "almost");

        job.Progress.Should().Be(0);
    }

    // ── MarkSucceeded ─────────────────────────────────────────────────

    [Fact]
    public void MarkSucceeded_TransitionsAndSetsResult()
    {
        var job = CreateQueued();
        job.MarkRunning(Now);

        job.MarkSucceeded("https://result.link", "Done!", Now.AddMinutes(5));

        job.Status.Should().Be(BackgroundJobStatuses.Succeeded);
        job.Progress.Should().Be(100);
        job.ResultLink.Should().Be("https://result.link");
        job.ResultSummary.Should().Be("Done!");
        job.FinishedAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void MarkSucceeded_NoOpWhenAlreadyTerminal()
    {
        var job = CreateQueued();
        job.MarkRunning(Now);
        job.MarkFailed("err", Now.AddMinutes(1));

        job.MarkSucceeded(null, null, Now.AddMinutes(5));

        job.Status.Should().Be(BackgroundJobStatuses.Failed);
    }

    // ── MarkFailed ────────────────────────────────────────────────────

    [Fact]
    public void MarkFailed_TransitionsWithError()
    {
        var job = CreateQueued();
        job.MarkRunning(Now);

        job.MarkFailed("something broke", Now.AddMinutes(5));

        job.Status.Should().Be(BackgroundJobStatuses.Failed);
        job.Error.Should().Be("something broke");
        job.FinishedAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void MarkFailed_NoOpWhenAlreadyTerminal()
    {
        var job = CreateQueued();
        job.MarkRunning(Now);
        job.MarkSucceeded(null, null, Now.AddMinutes(1));

        job.MarkFailed("late error", Now.AddMinutes(5));

        job.Status.Should().Be(BackgroundJobStatuses.Succeeded);
    }

    // ── RequestCancel ─────────────────────────────────────────────────

    [Fact]
    public void RequestCancel_FromQueued_CancelsImmediately()
    {
        var job = CreateQueued();

        job.RequestCancel(Now.AddMinutes(1));

        job.CancelRequested.Should().BeTrue();
        job.Status.Should().Be(BackgroundJobStatuses.Cancelled);
        job.FinishedAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void RequestCancel_FromRunning_SetsFlagOnly()
    {
        var job = CreateQueued();
        job.MarkRunning(Now);

        job.RequestCancel(Now.AddMinutes(1));

        job.CancelRequested.Should().BeTrue();
        job.Status.Should().Be(BackgroundJobStatuses.Running);
        job.FinishedAt.Should().BeNull();
    }

    [Fact]
    public void RequestCancel_NoOpWhenTerminal()
    {
        var job = CreateQueued();
        job.MarkRunning(Now);
        job.MarkSucceeded(null, null, Now.AddMinutes(1));

        job.RequestCancel(Now.AddMinutes(5));

        job.CancelRequested.Should().BeFalse();
    }

    // ── MarkCancelled ─────────────────────────────────────────────────

    [Fact]
    public void MarkCancelled_TransitionsToCancelled()
    {
        var job = CreateQueued();
        job.MarkRunning(Now);
        job.RequestCancel(Now.AddMinutes(1));

        job.MarkCancelled(Now.AddMinutes(5));

        job.Status.Should().Be(BackgroundJobStatuses.Cancelled);
        job.FinishedAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void MarkCancelled_NoOpWhenAlreadyTerminal()
    {
        var job = CreateQueued();
        job.MarkRunning(Now);
        job.MarkFailed("err", Now.AddMinutes(1));

        job.MarkCancelled(Now.AddMinutes(5));

        job.Status.Should().Be(BackgroundJobStatuses.Failed);
    }

    // ── Requeue ───────────────────────────────────────────────────────

    [Fact]
    public void Requeue_ResetsFailedJobToQueued()
    {
        var job = CreateQueued();
        job.MarkRunning(Now);
        job.MarkFailed("err", Now.AddMinutes(1));

        var requeued = job.Requeue();

        requeued.Should().BeTrue();
        job.Status.Should().Be(BackgroundJobStatuses.Queued);
        job.CancelRequested.Should().BeFalse();
        job.Progress.Should().Be(0);
        job.ProgressNote.Should().BeNull();
        job.Error.Should().BeNull();
        job.StartedAt.Should().BeNull();
        job.FinishedAt.Should().BeNull();
    }

    [Fact]
    public void Requeue_ResetsCancelledJobToQueued()
    {
        var job = CreateQueued();
        job.RequestCancel(Now.AddMinutes(1));

        var requeued = job.Requeue();

        requeued.Should().BeTrue();
        job.Status.Should().Be(BackgroundJobStatuses.Queued);
    }

    [Fact]
    public void Requeue_ReturnsFalseWhenNotTerminal()
    {
        var job = CreateQueued();
        job.MarkRunning(Now);

        var requeued = job.Requeue();

        requeued.Should().BeFalse();
        job.Status.Should().Be(BackgroundJobStatuses.Running);
    }

    [Fact]
    public void Requeue_ReturnsFalseWhenSucceeded()
    {
        var job = CreateQueued();
        job.MarkRunning(Now);
        job.MarkSucceeded(null, null, Now.AddMinutes(1));

        var requeued = job.Requeue();

        requeued.Should().BeFalse();
    }

    // ── BackgroundJobStatuses.IsTerminal ──────────────────────────────

    [Theory]
    [InlineData(BackgroundJobStatuses.Succeeded, true)]
    [InlineData(BackgroundJobStatuses.Failed, true)]
    [InlineData(BackgroundJobStatuses.Cancelled, true)]
    [InlineData(BackgroundJobStatuses.Queued, false)]
    [InlineData(BackgroundJobStatuses.Running, false)]
    public void IsTerminal_ClassifiesCorrectly(string status, bool expected)
    {
        BackgroundJobStatuses.IsTerminal(status).Should().Be(expected);
    }
}
