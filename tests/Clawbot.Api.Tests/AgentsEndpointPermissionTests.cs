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

    // Sandbox đã chuyển sang job nền (AgentSandboxJobHandler) — 2 test dưới soi đúng file đó.
    [Fact]
    public void Sandbox_trace_redacts_user_message_before_persisting()
    {
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Jobs", "AgentSandboxJobHandler.cs"));

        source.Should().Contain("IPiiRedactor pii");
        source.Should().Contain("redactedMessage = (await pii.RedactAsync(payload.Message, ct)");
        source.Should().Contain("AppendTrace(\"sandbox\", agent.DisplayName, \"input\", redactedMessage");
        source.Should().Contain("redactedReply = (await pii.RedactAsync(reply.Text, ct)");
        source.Should().Contain("AppendTrace(\"sandbox\", agent.DisplayName, \"reply\", redactedReply");
    }

    [Fact]
    public void Sandbox_uses_real_llm_client_with_agent_scope()
    {
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Jobs", "AgentSandboxJobHandler.cs"));

        source.Should().Contain("IClaudeChatClient chatClient");
        source.Should().Contain("ILlmCallScope llmScope");
        source.Should().Contain("llmScope.Begin(ctx.TenantId, agent.Code, now)");
        // 5cee084 (agent prompt system) doi bien config.SystemPrompt -> systemPrompt (compose guardrail truoc khi goi)
        source.Should().Contain("chatClient.CompleteAsync(systemPrompt, Array.Empty<ChatTurn>(), redactedMessage, ct)");
        source.Should().NotContain("BuildSandboxReply");
    }

    // Việc tương tác (chạy thử agent) không được bắn thông báo mỗi lần: user đang ngồi nhìn màn hình chờ.
    [Fact]
    public void Sandbox_job_does_not_notify_on_success()
    {
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Jobs", "AgentSandboxJobHandler.cs"));

        source.Should().Contain("public bool NotifyOnSuccess => false;");
    }

    [Fact]
    public void Llm_config_binding_requires_llm_config_manage_permission()
    {
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Endpoints", "AgentsEndpoints.cs"));

        source.Should().Contain("IPermissionResolver permissions");
        source.Should().Contain("llm-configs:manage");
        source.Should().Contain("req.LlmConfigId is not null");
    }

    [Theory]
    [InlineData("anthropic", "claude-opus-4", true)]
    [InlineData("anthropic", "gpt-4o", false)]
    [InlineData("openai", "gpt-4o", true)]
    [InlineData("openai", "claude-opus-4", false)]
    [InlineData("openai-compatible", "deepseek-chat", true)]
    [InlineData("openai-compatible", "claude-opus-4", false)]
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
