using Clawbot.Api.Services;
using Clawbot.Domain.Contacts;
using Clawbot.Domain.Leads;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Tests;

public sealed class LeadCsvServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly DateTimeOffset Now = new(2026, 6, 16, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExportCsvAsync_returns_tenant_scoped_contact_joined_csv()
    {
        using var fx = new TestApiAppDb(TenantId);
        var contact = Contact.Create(TenantId, "Nguyen \"Lan\"", Now);
        fx.Db.Entry(contact).Property(nameof(Contact.Phone)).CurrentValue = "0912345678";
        fx.Db.Entry(contact).Property(nameof(Contact.Email)).CurrentValue = "lan@example.com";
        var lead = Lead.Create(TenantId, contact.Id, "zalo", Now);
        lead.AdjustScore(35, "asked tuition", Now.AddMinutes(5));
        var otherTenantContact = Contact.Create(Guid.NewGuid(), "Other Tenant", Now);
        var otherTenantLead = Lead.Create(otherTenantContact.TenantId, otherTenantContact.Id, "facebook", Now);
        fx.Db.AddRange(contact, lead, otherTenantContact, otherTenantLead);
        await fx.Db.SaveChangesAsync();
        var sut = new LeadCsvService(fx.Db, new FixedClock(Now));

        var result = await sut.ExportCsvAsync(TenantId, CancellationToken.None);

        result.FileName.Should().Be("leads.csv");
        result.Content.Should().StartWith("lead_id,contact_id,display_name,phone,email,source_platform,score,stage,owner_user_id,last_activity_at,created_at");
        result.Content.Should().Contain($"{lead.Id},{contact.Id},\"Nguyen \"\"Lan\"\"\",0912345678,lan@example.com,zalo,35,warm,,");
        result.Content.Should().NotContain(otherTenantLead.Id.ToString());
        result.Content.Should().NotContain("Other Tenant");
    }

    [Fact]
    public async Task ImportCsvAsync_creates_contacts_and_leads_with_initial_score()
    {
        using var fx = new TestApiAppDb(TenantId);
        var sut = new LeadCsvService(fx.Db, new FixedClock(Now));
        const string csv = """
        display_name,phone,email,source_platform,score
        "Tran, Minh",0901112222,minh@example.com,facebook,72
        Le Hoa,,hoa@example.com,zalo,15
        """;

        var result = await sut.ImportCsvAsync(TenantId, csv, CancellationToken.None);

        result.Imported.Should().Be(2);
        result.Errors.Should().BeEmpty();
        var leads = await fx.Db.Leads.IgnoreQueryFilters().OrderByDescending(l => l.Score).ToListAsync();
        leads.Should().HaveCount(2);
        leads[0].Score.Should().Be(72);
        leads[0].Stage.Should().Be("hot");
        leads[0].SourcePlatform.Should().Be("facebook");
        leads[1].Score.Should().Be(15);
        leads[1].Stage.Should().Be("cold");
        var contacts = await fx.Db.Contacts.IgnoreQueryFilters().OrderBy(c => c.DisplayName).ToListAsync();
        contacts.Should().Contain(c => c.DisplayName == "Tran, Minh" && c.Phone == "0901112222" && c.Email == "minh@example.com");
        contacts.Should().Contain(c => c.DisplayName == "Le Hoa" && c.Phone == null && c.Email == "hoa@example.com");
    }

    [Fact]
    public async Task ImportCsvAsync_reports_row_errors_without_importing_invalid_rows()
    {
        using var fx = new TestApiAppDb(TenantId);
        var sut = new LeadCsvService(fx.Db, new FixedClock(Now));
        const string csv = """
        display_name,phone,email,source_platform,score
        ,0901112222,minh@example.com,facebook,10
        Valid Name,,,zalo,5
        """;

        var result = await sut.ImportCsvAsync(TenantId, csv, CancellationToken.None);

        result.Imported.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("row 2");
        (await fx.Db.Leads.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await fx.Db.Contacts.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : Clawbot.SharedKernel.Time.IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
