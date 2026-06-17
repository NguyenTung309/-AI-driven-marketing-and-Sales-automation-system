using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class BoundedContextEndpointSkeletonTests
{
    [Fact]
    public void Bounded_context_skeleton_does_not_shadow_implemented_endpoint_groups()
    {
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Endpoints", "BoundedContextEndpoints.cs"));

        source.Should().NotContain("Stub(app, \"/api/inbox\"");
        source.Should().NotContain("Stub(app, \"/api/kb\"");
        source.Should().NotContain("Stub(app, \"/api/kb/accuracy\"");
        source.Should().NotContain("Stub(app, \"/api/agents\"");
        source.Should().NotContain("Stub(app, \"/api/sale-assist\"");
        source.Should().NotContain("Stub(app, \"/api/leads\"");
        source.Should().NotContain("Stub(app, \"/api/docs\"");
        source.Should().NotContain("Stub(app, \"/api/ads\"");
        source.Should().Contain("Stub(app, \"/api/integrations\"");
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

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(segments)}");
    }
}
