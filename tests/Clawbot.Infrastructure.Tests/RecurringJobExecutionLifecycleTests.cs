using Clawbot.Domain.Jobs;
using Clawbot.Infrastructure.Jobs;
using FluentAssertions;

namespace Clawbot.Infrastructure.Tests;

public sealed class RecurringJobExecutionLifecycleTests
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateManual_StartsRequestedAndPreservesRequestAuditContext()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var requestKey = Guid.NewGuid().ToString("D");

        // Act
        var execution = RecurringJobExecution.CreateManual(
            "health-check",
            userId,
            tenantId,
            requestKey,
            RequestedAt);

        // Assert
        execution.Status.Should().Be(RecurringJobExecutionStatuses.Requested);
        execution.Source.Should().Be(RecurringJobExecutionSources.Manual);
        execution.DefinitionId.Should().Be("health-check");
        execution.RequestedByUserId.Should().Be(userId);
        execution.RequestedTenantId.Should().Be(tenantId);
        execution.RequestKey.Should().Be(requestKey);
        execution.RequestedAt.Should().Be(RequestedAt);
        execution.EnqueuedAt.Should().BeNull();
    }

    [Fact]
    public void AttachEnqueueAndComplete_RecordsLifecycleAndClampsProgress()
    {
        // Arrange
        var execution = RecurringJobExecution.CreateManual(
            "health-check",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid().ToString("D"),
            RequestedAt);
        var enqueuedAt = RequestedAt.AddMinutes(1);
        var startedAt = enqueuedAt.AddMinutes(1);
        var finishedAt = startedAt.AddMinutes(1);

        // Act
        execution.AttachEnqueuedHangfireJob("123", enqueuedAt);
        execution.MarkRunning(startedAt);
        execution.ReportProgress(140, "Kiểm tra kết nối cơ sở dữ liệu.");
        execution.MarkSucceeded("/system/health", "Cơ sở dữ liệu phản hồi.", finishedAt);

        // Assert
        execution.Status.Should().Be(RecurringJobExecutionStatuses.Succeeded);
        execution.HangfireBackgroundJobId.Should().Be("123");
        execution.EnqueuedAt.Should().Be(enqueuedAt);
        execution.StartedAt.Should().Be(startedAt);
        execution.FinishedAt.Should().Be(finishedAt);
        execution.ProgressPercent.Should().Be(100);
        execution.ProgressNote.Should().Be("Kiểm tra kết nối cơ sở dữ liệu.");
        execution.ResultLink.Should().Be("/system/health");
        execution.ResultSummary.Should().Be("Cơ sở dữ liệu phản hồi.");
        execution.Error.Should().BeNull();
    }

    [Fact]
    public void TerminalExecution_RejectsFurtherTransitionsAndPreservesHistoricalOutcome()
    {
        // Arrange
        var execution = RecurringJobExecution.CreateScheduled("health-check", "123", RequestedAt);
        execution.MarkRunning(RequestedAt.AddMinutes(1));
        execution.MarkSucceeded(null, "Cơ sở dữ liệu phản hồi.", RequestedAt.AddMinutes(2));

        // Act
        var transition = () => execution.MarkFailed("Không được ghi đè.", RequestedAt.AddMinutes(3));

        // Assert
        transition.Should().Throw<InvalidOperationException>()
            .WithMessage("recurring_execution_terminal");
        execution.Status.Should().Be(RecurringJobExecutionStatuses.Succeeded);
        execution.Error.Should().BeNull();
        execution.FinishedAt.Should().Be(RequestedAt.AddMinutes(2));
    }

    [Fact]
    public void CreateManualRetry_LinksNewRequestedExecutionWithoutChangingOriginal()
    {
        // Arrange
        var original = RecurringJobExecution.CreateManual(
            "health-check",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid().ToString("D"),
            RequestedAt);
        original.MarkEnqueueFailed("Không thể xác nhận đã xếp hàng.", RequestedAt.AddMinutes(1));

        // Act
        var retry = RecurringJobExecution.CreateManualRetry(
            original,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid().ToString("D"),
            RequestedAt.AddMinutes(2));

        // Assert
        retry.Id.Should().NotBe(original.Id);
        retry.DefinitionId.Should().Be(original.DefinitionId);
        retry.Source.Should().Be(RecurringJobExecutionSources.ManualRetry);
        retry.Status.Should().Be(RecurringJobExecutionStatuses.Requested);
        retry.RetryOfExecutionId.Should().Be(original.Id);
        original.Status.Should().Be(RecurringJobExecutionStatuses.EnqueueFailed);
        original.Error.Should().Be("Không thể xác nhận đã xếp hàng.");
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(101, 100)]
    public void ReportProgress_ClampsReportedValue(int reportedPercent, int expectedPercent)
    {
        // Arrange
        var execution = RecurringJobExecution.CreateScheduled("health-check", "123", RequestedAt);

        // Act
        execution.MarkRunning(RequestedAt.AddMinutes(1));
        execution.ReportProgress(reportedPercent, "Đang chạy.");

        // Assert
        execution.ProgressPercent.Should().Be(expectedPercent);
        execution.ProgressNote.Should().Be("Đang chạy.");
    }

    [Theory]
    [InlineData("https://example.test/result")]
    [InlineData("//example.test/result")]
    [InlineData("/%2Fexample.test/result")]
    [InlineData("/%252Fexample.test/result")]
    [InlineData("/result?token=secret")]
    [InlineData("javascript:alert(1)")]
    public void ResultLink_RejectsExternalAndUnsafeValues(string resultLink)
    {
        // Act
        var validate = () => RecurringJobResultLink.Validate(resultLink);

        // Assert
        validate.Should().Throw<ArgumentException>()
            .WithMessage("recurring_execution_result_link_invalid*");
    }

    [Fact]
    public void AttemptStart_DerivesImmutableAttemptNumberFromRetryCount()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var startedAt = RequestedAt.AddMinutes(1);

        // Act
        var attempt = RecurringJobExecutionAttempt.Start(executionId, "123", retryCount: 2, startedAt);
        attempt.MarkFailed("Lỗi an toàn.", startedAt.AddMinutes(1));

        // Assert
        attempt.ExecutionId.Should().Be(executionId);
        attempt.HangfireBackgroundJobId.Should().Be("123");
        attempt.RetryCount.Should().Be(2);
        attempt.AttemptNumber.Should().Be(3);
        attempt.Status.Should().Be(RecurringJobExecutionAttemptStatuses.Failed);
        attempt.StartedAt.Should().Be(startedAt);
        attempt.FinishedAt.Should().Be(startedAt.AddMinutes(1));
        attempt.Error.Should().Be("Lỗi an toàn.");
    }

    [Fact]
    public void AttemptStart_RejectsNegativeRetryCount()
    {
        // Act
        var create = () => RecurringJobExecutionAttempt.Start(
            Guid.NewGuid(),
            "123",
            retryCount: -1,
            RequestedAt);

        // Assert
        create.Should().Throw<ArgumentOutOfRangeException>();
    }
}
