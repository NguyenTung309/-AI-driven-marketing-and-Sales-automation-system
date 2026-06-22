using Clawbot.Api.Endpoints;
using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class AgentsEndpointPermissionTests
{
    [Fact]
    public void Mutating_agent_routes_require_agent_manage_permission()
    {
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Endpoints", "AgentsEndpoints.cs"));

        source.Should().Contain("grp.MapPost(\"/{code}/enable\", EnableAsync).RequirePermission(\"agent.manage\")");
        source.Should().Contain("grp.MapPost(\"/{code}/disable\", DisableAsync).RequirePermission(\"agent.manage\")");
        source.Should().Contain("grp.MapPut(\"/{code}/settings\", UpdateSettingsAsync).RequirePermission(\"agent.manage\")");
        source.Should().Contain("grp.MapPost(\"/{code}/sandbox\", SandboxAsync).RequirePermission(\"agent.manage\")");
    }

    [Fact]
    public void Sandbox_trace_redacts_user_message_before_persisting()
    {
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Endpoints", "AgentsEndpoints.cs"));

        source.Should().Contain("IPiiRedactor pii");
        source.Should().Contain("redactedMessage = (await pii.RedactAsync(req.Message.Trim(), ct)");
        source.Should().Contain("AppendTrace(\"sandbox\", agent.DisplayName, \"input\", redactedMessage");
        source.Should().Contain("BuildSandboxReply(agent, config, redactedMessage)");
    }

    [Theory]
    [InlineData("anthropic", "claude-opus-4", true)]
    [InlineData("anthropic", "gpt-4o", false)]
    [InlineData("openai", "gpt-4o", true)]
    [InlineData("openai", "claude-opus-4", false)]
    public void Model_provider_guard_still_blocks_cross_provider_binding(string provider, string model, bool expected)
    {
        AgentsEndpoints.IsModelCompatibleWithProvider(provider, model).Should().Be(expected);
    }

    private static string FindRepoFile(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(segments)}");
    }
}
