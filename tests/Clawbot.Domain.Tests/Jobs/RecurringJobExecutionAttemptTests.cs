using Clawbot.Domain.Jobs;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Jobs;

public sealed class RecurringJobExecutionAttemptTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ExecutionId = Guid.NewGuid();

    [Fact]
    public void Start_SetsAllFields()
    {
        var attempt = RecurringJobExecutionAttempt.Start(ExecutionId, "hf-job-1", 0, Now, "worker-A");

        attempt.ExecutionId.Should().Be(ExecutionId);
        attempt.HangfireBackgroundJobId.Should().Be("hf-job-1");
        attempt.RetryCount.Should().Be(0);
        attempt.AttemptNumber.Should().Be(1);
        attempt.Status.Should().Be("running");
        attempt.StartedAt.Should().Be(Now);
        attempt.FinishedAt.Should().BeNull();
        attempt.Error.Should().BeNull();
        attempt.WorkerId.Should().Be("worker-A");
        attempt.Version.Should().Be(0);
    }

    [Fact]
    public void Start_AttemptNumberIsRetryCountPlusOne()
    {
        var attempt = RecurringJobExecutionAttempt.Start(ExecutionId, "hf", 3, Now);

        attempt.RetryCount.Should().Be(3);
        attempt.AttemptNumber.Should().Be(4);
    }

    [Fact]
    public void Start_ThrowsOnNegativeRetryCount()
    {
        var act = () => RecurringJobExecutionAttempt.Start(ExecutionId, "hf", -1, Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Start_ThrowsOnEmptyExecutionId()
    {
        var act = () => RecurringJobExecutionAttempt.Start(Guid.Empty, "hf", 0, Now);

        act.Should().Throw<ArgumentException>().WithParameterName("executionId");
    }

    [Fact]
    public void Start_NullWorkerIdBecomesNull()
    {
        var attempt = RecurringJobExecutionAttempt.Start(ExecutionId, "hf", 0, Now, null);

        attempt.WorkerId.Should().BeNull();
    }

    [Fact]
    public void MarkSucceeded_TransitionsToSucceeded()
    {
        var attempt = RecurringJobExecutionAttempt.Start(ExecutionId, "hf", 0, Now);

        attempt.MarkSucceeded(Now.AddSeconds(10));

        attempt.Status.Should().Be("succeeded");
        attempt.FinishedAt.Should().Be(Now.AddSeconds(10));
        attempt.Error.Should().BeNull();
        attempt.Version.Should().Be(1);
    }

    [Fact]
    public void MarkFailed_TransitionsToFailedWithError()
    {
        var attempt = RecurringJobExecutionAttempt.Start(ExecutionId, "hf", 0, Now);

        attempt.MarkFailed("timeout exceeded", Now.AddSeconds(10));

        attempt.Status.Should().Be("failed");
        attempt.Error.Should().Be("timeout exceeded");
        attempt.FinishedAt.Should().Be(Now.AddSeconds(10));
        attempt.Version.Should().Be(1);
    }

    [Fact]
    public void MarkCancelled_TransitionsToCancelled()
    {
        var attempt = RecurringJobExecutionAttempt.Start(ExecutionId, "hf", 0, Now);

        attempt.MarkCancelled(Now.AddSeconds(5));

        attempt.Status.Should().Be("cancelled");
        attempt.FinishedAt.Should().Be(Now.AddSeconds(5));
        attempt.Version.Should().Be(1);
    }

    [Theory]
    [InlineData("succeeded")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    public void TerminalState_BlocksFurtherTransitions(string terminalStatus)
    {
        var attempt = RecurringJobExecutionAttempt.Start(ExecutionId, "hf", 0, Now);
        switch (terminalStatus)
        {
            case "succeeded": attempt.MarkSucceeded(Now.AddSeconds(1)); break;
            case "failed": attempt.MarkFailed("err", Now.AddSeconds(1)); break;
            case "cancelled": attempt.MarkCancelled(Now.AddSeconds(1)); break;
        }

        var actSucceed = () => attempt.MarkSucceeded(Now.AddSeconds(2));
        var actFail = () => attempt.MarkFailed("err", Now.AddSeconds(2));
        var actCancel = () => attempt.MarkCancelled(Now.AddSeconds(2));

        actSucceed.Should().Throw<InvalidOperationException>();
        actFail.Should().Throw<InvalidOperationException>();
        actCancel.Should().Throw<InvalidOperationException>();
    }
}
