using FluentAssertions;

namespace Clawbot.Infrastructure.Tests.Frontend;

public sealed class AgentQualityFrontendTests
{
    [Fact]
    public void Analytics_agent_report_surfaces_per_agent_quality_metrics()
    {
        var root = FindRepositoryRoot();
        var api = File.ReadAllText(Path.Combine(root, "src", "frontend", "clawbot-web", "src", "shared", "api", "analytics.ts"));
        var page = File.ReadAllText(Path.Combine(root, "src", "frontend", "clawbot-web", "src", "features", "analytics", "AnalyticsReportsPage.tsx"));

        api.Should().Contain("qualitySamples");
        api.Should().Contain("passedQualitySamples");
        api.Should().Contain("qualityPassRate");
        api.Should().Contain("averageQualityScore");
        page.Should().Contain("qualityPassRate");
        page.Should().Contain("qualitySamples");
        page.Should().Contain("Chất lượng");
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
