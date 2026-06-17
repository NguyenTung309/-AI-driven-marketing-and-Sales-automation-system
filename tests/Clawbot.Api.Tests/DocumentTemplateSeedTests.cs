using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class DocumentTemplateSeedTests
{
    [Fact]
    public void Document_template_seed_contains_quote_onboarding_brochure_and_slide_templates()
    {
        var sql = File.ReadAllText(FindRepoFile("deploy", "seed", "document-templates.sql"));

        sql.Should().Contain("(N'QUOTE-V1', N'quote'");
        sql.Should().Contain("(N'ONBOARDING-KIT', N'onboarding'");
        sql.Should().Contain("(N'BROCHURE-HSK', N'brochure'");
        sql.Should().Contain("(N'SLIDE-DEMO-5', N'slide'");
    }

    private static string FindRepoFile(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate repo file.", Path.Combine(segments));
    }
}
