using System.Text.Json;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Api.Contracts.SaleAssist;
using Clawbot.Api.Services;
using Clawbot.Domain.Agents;
using Clawbot.Domain.Conversations;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Clawbot.Api.Tests.Services;

// Unit test thuần cho SaleAssistDraftFeedbackService — không qua HTTP host. Dựng AppDbContext
// thật trên SQLite in-memory (không phải EF InMemory) theo pattern SaleAssistUpsellSuggestionServiceTests.
public sealed class SaleAssistDraftFeedbackServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecordAsync_EmptyConversationId_ThrowsArgumentException()
    {
        await using var fixture = await FeedbackFixture.CreateAsync();
        var request = new SaleAssistDraftFeedbackRequest(Guid.Empty, "nội dung nháp", "nội dung nháp", "sent");

        var act = async () => await fixture.Service.RecordAsync(fixture.TenantId, request);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RecordAsync_BlankDraftText_ThrowsArgumentException()
    {
        await using var fixture = await FeedbackFixture.CreateAsync();
        var conversation = await fixture.SeedConversationAsync();
        var request = new SaleAssistDraftFeedbackRequest(conversation.Id, "   ", "final", "sent");

        var act = async () => await fixture.Service.RecordAsync(fixture.TenantId, request);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RecordAsync_OutcomeNotAllowed_ThrowsArgumentException()
    {
        await using var fixture = await FeedbackFixture.CreateAsync();
        var conversation = await fixture.SeedConversationAsync();
        var request = new SaleAssistDraftFeedbackRequest(conversation.Id, "nội dung nháp", "nội dung nháp", "huy");

        var act = async () => await fixture.Service.RecordAsync(fixture.TenantId, request);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RecordAsync_ConversationNotFound_ThrowsKeyNotFoundException()
    {
        await using var fixture = await FeedbackFixture.CreateAsync();
        await fixture.SeedConversationAsync();
        var request = new SaleAssistDraftFeedbackRequest(Guid.NewGuid(), "nội dung nháp", "nội dung nháp", "sent");

        var act = async () => await fixture.Service.RecordAsync(fixture.TenantId, request);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task RecordAsync_DraftDifferentFromFinal_ReturnsEditedTrueAndPersistsSession()
    {
        await using var fixture = await FeedbackFixture.CreateAsync();
        var conversation = await fixture.SeedConversationAsync();
        var request = new SaleAssistDraftFeedbackRequest(
            conversation.Id,
            "Chào anh, sản phẩm còn hàng ạ",
            "Chào anh, sản phẩm còn hàng nhé",
            "Edited");

        var response = await fixture.Service.RecordAsync(fixture.TenantId, request);

        response.Edited.Should().BeTrue();
        response.RecordedAt.Should().Be(Now);
        response.SessionId.Should().NotBe(Guid.Empty);

        var sessions = await fixture.Db.AgentSessions
            .IgnoreQueryFilters()
            .Include(s => s.Traces)
            .Where(s => s.ConversationId == conversation.Id)
            .ToListAsync();
        sessions.Should().ContainSingle();
        var session = sessions[0];
        session.Status.Should().Be(AgentSessionStatuses.Completed);
        session.Traces.Should().ContainSingle();

        var trace = session.Traces.Single();
        using var doc = JsonDocument.Parse(trace.Message!);
        doc.RootElement.GetProperty("outcome").GetString().Should().Be("edited");
        doc.RootElement.GetProperty("edited").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task RecordAsync_DraftEqualsFinalAfterTrim_ReturnsEditedFalse()
    {
        await using var fixture = await FeedbackFixture.CreateAsync();
        var conversation = await fixture.SeedConversationAsync();
        var request = new SaleAssistDraftFeedbackRequest(
            conversation.Id,
            "  Chào anh, sản phẩm còn hàng ạ  ",
            "Chào anh, sản phẩm còn hàng ạ",
            "sent");

        var response = await fixture.Service.RecordAsync(fixture.TenantId, request);

        response.Edited.Should().BeFalse();
    }

    private sealed class FeedbackFixture(
        SqliteConnection connection,
        AppDbContext db,
        SaleAssistDraftFeedbackService service) : IAsyncDisposable
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public AppDbContext Db { get; } = db;
        public SaleAssistDraftFeedbackService Service { get; } = service;

        public static async Task<FeedbackFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new AppDbContext(options, new NullTenantAccessor());
            await db.Database.EnsureCreatedAsync();

            var pii = Substitute.For<IPiiRedactor>();
            pii.RedactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(callInfo => Task.FromResult(new RedactionResult((string)callInfo[0], Array.Empty<PiiSpan>())));

            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(Now);

            var service = new SaleAssistDraftFeedbackService(db, pii, clock);
            return new FeedbackFixture(connection, db, service);
        }

        public async Task<Conversation> SeedConversationAsync()
        {
            var conversation = Conversation.Open(
                TenantId,
                "facebook",
                $"thread-{Guid.NewGuid():N}",
                Now.AddHours(-2));
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
