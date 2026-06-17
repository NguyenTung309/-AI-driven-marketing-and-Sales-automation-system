using System.Text.RegularExpressions;
using FluentAssertions;

namespace Clawbot.AgentService.Tests;

public sealed partial class PythonAgentServiceScaffoldTests
{
    [Fact]
    public void Python_agent_service_scaffold_tracks_all_grpc_proto_services()
    {
        var root = FindRepositoryRoot();
        var serviceDir = Path.Combine(root, "src", "agents", "Clawbot.PythonAgentService");
        var mainPath = Path.Combine(serviceDir, "app", "main.py");
        var requirementsPath = Path.Combine(serviceDir, "requirements.txt");
        var dockerfilePath = Path.Combine(serviceDir, "Dockerfile");
        var readmePath = Path.Combine(serviceDir, "README.md");

        File.Exists(mainPath).Should().BeTrue();
        File.Exists(requirementsPath).Should().BeTrue();
        File.Exists(dockerfilePath).Should().BeTrue();
        File.Exists(readmePath).Should().BeTrue();

        var services = Directory.GetFiles(Path.Combine(root, "proto"), "*.proto")
            .SelectMany(ReadServiceNames)
            .Order(StringComparer.Ordinal)
            .ToArray();

        services.Should().HaveCount(9);

        var main = File.ReadAllText(mainPath);
        main.Should().Contain("grpc.server");
        main.Should().Contain("register_proto_services");
        main.Should().Contain("add_{service.name}Servicer_to_server");
        main.Should().Contain("parse_proto_services");

        var requirements = File.ReadAllText(requirementsPath);
        requirements.Should().Contain("grpcio");
        requirements.Should().Contain("grpcio-tools");
        requirements.Should().Contain("grpcio-health-checking");

        var dockerfile = File.ReadAllText(dockerfilePath);
        dockerfile.Should().Contain("COPY proto /proto");
        dockerfile.Should().Contain("CLAWBOT_PROTO_ROOT=/proto");
        dockerfile.Should().Contain("EXPOSE 5050");

        var readme = File.ReadAllText(readmePath);
        foreach (var service in services)
        {
            readme.Should().Contain(service);
        }
    }

    private static IEnumerable<string> ReadServiceNames(string protoPath)
    {
        return File.ReadLines(protoPath)
            .Select(line => ServiceDeclarationRegex().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["name"].Value);
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

    [GeneratedRegex(@"^\s*service\s+(?<name>[A-Za-z0-9_]+)\s*\{")]
    private static partial Regex ServiceDeclarationRegex();
}
