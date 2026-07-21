using Clawbot.Agents.Core.Skills.Lead;
using Clawbot.Domain.Leads;
using Clawbot.Infrastructure.Leads;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Leads;

public sealed class LeadBatchRescorerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RescoreTenantAsync_SkipsCustomerAndLostLeads()
    {
        using var fx = new TestAppDb();
        var customer = Lead.Create(fx.TenantId, Guid.NewGuid(), "facebook", Now.AddDays(-3));
        customer.AdjustScore(80, "hot", Now.AddDays(-2));
        customer.MarkCustomer("paid", Now.AddDays(-1));
        var lost = Lead.Create(fx.TenantId, Guid.NewGuid(), "pancake", Now.AddDays(-3));
        lost.AdjustScore(40, "warm", Now.AddDays(-2));
        lost.MarkLost("silent", Now.AddDays(-1));
        var pipeline = Lead.Create(fx.TenantId, Guid.NewGuid(), "facebook", Now.AddDays(-3));
        pipeline.AdjustScore(35, "warm", Now.AddDays(-2));
        fx.Db.Leads.AddRange(customer, lost, pipeline);
        await fx.Db.SaveChangesAsync();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        var sut = new LeadBatchRescorer(
            fx.Db,
            new KeywordLeadSignalClassifier(),
            clock,
            NullLogger<LeadBatchRescorer>.Instance);

        var result = await sut.RescoreTenantAsync(fx.TenantId);

        result.LeadsScanned.Should().Be(1);
        result.LeadsUpdated.Should().Be(1);
        var savedCustomer = await fx.Db.Leads.IgnoreQueryFilters().SingleAsync(l => l.Id == customer.Id);
        savedCustomer.Score.Should().Be(80);
        savedCustomer.Stage.Should().Be("customer");
        var savedLost = await fx.Db.Leads.IgnoreQueryFilters().SingleAsync(l => l.Id == lost.Id);
        savedLost.Score.Should().Be(40);
        savedLost.Stage.Should().Be("lost");
    }
}
