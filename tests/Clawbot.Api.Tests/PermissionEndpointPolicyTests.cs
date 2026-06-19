using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class PermissionEndpointPolicyTests
{
    [Fact]
    public void Endpoints_do_not_use_removed_perm_authorization_policies()
    {
        var endpointsDir = FindRepoDirectory("src", "api", "Clawbot.Api", "Endpoints");
        var offenders = Directory
            .EnumerateFiles(endpointsDir, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => new { File = Path.GetRelativePath(endpointsDir, file), Line = index + 1, Text = line }))
            .Where(row => row.Text.Contains("RequireAuthorization(\"perm:", StringComparison.Ordinal))
            .Select(row => $"{row.File}:{row.Line}: {row.Text.Trim()}")
            .ToArray();

        offenders.Should().BeEmpty("permission endpoints must use RequirePermission so runtime role_id permissions are resolved dynamically");
    }

    private static string FindRepoDirectory(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate repository directory: {Path.Combine(segments)}");
    }
}
