using Clawbot.Api.Contracts.SaleAssist;
using Clawbot.Api.Services;
using Clawbot.Domain.Contacts;
using Clawbot.Domain.Conversations;
using Clawbot.Domain.Leads;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Clawbot.Api.Tests;

public sealed class SaleAssistUpsellSuggestionServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetSuggestionsAsync_uses_dynamic_upsell_for_hot_lead_conversation()
    {
        using var fx = new TestApiAppDb(TenantId);
        var contact = Contact.Create(TenantId, "Nguyen Lan", Now);
        var lead = Lead.Create(TenantId, contact.Id, "facebook", Now);
        lead.AdjustScore(75, "customer asked for premium package", Now);
        var conversation = Conversation.Open(TenantId, "facebook", "thread-1", Now, contact.Id);
        conversation.AppendMessage("in", "contact", "Em muon chot combo HSK4 kem luyen noi 1:1", "text", Now);
        fx.Db.AddRange(contact, lead, conversation);
        await fx.Db.SaveChangesAsync();

        var upsell = new CapturingUpsellClient(
            new SaleAssistUpsellResponse(
                true,
                "De xuat combo HSK4 + luyen noi 1:1 trong 8 tuan.",
                "hot lead with closing signal",
                75));
        var sut = new SaleAssistUpsellSuggestionService(fx.Db, upsell);

        var result = await sut.GetSuggestionsAsync(TenantId);

        result.Count.Should().Be(1);
        var item = result.HotLeads.Should().ContainSingle().Subject;
        item.Id.Should().Be(lead.Id);
        item.ConversationId.Should().Be(conversation.Id);
        item.Contact.Should().BeEquivalentTo(new SaleAssistHotLeadContactDto("Nguyen Lan", null));
        item.Eligible.Should().BeTrue();
        item.Suggestion.Should().Be("De xuat combo HSK4 + luyen noi 1:1 trong 8 tuan.");
        item.Suggestion.Should().NotBe("Offer advanced course package or premium subscription");
        upsell.Requests.Should().Equal([(TenantId, conversation.Id)]);
    }

    [Fact]
    public async Task GetSuggestionsAsync_keeps_lead_with_fallback_reason_when_upsell_client_fails()
    {
        using var fx = new TestApiAppDb(TenantId);
        var contact = Contact.Create(TenantId, "Nguyen Minh", Now);
        var lead = Lead.Create(TenantId, contact.Id, "zalo", Now);
        lead.AdjustScore(80, "customer asked for enrollment deadline", Now);
        var conversation = Conversation.Open(TenantId, "zalo", "thread-2", Now, contact.Id);
        fx.Db.AddRange(contact, lead, conversation);
        await fx.Db.SaveChangesAsync();

        var sut = new SaleAssistUpsellSuggestionService(fx.Db, new ThrowingUpsellClient());

        var result = await sut.GetSuggestionsAsync(TenantId);

        var item = result.HotLeads.Should().ContainSingle().Subject;
        item.Id.Should().Be(lead.Id);
        item.ConversationId.Should().Be(conversation.Id);
        item.Eligible.Should().BeFalse();
        item.Suggestion.Should().BeEmpty();
        item.Reason.Should().Be("upsell service unavailable");
    }

    private sealed class CapturingUpsellClient(SaleAssistUpsellResponse response) : ISaleAssistUpsellClient
    {
        public List<(Guid TenantId, Guid ConversationId)> Requests { get; } = [];

        public Task<SaleAssistUpsellResponse> GetUpsellAsync(Guid tenantId, Guid conversationId, CancellationToken ct = default)
        {
            Requests.Add((tenantId, conversationId));
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingUpsellClient : ISaleAssistUpsellClient
    {
        public Task<SaleAssistUpsellResponse> GetUpsellAsync(Guid tenantId, Guid conversationId, CancellationToken ct = default) =>
            throw new InvalidOperationException("LLM unavailable");
    }
}

internal sealed class TestApiAppDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public AppDbContext Db { get; }
    public Guid TenantId { get; }

    public TestApiAppDb(Guid tenantId)
    {
        TenantId = tenantId;
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .ReplaceService<IModelCustomizer, ApiSqliteFriendlyModelCustomizer>();

        Db = new AppDbContext(builder.Options, new FixedTenantAccessor(tenantId));
        Db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}

internal sealed class ApiSqliteFriendlyModelCustomizer(ModelCustomizerDependencies dependencies)
    : RelationalModelCustomizer(dependencies)
{
    private static readonly ValueConverter<DateTimeOffset, long> DateTimeOffsetConverter =
        new(v => v.UtcTicks, v => new DateTimeOffset(new DateTime(v, DateTimeKind.Utc)));

    private static readonly ValueConverter<DateTimeOffset?, long?> NullableDateTimeOffsetConverter =
        new(
            v => v.HasValue ? v.Value.UtcTicks : null,
            v => v.HasValue ? new DateTimeOffset(new DateTime(v.Value, DateTimeKind.Utc)) : null);

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset))
                    property.SetValueConverter(DateTimeOffsetConverter);
                else if (property.ClrType == typeof(DateTimeOffset?))
                    property.SetValueConverter(NullableDateTimeOffsetConverter);

                if (property.GetColumnType() is not null)
                    property.SetColumnType(null);
            }
        }
    }
}

internal sealed class FixedTenantAccessor(Guid tenantId) : ITenantAccessor
{
    private readonly TenantContext _context = new(tenantId, "test");

    public TenantContext? Current => _context;

    public TenantContext Require() => _context;
}
