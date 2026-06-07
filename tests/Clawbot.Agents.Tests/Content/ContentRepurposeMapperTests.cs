using Clawbot.Agents.Core.Content;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Content;

public sealed class ContentRepurposeMapperTests
{
    [Fact]
    public void NormalizeTargets_trims_and_deduplicates_platforms_case_insensitively()
    {
        var targets = ContentRepurposeMapper.NormalizeTargets([" tiktok ", "TikTok", "zalo"]);

        targets.Should().Equal("tiktok", "zalo");
    }

    [Fact]
    public void NormalizeTargets_rejects_empty_target_set()
    {
        var act = () => ContentRepurposeMapper.NormalizeTargets([" ", ""]);

        act.Should().Throw<ArgumentException>();
    }
}
