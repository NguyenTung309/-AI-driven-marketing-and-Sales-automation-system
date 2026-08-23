using Clawbot.Agents.Core.Chat;
using Clawbot.Api.Endpoints;
using Clawbot.Domain.Agents;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// Unit test thuần cho OrchestrationV2Endpoints.BuildPlanSuggestionsAsync (internal, InternalsVisibleTo
/// Clawbot.Api.Tests) — không đi qua HTTP host. AppDbContext dựng bằng EF InMemory riêng (không qua
/// ApiTestFactory) vì hàm nhận AppDbContext trực tiếp; IClaudeChatClient/ILlmCallScope mock bằng NSubstitute.
/// </summary>
public sealed class OrchestrationV2BuildSuggestionsTests
{
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task BuildPlanSuggestionsAsync_ValidJsonNoDuplicates_ReturnsAllSuggestions()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateDb(tenantId);
        var chatClient = CreateChatClient("""
            {"suggestions":[
                {"name":"Cham diem khach hang","goal":"Cham diem toan bo khach tiem nang","cadence":"daily","reason":"vi du"},
                {"name":"Bao cao KPI tuan","goal":"Tong hop KPI van hanh hang tuan","cadence":"weekly","reason":"vi du 2"}
            ]}
            """);
        var llmScope = CreateLlmScope();

        var result = await OrchestrationV2Endpoints.BuildPlanSuggestionsAsync(
            tenantId, db, chatClient, llmScope, Clock, NullLoggerFactory.Instance, CancellationToken.None);

        result.Items.Should().HaveCount(2);
        result.SkippedDuplicates.Should().Be(0);
    }

    [Fact]
    public async Task BuildPlanSuggestionsAsync_SuggestionNameMatchesExistingSchedule_IsFilteredOutAsDuplicate()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateDb(tenantId);
        // Seed 1 schedule có tên TRÙNG với suggestion "Cham diem khach hang" mà chatClient sẽ trả về
        db.AgentSchedules.Add(AgentSchedule.Create(
            tenantId, "Cham diem khach hang", "Goal cu da co", "daily", cronExpression: null,
            timezoneId: "Asia/Ho_Chi_Minh", nextRunAt: Clock.UtcNow, requiresApproval: false, createdAt: Clock.UtcNow));
        await db.SaveChangesAsync();

        var chatClient = CreateChatClient("""
            {"suggestions":[
                {"name":"Cham diem khach hang","goal":"Cham diem toan bo khach tiem nang","cadence":"daily","reason":"vi du"},
                {"name":"Bao cao KPI tuan","goal":"Tong hop KPI van hanh hang tuan","cadence":"weekly","reason":"vi du 2"}
            ]}
            """);
        var llmScope = CreateLlmScope();

        var result = await OrchestrationV2Endpoints.BuildPlanSuggestionsAsync(
            tenantId, db, chatClient, llmScope, Clock, NullLoggerFactory.Instance, CancellationToken.None);

        result.Items.Should().ContainSingle();
        result.Items[0].Name.Should().Be("Bao cao KPI tuan");
        result.SkippedDuplicates.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task BuildPlanSuggestionsAsync_ReplyNeverParsesAsJson_ThrowsAfterTwoAttempts()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateDb(tenantId);
        // Text không phải JSON hợp lệ ở cả 2 lần thử (endpoint retry tối đa 2 lần khi parse rỗng)
        var chatClient = CreateChatClient("khong phai json chut nao");
        var llmScope = CreateLlmScope();

        var act = () => OrchestrationV2Endpoints.BuildPlanSuggestionsAsync(
            tenantId, db, chatClient, llmScope, Clock, NullLoggerFactory.Instance, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*kiểm tra cấu hình model*");
        await chatClient.Received(2).CompleteAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static AppDbContext CreateDb(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
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
}
