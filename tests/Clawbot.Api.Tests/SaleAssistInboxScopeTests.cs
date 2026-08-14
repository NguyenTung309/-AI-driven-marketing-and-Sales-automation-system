using System.Security.Claims;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Api.Contracts.SaleAssist;
using Clawbot.Api.Endpoints;
using Clawbot.Api.Services;
using Clawbot.Domain.Contacts;
using Clawbot.Domain.Conversations;
using Clawbot.Domain.Leads;
using Clawbot.Domain.SaleAssist;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Jobs;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Clawbot.Api.Tests;

public sealed class SaleAssistInboxScopeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 14, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DraftAsync_AllowsAssignedOrUnrestrictedConversation(bool unrestricted)
    {
        // Arrange
        await using var fixture = await SaleAssistScopeFixture.CreateAsync();
        var inboxId = Guid.NewGuid();
        var conversation = await fixture.SeedConversationAsync(inboxId);
        fixture.ResolveInboxes(unrestricted ? [] : [inboxId]);

        // Act
        var result = await SaleAssistEndpoints.DraftAsync(
            new SaleAssistDraftRequest(conversation.Id),
            fixture.Jobs,
            fixture.Tenants,
            fixture.Db,
            fixture.Resolver,
            fixture.Http,
            CancellationToken.None);

        // Assert
        StatusCode(result).Should().Be(StatusCodes.Status202Accepted);
        await fixture.Jobs.Received(1).LaunchAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<object?>(),
            fixture.UserId,
            $"saleassist.draft:{conversation.Id}",
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DraftAsync_RejectsForeignOrDenyAllConversationWithoutLaunchingJob(bool denyAll)
    {
        // Arrange
        await using var fixture = await SaleAssistScopeFixture.CreateAsync();
        var conversation = await fixture.SeedConversationAsync(Guid.NewGuid());
        fixture.ResolveInboxes(denyAll ? [Guid.Empty] : [Guid.NewGuid()]);

        // Act
        var result = await SaleAssistEndpoints.DraftAsync(
            new SaleAssistDraftRequest(conversation.Id),
            fixture.Jobs,
            fixture.Tenants,
            fixture.Db,
            fixture.Resolver,
            fixture.Http,
            CancellationToken.None);

        // Assert
        StatusCode(result).Should().Be(StatusCodes.Status404NotFound);
        await fixture.Jobs.DidNotReceiveWithAnyArgs().LaunchAsync(default!, default!);
    }

    [Fact]
    public async Task DraftFeedbackAsync_RejectsForeignConversationBeforeRedactionOrPersistence()
    {
        // Arrange
        await using var fixture = await SaleAssistScopeFixture.CreateAsync();
        var conversation = await fixture.SeedConversationAsync(Guid.NewGuid());
        fixture.ResolveInboxes([Guid.NewGuid()]);
        var pii = Substitute.For<IPiiRedactor>();
        var feedback = new SaleAssistDraftFeedbackService(fixture.Db, pii, new FixedClock(Now));
        var request = new SaleAssistDraftFeedbackRequest(
            conversation.Id,
            "Gọi 0901234567",
            "Gọi 0901234567",
            "sent");

        // Act
        var result = await SaleAssistEndpoints.DraftFeedbackAsync(
            request,
            fixture.Tenants,
            fixture.Db,
            fixture.Resolver,
            fixture.Http.User,
            feedback,
            CancellationToken.None);

        // Assert
        StatusCode(result).Should().Be(StatusCodes.Status404NotFound);
        await pii.DidNotReceiveWithAnyArgs().RedactAsync(default!, default);
        (await fixture.Db.AgentSessions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UpsellAsync_RejectsForeignConversationBeforeReturningCacheOrLaunchingJob()
    {
        // Arrange
        await using var fixture = await SaleAssistScopeFixture.CreateAsync();
        var foreignInboxId = Guid.NewGuid();
        var (lead, conversation) = await fixture.SeedHotLeadAsync(foreignInboxId);
        fixture.Db.UpsellSuggestionCaches.Add(UpsellSuggestionCache.Create(
            fixture.TenantId,
            conversation.Id,
            eligible: true,
            suggestion: "Thông tin nhạy cảm từ inbox khác",
            reason: "foreign cached result",
            leadScore: lead.Score,
            generatedAt: Now,
            sourceLastMessageAt: conversation.CreatedAt));
        await fixture.Db.SaveChangesAsync();
        fixture.ResolveInboxes([Guid.NewGuid()]);

        // Act
        var result = await SaleAssistEndpoints.UpsellAsync(
            conversation.Id,
            fixture.Jobs,
            fixture.Tenants,
            fixture.Db,
            fixture.Resolver,
            fixture.Http,
            CancellationToken.None);

        // Assert
        StatusCode(result).Should().Be(StatusCodes.Status404NotFound);
        await fixture.Jobs.DidNotReceiveWithAnyArgs().LaunchAsync(default!, default!);
    }

    [Fact]
    public async Task UpsellSuggestionsAsync_ReturnsOnlyLeadsFromResolvedInboxes()
    {
        // Arrange
        await using var fixture = await SaleAssistScopeFixture.CreateAsync();
        var allowedInboxId = Guid.NewGuid();
        var foreignInboxId = Guid.NewGuid();
        var (allowedLead, _) = await fixture.SeedHotLeadAsync(allowedInboxId, score: 80);
        await fixture.SeedHotLeadAsync(foreignInboxId, score: 95);
        fixture.ResolveInboxes([allowedInboxId]);
        var service = new SaleAssistUpsellSuggestionService(fixture.Db, fixture.Jobs);

        // Act
        var result = await SaleAssistEndpoints.UpsellSuggestionsAsync(
            fixture.Tenants,
            fixture.Resolver,
            fixture.Http.User,
            service,
            CancellationToken.None);

        // Assert
        StatusCode(result).Should().Be(StatusCodes.Status200OK);
        var response = Value(result).Should().BeOfType<SaleAssistUpsellSuggestionsResponse>().Subject;
        response.HotLeads.Should().ContainSingle(item => item.Id == allowedLead.Id);
    }

    [Theory]
    [InlineData("restricted", 1)]
    [InlineData("unrestricted", 2)]
    [InlineData("deny-all", 0)]
    public async Task DailySummaryAsync_AppliesInboxScopeToEveryMetric(
        string scopeMode,
        int expectedCount)
    {
        // Arrange
        await using var fixture = await SaleAssistScopeFixture.CreateAsync();
        var allowedInboxId = Guid.NewGuid();
        var foreignInboxId = Guid.NewGuid();
        var (_, allowedConversation) = await fixture.SeedHotLeadAsync(allowedInboxId, createdAt: Now);
        var (_, foreignConversation) = await fixture.SeedHotLeadAsync(foreignInboxId, createdAt: Now);
        await fixture.SeedOutboundMessageAsync(allowedConversation.Id, Now.AddMinutes(1));
        await fixture.SeedOutboundMessageAsync(foreignConversation.Id, Now.AddMinutes(1));
        fixture.ResolveInboxes(scopeMode switch
        {
            "restricted" => [allowedInboxId],
            "unrestricted" => [],
            "deny-all" => [Guid.Empty],
            _ => throw new ArgumentOutOfRangeException(nameof(scopeMode)),
        });

        // Act
        var result = await SaleAssistEndpoints.DailySummaryAsync(
            fixture.Db,
            fixture.Resolver,
            fixture.Tenants,
            fixture.Http.User,
            new FixedClock(Now),
            CancellationToken.None);

        // Assert
        StatusCode(result).Should().Be(StatusCodes.Status200OK);
        IntProperty(result, "new_leads").Should().Be(expectedCount);
        IntProperty(result, "conversations").Should().Be(expectedCount);
        IntProperty(result, "messages_sent").Should().Be(expectedCount);
        IntProperty(result, "hot_leads").Should().Be(expectedCount);
    }

    private static int? StatusCode(IResult result) =>
        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Subject.StatusCode;

    private static object? Value(IResult result) =>
        result.Should().BeAssignableTo<IValueHttpResult>().Subject.Value;

    private static int IntProperty(IResult result, string propertyName)
    {
        var value = Value(result);
        value.Should().NotBeNull();
        var property = value!.GetType().GetProperty(propertyName);
        property.Should().NotBeNull();
        return property!.GetValue(value).Should().BeOfType<int>().Subject;
    }

    private sealed class SaleAssistScopeFixture(
        SqliteConnection connection,
        AppDbContext db,
        Guid tenantId,
        FixedTenantAccessor tenants,
        IUserInboxResolver resolver,
        IJobLauncher jobs,
        DefaultHttpContext http,
        Guid userId) : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = connection;
        public AppDbContext Db { get; } = db;
        public Guid TenantId { get; } = tenantId;
        public FixedTenantAccessor Tenants { get; } = tenants;
        public IUserInboxResolver Resolver { get; } = resolver;
        public IJobLauncher Jobs { get; } = jobs;
        public DefaultHttpContext Http { get; } = http;
        public Guid UserId { get; } = userId;

        public static async Task<SaleAssistScopeFixture> CreateAsync()
        {
            var tenantId = Guid.NewGuid();
            var tenants = new FixedTenantAccessor(tenantId);
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new AppDbContext(options, tenants);
            await db.Database.EnsureCreatedAsync();

            var userId = Guid.NewGuid();
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")),
            ], "test");
            var http = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity),
            };
            var jobs = Substitute.For<IJobLauncher>();
            jobs.LaunchAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<object?>(),
                    Arg.Any<Guid?>(),
                    Arg.Any<string?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Guid.NewGuid());

            return new SaleAssistScopeFixture(
                connection,
                db,
                tenantId,
                tenants,
                Substitute.For<IUserInboxResolver>(),
                jobs,
                http,
                userId);
        }

        public void ResolveInboxes(List<Guid> inboxIds)
        {
            Resolver.GetInboxIdsAsync(
                    Arg.Any<ClaimsPrincipal>(),
                    Arg.Any<CancellationToken>())
                .Returns(inboxIds);
        }

        public async Task<Conversation> SeedConversationAsync(
            Guid inboxId,
            Guid? contactId = null,
            DateTimeOffset? createdAt = null)
        {
            var conversation = Conversation.Open(
                TenantId,
                "facebook",
                $"thread-{Guid.NewGuid():N}",
                createdAt ?? Now.AddHours(-2),
                contactId,
                inboxId);
            Db.Conversations.Add(conversation);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return conversation;
        }

        public async Task<(Lead Lead, Conversation Conversation)> SeedHotLeadAsync(
            Guid inboxId,
            int score = 80,
            DateTimeOffset? createdAt = null)
        {
            var created = createdAt ?? Now.AddDays(-2);
            var contact = Contact.Create(TenantId, $"Khách {Guid.NewGuid():N}", created);
            Db.Contacts.Add(contact);
            var lead = Lead.Create(TenantId, contact.Id, "facebook", created);
            lead.AdjustScore(score, "fixture", created.AddMinutes(1));
            Db.Leads.Add(lead);
            var conversation = Conversation.Open(
                TenantId,
                "facebook",
                $"thread-{Guid.NewGuid():N}",
                created,
                contact.Id,
                inboxId);
            Db.Conversations.Add(conversation);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return (lead, conversation);
        }

        public async Task SeedOutboundMessageAsync(
            Guid conversationId,
            DateTimeOffset sentAt)
        {
            var conversation = await Db.Conversations.IgnoreQueryFilters()
                .SingleAsync(item => item.Id == conversationId && item.TenantId == TenantId);
            conversation.AppendMessage(
                "out",
                "agent",
                "Tin nhắn fixture",
                "text",
                sentAt,
                UserId);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FixedTenantAccessor(Guid tenantId) : ITenantAccessor
    {
        public TenantContext? Current { get; } = new(tenantId, "test-tenant");

        public TenantContext Require() => Current!;
    }
}
