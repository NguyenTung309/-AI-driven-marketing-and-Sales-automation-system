using Clawbot.Domain.ChatScenarios;
using FluentAssertions;
using Xunit;

namespace Clawbot.Domain.Tests.ChatScenarios;

// M05 — ChatScenarioMatcher (regex/substring + platform filter) and RecordOutcome EMA.
public sealed class ChatScenarioMatcherTests
{
    private static ChatScenario Scenario(
        string code, string trigger, string platforms = "all", string group = "First") =>
        ChatScenario.Create(Guid.NewGuid(), code, group, trigger, $"resp-{code}", platforms, DateTimeOffset.UtcNow);

    [Fact]
    public void Match_returns_null_for_blank_text()
    {
        var result = ChatScenarioMatcher.Match("  ", null, new[] { Scenario("KB-001", "giá") });

        result.Should().BeNull();
    }

    [Fact]
    public void Match_returns_null_when_no_candidate_triggers()
    {
        var result = ChatScenarioMatcher.Match("nội dung không liên quan", null, new[] { Scenario("KB-001", "giá") });

        result.Should().BeNull();
    }

    [Fact]
    public void Match_finds_scenario_by_regex_trigger_case_insensitive()
    {
        var scenarios = new[]
        {
            Scenario("KB-019", "(?i)(giá|học phí|bao nhiêu tiền)"),
            Scenario("KB-001", "(?i)(xin chào|hello)"),
        };

        var result = ChatScenarioMatcher.Match("Cho mình hỏi HỌC PHÍ khoá HSK3 nhé", null, scenarios);

        result!.Code.Should().Be("KB-019");
    }

    [Fact]
    public void Match_prefers_more_specific_longer_regex_hit()
    {
        var scenarios = new[]
        {
            Scenario("KB-broad", "(?i)học"),
            Scenario("KB-specific", "(?i)học phí bao nhiêu"),
        };

        var result = ChatScenarioMatcher.Match("cho hỏi học phí bao nhiêu vậy shop", null, scenarios);

        result!.Code.Should().Be("KB-specific");
    }

    [Fact]
    public void Match_falls_back_to_substring_when_trigger_is_not_valid_regex()
    {
        // Unbalanced parenthesis -> invalid regex -> substring containment path.
        var scenario = Scenario("KB-x", "khoá (HSK3");

        var result = ChatScenarioMatcher.Match("mình muốn đăng ký khoá (HSK3 ạ", null, new[] { scenario });

        result!.Code.Should().Be("KB-x");
    }

    [Theory]
    [InlineData("facebook", "KB-040")]
    [InlineData("zalo", null)]
    public void Match_respects_platform_filter(string platform, string? expectedCode)
    {
        var scenarios = new[] { Scenario("KB-040", "(?i)giá", platforms: "facebook,instagram") };

        var result = ChatScenarioMatcher.Match("giá khoá học?", platform, scenarios);

        if (expectedCode is null)
            result.Should().BeNull();
        else
            result!.Code.Should().Be(expectedCode);
    }

    [Fact]
    public void Match_allows_all_platform_scenarios_regardless_of_requested_platform()
    {
        var scenarios = new[] { Scenario("KB-001", "(?i)giá", platforms: "all") };

        var result = ChatScenarioMatcher.Match("giá nhiêu?", "tiktok", scenarios);

        result!.Code.Should().Be("KB-001");
    }

    [Fact]
    public void Match_breaks_ties_by_higher_success_rate()
    {
        var lowRate = Scenario("KB-low", "(?i)giá");
        var highRate = Scenario("KB-high", "(?i)giá");
        highRate.RecordOutcome(converted: true, DateTimeOffset.UtcNow); // seeds success_rate to 1.0

        // Same trigger length -> same base score; higher success rate should win.
        var result = ChatScenarioMatcher.Match("giá?", null, new[] { lowRate, highRate });

        result!.Code.Should().Be("KB-high");
    }

    [Fact]
    public void RecordOutcome_seeds_rate_on_first_sample_then_emas()
    {
        var s = Scenario("KB-1", "(?i)giá");

        s.RecordOutcome(converted: true, DateTimeOffset.UtcNow);
        s.SuccessRate.Should().Be(1.0m);

        // EMA with alpha 0.1: 1.0 + 0.1*(0 - 1.0) = 0.9
        s.RecordOutcome(converted: false, DateTimeOffset.UtcNow);
        s.SuccessRate.Should().Be(0.9m);
    }

    [Fact]
    public void Update_replaces_editable_fields_and_keeps_code()
    {
        var at = new DateTimeOffset(2026, 6, 3, 10, 0, 0, TimeSpan.Zero);
        var s = Scenario("KB-1", "(?i)old");

        s.Update("Objection", "(?i)new", "new-resp", "zalo", "empathetic", at);

        s.Code.Should().Be("KB-1");
        s.GroupName.Should().Be("Objection");
        s.TriggerText.Should().Be("(?i)new");
        s.ResponseTemplate.Should().Be("new-resp");
        s.Platforms.Should().Be("zalo");
        s.ToneVoice.Should().Be("empathetic");
        s.UpdatedAt.Should().Be(at);
    }
}
