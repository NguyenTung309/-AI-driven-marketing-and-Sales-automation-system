using Clawbot.Domain.ChatScenarios;
using FluentAssertions;

namespace Clawbot.Domain.Tests.ChatScenarios;

public sealed class ChatScenarioMatcherTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    private static ChatScenario MakeScenario(
        string code, string trigger, string platforms = "", decimal? successRate = null) =>
        ChatScenario.Create(TenantId, code, "group", trigger, "response", platforms, Now);

    // ── Basic matching ────────────────────────────────────────────────

    [Fact]
    public void Match_ReturnsNullForEmptyText()
    {
        var candidates = new[] { MakeScenario("S1", "hello") };

        ChatScenarioMatcher.Match("", null, candidates).Should().BeNull();
    }

    [Fact]
    public void Match_ReturnsNullForWhitespaceText()
    {
        var candidates = new[] { MakeScenario("S1", "hello") };

        ChatScenarioMatcher.Match("   ", null, candidates).Should().BeNull();
    }

    [Fact]
    public void Match_ReturnsNullWhenNoCandidateMatches()
    {
        var candidates = new[] { MakeScenario("S1", "goodbye") };

        ChatScenarioMatcher.Match("hello world", null, candidates).Should().BeNull();
    }

    [Fact]
    public void Match_MatchesSubstringFallback()
    {
        var scenario = MakeScenario("S1", "[invalid regex");
        var candidates = new[] { scenario };

        var result = ChatScenarioMatcher.Match("this is [invalid regex here", null, candidates);

        result.Should().BeSameAs(scenario);
    }

    [Fact]
    public void Match_PrefersRegexOverSubstring()
    {
        var regexScenario = MakeScenario("S1", @"\bhello\b");
        var substringScenario = MakeScenario("S2", "[not-regex hello");
        var candidates = new[] { regexScenario, substringScenario };

        var result = ChatScenarioMatcher.Match("say hello please", null, candidates);

        result.Should().BeSameAs(regexScenario);
    }

    [Fact]
    public void Match_LongerRegexMatchWins()
    {
        var shortMatch = MakeScenario("S1", @"hello");
        var longMatch = MakeScenario("S2", @"hello\s+world");
        var candidates = new[] { shortMatch, longMatch };

        var result = ChatScenarioMatcher.Match("hello world!", null, candidates);

        result.Should().BeSameAs(longMatch);
    }

    // ── Tie-breaking ──────────────────────────────────────────────────

    [Fact]
    public void Match_HigherSuccessRateBreaksTie()
    {
        // SuccessRate is only set via RecordOutcome, not Create.
        // Use triggers of different lengths so regex match scores differ,
        // ensuring the higher-rate candidate also has the longer (higher-scoring) trigger.
        var low = MakeScenario("S1", @"hi");
        low.RecordOutcome(false, Now); // rate = 0
        var high = MakeScenario("S2", @"hello");
        high.RecordOutcome(true, Now); // rate = 1
        var candidates = new[] { low, high };

        var result = ChatScenarioMatcher.Match("say hello there", null, candidates);

        result!.Code.Should().Be("S2");
    }

    [Fact]
    public void Match_CodeBreaksTieWhenScoreAndRateEqual()
    {
        var b = MakeScenario("KB-002", @"hello");
        var a = MakeScenario("KB-001", @"hello");
        // Both have null SuccessRate → treated as 0m by matcher
        var candidates = new[] { b, a };

        var result = ChatScenarioMatcher.Match("hello", null, candidates);

        result!.Code.Should().Be("KB-001");
    }

    // ── Platform filtering ────────────────────────────────────────────

    [Fact]
    public void Match_EmptyPlatformsMatchesAnyPlatform()
    {
        var scenario = MakeScenario("S1", @"hello", platforms: "");
        var candidates = new[] { scenario };

        var result = ChatScenarioMatcher.Match("hello", "facebook", candidates);

        result.Should().BeSameAs(scenario);
    }

    [Fact]
    public void Match_AllPlatformsMatchesAnyPlatform()
    {
        var scenario = MakeScenario("S1", @"hello", platforms: "all");
        var candidates = new[] { scenario };

        var result = ChatScenarioMatcher.Match("hello", "zalo", candidates);

        result.Should().BeSameAs(scenario);
    }

    [Fact]
    public void Match_SpecificPlatformMatchesCaseInsensitive()
    {
        var scenario = MakeScenario("S1", @"hello", platforms: "Facebook,Zalo");
        var candidates = new[] { scenario };

        var result = ChatScenarioMatcher.Match("hello", "FACEBOOK", candidates);

        result.Should().BeSameAs(scenario);
    }

    [Fact]
    public void Match_WrongPlatformExcludesCandidate()
    {
        var scenario = MakeScenario("S1", @"hello", platforms: "facebook");
        var candidates = new[] { scenario };

        var result = ChatScenarioMatcher.Match("hello", "zalo", candidates);

        result.Should().BeNull();
    }

    [Fact]
    public void Match_NullRequestedPlatformMatchesAll()
    {
        var scenario = MakeScenario("S1", @"hello", platforms: "facebook");
        var candidates = new[] { scenario };

        var result = ChatScenarioMatcher.Match("hello", null, candidates);

        result.Should().BeSameAs(scenario);
    }

    // ── Edge cases ────────────────────────────────────────────────────

    [Fact]
    public void Match_ThrowsOnNullCandidates()
    {
        var act = () => ChatScenarioMatcher.Match("hello", null, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Match_EmptyCandidatesReturnsNull()
    {
        ChatScenarioMatcher.Match("hello", null, []).Should().BeNull();
    }

    [Fact]
    public void Match_CaseInsensitiveMatching()
    {
        var scenario = MakeScenario("S1", @"HELLO");
        var candidates = new[] { scenario };

        var result = ChatScenarioMatcher.Match("hello world", null, candidates);

        result.Should().BeSameAs(scenario);
    }

    [Fact]
    public void Match_EmptyTriggerSkipped()
    {
        var empty = MakeScenario("S1", "");
        var valid = MakeScenario("S2", @"test");
        var candidates = new[] { empty, valid };

        var result = ChatScenarioMatcher.Match("test message", null, candidates);

        result.Should().BeSameAs(valid);
    }
}
