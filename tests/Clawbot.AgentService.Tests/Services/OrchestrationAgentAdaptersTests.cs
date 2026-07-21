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
        var adapter = new LeadOrchestrationAdapter(runner, batchRescorer, fx.Db);

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

    [Fact]
    public async Task LeadAdapter_list_returns_lead_ids_for_cold_stage()
    {
        var tenantId = Guid.NewGuid();
        using var fx = new AgentServiceTestAppDb(tenantId);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero));

        // cold (score 0), warm (40), customer — list stage=cold only returns cold.
        var cold = Lead.Create(tenantId, Guid.NewGuid(), "facebook", clock.UtcNow);
        var warm = Lead.Create(tenantId, Guid.NewGuid(), "zalo", clock.UtcNow);
        warm.AdjustScore(40, "warm-up", clock.UtcNow);
        var customer = Lead.Create(tenantId, Guid.NewGuid(), "facebook", clock.UtcNow);
        customer.MarkCustomer("paid", clock.UtcNow);
        fx.Db.Leads.AddRange(cold, warm, customer);
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
        var adapter = new LeadOrchestrationAdapter(runner, batchRescorer, fx.Db);

        var result = await adapter.ExecuteAsync(Task("t1", new Dictionary<string, string>
        {
            ["operation"] = "list",
            ["tenant_id"] = tenantId.ToString("D"),
            ["stage"] = "cold",
            ["topN"] = "10",
        }), CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        result.Output.Should().Contain("lead_ids");
        result.Output.Should().Contain(cold.Id.ToString("D"));
        result.Output.Should().NotContain(warm.Id.ToString("D"));
        result.Output.Should().NotContain(customer.Id.ToString("D"));
        result.Output.Should().Contain("\"total\":1");
    }

    [Fact]
    public async Task LeadAdapter_infers_list_from_cold_description_without_operation()
    {
        var tenantId = Guid.NewGuid();
        using var fx = new AgentServiceTestAppDb(tenantId);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var cold = Lead.Create(tenantId, Guid.NewGuid(), "facebook", clock.UtcNow);
        fx.Db.Leads.Add(cold);
        await fx.Db.SaveChangesAsync();

        var runner = new LeadAgentRunner(
            fx.Db, clock,
            Substitute.For<ILeadDeduplicator>(),
            Substitute.For<IContactEnricher>(),
            Substitute.For<ITimezoneDetector>(),
            Substitute.For<ISpamDetector>());
        var batchRescorer = new LeadBatchRescorer(
            fx.Db, new KeywordLeadSignalClassifier(), clock, NullLogger<LeadBatchRescorer>.Instance);
        var adapter = new LeadOrchestrationAdapter(runner, batchRescorer, fx.Db);

        var task = new AgentTask(
            "t1",
            "lead-agent",
            "Xác định lead lạnh từ danh sách, ưu tiên ít nhất 5 để tương tác",
            new Dictionary<string, string> { ["tenant_id"] = tenantId.ToString("D") });

        var result = await adapter.ExecuteAsync(task, CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        result.Output.Should().Contain(cold.Id.ToString("D"));
        result.Output.Should().Contain("\"operation\":\"list\"");
    }

    [Fact]
    public async Task LeadAdapter_score_without_lead_id_fails_clearly()
    {
        var tenantId = Guid.NewGuid();
        using var fx = new AgentServiceTestAppDb(tenantId);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var runner = new LeadAgentRunner(
            fx.Db, clock,
            Substitute.For<ILeadDeduplicator>(),
            Substitute.For<IContactEnricher>(),
            Substitute.For<ITimezoneDetector>(),
            Substitute.For<ISpamDetector>());
        var batchRescorer = new LeadBatchRescorer(
            fx.Db, new KeywordLeadSignalClassifier(), clock, NullLogger<LeadBatchRescorer>.Instance);
        var adapter = new LeadOrchestrationAdapter(runner, batchRescorer, fx.Db);

        var result = await adapter.ExecuteAsync(Task("t1", new Dictionary<string, string>
        {
            ["operation"] = "score",
            ["tenant_id"] = tenantId.ToString("D"),
        }), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("lead_id required");
    }
}
