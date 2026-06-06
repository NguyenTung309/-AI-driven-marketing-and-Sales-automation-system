using Clawbot.Api.Endpoints;
using FluentAssertions;
using Xunit;

namespace Clawbot.Api.Tests;

// M04 — UnifiedDiff line-based KB version diff.
public sealed class UnifiedDiffTests
{
    [Fact]
    public void Identical_text_has_no_changes()
    {
        var (added, removed, text) = UnifiedDiff.Compute("a\nb", "a\nb");

        added.Should().Be(0);
        removed.Should().Be(0);
        text.Should().Contain(" a").And.Contain(" b");
    }

    [Fact]
    public void Added_line_counts_as_added()
    {
        var (added, removed, text) = UnifiedDiff.Compute("a", "a\nb");

        added.Should().Be(1);
        removed.Should().Be(0);
        text.Should().Contain("+b");
    }

    [Fact]
    public void Removed_line_counts_as_removed()
    {
        var (added, removed, text) = UnifiedDiff.Compute("a\nb", "a");

        added.Should().Be(0);
        removed.Should().Be(1);
        text.Should().Contain("-b");
    }

    [Fact]
    public void Null_inputs_treated_as_empty()
    {
        var (added, removed, _) = UnifiedDiff.Compute(null!, null!);

        added.Should().Be(0);
        removed.Should().Be(0);
    }

    [Fact]
    public void Normalizes_crlf_to_lf()
    {
        var (added, removed, _) = UnifiedDiff.Compute("a\r\nb", "a\nb");

        added.Should().Be(0);
        removed.Should().Be(0);
    }
}
