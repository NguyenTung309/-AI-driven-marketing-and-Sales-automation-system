using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Rag;
using Clawbot.Agents.Core.Research;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Clawbot.Agents.Tests.Research;

public sealed class SemanticLlmTrendScorerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static readonly IReadOnlyList<RawTrend> Trends =
    [
        new RawTrend("HSK 5 vocabulary", "google_trends", "2K+", 2_000d, []),
        new RawTrend("vietlott mega 6 45", "google_trends", "20K+", 20_000d, []),
    ];

    private static IClaudeChatClient ClaudeReturning(string text)
    {
        var claude = Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeReply(text, 100, 50, 0.001m));
        return claude;
    }

    private static IRagRetriever RagReturning(float score)
    {
        var rag = Substitute.For<IRagRetriever>();
        rag.RetrieveAsync(Arg.Any<RagRequest>(), Arg.Any<CancellationToken>())
            .Returns([new RagChunk("v1", "hsk", "snippet", score)]);
        return rag;
    }

    [Fact]
    public async Task Llm_verdicts_filter_irrelevant_trends_and_carry_ideas()
    {
        const string json = """
            [
              {"i":0,"relevant":true,"score":9,"ideas":["Meo hoc tu vung HSK 5","Video 60s ve HSK 5"]},
              {"i":1,"relevant":false,"score":0,"ideas":[]}
            ]
            """;
        var scorer = new SemanticLlmTrendScorer(RagReturning(0.8f), ClaudeReturning(json));

        var scored = await scorer.ScoreAsync(TenantId, Trends, ["hsk"], CancellationToken.None);

        scored.Should().ContainSingle();
        scored[0].Topic.Should().Be("HSK 5 vocabulary");
        scored[0].RelevanceScore.Should().BeGreaterThan(90d); // 9*10 + sim*5
        scored[0].ContentIdeas.Should().HaveCount(2);
        scored[0].ContentIdeas[0].Should().Contain("HSK 5");
    }

    [Fact]
    public async Task Fenced_json_is_parsed()
    {
        const string fenced = "```json\n[{\"i\":0,\"relevant\":true,\"score\":7,\"ideas\":[\"y tuong\"]}]\n```";
        var scorer = new SemanticLlmTrendScorer(RagReturning(0.5f), ClaudeReturning(fenced));

        var scored = await scorer.ScoreAsync(TenantId, Trends, ["hsk"], CancellationToken.None);

        scored.Should().ContainSingle();
        scored[0].RelevanceScore.Should().BeApproximately(72.5d, 0.01d);
    }

    [Fact]
    public async Task Missing_llm_falls_back_to_keyword_heuristic()
    {
        var scorer = new SemanticLlmTrendScorer(rag: null, claude: null);

        var scored = await scorer.ScoreAsync(TenantId, Trends, ["hsk"], CancellationToken.None);

        // Heuristic giu ca hai trend, trend khop keyword xep tren
        scored.Should().HaveCount(2);
        var hsk = scored.Single(t => t.Topic.StartsWith("HSK", StringComparison.Ordinal));
        var lotto = scored.Single(t => t.Topic.StartsWith("vietlott", StringComparison.Ordinal));
        hsk.RelevanceScore.Should().BeGreaterThan(lotto.RelevanceScore);
    }

    [Fact]
    public async Task Llm_failure_falls_back_to_keyword_heuristic()
    {
        var claude = Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<ClaudeReply>>(_ => throw new InvalidOperationException("llm_config_not_configured"));
        var scorer = new SemanticLlmTrendScorer(RagReturning(0.5f), claude);

        var scored = await scorer.ScoreAsync(TenantId, Trends, ["hsk"], CancellationToken.None);

        scored.Should().HaveCount(2);
    }

    [Fact]
    public async Task Garbage_llm_output_falls_back_to_keyword_heuristic()
    {
        var scorer = new SemanticLlmTrendScorer(RagReturning(0.5f), ClaudeReturning("xin loi, toi khong the tra JSON"));

        var scored = await scorer.ScoreAsync(TenantId, Trends, ["hsk"], CancellationToken.None);

        scored.Should().HaveCount(2);
    }

    [Fact]
    public async Task Rag_failure_still_scores_via_llm_with_zero_similarity()
    {
        var rag = Substitute.For<IRagRetriever>();
        rag.RetrieveAsync(Arg.Any<RagRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<RagChunk>>>(_ => throw new InvalidOperationException("qdrant down"));
        const string json = """[{"i":0,"relevant":true,"score":8,"ideas":["y tuong"]}]""";
        var scorer = new SemanticLlmTrendScorer(rag, ClaudeReturning(json));

        var scored = await scorer.ScoreAsync(TenantId, Trends, ["hsk"], CancellationToken.None);

        scored.Should().ContainSingle();
        scored[0].RelevanceScore.Should().Be(80d); // sim = 0
    }
}
