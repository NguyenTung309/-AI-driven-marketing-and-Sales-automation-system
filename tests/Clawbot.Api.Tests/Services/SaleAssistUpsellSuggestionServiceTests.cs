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

        var result = await fixture.Service.GetSuggestionsAsync(fixture.TenantId, []);

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

        var result = await fixture.Service.GetSuggestionsAsync(fixture.TenantId, []);

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

        var result = await fixture.Service.GetSuggestionsAsync(fixture.TenantId, []);

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

        var result = await fixture.Service.GetSuggestionsAsync(fixture.TenantId, []);

        result.HotLeads.Should().ContainSingle();
        var item = result.HotLeads[0];
        item.Pending.Should().BeFalse();
        item.Eligible.Should().BeFalse();
        item.Reason.Should().Be("no conversation for lead");
        await fixture.Jobs.DidNotReceiveWithAnyArgs().LaunchAsync(default!, default!);
    }

    [Fact]
    public async Task GetSuggestionsAsync_DenyAllScope_ReturnsNoLeadsWithoutLaunchingJobs()
    {
        await using var fixture = await UpsellFixture.CreateAsync();
        await fixture.SeedHotLeadWithConversationAsync(Guid.NewGuid());

        var result = await fixture.Service.GetSuggestionsAsync(fixture.TenantId, [Guid.Empty]);

        result.HotLeads.Should().BeEmpty();
        result.Count.Should().Be(0);
        await fixture.Jobs.DidNotReceiveWithAnyArgs().LaunchAsync(default!, default!);
    }

    [Fact]
    public async Task GetSuggestionsAsync_RestrictedScope_FiltersBeforeOrderingAndTake()
    {
        await using var fixture = await UpsellFixture.CreateAsync();
        var allowedInboxId = Guid.NewGuid();
        var foreignInboxId = Guid.NewGuid();
        var (allowedLead, allowedConversation) = await fixture.SeedHotLeadWithConversationAsync(
            allowedInboxId,
            score: 80);
        var (_, foreignConversation) = await fixture.SeedHotLeadWithConversationAsync(
            foreignInboxId,
            score: 95);

        var result = await fixture.Service.GetSuggestionsAsync(
            fixture.TenantId,
            [allowedInboxId],
            take: 1);

        result.HotLeads.Should().ContainSingle(item =>
            item.Id == allowedLead.Id && item.ConversationId == allowedConversation.Id);
        await fixture.Jobs.Received(1).LaunchAsync(
            SaleAssistUpsellJobHandler.JobType,
            Arg.Any<string>(),
            Arg.Any<object?>(),
            Arg.Any<Guid?>(),
            $"saleassist.upsell:{allowedConversation.Id}",
            Arg.Any<CancellationToken>());
        await fixture.Jobs.DidNotReceive().LaunchAsync(
            SaleAssistUpsellJobHandler.JobType,
            Arg.Any<string>(),
            Arg.Any<object?>(),
            Arg.Any<Guid?>(),
            $"saleassist.upsell:{foreignConversation.Id}",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSuggestionsAsync_RestrictedScope_UsesLatestAllowedConversation()
    {
        await using var fixture = await UpsellFixture.CreateAsync();
        var allowedInboxId = Guid.NewGuid();
        var foreignInboxId = Guid.NewGuid();
        var (lead, olderAllowedConversation) = await fixture.SeedHotLeadWithConversationAsync(allowedInboxId);
        var newerAllowedConversation = await fixture.SeedConversationAsync(
            lead.ContactId!.Value,
            allowedInboxId,
            Now.AddMinutes(-90));
        var foreignConversation = await fixture.SeedConversationAsync(
            lead.ContactId.Value,
            foreignInboxId,
            Now.AddHours(-1));

        var result = await fixture.Service.GetSuggestionsAsync(
            fixture.TenantId,
            [allowedInboxId]);

        result.HotLeads.Should().ContainSingle(item => item.ConversationId == newerAllowedConversation.Id);
        await fixture.Jobs.Received(1).LaunchAsync(
            SaleAssistUpsellJobHandler.JobType,
            Arg.Any<string>(),
            Arg.Any<object?>(),
            Arg.Any<Guid?>(),
            $"saleassist.upsell:{newerAllowedConversation.Id}",
            Arg.Any<CancellationToken>());
        await fixture.Jobs.DidNotReceive().LaunchAsync(
            SaleAssistUpsellJobHandler.JobType,
            Arg.Any<string>(),
            Arg.Any<object?>(),
            Arg.Any<Guid?>(),
            Arg.Is<string?>(key =>
                key == $"saleassist.upsell:{olderAllowedConversation.Id}" ||
                key == $"saleassist.upsell:{foreignConversation.Id}"),
            Arg.Any<CancellationToken>());
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

        public async Task<(Lead Lead, Conversation Conversation)> SeedHotLeadWithConversationAsync(
            Guid? inboxId = null,
            int score = 80)
        {
            var (lead, conversation) = await SeedHotLeadAsync(
                withConversation: true,
                inboxId,
                score);
            return (lead, conversation!);
        }

        public async Task<(Lead Lead, Conversation? Conversation)> SeedHotLeadAsync(
            bool withConversation,
            Guid? inboxId = null,
            int score = 80)
        {
            var contact = Contact.Create(TenantId, $"Khách {Guid.NewGuid():N}", Now.AddDays(-2));
            Db.Contacts.Add(contact);

            var lead = Lead.Create(TenantId, contact.Id, "facebook", Now.AddDays(-2));
            lead.AdjustScore(score, "fixture", Now.AddHours(-1));
            Db.Leads.Add(lead);

            Conversation? conversation = null;
            if (withConversation)
            {
                conversation = Conversation.Open(
                    TenantId,
                    "facebook",
                    $"thread-{Guid.NewGuid():N}",
                    Now.AddHours(-2),
                    contactId: contact.Id,
                    inboxId: inboxId);
                Db.Conversations.Add(conversation);
            }

            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return (lead, conversation);
        }

        public async Task<Conversation> SeedConversationAsync(
            Guid contactId,
            Guid inboxId,
            DateTimeOffset createdAt)
        {
            var conversation = Conversation.Open(
                TenantId,
                "facebook",
                $"thread-{Guid.NewGuid():N}",
                createdAt,
                contactId,
                inboxId);
            Db.Conversations.Add(conversation);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return conversation;
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
