using System.Text.Json;
using Clawbot.Agents.Core.Chat;
using Clawbot.Api.Endpoints;
using Clawbot.Api.Jobs;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Jobs;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Clawbot.Api.Tests.Integration;

public sealed class OrchestrationPlanSuggestionsJobHandlerTests
{
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task RunAsync_SingleSuggestion_ReturnsLinkWithJobIdAndCamelCaseJson()
    {
        var tenantId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        using var db = CreateDb(tenantId);
        var chatClient = CreateChatClient("""
            {"suggestions":[{"name":"Cham diem khach hang","goal":"Cham diem toan bo khach tiem nang","cadence":"daily","reason":"vi du"}]}
            """);
        var llmScope = CreateLlmScope();
        var handler = new OrchestrationPlanSuggestionsJobHandler(db, chatClient, llmScope, Clock, NullLoggerFactory.Instance);
        var ctx = new JobContext(jobId, tenantId, Guid.NewGuid(), "{}", new NoopProgress());

        var result = await handler.RunAsync(ctx, CancellationToken.None);

        result.ResultLink.Should().Be($"/agents?planResult={jobId}");
        var doc = JsonDocument.Parse(result.Summary!);
        doc.RootElement.TryGetProperty("items", out var items).Should().BeTrue("FE đọc field camelCase 'items'");
        items.GetArrayLength().Should().Be(1);
        doc.RootElement.GetProperty("skippedDuplicates").GetInt32().Should().Be(0);
        doc.RootElement.TryGetProperty("Items", out _).Should().BeFalse("không được PascalCase");
    }

    [Fact]
    public async Task RunAsync_EmptySuggestions_ThrowsAfterTwoAttempts()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateDb(tenantId);
        var chatClient = CreateChatClient("""{"suggestions":[]}""");
        var llmScope = CreateLlmScope();
        var handler = new OrchestrationPlanSuggestionsJobHandler(db, chatClient, llmScope, Clock, NullLoggerFactory.Instance);
        var ctx = new JobContext(Guid.NewGuid(), tenantId, Guid.NewGuid(), "{}", new NoopProgress());

        var act = async () => await handler.RunAsync(ctx, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static AppDbContext CreateDb(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        return new AppDbContext(options, new FixedTenantAccessor(tenantId));
    }

    private static IClaudeChatClient CreateChatClient(string replyText)
    {
        var chatClient = Substitute.For<IClaudeChatClient>();
        chatClient.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ClaudeReply(replyText, 10, 5, 0m)));
        return chatClient;
    }

    private static ILlmCallScope CreateLlmScope()
    {
        var llmScope = Substitute.For<ILlmCallScope>();
        llmScope.Begin(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<Guid?>(), Arg.Any<Guid?>())
            .Returns(Substitute.For<IDisposable>());
        return llmScope;
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

    private sealed class NoopProgress : IJobProgress
    {
        public Task ReportAsync(int percent, string? note, CancellationToken ct = default) => Task.CompletedTask;
    }
}
