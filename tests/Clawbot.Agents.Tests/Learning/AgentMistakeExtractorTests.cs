using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Learning;
using FluentAssertions;
using NSubstitute;

namespace Clawbot.Agents.Tests.Learning;

// Rút bài học lỗi lặp cho agent từ lý do bị loại: validate op/factId, category ép "mistake".
public sealed class AgentMistakeExtractorTests
{
    private static readonly Guid Tenant = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }

    private static (AgentMistakeExtractor Extractor, IClaudeChatClient Claude) NewExtractor(params string[] replies)
    {
        var claude = Substitute.For<IClaudeChatClient>();
        var scope = Substitute.For<ILlmCallScope>();
        scope.Begin(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<Guid?>(), Arg.Any<Guid?>())
            .Returns(new NoopDisposable());

        var queued = replies.Select(r => new ClaudeReply(r, 0, 0, 0m)).ToArray();
        if (queued.Length == 1)
            claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(queued[0]);
        else if (queued.Length > 1)
            claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(queued[0], queued[1..]);

        return (new AgentMistakeExtractor(claude, scope), claude);
    }

    [Fact]
    public async Task Extract_NoRejections_ReturnsEmptyWithoutCallingLlm()
    {
        var (extractor, claude) = NewExtractor("unused");

        var result = await extractor.ExtractAsync(Tenant, "content-writer", Array.Empty<string>(), Array.Empty<ContactFact>());

        result.Should().BeEmpty();
        await claude.DidNotReceive().CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Extract_AddLesson_CategoryForcedToMistake()
    {
        var json = """{"ops":[{"op":"add","factId":null,"fact":"Hay quên chèn CTA cuối bài","category":"whatever","confidence":0.9}]}""";
        var (extractor, _) = NewExtractor(json);

        var result = await extractor.ExtractAsync(Tenant, "content-writer", new[] { "thiếu CTA" }, Array.Empty<ContactFact>());

        result.Should().ContainSingle();
        result![0].Category.Should().Be("mistake");
    }

    [Fact]
    public async Task Extract_DeleteWithKnownFactId_Accepted()
    {
        var lesson = new ContactFact(Guid.NewGuid(), "bài học cũ", "mistake", 0.8m);
        var json = $$"""{"ops":[{"op":"delete","factId":"{{lesson.Id}}","fact":null,"category":"mistake","confidence":0.5}]}""";
        var (extractor, _) = NewExtractor(json);

        var result = await extractor.ExtractAsync(Tenant, "content-writer", new[] { "lỗi" }, new[] { lesson });

        result.Should().ContainSingle();
        result![0].Op.Should().Be("delete");
    }

    [Fact]
    public async Task Extract_DefaultConfidence_WhenNull()
    {
        var json = """{"ops":[{"op":"add","factId":null,"fact":"lỗi lặp","category":"mistake","confidence":null}]}""";
        var (extractor, _) = NewExtractor(json);

        var result = await extractor.ExtractAsync(Tenant, "content-writer", new[] { "lỗi" }, Array.Empty<ContactFact>());

        result![0].Confidence.Should().Be(0.7m);
    }

    [Fact]
    public async Task Extract_UnknownOp_RejectedThenNull()
    {
        var bad = """{"ops":[{"op":"frobnicate","factId":null,"fact":"x","category":"mistake","confidence":0.5}]}""";
        var (extractor, _) = NewExtractor(bad, bad, bad);

        var result = await extractor.ExtractAsync(Tenant, "content-writer", new[] { "lỗi" }, Array.Empty<ContactFact>());

        result.Should().BeNull();
    }
}
