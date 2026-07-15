using Clawbot.AgentService.Services;
using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Skills.Lead;
using Clawbot.Agents.Core.Skills.Ops;
using Clawbot.Domain.Analytics;
using Clawbot.Domain.Leads;
using Clawbot.Infrastructure.Leads;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Clawbot.AgentService.Tests.Services;

public sealed class OrchestrationAgentAdaptersTests
{
    private static AgentTask Task(string id, IReadOnlyDictionary<string, string> input) =>
        new(id, "x", "desc", input);

    [Fact]
    public async Task ReportAdapter_snapshot_returns_kpi_rows_json()
    {
        var tenantId = Guid.NewGuid();
        using var fx = new AgentServiceTestAppDb(tenantId);
        var date = new DateOnly(2026, 6, 7);
        var row = KpiDaily.Create(tenantId, date, "facebook", DateTimeOffset.UtcNow);
        row.Record(12, 5, 4, 2, avgRespSec: 100m, adSpend: 50m);
        fx.Db.KpiDailies.Add(row);
        await fx.Db.SaveChangesAsync();

        var runner = new ReportAgentRunner(fx.Db, Substitute.For<IAnomalyDetector>(), Substitute.For<IForecaster>());
        var adapter = new ReportOrchestrationAdapter(runner);

        var result = await adapter.ExecuteAsync(Task("t1", new Dictionary<string, string>
        {
            ["operation"] = "snapshot",
            ["tenant_id"] = tenantId.ToString("D"),
            ["date"] = "2026-06-07",
        }), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("facebook");
    }

    [Fact]
    public async Task ReportAdapter_maps_invalid_date_to_error_result()
    {
        var tenantId = Guid.NewGuid();
        using var fx = new AgentServiceTestAppDb(tenantId);
        var runner = new ReportAgentRunner(fx.Db, Substitute.For<IAnomalyDetector>(), Substitute.For<IForecaster>());
        var adapter = new ReportOrchestrationAdapter(runner);

        var result = await adapter.ExecuteAsync(Task("t1", new Dictionary<string, string>
        {
            ["operation"] = "snapshot",
            ["tenant_id"] = tenantId.ToString("D"),
            ["date"] = "not-a-date",
        }), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("YYYY-MM-DD");
    }

    [Fact]
    public async Task LeadAdapter_score_returns_score_json()
    {
        var tenantId = Guid.NewGuid();
        using var fx = new AgentServiceTestAppDb(tenantId);
        var contactId = Guid.NewGuid();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var lead = Lead.Create(tenantId, contactId, "facebook", clock.UtcNow);
        fx.Db.Leads.Add(lead);
        await fx.Db.SaveChangesAsync();

        var runner = new LeadAgentRunner(
            fx.Db, clock,
            Substitute.For<ILeadDeduplicator>(),
            Substitute.For<IContactEnricher>(),
            Substitute.For<ITimezoneDetector>(),
            Substitute.For<ISpamDetector>());
        var batchRescorer = new LeadBatchRescorer(
            fx.Db,
            new KeywordLeadSignalClassifier(),
            clock,
            NullLogger<LeadBatchRescorer>.Instance);
        var adapter = new LeadOrchestrationAdapter(runner, batchRescorer);

        var result = await adapter.ExecuteAsync(Task("t1", new Dictionary<string, string>
        {
            ["operation"] = "score",
            ["tenant_id"] = tenantId.ToString("D"),
            ["lead_id"] = lead.Id.ToString("D"),
            ["event_code"] = "default",
            ["platform"] = "facebook",
        }), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("score");
    }
}
