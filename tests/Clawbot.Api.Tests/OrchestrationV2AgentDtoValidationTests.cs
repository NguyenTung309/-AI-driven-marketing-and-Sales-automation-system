using Clawbot.Api.Endpoints;
using FluentAssertions;
using Xunit;

namespace Clawbot.Api.Tests;

public sealed class OrchestrationV2AgentDtoValidationTests
{
    [Theory]
    [InlineData(null, "[]")]
    [InlineData("", "[]")]
    [InlineData("   ", "[]")]
    [InlineData("""["content.approve"]""", """["content.approve"]""")]
    [InlineData("""["a","b"]""", """["a","b"]""")]
    public void NormalizeAllowedTools_AcceptsArrayOrNull_AndDefaultsToEmptyArray(string? raw, string expected)
    {
        // EARS[WHEN allowedTools is null/blank THE SYSTEM SHALL default to "[]"; WHEN a JSON array THE SYSTEM SHALL accept it]
        OrchestrationV2Endpoints.NormalizeAllowedTools(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData("""{"a":1}""")]     // object, not array
    [InlineData("""{}""")]          // empty object, not array
    [InlineData("not json")]
    [InlineData("[1,2")]            // malformed
    public void NormalizeAllowedTools_RejectsNonArray(string raw)
    {
        // EARS[WHEN allowedTools is not a JSON array THE SYSTEM SHALL reject it (null) so a malformed allow-list cannot be stored]
        OrchestrationV2Endpoints.NormalizeAllowedTools(raw).Should().BeNull();
    }

    [Theory]
    [InlineData(null, "{}")]
    [InlineData("", "{}")]
    [InlineData("""{"type":"object"}""", """{"type":"object"}""")]
    public void NormalizeJsonObject_AcceptsObjectOrNull_AndDefaultsToEmptyObject(string? raw, string expected)
    {
        OrchestrationV2Endpoints.NormalizeJsonObject(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData("""["a"]""")]      // array, not object
    [InlineData("42")]
    [InlineData("\"string\"")]
    public void NormalizeJsonObject_RejectsNonObject(string raw)
    {
        OrchestrationV2Endpoints.NormalizeJsonObject(raw).Should().BeNull();
    }
}
