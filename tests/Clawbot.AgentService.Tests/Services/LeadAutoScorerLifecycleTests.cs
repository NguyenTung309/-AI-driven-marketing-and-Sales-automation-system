using Clawbot.AgentService.Services;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Skills.Lead;
using Clawbot.Domain.Leads;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Clawbot.AgentService.Tests.Services;

public sealed class LeadAutoScorerLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ScoreFromMessageAsync_ReactivatesLostLead_WhenInboundMessageHasNoScoringSignal()
    {
        var tenantId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        using var fx = new AgentServiceTestAppDb(tenantId);
        var lead = Lead.Create(tenantId, contactId, "facebook", Now.AddDays(-70));
        lead.AdjustScore(40, "warm", Now.AddDays(-69));
        lead.MarkLost("silent", Now.AddDays(-1));
        fx.Db.Leads.Add(lead);
        await fx.Db.SaveChangesAsync();
        var classifier = Substitute.For<ILeadSignalClassifier>();
        classifier.ClassifyAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new LeadSignalResult(Array.Empty<string>()));
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        var sut = new LeadAutoScorer(
            fx.Db,
            classifier,
            new LlmCallScope(),
            clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LeadAutoScorer>.Instance);

        var outcome = await sut.ScoreFromMessageAsync(new LeadAutoScoreInput(
            tenantId,
            contactId,
            "facebook",
            "alo em",
            Now,
            LastAgentReplyAt: null), CancellationToken.None);

        outcome.LeadId.Should().Be(lead.Id);
        outcome.Score.Should().Be(40);
        outcome.Stage.Should().Be("warm");
        lead.Activities.Should().ContainSingle(a =>
            a.ActivityType == "stage_change"
            && a.Notes == "customer_inbound");
    }
}
