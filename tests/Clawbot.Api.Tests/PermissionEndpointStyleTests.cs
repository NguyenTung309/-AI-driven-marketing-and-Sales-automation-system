namespace Clawbot.Api.Tests;

public sealed class PermissionEndpointStyleTests
{
    [Fact]
    public void RequirePermission_preserves_api_key_perm_claims()
    {
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Auth", "PermissionEndpointExtensions.cs"));

        Assert.Contains("http.User.HasClaim(\"perm\", code)", source);
    }

    [Fact]
    public void Endpoints_do_not_use_removed_perm_policies()
    {
        var endpointsDir = FindRepoDir("src", "api", "Clawbot.Api", "Endpoints");
        var offenders = Directory.GetFiles(endpointsDir, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("RequireAuthorization(\"perm:"))
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Empty(offenders);
    }

    private static string FindRepoDir(params string[] segments) =>
        FindRepoPath(segments, Directory.Exists, "directory");

    private static string FindRepoFile(params string[] segments) =>
        FindRepoPath(segments, File.Exists, "file");

    private static string FindRepoPath(string[] segments, Func<string, bool> exists, string kind)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
            if (exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository {kind}: {Path.Combine(segments)}");
    }
}
