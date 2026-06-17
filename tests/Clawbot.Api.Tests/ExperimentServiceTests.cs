using Clawbot.Api.Services;
using Clawbot.Domain.ChatScenarios;
using Clawbot.Domain.Experiments;
using Clawbot.Domain.KnowledgeBase;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Tests;

public sealed class ExperimentServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("abababab-abab-abab-abab-abababababab");
    private static readonly DateTimeOffset Now = new(2026, 6, 16, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task AssignAsync_returns_stable_weighted_variant_and_records_one_exposure()
    {
        using var fx = new TestApiAppDb(TenantId);
        var scenarioA = ChatScenario.Create(TenantId, "KB-A", "pricing", "hoc phi", "Control", "all", Now);
        var scenarioB = ChatScenario.Create(TenantId, "KB-B", "pricing", "hoc phi", "Direct CTA", "all", Now);
        var experiment = Experiment.Create(TenantId, "chat-pricing", "chat_scenario", Guid.NewGuid(), "Pricing script A/B", Now);
        var control = experiment.AddVariant("A", "Control", 50, chatScenarioId: scenarioA.Id, kbVersionId: null, Now);
        experiment.AddVariant("B", "Direct CTA", 50, chatScenarioId: scenarioB.Id, kbVersionId: null, Now);
        fx.Db.AddRange(scenarioA, scenarioB, experiment);
        await fx.Db.SaveChangesAsync();
        var sut = new ExperimentService(fx.Db, new FixedClock(Now));

        var first = await sut.AssignAsync(TenantId, experiment.Id, "conversation:123", CancellationToken.None);
        var second = await sut.AssignAsync(TenantId, experiment.Id, "conversation:123", CancellationToken.None);

        first.ExperimentId.Should().Be(experiment.Id);
        first.VariantId.Should().Be(second.VariantId);
        first.VariantCode.Should().BeOneOf("A", "B");
        first.ChatScenarioId.Should().NotBeNull();
        first.KbVersionId.Should().BeNull();

        var assignments = await fx.Db.ExperimentAssignments.IgnoreQueryFilters().ToListAsync();
        assignments.Should().ContainSingle(a => a.SubjectKey == "conversation:123" && a.ExperimentId == experiment.Id);
        var exposures = await fx.Db.ExperimentEvents.IgnoreQueryFilters().Where(e => e.EventType == "exposure").ToListAsync();
        exposures.Should().ContainSingle(e => e.VariantId == first.VariantId);
        control.Weight.Should().Be(50);
    }

    [Fact]
    public async Task GetSummaryAsync_reports_conversion_rates_and_winner()
    {
        using var fx = new TestApiAppDb(TenantId);
        var module = KbModule.Create(TenantId, "HSK3", "HSK 3", Now);
        var versionA = KbVersion.Create(module.Id, 1, "Current KB", Now);
        var versionB = KbVersion.Create(module.Id, 2, "New KB", Now);
        var experiment = Experiment.Create(TenantId, "kb-hsk3", "kb_version", Guid.NewGuid(), "HSK3 KB A/B", Now);
        var a = experiment.AddVariant("A", "Current KB", 50, chatScenarioId: null, kbVersionId: versionA.Id, Now);
        var b = experiment.AddVariant("B", "New KB", 50, chatScenarioId: null, kbVersionId: versionB.Id, Now);
        fx.Db.AddRange(module, versionA, versionB, experiment);
        await fx.Db.SaveChangesAsync();
        var sut = new ExperimentService(fx.Db, new FixedClock(Now));

        await sut.RecordEventAsync(TenantId, experiment.Id, a.Id, "lead-1", "exposure", null, CancellationToken.None);
        await sut.RecordEventAsync(TenantId, experiment.Id, a.Id, "lead-1", "conversion", null, CancellationToken.None);
        await sut.RecordEventAsync(TenantId, experiment.Id, b.Id, "lead-2", "exposure", null, CancellationToken.None);
        await sut.RecordEventAsync(TenantId, experiment.Id, b.Id, "lead-3", "exposure", null, CancellationToken.None);

        var summary = await sut.GetSummaryAsync(TenantId, experiment.Id, CancellationToken.None);

        summary.ExperimentId.Should().Be(experiment.Id);
        summary.WinnerVariantCode.Should().Be("A");
        summary.Variants.Should().Contain(v => v.Code == "A" && v.Exposures == 1 && v.Conversions == 1 && v.ConversionRate == 1m);
        summary.Variants.Should().Contain(v => v.Code == "B" && v.Exposures == 2 && v.Conversions == 0 && v.ConversionRate == 0m);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : Clawbot.SharedKernel.Time.IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
