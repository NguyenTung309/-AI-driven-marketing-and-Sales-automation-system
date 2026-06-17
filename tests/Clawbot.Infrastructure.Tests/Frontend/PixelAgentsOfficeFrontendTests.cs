using FluentAssertions;

namespace Clawbot.Infrastructure.Tests.Frontend;

public sealed class PixelAgentsOfficeFrontendTests
{
    [Fact]
    public void Frontend_exposes_pixel_agents_office_route_and_nav_entry()
    {
        var root = FindRepositoryRoot();
        var routes = File.ReadAllText(Path.Combine(root, "src", "frontend", "clawbot-web", "src", "app", "routes.tsx"));
        var nav = File.ReadAllText(Path.Combine(root, "src", "frontend", "clawbot-web", "src", "shared", "layout", "nav.ts"));
        var pagePath = Path.Combine(root, "src", "frontend", "clawbot-web", "src", "features", "agents", "PixelAgentsOfficePage.tsx");

        File.Exists(pagePath).Should().BeTrue();

        routes.Should().Contain("PixelAgentsOfficePage");
        routes.Should().Contain("path: \"/agents-office\"");
        nav.Should().Contain("Pixel Agents Office");
        nav.Should().Contain("to: \"/agents-office\"");

        var page = File.ReadAllText(pagePath);
        page.Should().Contain("Pixel Agents Office");
        page.Should().Contain("Agent floor");
        page.Should().Contain("Task queue");
        page.Should().Contain("Trace feed");
        page.Should().Contain("Health");
        page.Should().Contain("refetchInterval");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Clawbot.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
