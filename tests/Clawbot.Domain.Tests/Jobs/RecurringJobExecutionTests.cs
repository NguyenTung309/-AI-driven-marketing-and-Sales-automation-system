using Clawbot.Domain.Jobs;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Jobs;

public sealed class RecurringJobExecutionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();

    // ── CreateManual ──────────────────────────────────────────────────

    [Fact]
    public void CreateManual_SetsAllFields()
    {
        var exec = RecurringJobExecution.CreateManual("job.sync", UserId, TenantId, "req-1", Now);

        exec.DefinitionId.Should().Be("job.sync");
        exec.Source.Should().Be(RecurringJobExecutionSources.Manual);
        exec.RequestedByUserId.Should().Be(UserId);
        exec.RequestedTenantId.Should().Be(TenantId);
        exec.RequestKey.Should().Be("req-1");
        exec.RetryOfExecutionId.Should().BeNull();
        exec.Status.Should().Be(RecurringJobExecutionStatuses.Requested);
        exec.RequestedAt.Should().Be(Now);
        exec.Version.Should().Be(0);
    }

    [Fact]
    public void CreateManual_ThrowsOnEmptyDefinitionId()
    {
        var act = () => RecurringJobExecution.CreateManual("", UserId, TenantId, "k", Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreateManual_TrimsRequestKey()
    {
        var exec = RecurringJobExecution.CreateManual("j", UserId, TenantId, "  key  ", Now);

        exec.RequestKey.Should().Be("key");
    }

    // ── CreateScheduled ───────────────────────────────────────────────

    [Fact]
    public void CreateScheduled_AttachesHangfireJobAndQueues()
    {
        var exec = RecurringJobExecution.CreateScheduled("job.daily", "hf-123", Now);

        exec.Source.Should().Be(RecurringJobExecutionSources.Scheduled);
        exec.HangfireBackgroundJobId.Should().Be("hf-123");
        exec.Status.Should().Be(RecurringJobExecutionStatuses.Queued);
        exec.EnqueuedAt.Should().Be(Now);
        exec.RequestedByUserId.Should().BeNull();
    }

    // ── CreateManualRetry ─────────────────────────────────────────────

    [Fact]
    public void CreateManualRetry_CopiesDefinitionFromOriginal()
    {
        var original = RecurringJobExecution.CreateManual("job.x", UserId, TenantId, "k", Now);
        original.MarkCancelled(Now.AddMinutes(1));

        var retry = RecurringJobExecution.CreateManualRetry(original, UserId, TenantId, "k2", Now.AddMinutes(2));

        retry.DefinitionId.Should().Be("job.x");
        retry.Source.Should().Be(RecurringJobExecutionSources.ManualRetry);
        retry.RetryOfExecutionId.Should().Be(original.Id);
    }

    [Fact]
    public void CreateManualRetry_ThrowsWhenOriginalNotTerminal()
    {
        var original = RecurringJobExecution.CreateManual("job.x", UserId, TenantId, "k", Now);

        var act = () => RecurringJobExecution.CreateManualRetry(original, UserId, TenantId, "k2", Now);

        act.Should().Throw<InvalidOperationException>();
    }

    // ── ClaimEnqueue ──────────────────────────────────────────────────

    [Fact]
    public void ClaimEnqueue_SetsTokenAndBumpsVersion()
    {
        var exec = RecurringJobExecution.CreateManual("j", UserId, TenantId, "k", Now);
        var token = Guid.NewGuid();

        exec.ClaimEnqueue(token, Now.AddSeconds(1));

        exec.EnqueueClaimToken.Should().Be(token);
        exec.EnqueueClaimedAt.Should().Be(Now.AddSeconds(1));
        exec.Version.Should().Be(1);
    }

    [Fact]
    public void ClaimEnqueue_ThrowsWhenAlreadyQueued()
    {
        var exec = RecurringJobExecution.CreateScheduled("j", "hf-1", Now);

        var act = () => exec.ClaimEnqueue(Guid.NewGuid(), Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ClaimEnqueue_ThrowsOnEmptyToken()
    {
        var exec = RecurringJobExecution.CreateManual("j", UserId, TenantId, "k", Now);

        var act = () => exec.ClaimEnqueue(Guid.Empty, Now);

        act.Should().Throw<ArgumentException>();
    }

    // ── AttachEnqueuedHangfireJob ─────────────────────────────────────

    [Fact]
    public void AttachEnqueuedHangfireJob_TransitionsToQueued()
    {
        var exec = RecurringJobExecution.CreateManual("j", UserId, TenantId, "k", Now);

        exec.AttachEnqueuedHangfireJob("hf-456", Now.AddSeconds(5));

        exec.HangfireBackgroundJobId.Should().Be("hf-456");
        exec.Status.Should().Be(RecurringJobExecutionStatuses.Queued);
        exec.EnqueueClaimToken.Should().BeNull();
        exec.Version.Should().Be(1);
    }

    [Fact]
    public void AttachEnqueuedHangfireJob_ThrowsOnConflict()
    {
        var exec = RecurringJobExecution.CreateScheduled("j", "hf-1", Now);

        var act = () => exec.AttachEnqueuedHangfireJob("hf-2", Now);

        act.Should().Throw<InvalidOperationException>();
    }

    // ── ReleaseEnqueueClaim ───────────────────────────────────────────

    [Fact]
    public void ReleaseEnqueueClaim_ClearsMatchingToken()
    {
        var exec = RecurringJobExecution.CreateManual("j", UserId, TenantId, "k", Now);
        var token = Guid.NewGuid();
        exec.ClaimEnqueue(token, Now);

        exec.ReleaseEnqueueClaim(token);

        exec.EnqueueClaimToken.Should().BeNull();
        exec.EnqueueClaimedAt.Should().BeNull();
    }

    [Fact]
    public void ReleaseEnqueueClaim_IgnoresMismatchedToken()
    {
        var exec = RecurringJobExecution.CreateManual("j", UserId, TenantId, "k", Now);
        var token = Guid.NewGuid();
        exec.ClaimEnqueue(token, Now);

        exec.ReleaseEnqueueClaim(Guid.NewGuid());

        exec.EnqueueClaimToken.Should().Be(token);
    }

    // ── MarkRunning ───────────────────────────────────────────────────

    [Fact]
    public void MarkRunning_TransitionsFromQueued()
    {
        var exec = RecurringJobExecution.CreateScheduled("j", "hf-1", Now);

        exec.MarkRunning(Now.AddSeconds(10));

        exec.Status.Should().Be(RecurringJobExecutionStatuses.Running);
        exec.StartedAt.Should().Be(Now.AddSeconds(10));
    }

    [Fact]
    public void MarkRunning_ThrowsWhenRequested()
    {
        var exec = RecurringJobExecution.CreateManual("j", UserId, TenantId, "k", Now);

        var act = () => exec.MarkRunning(Now);

        act.Should().Throw<InvalidOperationException>();
    }

    // ── ReportProgress ────────────────────────────────────────────────

    [Fact]
    public void ReportProgress_ClampsPercent()
    {
        var exec = RecurringJobExecution.CreateScheduled("j", "hf-1", Now);
        exec.MarkRunning(Now);

        exec.ReportProgress(150, "almost done");

        exec.ProgressPercent.Should().Be(100);
        exec.ProgressNote.Should().Be("almost done");
    }

    [Fact]
    public void ReportProgress_ThrowsWhenNotRunning()
    {
        var exec = RecurringJobExecution.CreateScheduled("j", "hf-1", Now);

        var act = () => exec.ReportProgress(50, null);

        act.Should().Throw<InvalidOperationException>();
    }

    // ── Terminal transitions ──────────────────────────────────────────

    [Fact]
    public void MarkSucceeded_SetsResultAndClearsError()
    {
        var exec = RecurringJobExecution.CreateScheduled("j", "hf-1", Now);
        exec.MarkRunning(Now);

        exec.MarkSucceeded("https://link", "summary text", Now.AddMinutes(5));

        exec.Status.Should().Be(RecurringJobExecutionStatuses.Succeeded);
        exec.ProgressPercent.Should().Be(100);
        exec.ResultLink.Should().Be("https://link");
        exec.ResultSummary.Should().Be("summary text");
        exec.Error.Should().BeNull();
        exec.FinishedAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void MarkFailed_SetsError()
    {
        var exec = RecurringJobExecution.CreateScheduled("j", "hf-1", Now);
        exec.MarkRunning(Now);

        exec.MarkFailed("timeout", Now.AddMinutes(5));

        exec.Status.Should().Be(RecurringJobExecutionStatuses.Failed);
        exec.Error.Should().Be("timeout");
    }

    [Fact]
    public void MarkCancelled_SetsFinishedAt()
    {
        var exec = RecurringJobExecution.CreateScheduled("j", "hf-1", Now);

        exec.MarkCancelled(Now.AddMinutes(1));

        exec.Status.Should().Be(RecurringJobExecutionStatuses.Cancelled);
        exec.FinishedAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void MarkSkipped_SetsOptionalSummary()
    {
        var exec = RecurringJobExecution.CreateManual("j", UserId, TenantId, "k", Now);

        exec.MarkSkipped("nothing to do", Now);

        exec.Status.Should().Be(RecurringJobExecutionStatuses.Skipped);
        exec.ResultSummary.Should().Be("nothing to do");
    }

    [Fact]
    public void MarkEnqueueFailed_OnlyFromRequested()
    {
        var exec = RecurringJobExecution.CreateManual("j", UserId, TenantId, "k", Now);

        exec.MarkEnqueueFailed("enqueue error", Now);

        exec.Status.Should().Be(RecurringJobExecutionStatuses.EnqueueFailed);
        exec.Error.Should().Be("enqueue error");
        exec.EnqueueClaimToken.Should().BeNull();
    }

    [Fact]
    public void MarkEnqueueFailed_ThrowsWhenNotRequested()
    {
        var exec = RecurringJobExecution.CreateScheduled("j", "hf-1", Now);

        var act = () => exec.MarkEnqueueFailed("err", Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TerminalState_BlocksFurtherTransitions()
    {
        var exec = RecurringJobExecution.CreateManual("j", UserId, TenantId, "k", Now);
        exec.MarkCancelled(Now);

        var act = () => exec.MarkRunning(Now);

        act.Should().Throw<InvalidOperationException>();
    }

    // ── IsTerminal ────────────────────────────────────────────────────

    [Theory]
    [InlineData(RecurringJobExecutionStatuses.Succeeded)]
    [InlineData(RecurringJobExecutionStatuses.Failed)]
    [InlineData(RecurringJobExecutionStatuses.Cancelled)]
    [InlineData(RecurringJobExecutionStatuses.Skipped)]
    [InlineData(RecurringJobExecutionStatuses.EnqueueFailed)]
    public void IsTerminal_ReturnsTrueForTerminalStatuses(string status)
    {
        RecurringJobExecutionStatuses.IsTerminal(status).Should().BeTrue();
    }

    [Theory]
    [InlineData(RecurringJobExecutionStatuses.Requested)]
    [InlineData(RecurringJobExecutionStatuses.Queued)]
    [InlineData(RecurringJobExecutionStatuses.Running)]
    [InlineData(RecurringJobExecutionStatuses.Retrying)]
    public void IsTerminal_ReturnsFalseForActiveStatuses(string status)
    {
        RecurringJobExecutionStatuses.IsTerminal(status).Should().BeFalse();
    }

    // ── MarkRetrying ──────────────────────────────────────────────────

    [Fact]
    public void MarkRetrying_TransitionsFromQueued()
    {
        var exec = RecurringJobExecution.CreateScheduled("j", "hf-1", Now);

        exec.MarkRetrying(Now.AddSeconds(10));

        exec.Status.Should().Be(RecurringJobExecutionStatuses.Retrying);
        exec.StartedAt.Should().Be(Now.AddSeconds(10));
    }
}
