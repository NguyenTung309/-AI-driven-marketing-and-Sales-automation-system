using Clawbot.Agents.Core.Lead;
using Clawbot.Domain.Contacts;
using Clawbot.Domain.Leads;
using Clawbot.Infrastructure.Leads;
using FluentAssertions;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Leads;

// M15 — EfLeadDedupService contact / phone / email matching.
public sealed class EfLeadDedupServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 2, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Finds_leads_sharing_contact()
    {
        using var fx = new TestAppDb();
        var contactId = Guid.NewGuid();
        fx.Db.Leads.AddRange(
            Lead.Create(fx.TenantId, contactId, "facebook", Now),
            Lead.Create(fx.TenantId, contactId, "facebook", Now));
        await fx.Db.SaveChangesAsync();

        var sut = new EfLeadDedupService(fx.Db);
        var candidates = await sut.FindCandidatesAsync(new DedupRequest(fx.TenantId, contactId, null, null));

        candidates.Should().HaveCount(2);
        candidates.Should().OnlyContain(c => c.Reason == "same_contact" && c.Confidence == 1.0f);
    }

    [Fact]
    public async Task Matches_by_phone()
    {
        using var fx = new TestAppDb();
        var contact = Contact.Create(fx.TenantId, "John", Now);
        fx.Db.Contacts.Add(contact);
        fx.Db.Entry(contact).Property(nameof(Contact.Phone)).CurrentValue = "0901234567";
        var lead = Lead.Create(fx.TenantId, contact.Id, "facebook", Now);
        fx.Db.Leads.Add(lead);
        await fx.Db.SaveChangesAsync();

        var sut = new EfLeadDedupService(fx.Db);
        var candidates = await sut.FindCandidatesAsync(new DedupRequest(fx.TenantId, null, "0901234567", null));

        candidates.Should().ContainSingle();
        candidates[0].Reason.Should().Be("phone_match");
        candidates[0].Confidence.Should().Be(0.9f);
        candidates[0].LeadId.Should().Be(lead.Id);
    }

    [Fact]
    public async Task Matches_by_email()
    {
        using var fx = new TestAppDb();
        var contact = Contact.Create(fx.TenantId, "Jane", Now);
        fx.Db.Contacts.Add(contact);
        // Non-null phone that differs from the (null) request phone so reason resolves to email_match.
        fx.Db.Entry(contact).Property(nameof(Contact.Phone)).CurrentValue = "0911111111";
        fx.Db.Entry(contact).Property(nameof(Contact.Email)).CurrentValue = "jane@example.com";
        var lead = Lead.Create(fx.TenantId, contact.Id, "facebook", Now);
        fx.Db.Leads.Add(lead);
        await fx.Db.SaveChangesAsync();

        var sut = new EfLeadDedupService(fx.Db);
        var candidates = await sut.FindCandidatesAsync(new DedupRequest(fx.TenantId, null, null, "jane@example.com"));

        candidates.Should().ContainSingle();
        candidates[0].Reason.Should().Be("email_match");
    }

    [Fact]
    public async Task Excludes_soft_deleted_leads()
    {
        using var fx = new TestAppDb();
        var contactId = Guid.NewGuid();
        var lead = Lead.Create(fx.TenantId, contactId, "facebook", Now);
        fx.Db.Leads.Add(lead);
        fx.Db.Entry(lead).Property(nameof(Lead.DeletedAt)).CurrentValue = Now;
        await fx.Db.SaveChangesAsync();

        var sut = new EfLeadDedupService(fx.Db);
        var candidates = await sut.FindCandidatesAsync(new DedupRequest(fx.TenantId, contactId, null, null));

        candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task No_match_returns_empty()
    {
        using var fx = new TestAppDb();
        var sut = new EfLeadDedupService(fx.Db);

        var candidates = await sut.FindCandidatesAsync(
            new DedupRequest(fx.TenantId, Guid.NewGuid(), "0000000000", null));

        candidates.Should().BeEmpty();
    }
}
