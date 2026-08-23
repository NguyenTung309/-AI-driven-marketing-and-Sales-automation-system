using Clawbot.Agents.Core.Skills.Content;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Skills;

// Parser JSON prompt ảnh: prompt/negative_prompt + hints (composition/lighting/mood), tolerant.
public sealed class ClaudeImagePromptGeneratorTests
{
    [Fact]
    public void ParseResult_WellFormed_ExtractsPromptAndHints()
    {
        var json = """{"prompt":"a serene classroom","negative_prompt":"blurry, low quality","hints":{"composition":"rule of thirds","lighting":"soft","mood":"calm"}}""";

        var result = ClaudeImagePromptGenerator.ParseResult(json);

        result.Prompt.Should().Be("a serene classroom");
        result.NegativePrompt.Should().Be("blurry, low quality");
        result.Hints.Should().ContainKey("composition").WhoseValue.Should().Be("rule of thirds");
        result.Hints.Should().ContainKey("lighting").WhoseValue.Should().Be("soft");
        result.Hints.Should().ContainKey("mood").WhoseValue.Should().Be("calm");
    }

    [Fact]
    public void ParseResult_MissingNegative_DefaultsEmpty()
    {
        var json = """{"prompt":"just a prompt"}""";

        var result = ClaudeImagePromptGenerator.ParseResult(json);

        result.Prompt.Should().Be("just a prompt");
        result.NegativePrompt.Should().BeEmpty();
        result.Hints.Should().BeEmpty();
    }

    [Fact]
    public void ParseResult_NoPromptField_FallsBackToWholeText()
    {
        var result = ClaudeImagePromptGenerator.ParseResult("  raw fallback  ");

        result.Prompt.Should().Be("raw fallback");
    }

    [Fact]
    public void ParseResult_HintsKeysAreCaseInsensitive()
    {
        var json = """{"prompt":"p","negative_prompt":"n","hints":{"Composition":"centered"}}""";

        var result = ClaudeImagePromptGenerator.ParseResult(json);

        result.Hints.Should().ContainKey("composition");
    }
}
