using System.Text.Json;
using Clawbot.Agents.Contracts.SaleAssist;
using Clawbot.Api.Jobs;
using Clawbot.Domain.Conversations;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Jobs;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Clawbot.Api.Tests.Integration;

// Unit test thuần cho SaleAssistUpsellJobHandler (saleassist.upsell) — SQLite in-memory để phủ nhánh cache.
public sealed class SaleAssistUpsellJobHandlerTests
{
    [Fact]
    public async Task RunAsync_NoExistingCache_CreatesCacheRowAndReturnsJson()
    {
        var grpc = Substitute.For<SaleAssistAgent.SaleAssistAgentClient>();
        grpc.UpsellAsync(Arg.Any<UpsellRequest>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(CompletedUnaryCall(new UpsellResponse
            {
                Eligible = true,
                Suggestion = "Goi combo A+B",
                Reason = "Khach quan tam san pham B",
                LeadScore = 85,
            }));

        await using var fixture = await UpsellFixture.CreateAsync();
        var conversationId = await fixture.SeedConversationAsync();
        var handler = new SaleAssistUpsellJobHandler(grpc, fixture.Db);
        var tenantId = fixture.TenantId;
        var payload = new SaleAssistConversationJobPayload(conversationId);
        var ctx = new JobContext(Guid.NewGuid(), tenantId, Guid.NewGuid(), JsonSerializer.Serialize(payload), new NoopProgress());

        var result = await handler.RunAsync(ctx, CancellationToken.None);

        result.ResultLink.Should().Contain(conversationId.ToString());
        var json = JsonDocument.Parse(result.Summary!);
        json.RootElement.GetProperty("eligible").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("suggestion").GetString().Should().Be("Goi combo A+B");
        json.RootElement.GetProperty("leadScore").GetInt32().Should().Be(85);

        var caches = await fixture.Db.UpsellSuggestionCaches.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && c.ConversationId == conversationId).ToListAsync();
        caches.Should().ContainSingle();
        caches[0].Suggestion.Should().Be("Goi combo A+B");
        caches[0].Eligible.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_ExistingCache_UpdatesSameRowWithoutCreatingNewOne()
    {
        var grpc = Substitute.For<SaleAssistAgent.SaleAssistAgentClient>();
        grpc.UpsellAsync(Arg.Any<UpsellRequest>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(CompletedUnaryCall(new UpsellResponse
            {
                Eligible = false,
                Suggestion = "Khong phu hop luc nay",
                Reason = "Khach chua co nhu cau",
                LeadScore = 20,
            }));

        await using var fixture = await UpsellFixture.CreateAsync();
        var conversationId = await fixture.SeedConversationAsync();
        // Seed cache cũ bằng lần chạy đầu với suggestion khác
        var firstGrpc = Substitute.For<SaleAssistAgent.SaleAssistAgentClient>();
        firstGrpc.UpsellAsync(Arg.Any<UpsellRequest>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(CompletedUnaryCall(new UpsellResponse
            {
                Eligible = true,
                Suggestion = "Goi cu",
                Reason = "Ly do cu",
                LeadScore = 90,
            }));
        var firstHandler = new SaleAssistUpsellJobHandler(firstGrpc, fixture.Db);
        var firstCtx = new JobContext(Guid.NewGuid(), fixture.TenantId, Guid.NewGuid(),
            JsonSerializer.Serialize(new SaleAssistConversationJobPayload(conversationId)), new NoopProgress());
        await firstHandler.RunAsync(firstCtx, CancellationToken.None);
        fixture.Db.ChangeTracker.Clear();

        // Lần 2: update
        var handler = new SaleAssistUpsellJobHandler(grpc, fixture.Db);
        var ctx = new JobContext(Guid.NewGuid(), fixture.TenantId, Guid.NewGuid(),
            JsonSerializer.Serialize(new SaleAssistConversationJobPayload(conversationId)), new NoopProgress());

        await handler.RunAsync(ctx, CancellationToken.None);

        var caches = await fixture.Db.UpsellSuggestionCaches.IgnoreQueryFilters()
            .Where(c => c.TenantId == fixture.TenantId && c.ConversationId == conversationId).ToListAsync();
        caches.Should().ContainSingle();
        caches[0].Suggestion.Should().Be("Khong phu hop luc nay");
        caches[0].Eligible.Should().BeFalse();
        caches[0].LeadScore.Should().Be(20);
    }

    [Fact]
    public async Task RunAsync_NullPayload_Throws()
    {
        var grpc = Substitute.For<SaleAssistAgent.SaleAssistAgentClient>();
        await using var fixture = await UpsellFixture.CreateAsync();
        var handler = new SaleAssistUpsellJobHandler(grpc, fixture.Db);
        var ctx = new JobContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "null", new NoopProgress());

        var act = async () => await handler.RunAsync(ctx, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static AsyncUnaryCall<T> CompletedUnaryCall<T>(T response) where T : class =>
        new(Task.FromResult(response), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });

    private sealed class NoopProgress : IJobProgress
    {
        public Task ReportAsync(int percent, string? note, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class UpsellFixture(SqliteConnection connection, AppDbContext db) : IAsyncDisposable
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public AppDbContext Db { get; } = db;

        public static async Task<UpsellFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options, new NullTenantAccessor());
            await db.Database.EnsureCreatedAsync();
            return new UpsellFixture(connection, db);
        }

        public async Task<Guid> SeedConversationAsync()
        {
            var now = DateTimeOffset.UtcNow;
            var conversation = Conversation.Open(TenantId, "facebook", $"thread-{Guid.NewGuid():N}", now);
            Db.Conversations.Add(conversation);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return conversation.Id;
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
        public TenantContext Require() => throw new InvalidOperationException("No tenant in unit test scope.");
    }
}
