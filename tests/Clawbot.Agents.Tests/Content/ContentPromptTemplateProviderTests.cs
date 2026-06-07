using Clawbot.Agents.Core.Content;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Clawbot.Agents.Tests.Content;

public sealed class ContentPromptTemplateProviderTests
{
    [Fact]
    public void Returns_template_from_configuration_by_platform()
    {
        var provider = Build(new Dictionary<string, string>
        {
            ["tiktok"] = "configured tiktok template",
        });

        var template = provider.GetTemplate("TikTok");

        template.Should().Be("configured tiktok template");
    }

    [Fact]
    public void Missing_template_throws_clear_exception()
    {
        var provider = Build(new Dictionary<string, string>
        {
            ["facebook"] = "configured facebook template",
        });

        var act = () => provider.GetTemplate("zalo");

        act.Should().Throw<ContentPromptTemplateException>()
            .WithMessage("*zalo*");
    }

    [Fact]
    public void Blank_platform_throws()
    {
        var provider = Build(new Dictionary<string, string>());

        var act = () => provider.GetTemplate(" ");

        act.Should().Throw<ArgumentException>();
    }

    private static ConfigPromptTemplateProvider Build(Dictionary<string, string> templates) =>
        new(Options.Create(new ContentPromptTemplateOptions { Templates = templates }));
}
