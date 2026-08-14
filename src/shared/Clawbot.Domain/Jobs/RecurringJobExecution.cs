using Clawbot.Domain.Common;

namespace Clawbot.Domain.Jobs;

public static class RecurringJobExecutionSources
{
    public const string Scheduled = "scheduled";
    public const string Manual = "manual";
    public const string ManualRetry = "manual_retry";
}

public static class RecurringJobExecutionStatuses
{
    public const string Requested = "requested";
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Retrying = "retrying";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Skipped = "skipped";
    public const string EnqueueFailed = "enqueue_failed";

    public static bool IsTerminal(string status) => status is Succeeded
        or Failed
        or Cancelled
        or Skipped
        or EnqueueFailed;
}

public sealed class RecurringJobExecution : Entity<Guid>, IAuditExempt
{
    public const int MaxDefinitionIdLength = 128;
    public const int MaxRequestKeyLength = 64;
    public const int MaxHangfireJobIdLength = 64;
    public const int MaxProgressNoteLength = 200;
    public const int MaxResultSummaryLength = 1000;
    public const int MaxResultLinkLength = 400;
    public const int MaxErrorLength = 1000;

    public string DefinitionId { get; private set; } = string.Empty;
    public string Source { get; private set; } = string.Empty;
    public string Status { get; private set; } = RecurringJobExecutionStatuses.Requested;
    public Guid? RequestedByUserId { get; private set; }
    public Guid? RequestedTenantId { get; private set; }
    public Guid? RetryOfExecutionId { get; private set; }
    public string? RequestKey { get; private set; }
    public string? HangfireBackgroundJobId { get; private set; }
    public Guid? EnqueueClaimToken { get; private set; }
    public DateTimeOffset? EnqueueClaimedAt { get; private set; }
    public int? ProgressPercent { get; private set; }
    public string? ProgressNote { get; private set; }
    public string? ResultSummary { get; private set; }
    public string? ResultLink { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset? EnqueuedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    private RecurringJobExecution() { }

    public static RecurringJobExecution CreateManual(
        string definitionId,
        Guid requestedByUserId,
        Guid requestedTenantId,
        string requestKey,
        DateTimeOffset requestedAt) =>
        Create(
            definitionId,
            RecurringJobExecutionSources.Manual,
            requestedByUserId,
            requestedTenantId,
            requestKey,
            retryOfExecutionId: null,
            requestedAt);

    public static RecurringJobExecution CreateScheduled(
        string definitionId,
        string hangfireBackgroundJobId,
        DateTimeOffset requestedAt)
    {
        var execution = Create(
            definitionId,
            RecurringJobExecutionSources.Scheduled,
            requestedByUserId: null,
            requestedTenantId: null,
            requestKey: null,
            retryOfExecutionId: null,
            requestedAt);
        execution.AttachEnqueuedHangfireJob(hangfireBackgroundJobId, requestedAt);
        return execution;
    }

    public static RecurringJobExecution CreateManualRetry(
        RecurringJobExecution original,
        Guid requestedByUserId,
        Guid requestedTenantId,
        string requestKey,
        DateTimeOffset requestedAt)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (!RecurringJobExecutionStatuses.IsTerminal(original.Status))
            throw new InvalidOperationException("recurring_execution_not_terminal");

        return Create(
            original.DefinitionId,
            RecurringJobExecutionSources.ManualRetry,
            requestedByUserId,
            requestedTenantId,
            requestKey,
            original.Id,
            requestedAt);
    }

    public void ClaimEnqueue(Guid claimToken, DateTimeOffset claimedAt)
    {
        EnsureActive();
        if (Status != RecurringJobExecutionStatuses.Requested || HangfireBackgroundJobId is not null)
            throw new InvalidOperationException("recurring_execution_enqueue_already_claimed");
        if (claimToken == Guid.Empty)
            throw new ArgumentException("recurring_execution_enqueue_claim_required", nameof(claimToken));

        EnqueueClaimToken = claimToken;
        EnqueueClaimedAt = claimedAt;
        Version++;
    }

    public void AttachEnqueuedHangfireJob(
        string hangfireBackgroundJobId,
        DateTimeOffset enqueuedAt,
        Guid? claimToken = null)
    {
        EnsureActive();
        var normalizedJobId = NormalizeRequired(
            hangfireBackgroundJobId,
            MaxHangfireJobIdLength,
            "recurring_execution_hangfire_job_id_required");

        if (HangfireBackgroundJobId is not null
            && !string.Equals(HangfireBackgroundJobId, normalizedJobId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("recurring_execution_hangfire_job_id_conflict");
        }
        if (EnqueueClaimToken is not null && EnqueueClaimToken != claimToken)
            throw new InvalidOperationException("recurring_execution_enqueue_claim_conflict");

        HangfireBackgroundJobId = normalizedJobId;
        EnqueueClaimToken = null;
        EnqueueClaimedAt = null;
        EnqueuedAt ??= enqueuedAt;
        Status = RecurringJobExecutionStatuses.Queued;
        Version++;
    }

    public void ReleaseEnqueueClaim(Guid claimToken)
    {
        if (HangfireBackgroundJobId is not null || EnqueueClaimToken != claimToken)
            return;

        EnqueueClaimToken = null;
        EnqueueClaimedAt = null;
        Version++;
    }

    public void MarkRunning(DateTimeOffset startedAt)
    {
        EnsureActive();
        if (Status == RecurringJobExecutionStatuses.Requested)
            throw new InvalidOperationException("recurring_execution_not_enqueued");

        Status = RecurringJobExecutionStatuses.Running;
        StartedAt ??= startedAt;
        Version++;
    }

    public void ReportProgress(int percent, string? note)
    {
        EnsureActive();
        if (Status is not (RecurringJobExecutionStatuses.Running or RecurringJobExecutionStatuses.Retrying))
            throw new InvalidOperationException("recurring_execution_not_running");

        ProgressPercent = Math.Clamp(percent, 0, 100);
        ProgressNote = NormalizeOptional(note, MaxProgressNoteLength);
        Version++;
    }

    public void MarkRetrying(DateTimeOffset at)
    {
        EnsureActive();
        if (Status == RecurringJobExecutionStatuses.Requested)
            throw new InvalidOperationException("recurring_execution_not_enqueued");

        Status = RecurringJobExecutionStatuses.Retrying;
        StartedAt ??= at;
        Version++;
    }

    public void MarkSucceeded(string? resultLink, string? resultSummary, DateTimeOffset finishedAt)
    {
        EnsureActive();
        EnsureStarted();
        Status = RecurringJobExecutionStatuses.Succeeded;
        ProgressPercent = 100;
        ResultLink = NormalizeOptional(resultLink, MaxResultLinkLength);
        ResultSummary = NormalizeOptional(resultSummary, MaxResultSummaryLength);
        Error = null;
        FinishedAt = finishedAt;
        Version++;
    }

    public void MarkFailed(string error, DateTimeOffset finishedAt)
    {
        EnsureActive();
        EnsureStarted();
        Status = RecurringJobExecutionStatuses.Failed;
        Error = NormalizeRequired(error, MaxErrorLength, "recurring_execution_error_required");
        FinishedAt = finishedAt;
        Version++;
    }

    public void MarkCancelled(DateTimeOffset finishedAt)
    {
        EnsureActive();
        Status = RecurringJobExecutionStatuses.Cancelled;
        FinishedAt = finishedAt;
        Version++;
    }

    public void MarkSkipped(string? summary, DateTimeOffset finishedAt)
    {
        EnsureActive();
        Status = RecurringJobExecutionStatuses.Skipped;
        ResultSummary = NormalizeOptional(summary, MaxResultSummaryLength);
        FinishedAt = finishedAt;
        Version++;
    }

    public void MarkEnqueueFailed(string error, DateTimeOffset finishedAt)
    {
        EnsureActive();
        if (Status != RecurringJobExecutionStatuses.Requested)
            throw new InvalidOperationException("recurring_execution_not_requested");

        Status = RecurringJobExecutionStatuses.EnqueueFailed;
        EnqueueClaimToken = null;
        EnqueueClaimedAt = null;
        Error = NormalizeRequired(error, MaxErrorLength, "recurring_execution_error_required");
        FinishedAt = finishedAt;
        Version++;
    }

    private static RecurringJobExecution Create(
        string definitionId,
        string source,
        Guid? requestedByUserId,
        Guid? requestedTenantId,
        string? requestKey,
        Guid? retryOfExecutionId,
        DateTimeOffset requestedAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            DefinitionId = NormalizeRequired(
                definitionId,
                MaxDefinitionIdLength,
                "recurring_execution_definition_id_required"),
            Source = source,
            RequestedByUserId = requestedByUserId,
            RequestedTenantId = requestedTenantId,
            RequestKey = NormalizeOptional(requestKey, MaxRequestKeyLength),
            RetryOfExecutionId = retryOfExecutionId,
            RequestedAt = requestedAt,
        };

    private void EnsureActive()
    {
        if (RecurringJobExecutionStatuses.IsTerminal(Status))
            throw new InvalidOperationException("recurring_execution_terminal");
    }

    private void EnsureStarted()
    {
        if (StartedAt is null)
            throw new InvalidOperationException("recurring_execution_not_started");
    }

    private static string NormalizeRequired(string value, int maximumLength, string errorCode)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maximumLength)
            throw new ArgumentException(errorCode, nameof(value));
        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }
}

public static class RecurringJobExecutionAttemptStatuses
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public sealed class RecurringJobExecutionAttempt : Entity<Guid>, IAuditExempt
{
    public const int MaxHangfireJobIdLength = 64;
    public const int MaxErrorLength = 1000;
    public const int MaxWorkerIdLength = 128;

    public Guid ExecutionId { get; private set; }
    public string HangfireBackgroundJobId { get; private set; } = string.Empty;
    public int RetryCount { get; private set; }
    public int AttemptNumber { get; private set; }
    public string Status { get; private set; } = RecurringJobExecutionAttemptStatuses.Running;
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public string? Error { get; private set; }
    public string? WorkerId { get; private set; }
    public int Version { get; private set; }

    private RecurringJobExecutionAttempt() { }

    public static RecurringJobExecutionAttempt Start(
        Guid executionId,
        string hangfireBackgroundJobId,
        int retryCount,
        DateTimeOffset startedAt,
        string? workerId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retryCount);
        if (executionId == Guid.Empty)
            throw new ArgumentException("recurring_execution_attempt_execution_id_required", nameof(executionId));

        return new RecurringJobExecutionAttempt
        {
            Id = Guid.NewGuid(),
            ExecutionId = executionId,
            HangfireBackgroundJobId = NormalizeRequired(
                hangfireBackgroundJobId,
                MaxHangfireJobIdLength,
                "recurring_execution_attempt_hangfire_job_id_required"),
            RetryCount = retryCount,
            AttemptNumber = checked(retryCount + 1),
            StartedAt = startedAt,
            WorkerId = NormalizeOptional(workerId, MaxWorkerIdLength),
        };
    }

    public void MarkSucceeded(DateTimeOffset finishedAt)
    {
        EnsureRunning();
        Status = RecurringJobExecutionAttemptStatuses.Succeeded;
        FinishedAt = finishedAt;
        Error = null;
        Version++;
    }

    public void MarkFailed(string error, DateTimeOffset finishedAt)
    {
        EnsureRunning();
        Status = RecurringJobExecutionAttemptStatuses.Failed;
        Error = NormalizeRequired(error, MaxErrorLength, "recurring_execution_attempt_error_required");
        FinishedAt = finishedAt;
        Version++;
    }

    public void MarkCancelled(DateTimeOffset finishedAt)
    {
        EnsureRunning();
        Status = RecurringJobExecutionAttemptStatuses.Cancelled;
        FinishedAt = finishedAt;
        Version++;
    }

    private void EnsureRunning()
    {
        if (Status != RecurringJobExecutionAttemptStatuses.Running)
            throw new InvalidOperationException("recurring_execution_attempt_terminal");
    }

    private static string NormalizeRequired(string value, int maximumLength, string errorCode)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maximumLength)
            throw new ArgumentException(errorCode, nameof(value));
        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }
}
