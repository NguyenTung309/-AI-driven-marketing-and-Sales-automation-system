using System.Text.Json;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Api.Jobs;
using Clawbot.Domain.Agents;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Jobs;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Clawbot.Api.Tests.Integration;

// Unit test thuần cho AgentSandboxJobHandler (JobType "agents.sandbox") — job chạy qua Hangfire,
// không có route HTTP riêng. Dựng AppDbContext thật trên SQLite in-memory (không phải EF InMemory
// provider) theo pattern SaleAssistUpsellSuggestionServiceTests; mock IClaudeChatClient/ILlmCallScope/
// IPiiRedactor/IClock bằng NSubstitute.
public sealed class AgentSandboxJobHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_ValidPayload_CompletesSessionAndReturnsReply()
    {
        await using var fixture = await SandboxFixture.CreateAsync();
        var agent = fixture.SeedAgentConfig();

        var payload = new AgentSandboxJobPayload(agent.Id, agent.Code, "Bạn là trợ lý bán hàng.", "Xin chào");
        var ctx = fixture.BuildContext(payload);

        var result = await fixture.Handler.RunAsync(ctx, CancellationToken.None);

        result.ResultLink.Should().NotBeNull();
        result.ResultLink.Should().Contain("/agents/runs/");
        result.Summary.Should().NotBeNullOrWhiteSpace();

        using var doc = JsonDocument.Parse(result.Summary!);
        doc.RootElement.GetProperty("reply").GetString().Should().Be("phan hoi test");
        doc.RootElement.TryGetProperty("sessionId", out _).Should().BeTrue();

        var sessions = await fixture.Db.AgentSessions
            .IgnoreQueryFilters()
            .Include(s => s.Traces)
            .Where(s => s.AgentId == agent.Id)
            .ToListAsync();
        sessions.Should().ContainSingle();
        var session = sessions[0];
        session.Status.Should().Be(AgentSessionStatuses.Completed);
        session.Traces.Should().HaveCount(2);
        session.Traces.Should().Contain(t => t.Phase == "input" && t.Message == "Xin chào");
        session.Traces.Should().Contain(t => t.Phase == "reply" && t.Message == "phan hoi test");
    }

    [Fact]
    public async Task RunAsync_UnknownAgentConfigId_ThrowsBeforeCallingChatClient()
    {
        await using var fixture = await SandboxFixture.CreateAsync();

        var payload = new AgentSandboxJobPayload(Guid.NewGuid(), "khong-ton-tai", "system prompt", "Xin chào");
        var ctx = fixture.BuildContext(payload);

        var act = async () => await fixture.Handler.RunAsync(ctx, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Không tìm thấy agent.");
        await fixture.ChatClient.DidNotReceiveWithAnyArgs()
            .CompleteAsync(default!, default!, default!, CancellationToken.None);
    }

    private sealed class SandboxFixture(
        SqliteConnection connection,
        AppDbContext db,
        IClaudeChatClient chatClient,
        ILlmCallScope llmScope,
        IPiiRedactor pii,
        IClock clock) : IAsyncDisposable
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public AppDbContext Db { get; } = db;
        public IClaudeChatClient ChatClient { get; } = chatClient;
        public AgentSandboxJobHandler Handler { get; } = new(db, chatClient, llmScope, pii, clock);

        public static async Task<SandboxFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new AppDbContext(options, new NullTenantAccessor());
            await db.Database.EnsureCreatedAsync();

            var chatClient = Substitute.For<IClaudeChatClient>();
            chatClient
                .CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ClaudeReply("phan hoi test", 10, 5, 0.001m)));

            var llmScope = Substitute.For<ILlmCallScope>();
            llmScope
                .Begin(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<Guid?>(), Arg.Any<Guid?>())
                .Returns(Substitute.For<IDisposable>());

            var pii = Substitute.For<IPiiRedactor>();
            pii.RedactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(callInfo => Task.FromResult(new RedactionResult((string)callInfo[0], Array.Empty<PiiSpan>())));

            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(Now);

            return new SandboxFixture(connection, db, chatClient, llmScope, pii, clock);
        }

        public AgentConfig SeedAgentConfig()
        {
            var agent = AgentConfig.Create(TenantId, "sale-sandbox", "Agent chạy thử", "chat", "gpt-test", Now.AddDays(-1));
            Db.AgentConfigs.Add(agent);
            Db.SaveChanges();
            Db.ChangeTracker.Clear();
            return agent;
        }

        public JobContext BuildContext(AgentSandboxJobPayload payload) =>
            new(
                Guid.NewGuid(),
                TenantId,
                Guid.NewGuid(),
                JsonSerializer.Serialize(payload),
                Substitute.For<IJobProgress>());

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
