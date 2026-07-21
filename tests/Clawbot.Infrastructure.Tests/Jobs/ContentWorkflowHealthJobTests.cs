using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Content;
using Clawbot.Infrastructure.Jobs;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Jobs;

public sealed class ContentWorkflowHealthJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_emits_warning_when_old_pending_review_tasks_exist()
    {
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "facebook", "body", createdBy: null, Now.AddHours(-2));
        fx.Db.ContentItems.Add(item);
        fx.Db.ContentReviewTasks.Add(ContentReviewTask.CreatePending(
            fx.TenantId,
            item.Id,
            contentRevision: item.ContentRevision,
            nextAttemptAt: Now.AddMinutes(-5),
            createdAt: Now.AddMinutes(-45)));
        await fx.Db.SaveChangesAsync();

        var logger = new RecordingLogger();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var job = BuildJob(fx, logger, cache, publicationPaused: false);

        await job.RunAsync(CancellationToken.None);

        logger.Warnings.Should().ContainSingle(m =>
            m.Contains("Content workflow debt", StringComparison.Ordinal)
            && m.Contains("oldReviewTasks=1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_emits_warning_when_held_and_outcome_unknown_exceed_thresholds()
    {
        using var fx = new TestAppDb();
        for (var i = 0; i < 3; i++)
        {
            var item = ContentItem.Create(fx.TenantId, "facebook", $"held-{i}", createdBy: null, Now.AddHours(-2));
            var schedule = ContentSchedule.Schedule(
                fx.TenantId,
                item.Id,
                contentRevision: item.ContentRevision,
                platform: "facebook",
                scheduledAt: Now.AddMinutes(-10),
                createdAt: Now.AddHours(-1));
            schedule.MarkHeld(ContentSchedule.ErrorHeldForReview, Now.AddMinutes(-9));
            fx.Db.ContentItems.Add(item);
            fx.Db.ContentSchedules.Add(schedule);
        }

        var unknownItem = ContentItem.Create(fx.TenantId, "facebook", "unknown", createdBy: null, Now.AddHours(-3));
        var unknown = ContentSchedule.Schedule(
            fx.TenantId,
            unknownItem.Id,
            contentRevision: unknownItem.ContentRevision,
            platform: "facebook",
            scheduledAt: Now.AddMinutes(-20),
            createdAt: Now.AddHours(-2));
        unknown.MarkPublishing(Now.AddMinutes(-15));
        unknown.MarkOutcomeUnknown(Now.AddMinutes(-14), "timeout");
        fx.Db.ContentItems.Add(unknownItem);
        fx.Db.ContentSchedules.Add(unknown);
        await fx.Db.SaveChangesAsync();

        var logger = new RecordingLogger();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new ContentWorkflowHealthOptions
        {
            ReviewTaskAgeWarnMinutes = 30,
            HeldScheduleWarnCount = 3,
            OutcomeUnknownWarnCount = 1,
            AlertCooldownMinutes = 1,
        });
        var job = BuildJob(fx, logger, cache, publicationPaused: false, options);

        await job.RunAsync(CancellationToken.None);

        logger.Warnings.Should().ContainSingle(m =>
            m.Contains("held=3", StringComparison.Ordinal)
            && m.Contains("outcomeUnknown=1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_emits_warning_when_publication_is_paused()
    {
        using var fx = new TestAppDb();
        var logger = new RecordingLogger();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var job = BuildJob(fx, logger, cache, publicationPaused: true);

        await job.RunAsync(CancellationToken.None);

        logger.Warnings.Should().ContainSingle(m =>
            m.Contains("paused=True", StringComparison.Ordinal)
            || m.Contains("paused=true", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_is_silent_when_no_debt_and_publication_running()
    {
        using var fx = new TestAppDb();
        var logger = new RecordingLogger();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var job = BuildJob(fx, logger, cache, publicationPaused: false);

        await job.RunAsync(CancellationToken.None);

        logger.Warnings.Should().BeEmpty();
        logger.Informations.Should().ContainSingle(m =>
            m.Contains("Content workflow health", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_respects_alert_cooldown_cache()
    {
        using var fx = new TestAppDb();
        var item = ContentItem.Create(fx.TenantId, "facebook", "body", createdBy: null, Now.AddHours(-2));
        fx.Db.ContentItems.Add(item);
        fx.Db.ContentReviewTasks.Add(ContentReviewTask.CreatePending(
            fx.TenantId,
            item.Id,
            contentRevision: item.ContentRevision,
            nextAttemptAt: Now.AddMinutes(-5),
            createdAt: Now.AddMinutes(-45)));
        await fx.Db.SaveChangesAsync();

        var logger = new RecordingLogger();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var job = BuildJob(fx, logger, cache, publicationPaused: false);

        await job.RunAsync(CancellationToken.None);
        await job.RunAsync(CancellationToken.None);

        logger.Warnings.Should().ContainSingle();
    }

    private static ContentWorkflowHealthJob BuildJob(
        TestAppDb fx,
        ILogger<ContentWorkflowHealthJob> logger,
        IMemoryCache cache,
        bool publicationPaused,
        IOptions<ContentWorkflowHealthOptions>? options = null)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        var gate = Substitute.For<IContentWorkflowRuntimeGate>();
        gate.GetAsync(Arg.Any<CancellationToken>()).Returns(new ContentWorkflowRuntimeGateSnapshot(
            PublicationPaused: publicationPaused,
            MinimumWriterVersion: publicationPaused ? 1 : 0,
            UpdatedAt: Now,
            UpdatedBy: null,
            Notes: "test"));
        return new ContentWorkflowHealthJob(
            fx.Db,
            gate,
            clock,
            cache,
            options ?? Options.Create(new ContentWorkflowHealthOptions
            {
                ReviewTaskAgeWarnMinutes = 30,
                HeldScheduleWarnCount = 25,
                OutcomeUnknownWarnCount = 5,
                AlertCooldownMinutes = 1,
            }),
            logger);
    }

    private sealed class RecordingLogger : ILogger<ContentWorkflowHealthJob>
    {
        public List<string> Informations { get; } = [];
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (logLevel == LogLevel.Warning)
                Warnings.Add(message);
            else if (logLevel == LogLevel.Information)
                Informations.Add(message);
        }
    }
}
