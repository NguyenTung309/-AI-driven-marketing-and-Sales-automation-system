using Clawbot.Api.Jobs;
using Clawbot.Api.Services;
using Clawbot.Domain.Contacts;
using Clawbot.Domain.Conversations;
using Clawbot.Domain.Leads;
using Clawbot.Domain.SaleAssist;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Jobs;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Clawbot.Api.Tests.Services;

public sealed class SaleAssistUpsellSuggestionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetSuggestionsAsync_FreshCache_ReturnsCachedSuggestionWithoutLaunchingJob()
    {
        await using var fixture = await UpsellFixture.CreateAsync();
        var (lead, conversation) = await fixture.SeedHotLeadWithConversationAsync();
        fixture.Db.UpsellSuggestionCaches.Add(UpsellSuggestionCache.Create(
            fixture.TenantId, conversation.Id,
            eligible: true, suggestion: "Combo premium", reason: "hot lead with closing signal",
            leadScore: 80, generatedAt: Now, sourceLastMessageAt: conversation.CreatedAt));
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.GetSuggestionsAsync(fixture.TenantId);

        result.HotLeads.Should().ContainSingle();
        var item = result.HotLeads[0];
        item.Id.Should().Be(lead.Id);
        item.Pending.Should().BeFalse();
        item.Eligible.Should().BeTrue();
        item.Suggestion.Should().Be("Combo premium");
        await fixture.Jobs.DidNotReceiveWithAnyArgs().LaunchAsync(default!, default!);
    }

    [Fact]
    public async Task GetSuggestionsAsync_StaleCache_ReturnsPendingAndLaunchesJobOnce()
    {
        await using var fixture = await UpsellFixture.CreateAsync();
        var (_, conversation) = await fixture.SeedHotLeadWithConversationAsync();
        // Cache sinh TRƯỚC khi hội thoại có hoạt động mới nhất -> coi như cũ.
        fixture.Db.UpsellSuggestionCaches.Add(UpsellSuggestionCache.Create(
            fixture.TenantId, conversation.Id,
            eligible: true, suggestion: "Cũ rồi", reason: "hot lead with closing signal",
            leadScore: 80, generatedAt: Now.AddDays(-1), sourceLastMessageAt: conversation.CreatedAt.AddDays(-1)));
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.GetSuggestionsAsync(fixture.TenantId);

        result.HotLeads.Should().ContainSingle();
        var item = result.HotLeads[0];
        item.Pending.Should().BeTrue();
        item.Suggestion.Should().BeEmpty();
        await fixture.Jobs.Received(1).LaunchAsync(
            SaleAssistUpsellJobHandler.JobType,
            Arg.Any<string>(),
            Arg.Any<object?>(),
            Arg.Any<Guid?>(),
            $"saleassist.upsell:{conversation.Id}",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSuggestionsAsync_NoCache_ReturnsPendingAndLaunchesJob()
    {
        await using var fixture = await UpsellFixture.CreateAsync();
        var (_, conversation) = await fixture.SeedHotLeadWithConversationAsync();

        var result = await fixture.Service.GetSuggestionsAsync(fixture.TenantId);

        result.HotLeads.Should().ContainSingle();
        result.HotLeads[0].Pending.Should().BeTrue();
        await fixture.Jobs.Received(1).LaunchAsync(
            SaleAssistUpsellJobHandler.JobType,
            Arg.Any<string>(),
            Arg.Any<object?>(),
            Arg.Any<Guid?>(),
            $"saleassist.upsell:{conversation.Id}",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSuggestionsAsync_LeadWithoutConversation_ReturnsIneligibleWithoutLaunchingJob()
    {
        await using var fixture = await UpsellFixture.CreateAsync();
        await fixture.SeedHotLeadAsync(withConversation: false);

        var result = await fixture.Service.GetSuggestionsAsync(fixture.TenantId);

        result.HotLeads.Should().ContainSingle();
        var item = result.HotLeads[0];
        item.Pending.Should().BeFalse();
        item.Eligible.Should().BeFalse();
        item.Reason.Should().Be("no conversation for lead");
        await fixture.Jobs.DidNotReceiveWithAnyArgs().LaunchAsync(default!, default!);
    }

    private sealed class UpsellFixture(
        SqliteConnection connection,
        AppDbContext db,
        IJobLauncher jobs) : IAsyncDisposable
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public AppDbContext Db { get; } = db;
        public IJobLauncher Jobs { get; } = jobs;
        public SaleAssistUpsellSuggestionService Service { get; } = new(db, jobs);

        public static async Task<UpsellFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new AppDbContext(options, new NullTenantAccessor());
            await db.Database.EnsureCreatedAsync();
            return new UpsellFixture(connection, db, Substitute.For<IJobLauncher>());
        }

        public async Task<(Lead Lead, Conversation Conversation)> SeedHotLeadWithConversationAsync()
        {
            var (lead, conversation) = await SeedHotLeadAsync(withConversation: true);
            return (lead, conversation!);
        }

        public async Task<(Lead Lead, Conversation? Conversation)> SeedHotLeadAsync(bool withConversation)
        {
            var contact = Contact.Create(TenantId, "Khách fixture", Now.AddDays(-2));
            Db.Contacts.Add(contact);

            var lead = Lead.Create(TenantId, contact.Id, "facebook", Now.AddDays(-2));
            lead.AdjustScore(80, "fixture", Now.AddHours(-1)); // score 80 -> stage hot
            Db.Leads.Add(lead);

            Conversation? conversation = null;
            if (withConversation)
            {
                conversation = Conversation.Open(TenantId, "facebook", "thread-1", Now.AddHours(-2), contactId: contact.Id);
                Db.Conversations.Add(conversation);
            }

            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return (lead, conversation);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class NullTenantAccessor : ITenantAccessor
    {
        public TenantContext? Current => null;

        public TenantContext Require() =>
            throw new InvalidOperationException("No tenant in unit test scope.");
    }
}
