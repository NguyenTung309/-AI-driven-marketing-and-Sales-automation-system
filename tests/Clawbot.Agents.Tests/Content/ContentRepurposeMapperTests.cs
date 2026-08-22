using Clawbot.Agents.Core.Content;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Content;

public sealed class ContentRepurposeMapperTests
{
    [Fact]
    public void NormalizeTargets_TrimsLowercasesAndDedupes()
    {
        var result = ContentRepurposeMapper.NormalizeTargets([" Facebook ", "facebook", "TIKTOK", "zalo"]);

        result.Should().BeEquivalentTo("facebook", "tiktok", "zalo");
    }

    [Fact]
    public void NormalizeTargets_SkipsBlankEntries()
    {
        var result = ContentRepurposeMapper.NormalizeTargets(["", "  ", "facebook"]);

        result.Should().ContainSingle().Which.Should().Be("facebook");
    }

    [Fact]
    public void NormalizeTargets_AllBlank_Throws()
    {
        var act = () => ContentRepurposeMapper.NormalizeTargets(["", "   "]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*target platforms required*");
    }

    [Fact]
    public void NormalizeTargets_Null_Throws()
    {
        var act = () => ContentRepurposeMapper.NormalizeTargets(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
