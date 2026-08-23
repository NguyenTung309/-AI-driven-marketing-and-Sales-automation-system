using System.Text.Json;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Learning;
using FluentAssertions;
using NSubstitute;

namespace Clawbot.Agents.Tests.Learning;

// Vòng lặp tự sửa JSON: gọi LLM, parse; nếu hỏng thì phản hồi lỗi và thử lại tới maxAttempts.
public sealed class LlmJsonRepairTests
{
    private sealed record Payload(string Name, int Value);

    private static readonly JsonSerializerOptions ParseOptions = new(JsonSerializerDefaults.Web);

    private static Payload? Parse(string json) =>
        JsonSerializer.Deserialize<Payload>(json, ParseOptions);

    private static ClaudeReply Reply(string text) => new(text, 0, 0, 0m);

    [Fact]
    public async Task CompleteAsync_ValidFirstReply_ReturnsParsedImmediately()
    {
        var claude = Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Reply("""{"name":"a","value":5}"""));

        var result = await LlmJsonRepair.CompleteAsync(claude, "sys", "user", Parse, maxAttempts: 3, default);

        result.Should().Be(new Payload("a", 5));
        await claude.Received(1).CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteAsync_StripsCodeFencesBeforeParsing()
    {
        var claude = Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Reply("Here you go:\n```json\n{\"name\":\"b\",\"value\":7}\n```\nDone."));

        var result = await LlmJsonRepair.CompleteAsync(claude, "sys", "user", Parse, maxAttempts: 2, default);

        result.Should().Be(new Payload("b", 7));
    }

    [Fact]
    public async Task CompleteAsync_RetriesOnMalformedThenSucceeds()
    {
        var claude = Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Reply("not json at all"), Reply("""{"name":"c","value":9}"""));

        var result = await LlmJsonRepair.CompleteAsync(claude, "sys", "user", Parse, maxAttempts: 3, default);

        result.Should().Be(new Payload("c", 9));
        await claude.Received(2).CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteAsync_AllAttemptsFail_ReturnsNull()
    {
        var claude = Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Reply("garbage"));

        var result = await LlmJsonRepair.CompleteAsync(claude, "sys", "user", Parse, maxAttempts: 2, default);

        result.Should().BeNull();
        await claude.Received(2).CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteAsync_ParseReturnsNull_TreatedAsInvalidAndRetries()
    {
        var claude = Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Reply("null"), Reply("""{"name":"d","value":1}"""));

        var result = await LlmJsonRepair.CompleteAsync(claude, "sys", "user", Parse, maxAttempts: 3, default);

        result.Should().Be(new Payload("d", 1));
    }
}
