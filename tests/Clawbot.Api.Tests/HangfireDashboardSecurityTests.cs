using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class HangfireDashboardSecurityTests
{
    [Fact]
    public void Hangfire_dashboard_is_mounted_with_admin_only_authorization()
    {
        var program = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Program.cs"));
        var filter = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Auth", "HangfireAdminFilter.cs"));

        program.Should().Contain("MapHangfireDashboard(\"/hangfire\"");
        program.Should().Contain("Authorization = [new HangfireAdminFilter()]");

        filter.Should().Contain("HasClaim(\"perm\", \"admin.system\")");
        filter.Should().Contain("IsInRole(\"Admin\")");
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
