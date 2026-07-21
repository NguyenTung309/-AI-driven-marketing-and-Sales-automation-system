using Clawbot.Domain.Leads;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Jobs;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Jobs;

public sealed class LeadFollowUpJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_MarksLeadLost_WhenThresholdAndOldReengageAreMet()
    {
        var tenant = Tenant.Create("lost-threshold", "Lost threshold", "free", Now.AddYears(-1));
        tenant.SetLeadLostAfterDays(60);
        using var fx = new TestAppDb(tenant.Id);
        fx.Db.Tenants.Add(tenant);
        var lead = Lead.Create(tenant.Id, Guid.NewGuid(), "facebook", Now.AddDays(-61));
        fx.Db.Leads.Add(lead);
        await fx.Db.SaveChangesAsync();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now.AddDays(-8));
        var sut = new LeadFollowUpJob(fx.Db, clock, NullLogger<LeadFollowUpJob>.Instance);

        await sut.RunAsync();
        clock.UtcNow.Returns(Now);
        await sut.RunAsync();

        var saved = await fx.Db.Leads.IgnoreQueryFilters().SingleAsync(l => l.Id == lead.Id);
        saved.Stage.Should().Be("lost");
        saved.Activities.Should().ContainSingle(a =>
            a.ActivityType == "stage_change"
            && a.Notes!.Contains("60+ ngày", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_DoesNotMarkLost_WhenTenantDisablesSweep()
    {
        var tenant = Tenant.Create("lost-disabled", "Lost disabled", "free", Now.AddYears(-1));
        tenant.SetLeadLostAfterDays(0);
        using var fx = new TestAppDb(tenant.Id);
        fx.Db.Tenants.Add(tenant);
        var lead = Lead.Create(tenant.Id, Guid.NewGuid(), "facebook", Now.AddDays(-200));
        fx.Db.Leads.Add(lead);
        await fx.Db.SaveChangesAsync();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        var sut = new LeadFollowUpJob(fx.Db, clock, NullLogger<LeadFollowUpJob>.Instance);

        await sut.RunAsync();

        lead.Stage.Should().Be("cold");
    }

    [Fact]
    public async Task RunAsync_DoesNotChangeExistingCustomerOrLostLead()
    {
        var tenant = Tenant.Create("lost-terminal", "Lost terminal", "free", Now.AddYears(-1));
        tenant.SetLeadLostAfterDays(30);
        using var fx = new TestAppDb(tenant.Id);
        fx.Db.Tenants.Add(tenant);
        var customer = Lead.Create(tenant.Id, Guid.NewGuid(), "facebook", Now.AddDays(-200));
        customer.MarkCustomer("paid", Now.AddDays(-190));
        var lost = Lead.Create(tenant.Id, Guid.NewGuid(), "facebook", Now.AddDays(-200));
        lost.MarkLost("manual", Now.AddDays(-190));
        var customerActivities = customer.Activities.Count;
        var lostActivities = lost.Activities.Count;
        fx.Db.Leads.AddRange(customer, lost);
        await fx.Db.SaveChangesAsync();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        var sut = new LeadFollowUpJob(fx.Db, clock, NullLogger<LeadFollowUpJob>.Instance);

        await sut.RunAsync();

        customer.Stage.Should().Be("customer");
        customer.Activities.Should().HaveCount(customerActivities);
        lost.Stage.Should().Be("lost");
        lost.Activities.Should().HaveCount(lostActivities);
    }

    [Fact]
    public async Task RunAsync_WaitsSevenDaysAfterLatestReengageAttempt()
    {
        var tenant = Tenant.Create("lost-rescue", "Lost rescue", "free", Now.AddYears(-1));
        tenant.SetLeadLostAfterDays(60);
        using var fx = new TestAppDb(tenant.Id);
        fx.Db.Tenants.Add(tenant);
        var lead = Lead.Create(tenant.Id, Guid.NewGuid(), "facebook", Now.AddDays(-61));
        fx.Db.Leads.Add(lead);
        await fx.Db.SaveChangesAsync();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now.AddDays(-6));
        var sut = new LeadFollowUpJob(fx.Db, clock, NullLogger<LeadFollowUpJob>.Instance);

        await sut.RunAsync();
        clock.UtcNow.Returns(Now);
        await sut.RunAsync();

        lead.Stage.Should().Be("cold");
    }
}
