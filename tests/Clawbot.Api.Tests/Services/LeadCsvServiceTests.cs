using Clawbot.Api.Services;
using Clawbot.Domain.Contacts;
using Clawbot.Domain.Conversations;
using Clawbot.Domain.Leads;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Tests.Services;

public sealed class LeadCsvServiceExportTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExportCsvAsync_NoLeads_WritesHeaderOnly()
    {
        var tenantId = Guid.NewGuid();
        await using var db = LeadCsvHarness.CreateDb(tenantId);
        var service = new LeadCsvService(db, new FixedClock(Now));

        var result = await service.ExportCsvAsync(tenantId);

        result.FileName.Should().Be("leads.csv");
        result.Content.Trim().Should().Be(
            "lead_id,contact_id,display_name,phone,email,source_platform,score,stage,owner_user_id,last_activity_at,created_at");
    }

    [Fact]
    public async Task ExportCsvAsync_JoinsContactColumns()
    {
        var tenantId = Guid.NewGuid();
        await using var db = LeadCsvHarness.CreateDb(tenantId);
        var contact = Contact.Create(tenantId, "Nguyễn Văn A", Now);
        var lead = Lead.Create(tenantId, contact.Id, "facebook", Now);
        db.Contacts.Add(contact);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        var service = new LeadCsvService(db, new FixedClock(Now));

        var content = (await service.ExportCsvAsync(tenantId)).Content;

        content.Should().Contain("Nguyễn Văn A");
        content.Should().Contain("facebook");
        content.Should().Contain(lead.Id.ToString("D"));
        content.Should().Contain(contact.Id.ToString("D"));
    }

    [Fact]
    public async Task ExportCsvAsync_LeadWithoutContact_LeavesContactColumnsEmpty()
    {
        var tenantId = Guid.NewGuid();
        await using var db = LeadCsvHarness.CreateDb(tenantId);
        // Contact bị xoá khỏi bảng contacts: lead vẫn xuất được, cột contact để trống.
        db.Leads.Add(Lead.Create(tenantId, Guid.NewGuid(), "zalo", Now));
        await db.SaveChangesAsync();
        var service = new LeadCsvService(db, new FixedClock(Now));

        var lines = (await service.ExportCsvAsync(tenantId)).Content
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines.Should().HaveCount(2);
        lines[1].Should().Contain(",,,zalo,");
    }

    [Fact]
    public async Task ExportCsvAsync_EscapesCommasInDisplayName()
    {
        var tenantId = Guid.NewGuid();
        await using var db = LeadCsvHarness.CreateDb(tenantId);
        var contact = Contact.Create(tenantId, "Trần, Bình", Now);
        db.Contacts.Add(contact);
        db.Leads.Add(Lead.Create(tenantId, contact.Id, "facebook", Now));
        await db.SaveChangesAsync();
        var service = new LeadCsvService(db, new FixedClock(Now));

        (await service.ExportCsvAsync(tenantId)).Content.Should().Contain("\"Trần, Bình\"");
    }

    [Fact]
    public async Task ExportCsvAsync_ExcludesSoftDeletedAndOtherTenants()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        await using var db = LeadCsvHarness.CreateDb(tenantId);
        var kept = Lead.Create(tenantId, Guid.NewGuid(), "kept", Now);
        var deleted = Lead.Create(tenantId, Guid.NewGuid(), "deleted", Now);
        db.Leads.AddRange(kept, deleted, Lead.Create(otherTenantId, Guid.NewGuid(), "other", Now));
        db.Entry(deleted).Property(nameof(Lead.DeletedAt)).CurrentValue = Now;
        await db.SaveChangesAsync();
        var service = new LeadCsvService(db, new FixedClock(Now));

        var content = (await service.ExportCsvAsync(tenantId)).Content;

        content.Should().Contain("kept");
        content.Should().NotContain("deleted");
        content.Should().NotContain("other");
    }

    [Fact]
    public async Task ExportCsvAsync_SortsByScoreDescending()
    {
        var tenantId = Guid.NewGuid();
        await using var db = LeadCsvHarness.CreateDb(tenantId);
        var low = Lead.Create(tenantId, Guid.NewGuid(), "low", Now);
        var high = Lead.Create(tenantId, Guid.NewGuid(), "high", Now);
        high.AdjustScore(80, "test", Now);
        db.Leads.AddRange(low, high);
        await db.SaveChangesAsync();
        var service = new LeadCsvService(db, new FixedClock(Now));

        var content = (await service.ExportCsvAsync(tenantId)).Content;

        content.IndexOf("high", StringComparison.Ordinal)
            .Should().BeLessThan(content.IndexOf("low", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExportCsvAsync_RestrictedScope_OnlyReturnsOwnLeads()
    {
        // Sale chỉ được tải về lead của mình — file CSV phải khớp danh sách trên trang.
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        await using var db = LeadCsvHarness.CreateDb(tenantId);
        var mine = Lead.Create(tenantId, Guid.NewGuid(), "mine", Now);
        mine.Assign(ownerId);
        db.Leads.AddRange(mine, Lead.Create(tenantId, Guid.NewGuid(), "theirs", Now));
        await db.SaveChangesAsync();
        var service = new LeadCsvService(db, new FixedClock(Now));

        var content = (await service.ExportCsvAsync(
            tenantId, new LeadScope(false, ownerId, []))).Content;

        content.Should().Contain("mine");
        content.Should().NotContain("theirs");
    }

    [Fact]
    public async Task ExportCsvAsync_InboxScope_IncludesLeadsFromOwnInbox()
    {
        var tenantId = Guid.NewGuid();
        var inboxId = Guid.NewGuid();
        await using var db = LeadCsvHarness.CreateDb(tenantId);
        var contact = Contact.Create(tenantId, "Khách inbox", Now);
        var conversation = Conversation.Open(
            tenantId, "facebook", "t-1", Now, contactId: contact.Id, inboxId: inboxId);
        db.Contacts.Add(contact);
        db.Conversations.Add(conversation);
        db.Leads.Add(Lead.Create(tenantId, contact.Id, "from-inbox", Now));
        db.Leads.Add(Lead.Create(tenantId, Guid.NewGuid(), "unrelated", Now));
        await db.SaveChangesAsync();
        var service = new LeadCsvService(db, new FixedClock(Now));

        var content = (await service.ExportCsvAsync(
            tenantId, new LeadScope(false, Guid.Empty, [inboxId]))).Content;

        content.Should().Contain("from-inbox");
        content.Should().NotContain("unrelated");
    }
}

public sealed class LeadCsvServiceImportTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 3, 0, 0, TimeSpan.Zero);

    private static LeadCsvService Service(AppDbContext db) => new(db, new FixedClock(Now));

    [Fact]
    public async Task ImportCsvAsync_EmptyInput_ReportsMissingHeader()
    {
        var tenantId = Guid.NewGuid();
        await using var db = LeadCsvHarness.CreateDb(tenantId);

        var result = await Service(db).ImportCsvAsync(tenantId, "");

        result.Imported.Should().Be(0);
        result.Errors.Should().ContainSingle().Which.Should().Contain("header");
    }

    [Fact]
    public async Task ImportCsvAsync_MissingRequiredColumns_ReportsThem()
    {
        var tenantId = Guid.NewGuid();
        await using var db = LeadCsvHarness.CreateDb(tenantId);

        var result = await Service(db).ImportCsvAsync(tenantId, "display_name,score\n");

        result.Imported.Should().Be(0);
        result.Errors.Should().ContainSingle().Which.Should().Contain("source_platform");
    }

    [Fact]
    public async Task ImportCsvAsync_ValidRows_CreatesContactAndLead()
    {
        var tenantId = Guid.NewGuid();
        await using var db = LeadCsvHarness.CreateDb(tenantId);
        var csv = "display_name,source_platform\nNguyễn Văn A,facebook\nTrần Thị B,zalo\n";

        var result = await Service(db).ImportCsvAsync(tenantId, csv);

        result.Imported.Should().Be(2);
        result.LeadIds.Should().HaveCount(2);
        result.Errors.Should().BeEmpty();
        db.Leads.Count().Should().Be(2);
        db.Contacts.Count().Should().Be(2);
    }

    [Fact]
    public async Task ImportCsvAsync_StripsUtf8BomFromFirstHeader()
    {
        var tenantId = Guid.NewGuid();
        await using var db = LeadCsvHarness.CreateDb(tenantId);
        var csv = "﻿display_name,source_platform\nA,facebook\n";

        var result = await Service(db).ImportCsvAsync(tenantId, csv);

        result.Imported.Should().Be(1);
    }

    [Fact]
    public async Task ImportCsvAsync_HeaderMatchIsCaseInsensitive()
    {
        var tenantId = Guid.NewGuid();
        await using var db = LeadCsvHarness.CreateDb(tenantId);

        var result = await Service(db).ImportCsvAsync(
            tenantId, "Display_Name,SOURCE_PLATFORM\nA,facebook\n");

        result.Imported.Should().Be(1);
    }

    [Fact]
    public async Task ImportCsvAsync_MapsOptionalPhoneAndEmail()
    {
        var tenantId = Guid.NewGuid();
        await using var db = LeadCsvHarness.CreateDb(tenantId);
        var csv = "display_name,source_platform,phone,email\nA,facebook,0900000000,a@b.vn\n";

        await Service(db).ImportCsvAsync(tenantId, csv);

        var contact = db.Contacts.Single();
        contact.Phone.Should().Be("0900000000");
        contact.Email.Should().Be("a@b.vn");
    }

    [Fact]
    public async Task ImportCsvAsync_AppliesScoreWhenPositive()
    {
        var tenantId = Guid.NewGuid();
        await using var db = LeadCsvHarness.CreateDb(tenantId);

        await Service(db).ImportCsvAsync(
            tenantId, "display_name,source_platform,score\nA,facebook,55\n");

        db.Leads.Single().Score.Should().Be(55);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("101")]
    public async Task ImportCsvAsync_InvalidScore_SkipsRowWithError(string score)
    {
        var tenantId = Guid.NewGuid();
        await using var db = LeadCsvHarness.CreateDb(tenantId);

        var result = await Service(db).ImportCsvAsync(
            tenantId, $"display_name,source_platform,score\nA,facebook,{score}\n");

        result.Imported.Should().Be(0);
        result.Errors.Should().ContainSingle().Which.Should().Contain("score");
    }

    [Fact]
    public async Task ImportCsvAsync_BlankRequiredValues_SkipRowWithError()
    {
        var tenantId = Guid.NewGuid();
        await using var db = LeadCsvHarness.CreateDb(tenantId);
        var csv = "display_name,source_platform\n,facebook\nB,\nC,zalo\n";

        var result = await Service(db).ImportCsvAsync(tenantId, csv);

        result.Imported.Should().Be(1);
        result.Errors.Should().HaveCount(2);
        result.Errors.Should().Contain(e => e.Contains("display_name", StringComparison.Ordinal));
        result.Errors.Should().Contain(e => e.Contains("source_platform", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportCsvAsync_HandlesQuotedFieldsWithCommasAndEscapedQuotes()
    {
        var tenantId = Guid.NewGuid();
        await using var db = LeadCsvHarness.CreateDb(tenantId);
        var csv = "display_name,source_platform\n\"Trần, Bình\",facebook\n\"Lê \"\"Bo\"\"\",zalo\n";

        var result = await Service(db).ImportCsvAsync(tenantId, csv);

        result.Imported.Should().Be(2);
        db.Contacts.Select(c => c.DisplayName).Should().Contain("Trần, Bình");
        db.Contacts.Select(c => c.DisplayName).Should().Contain("Lê \"Bo\"");
    }

    [Fact]
    public async Task ImportCsvAsync_HandlesCrLfLineEndings()
    {
        var tenantId = Guid.NewGuid();
        await using var db = LeadCsvHarness.CreateDb(tenantId);

        var result = await Service(db).ImportCsvAsync(
            tenantId, "display_name,source_platform\r\nA,facebook\r\nB,zalo\r\n");

        result.Imported.Should().Be(2);
    }

    [Fact]
    public async Task ImportCsvAsync_LastRowWithoutTrailingNewline_IsImported()
    {
        var tenantId = Guid.NewGuid();
        await using var db = LeadCsvHarness.CreateDb(tenantId);

        var result = await Service(db).ImportCsvAsync(
            tenantId, "display_name,source_platform\nA,facebook");

        result.Imported.Should().Be(1);
    }

    [Fact]
    public async Task ImportCsvAsync_BlankLinesAreSkipped()
    {
        var tenantId = Guid.NewGuid();
        await using var db = LeadCsvHarness.CreateDb(tenantId);

        var result = await Service(db).ImportCsvAsync(
            tenantId, "display_name,source_platform\nA,facebook\n\n\nB,zalo\n");

        result.Imported.Should().Be(2);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportCsvAsync_ShortRow_TreatsMissingColumnsAsEmpty()
    {
        var tenantId = Guid.NewGuid();
        await using var db = LeadCsvHarness.CreateDb(tenantId);

        var result = await Service(db).ImportCsvAsync(
            tenantId, "display_name,source_platform,phone\nA,facebook\n");

        result.Imported.Should().Be(1);
        db.Contacts.Single().Phone.Should().BeNull();
    }
}

internal static class LeadCsvHarness
{
    public static AppDbContext CreateDb(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options, new FixedTenant(tenantId));
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantAccessor
    {
        public TenantContext? Current { get; } = new(tenantId, "test-tenant");

        public TenantContext Require() => Current!;
    }
}

internal sealed class FixedClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; } = utcNow;
}
