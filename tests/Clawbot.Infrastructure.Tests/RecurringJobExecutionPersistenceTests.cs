using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Jobs;
using Clawbot.Infrastructure.Jobs;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clawbot.Infrastructure.Tests;

public sealed class RecurringJobExecutionPersistenceTests
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Attempts_WithSameExecutionAndAttemptNumber_ViolateUniqueConstraint()
    {
        // Arrange
        await using var fixture = await SqliteFixture.CreateAsync();
        var execution = RecurringJobExecution.CreateScheduled("health-check", "123", RequestedAt);
        fixture.Db.RecurringJobExecutions.Add(execution);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.RecurringJobExecutionAttempts.AddRange(
            RecurringJobExecutionAttempt.Start(execution.Id, "123", 0, RequestedAt),
            RecurringJobExecutionAttempt.Start(execution.Id, "123", 0, RequestedAt));

        // Act
        var save = () => fixture.Db.SaveChangesAsync();

        // Assert
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Attempts_WithSameHangfireIdButDistinctRetrySlots_AreAllowed()
    {
        // Arrange
        await using var fixture = await SqliteFixture.CreateAsync();
        var execution = RecurringJobExecution.CreateScheduled("health-check", "123", RequestedAt);
        fixture.Db.RecurringJobExecutions.Add(execution);
        var first = RecurringJobExecutionAttempt.Start(execution.Id, "123", 0, RequestedAt);
        first.MarkFailed("Lỗi an toàn.", RequestedAt.AddSeconds(30));
        fixture.Db.RecurringJobExecutionAttempts.AddRange(
            first,
            RecurringJobExecutionAttempt.Start(execution.Id, "123", 1, RequestedAt.AddMinutes(1)));

        // Act
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var attempts = await fixture.Db.RecurringJobExecutionAttempts
            .Where(attempt => attempt.ExecutionId == execution.Id)
            .OrderBy(attempt => attempt.AttemptNumber)
            .ToListAsync();

        // Assert
        attempts.Should().HaveCount(2);
        attempts.Select(attempt => attempt.AttemptNumber).Should().Equal(1, 2);
        attempts.Select(attempt => attempt.HangfireBackgroundJobId).Should().OnlyContain(id => id == "123");
    }

    [Fact]
    public async Task CreateOrGetScheduledAsync_ReusesOneLogicalExecutionForRetryCorrelation()
    {
        // Arrange
        await using var fixture = await SqliteFixture.CreateAsync();
        var service = fixture.CreateService();

        // Act
        var first = await service.CreateOrGetScheduledAsync("health-check", "123");
        var second = await service.CreateOrGetScheduledAsync("health-check", "123");

        // Assert
        first.Id.Should().Be(second.Id);
        (await fixture.Db.RecurringJobExecutions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Attempts_WithSameExecutionAndRunningStatus_ViolateUniqueConstraint()
    {
        // Arrange
        await using var fixture = await SqliteFixture.CreateAsync();
        var execution = RecurringJobExecution.CreateScheduled("health-check", "123", RequestedAt);
        fixture.Db.RecurringJobExecutions.Add(execution);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.RecurringJobExecutionAttempts.AddRange(
            RecurringJobExecutionAttempt.Start(execution.Id, "123", 0, RequestedAt),
            RecurringJobExecutionAttempt.Start(execution.Id, "123", 1, RequestedAt.AddMinutes(1)));

        // Act
        var save = () => fixture.Db.SaveChangesAsync();

        // Assert
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ScheduledExecutions_WithSameDefinitionAndHangfireId_ViolateUniqueConstraint()
    {
        // Arrange
        await using var fixture = await SqliteFixture.CreateAsync();
        fixture.Db.RecurringJobExecutions.AddRange(
            RecurringJobExecution.CreateScheduled("health-check", "123", RequestedAt),
            RecurringJobExecution.CreateScheduled("health-check", "123", RequestedAt.AddMinutes(1)));

        // Act
        var save = () => fixture.Db.SaveChangesAsync();

        // Assert
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task CreateOrReuseManualAsync_RejectsMalformedRequestKey()
    {
        // Arrange
        await using var fixture = await SqliteFixture.CreateAsync();
        var service = fixture.CreateService();

        // Act
        var create = () => service.CreateOrReuseManualAsync(new RecurringJobExecutionRequest(
            "health-check",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "not-a-uuid"));

        // Assert
        await create.Should().ThrowAsync<ArgumentException>()
            .WithMessage("recurring_execution_request_key_invalid*");
        (await fixture.Db.RecurringJobExecutions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateOrReuseManualAsync_ReusesEquivalentRequestAndRejectsIncompatibleReuse()
    {
        // Arrange
        await using var fixture = await SqliteFixture.CreateAsync();
        var service = fixture.CreateService();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var requestKey = Guid.NewGuid().ToString("D");
        var request = new RecurringJobExecutionRequest("health-check", userId, tenantId, requestKey);

        // Act
        var first = await service.CreateOrReuseManualAsync(request);
        var second = await service.CreateOrReuseManualAsync(request);
        var conflict = () => service.CreateOrReuseManualAsync(request with { DefinitionId = "other-job" });

        // Assert
        first.Id.Should().Be(second.Id);
        await conflict.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("recurring_execution_request_key_conflict");
        (await fixture.Db.RecurringJobExecutions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateManualRetryAsync_CreatesLinkedExecutionAndReusesTheRequestKey()
    {
        // Arrange
        await using var fixture = await SqliteFixture.CreateAsync();
        var service = fixture.CreateService();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var original = await service.CreateOrReuseManualAsync(new RecurringJobExecutionRequest(
            "health-check",
            userId,
            tenantId,
            Guid.NewGuid().ToString("D")));
        await service.MarkEnqueueFailedAsync(original.Id, "safe failure");
        var request = new RecurringJobExecutionRequest(
            "health-check",
            userId,
            tenantId,
            Guid.NewGuid().ToString("D"));

        // Act
        var first = await service.CreateManualRetryAsync(original.Id, request);
        var second = await service.CreateManualRetryAsync(original.Id, request);

        // Assert
        first.Id.Should().Be(second.Id);
        first.Source.Should().Be(RecurringJobExecutionSources.ManualRetry);
        first.DefinitionId.Should().Be(original.DefinitionId);
        first.RetryOfExecutionId.Should().Be(original.Id);
        first.Status.Should().Be(RecurringJobExecutionStatuses.Requested);
        (await fixture.Db.RecurringJobExecutions.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task CreateOrReuseManualAsync_RejectsManualRetryRequestKey()
    {
        // Arrange
        await using var fixture = await SqliteFixture.CreateAsync();
        var service = fixture.CreateService();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var original = await service.CreateOrReuseManualAsync(new RecurringJobExecutionRequest(
            "health-check",
            userId,
            tenantId,
            Guid.NewGuid().ToString("D")));
        await service.MarkEnqueueFailedAsync(original.Id, "safe failure");
        var retryRequest = new RecurringJobExecutionRequest(
            "health-check",
            userId,
            tenantId,
            Guid.NewGuid().ToString("D"));
        await service.CreateManualRetryAsync(original.Id, retryRequest);

        // Act
        var trigger = () => service.CreateOrReuseManualAsync(retryRequest);

        // Assert
        await trigger.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("recurring_execution_request_key_conflict");
    }

    [Fact]
    public async Task CreateManualRetryAsync_RejectsOriginalFromAnotherTenant()
    {
        // Arrange
        await using var fixture = await SqliteFixture.CreateAsync();
        var service = fixture.CreateService();
        var originalTenantId = Guid.NewGuid();
        var original = await service.CreateOrReuseManualAsync(new RecurringJobExecutionRequest(
            "health-check",
            Guid.NewGuid(),
            originalTenantId,
            Guid.NewGuid().ToString("D")));
        await service.MarkEnqueueFailedAsync(original.Id, "safe failure");

        // Act
        var retry = () => service.CreateManualRetryAsync(original.Id, new RecurringJobExecutionRequest(
            "health-check",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid().ToString("D")));

        // Assert
        await retry.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("recurring_execution_tenant_id_conflict");
        (await fixture.Db.RecurringJobExecutions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateManualRetryAsync_RejectsNonterminalOriginalExecution()
    {
        // Arrange
        await using var fixture = await SqliteFixture.CreateAsync();
        var service = fixture.CreateService();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var original = await service.CreateOrReuseManualAsync(new RecurringJobExecutionRequest(
            "health-check",
            userId,
            tenantId,
            Guid.NewGuid().ToString("D")));

        // Act
        var retry = () => service.CreateManualRetryAsync(original.Id, new RecurringJobExecutionRequest(
            "health-check",
            userId,
            tenantId,
            Guid.NewGuid().ToString("D")));

        // Assert
        await retry.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("recurring_execution_not_terminal");
        (await fixture.Db.RecurringJobExecutions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ClaimEnqueueAsync_ClaimsOnlyOneManualExecutionAtATime()
    {
        // Arrange
        await using var fixture = await SqliteFixture.CreateAsync();
        var service = fixture.CreateService();
        var execution = await service.CreateOrReuseManualAsync(new RecurringJobExecutionRequest(
            "health-check",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid().ToString("D")));

        // Act
        var first = await service.ClaimEnqueueAsync(execution.Id);
        var second = await service.ClaimEnqueueAsync(execution.Id);

        // Assert
        first.Should().NotBeNull();
        second.Should().BeNull();
    }

    [Fact]
    public async Task MarkEnqueueFailedAsync_ClearsActiveEnqueueClaim()
    {
        // Arrange
        await using var fixture = await SqliteFixture.CreateAsync();
        var service = fixture.CreateService();
        var execution = await service.CreateOrReuseManualAsync(new RecurringJobExecutionRequest(
            "health-check",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid().ToString("D")));
        _ = await service.ClaimEnqueueAsync(execution.Id);

        // Act
        await service.MarkEnqueueFailedAsync(execution.Id, "safe failure");
        fixture.Db.ChangeTracker.Clear();
        var persisted = await fixture.Db.RecurringJobExecutions.SingleAsync();

        // Assert
        persisted.Status.Should().Be(RecurringJobExecutionStatuses.EnqueueFailed);
        persisted.EnqueueClaimToken.Should().BeNull();
        persisted.EnqueueClaimedAt.Should().BeNull();
    }

    [Fact]
    public async Task ClaimEnqueueAsync_ReturnsNoClaimForTerminalExecution()
    {
        // Arrange
        await using var fixture = await SqliteFixture.CreateAsync();
        var service = fixture.CreateService();
        var execution = await service.CreateOrReuseManualAsync(new RecurringJobExecutionRequest(
            "health-check",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid().ToString("D")));
        await service.MarkEnqueueFailedAsync(execution.Id, "safe failure");

        // Act
        var claim = await service.ClaimEnqueueAsync(execution.Id);

        // Assert
        claim.Should().BeNull();
    }

    [Fact]
    public async Task AttachEnqueueAsync_CompletesMatchingClaimAndRejectsOtherClaim()
    {
        // Arrange
        await using var fixture = await SqliteFixture.CreateAsync();
        var service = fixture.CreateService();
        var execution = await service.CreateOrReuseManualAsync(new RecurringJobExecutionRequest(
            "health-check",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid().ToString("D")));
        var claim = await service.ClaimEnqueueAsync(execution.Id);

        // Act
        var attachOtherClaim = () => service.AttachEnqueueAsync(
            new RecurringJobExecutionEnqueueClaim(execution.Id, Guid.NewGuid()),
            "123");
        await attachOtherClaim.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("recurring_execution_enqueue_claim_conflict");
        await service.AttachEnqueueAsync(claim!, "123");
        fixture.Db.ChangeTracker.Clear();
        var persisted = await fixture.Db.RecurringJobExecutions.SingleAsync();

        // Assert
        persisted.Status.Should().Be(RecurringJobExecutionStatuses.Queued);
        persisted.HangfireBackgroundJobId.Should().Be("123");
        persisted.EnqueueClaimToken.Should().BeNull();
        persisted.EnqueueClaimedAt.Should().BeNull();
    }

    [Fact]
    public async Task Service_RedactsAndBoundsProgressSummaryAndErrorBeforePersistence()
    {
        // Arrange
        await using var fixture = await SqliteFixture.CreateAsync();
        var redactor = new RecordingRedactor();
        var service = fixture.CreateService(redactor);
        var execution = await service.CreateOrReuseManualAsync(new RecurringJobExecutionRequest(
            "health-check",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid().ToString("D")));
        await service.AttachEnqueueAsync(execution.Id, "123");
        var attempt = await service.StartAttemptAsync(execution.Id, "123", 0, workerId: null);

        // Act
        await service.ReportProgressAsync(execution.Id, new RecurringJobExecutionProgress(110, "raw progress"));
        await service.RecordRetryableFailureAsync(execution.Id, attempt!.Id, "raw error");
        await service.FinalizeFailureAsync(execution.Id, "123", retryCount: 0);
        fixture.Db.ChangeTracker.Clear();
        var persisted = await fixture.Db.RecurringJobExecutions.SingleAsync();
        var persistedAttempt = await fixture.Db.RecurringJobExecutionAttempts.SingleAsync();

        // Assert
        persisted.ProgressPercent.Should().Be(100);
        persisted.ProgressNote.Should().Be("safe:raw progress");
        persisted.Error.Should().Be("safe:raw error");
        persistedAttempt.Error.Should().Be("safe:raw error");
    }

    [Fact]
    public async Task ManualExecutions_WithEquivalentRequestKey_ViolateUniqueConstraint()
    {
        // Arrange
        await using var fixture = await SqliteFixture.CreateAsync();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var requestKey = Guid.NewGuid().ToString("D");
        fixture.Db.RecurringJobExecutions.AddRange(
            RecurringJobExecution.CreateManual("health-check", userId, tenantId, requestKey, RequestedAt),
            RecurringJobExecution.CreateManual("health-check", userId, tenantId, requestKey, RequestedAt.AddMinutes(1)));

        // Act
        var save = () => fixture.Db.SaveChangesAsync();

        // Assert
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    private sealed class SqliteFixture(SqliteConnection connection, AppDbContext db) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;

        public RecurringJobExecutionService CreateService(IPiiRedactor? redactor = null) => new(
            Db,
            redactor ?? new RegexPiiRedactor(),
            new FixedClock(RequestedAt),
            NullLogger<RecurringJobExecutionService>.Instance);

        public static async Task<SqliteFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options, new NullTenantAccessor());
            await db.Database.EnsureCreatedAsync();
            return new SqliteFixture(connection, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class RecordingRedactor : IPiiRedactor
    {
        public string Name => "test";

        public Task<RedactionResult> RedactAsync(string text, CancellationToken ct) =>
            Task.FromResult(new RedactionResult($"safe:{text}", Array.Empty<PiiSpan>()));
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class NullTenantAccessor : ITenantAccessor
    {
        public TenantContext? Current => null;

        public TenantContext Require() => throw new InvalidOperationException("No tenant in test scope.");
    }
}
