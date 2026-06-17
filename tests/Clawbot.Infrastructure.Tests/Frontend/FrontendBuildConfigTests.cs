using FluentAssertions;

namespace Clawbot.Infrastructure.Tests.Frontend;

public sealed class FrontendBuildConfigTests
{
    [Fact]
    public void Vite_build_suppresses_only_invalid_pure_annotation_noise()
    {
        var root = FindRepositoryRoot();
        var config = File.ReadAllText(Path.Combine(root, "src", "frontend", "clawbot-web", "vite.config.ts"));

        config.Should().Contain("rolldownOptions");
        config.Should().Contain("checks");
        config.Should().Contain("invalidAnnotation: false");
        config.Should().NotContain("logLevel: \"silent\"");
        config.Should().NotContain("logLevel: 'silent'");
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

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
