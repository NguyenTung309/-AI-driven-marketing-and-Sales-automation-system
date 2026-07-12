using FluentAssertions;

namespace Clawbot.Infrastructure.Tests.Architecture;

public sealed class LocalRunDocumentationTests
{
    [Fact]
    public void Install_guide_and_one_click_runner_document_the_local_stack()
    {
        var root = FindRepositoryRoot();
        var guidePath = Path.Combine(root, "HUONG_DAN_CAI_DAT_VA_CHAY.md");
        var runnerPath = Path.Combine(root, "run-all.bat");

        File.Exists(guidePath).Should().BeTrue("the repository should include one Vietnamese install/run guide at the root");
        File.Exists(runnerPath).Should().BeTrue("the repository should include a one-click Windows runner at the root");

        var guide = File.ReadAllText(guidePath);
        var runner = File.ReadAllText(runnerPath);

        guide.Should().Contain("run-all.bat");
        guide.Should().Contain(".NET SDK 8");
        guide.Should().Contain("Node.js 20");
        guide.Should().Contain("Docker Desktop");
        guide.Should().Contain("http://localhost:15876");
        guide.Should().Contain("http://localhost:15873");
        guide.Should().Contain("http://localhost:15874");
        guide.Should().Contain("http://localhost:15875");
        guide.Should().NotContain("http://localhost:5000");
        guide.Should().NotContain("http://localhost:5001");
        guide.Should().Contain("admin@clawbot.local");
        guide.Should().Contain("Admin@12345");
        guide.Should().Contain("tu dong seed default tenant");
        guide.Should().NotContain("repo hien chua co default tenant/user seed");
        guide.Should().Contain("deploy/.env.example");
        guide.Should().Contain("deploy/.env");
        guide.Should().Contain("Docker/Testcontainers");
        guide.Should().Contain("go-live readiness");

        runner.Should().Contain("docker compose");
        runner.Should().Contain("deploy\\docker-compose.yml");
        runner.Should().Contain("deploy\\.env.example");
        runner.Should().Contain("deploy\\.env");
        runner.Should().Contain("dotnet restore");
        runner.Should().Contain("dotnet build");
        runner.Should().Contain("src\\agents\\Clawbot.AgentService\\Clawbot.AgentService.csproj");
        runner.Should().Contain("src\\api\\Clawbot.Api\\Clawbot.Api.csproj");
        runner.Should().Contain("src\\gateway\\Clawbot.Gateway\\Clawbot.Gateway.csproj");
        runner.Should().Contain("src\\frontend\\clawbot-web");
        runner.Should().Contain("npm ci");
        runner.Should().Contain("npm run dev");
        runner.Should().Contain("ASPNETCORE_URLS=http://localhost:15875");
        runner.Should().Contain("ASPNETCORE_URLS=http://localhost:15874");
        runner.Should().Contain("ASPNETCORE_URLS=http://localhost:15873");
        runner.Should().Contain("AgentService__Url=http://localhost:15875");
        runner.Should().Contain("--port 15876");
        runner.Should().NotContain("localhost:5000");
        runner.Should().NotContain("localhost:5001");
        runner.Should().Contain("--dry-run");
        runner.Should().MatchRegex(
            @"(?ms)^:apply_meta_migration\r?$.*?SET QUOTED_IDENTIFIER ON;.*?SET ARITHABORT ON;.*?0055_meta_facebook_login_for_business\.sql");

        var viteConfig = File.ReadAllText(Path.Combine(root, "src", "frontend", "clawbot-web", "vite.config.ts"));
        viteConfig.Should().Contain("port: 15876");
        viteConfig.Should().Contain("\"/api\": {");
        viteConfig.Should().Contain("target: \"http://localhost:15873\"");
        viteConfig.Should().NotContain("localhost:5000");
        viteConfig.Should().NotContain("localhost:5001");
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
