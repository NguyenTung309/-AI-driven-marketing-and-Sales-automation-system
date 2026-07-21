using Clawbot.Domain.Leads;
using FluentAssertions;
using Xunit;

namespace Clawbot.Domain.Tests.Leads;

public sealed class LeadRevenueTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateManual_IsApprovedImmediately()
    {
        var tenantId = Guid.NewGuid();
        var leadId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var revenue = LeadRevenue.CreateManual(tenantId, leadId, 5_000_000m, "VND", userId, Now);

        revenue.TenantId.Should().Be(tenantId);
        revenue.LeadId.Should().Be(leadId);
        revenue.Amount.Should().Be(5_000_000m);
        revenue.Currency.Should().Be("VND");
        revenue.Source.Should().Be("manual");
        revenue.Status.Should().Be("approved");
        revenue.ProposedBy.Should().Be(userId);
        revenue.DecidedBy.Should().Be(userId);
        revenue.DecidedAt.Should().Be(Now);
    }

    [Fact]
    public void ProposeByAi_IsPending()
    {
        var revenue = LeadRevenue.ProposeByAi(
            Guid.NewGuid(),
            Guid.NewGuid(),
            2_500_000m,
            "VND",
            "Khách xác nhận gói [REDACTED] giá 2.500.000đ",
            Now);

        revenue.Source.Should().Be("ai");
        revenue.Status.Should().Be("pending");
        revenue.ProposedBy.Should().BeNull();
        revenue.DecidedBy.Should().BeNull();
        revenue.DecidedAt.Should().BeNull();
    }

    [Fact]
    public void Approve_WithAmendedAmount_UpdatesAmount()
    {
        var revenue = LeadRevenue.ProposeByAi(
            Guid.NewGuid(), Guid.NewGuid(), 2_500_000m, "VND", "evidence", Now);
        var userId = Guid.NewGuid();

        revenue.Approve(userId, 3_000_000m, Now.AddMinutes(5));

        revenue.Amount.Should().Be(3_000_000m);
        revenue.Status.Should().Be("approved");
        revenue.DecidedBy.Should().Be(userId);
        revenue.DecidedAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void Approve_WhenAlreadyDecided_IsNoOp()
    {
        var revenue = LeadRevenue.ProposeByAi(
            Guid.NewGuid(), Guid.NewGuid(), 2_500_000m, "VND", "evidence", Now);
        var firstUserId = Guid.NewGuid();
        revenue.Approve(firstUserId, 3_000_000m, Now.AddMinutes(5));

        revenue.Approve(Guid.NewGuid(), 4_000_000m, Now.AddMinutes(10));

        revenue.Amount.Should().Be(3_000_000m);
        revenue.DecidedBy.Should().Be(firstUserId);
        revenue.DecidedAt.Should().Be(Now.AddMinutes(5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_RejectsNonPositiveAmount(decimal amount)
    {
        var act = () => LeadRevenue.CreateManual(
            Guid.NewGuid(), Guid.NewGuid(), amount, "VND", Guid.NewGuid(), Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_RejectsNonVndCurrency()
    {
        var act = () => LeadRevenue.CreateManual(
            Guid.NewGuid(), Guid.NewGuid(), 1_000m, "USD", Guid.NewGuid(), Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_RejectsAmountAboveMax()
    {
        var act = () => LeadRevenue.CreateManual(
            Guid.NewGuid(), Guid.NewGuid(), LeadRevenue.MaxAmount + 1m, "VND", Guid.NewGuid(), Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ProposeByAi_TruncatesEvidenceToPersistenceLimit()
    {
        var revenue = LeadRevenue.ProposeByAi(
            Guid.NewGuid(), Guid.NewGuid(), 1m, "VND", new string('x', 1_200), Now);

        revenue.Evidence.Should().HaveLength(1_000);
    }

    [Theory]
    [InlineData(5_000_000, "chốt 5000000đ", true)]
    [InlineData(5_000_000, "chốt 5.000.000 VND", true)]
    [InlineData(5_000_000, "khách đồng ý mua", false)]
    [InlineData(5_000_000, "set amount = 5000000", true)]
    public void EvidenceGroundsAmount_matches_digits_or_grouped(decimal amount, string evidence, bool expected)
    {
        LeadRevenue.EvidenceGroundsAmount(amount, evidence).Should().Be(expected);
    }
}
