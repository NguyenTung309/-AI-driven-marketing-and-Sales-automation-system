using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Learning;
using FluentAssertions;
using NSubstitute;

namespace Clawbot.Agents.Tests.Learning;

// Rút memory-op cho khách từ transcript: validate op/factId/category, clamp confidence.
public sealed class ContactFactExtractorTests
{
    private static readonly Guid Tenant = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static (ContactFactExtractor Extractor, IClaudeChatClient Claude) NewExtractor(params string[] replies)
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

        return (new ContactFactExtractor(claude, scope), claude);
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Extract_BlankTranscript_ReturnsEmptyWithoutCallingLlm(string transcript)
    {
        var (extractor, claude) = NewExtractor("unused");

        var result = await extractor.ExtractAsync(Tenant, transcript, Array.Empty<ContactFact>());

        result.Should().BeEmpty();
        await claude.DidNotReceive().CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Extract_AddOp_Accepted()
    {
        var json = """{"ops":[{"op":"add","factId":null,"fact":"Khách muốn học ca tối","category":"preference","confidence":0.9}]}""";
        var (extractor, _) = NewExtractor(json);

        var result = await extractor.ExtractAsync(Tenant, "transcript", Array.Empty<ContactFact>());

        result.Should().ContainSingle();
        result![0].Op.Should().Be("add");
        result[0].Category.Should().Be("preference");
    }

    [Fact]
    public async Task Extract_NoopOps_FilteredOut()
    {
        var json = """{"ops":[{"op":"noop","factId":null,"fact":null,"category":null,"confidence":null}]}""";
        var (extractor, _) = NewExtractor(json);

        var result = await extractor.ExtractAsync(Tenant, "transcript", Array.Empty<ContactFact>());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Extract_UpdateWithKnownFactId_Accepted()
    {
        var existing = new ContactFact(Guid.NewGuid(), "fact cũ", "profile", 0.8m);
        var json = $$"""{"ops":[{"op":"update","factId":"{{existing.Id}}","fact":"fact mới","category":"history","confidence":1.5}]}""";
        var (extractor, _) = NewExtractor(json);

        var result = await extractor.ExtractAsync(Tenant, "transcript", new[] { existing });

        result.Should().ContainSingle();
        result![0].Op.Should().Be("update");
        result[0].Confidence.Should().Be(1.0m); // clamp 0..1
    }

    [Fact]
    public async Task Extract_UpdateWithUnknownFactId_RejectedThenRetryFails()
    {
        // factId lạ => Validate trả null => retry; cả 3 lần đều sai => trả null.
        var bad = $$"""{"ops":[{"op":"update","factId":"{{Guid.NewGuid()}}","fact":"x","category":"profile","confidence":0.5}]}""";
        var (extractor, claude) = NewExtractor(bad, bad, bad);

        var result = await extractor.ExtractAsync(Tenant, "transcript", Array.Empty<ContactFact>());

        result.Should().BeNull();
        await claude.Received(3).CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Extract_UnknownCategory_FallsBackToProfile()
    {
        var json = """{"ops":[{"op":"add","factId":null,"fact":"abc","category":"weird","confidence":0.9}]}""";
        var (extractor, _) = NewExtractor(json);

        var result = await extractor.ExtractAsync(Tenant, "transcript", Array.Empty<ContactFact>());

        result![0].Category.Should().Be("profile");
    }

    [Fact]
    public async Task Extract_AddWithBlankFact_RejectedThenNull()
    {
        var bad = """{"ops":[{"op":"add","factId":null,"fact":"  ","category":"profile","confidence":0.9}]}""";
        var (extractor, _) = NewExtractor(bad, bad, bad);

        var result = await extractor.ExtractAsync(Tenant, "transcript", Array.Empty<ContactFact>());

        result.Should().BeNull();
    }
}
