using FluentAssertions;

namespace Clawbot.Infrastructure.Tests.Deployment;

public sealed class MultiRegionReplicationArtifactsTests
{
    [Fact]
    public void Multi_region_replication_has_runbook_config_and_checklist_trace()
    {
        var root = FindRepositoryRoot();
        var runbookPath = Path.Combine(root, "deploy", "multi-region", "README.md");
        var appsettings = File.ReadAllText(Path.Combine(root, "src", "api", "Clawbot.Api", "appsettings.json"));
        var checklist = File.ReadAllText(Path.Combine(root, "docs", "module-checklist.md"));

        File.Exists(runbookPath).Should().BeTrue("ops need a concrete failover and replica-lag runbook");
        var runbook = File.ReadAllText(runbookPath);
        runbook.Should().Contain("/health/replication");
        runbook.Should().Contain("Deployment__Replication__CurrentRegion");
        runbook.Should().Contain("Deployment__Replication__LagProbeSql");
        runbook.Should().Contain("failover");

        appsettings.Should().Contain("\"Deployment\"");
        appsettings.Should().Contain("\"Replication\"");
        appsettings.Should().Contain("\"MaxReplicaLagSeconds\"");

        checklist.Should().Contain("[x] Multi-region replication");
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
