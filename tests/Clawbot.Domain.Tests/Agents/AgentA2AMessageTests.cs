using Clawbot.Domain.Agents;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Agents;

public sealed class AgentA2AMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly Guid FromAgent = Guid.NewGuid();
    private static readonly Guid ToAgent = Guid.NewGuid();

    [Fact]
    public void Send_SetsAllFields()
    {
        var msg = AgentA2AMessage.Send(TenantId, SessionId, FromAgent, ToAgent, "task-1", "Analyze", "{\"x\":1}", Now);

        msg.TenantId.Should().Be(TenantId);
        msg.SessionId.Should().Be(SessionId);
        msg.FromAgentDefinitionId.Should().Be(FromAgent);
        msg.ToAgentDefinitionId.Should().Be(ToAgent);
        msg.TaskId.Should().Be("task-1");
        msg.Intent.Should().Be("analyze");
        msg.PayloadJson.Should().Be("{\"x\":1}");
        msg.Status.Should().Be("pending");
        msg.Error.Should().BeNull();
        msg.ProcessedAt.Should().BeNull();
    }

    [Fact]
    public void Send_NormalizesIntentToLower()
    {
        var msg = AgentA2AMessage.Send(TenantId, SessionId, null, ToAgent, "t", "REVIEW", "{}", Now);

        msg.Intent.Should().Be("review");
        msg.FromAgentDefinitionId.Should().BeNull();
    }

    [Fact]
    public void Send_DefaultsBlankPayloadToJsonObject()
    {
        var msg = AgentA2AMessage.Send(TenantId, SessionId, null, ToAgent, "t", "do", "", Now);

        msg.PayloadJson.Should().Be("{}");
    }

    [Fact]
    public void Claim_TransitionsPendingToProcessing()
    {
        var msg = AgentA2AMessage.Send(TenantId, SessionId, null, ToAgent, "t", "do", "{}", Now);

        msg.Claim(Now.AddSeconds(5));

        msg.Status.Should().Be("processing");
        msg.ProcessedAt.Should().Be(Now.AddSeconds(5));
        msg.Error.Should().BeNull();
    }

    [Fact]
    public void Claim_ThrowsWhenNotPending()
    {
        var msg = AgentA2AMessage.Send(TenantId, SessionId, null, ToAgent, "t", "do", "{}", Now);
        msg.Claim(Now);

        var act = () => msg.Claim(Now.AddSeconds(1));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Complete_TransitionsProcessingToCompleted()
    {
        var msg = AgentA2AMessage.Send(TenantId, SessionId, null, ToAgent, "t", "do", "{}", Now);
        msg.Claim(Now);

        msg.Complete("{\"result\":\"ok\"}", Now.AddSeconds(10));

        msg.Status.Should().Be("completed");
        msg.PayloadJson.Should().Be("{\"result\":\"ok\"}");
        msg.ProcessedAt.Should().Be(Now.AddSeconds(10));
    }

    [Fact]
    public void Complete_DefaultsBlankPayloadToJson()
    {
        var msg = AgentA2AMessage.Send(TenantId, SessionId, null, ToAgent, "t", "do", "{}", Now);
        msg.Claim(Now);

        msg.Complete("", Now.AddSeconds(10));

        msg.PayloadJson.Should().Be("{}");
    }

    [Fact]
    public void Complete_ThrowsWhenNotProcessing()
    {
        var msg = AgentA2AMessage.Send(TenantId, SessionId, null, ToAgent, "t", "do", "{}", Now);

        var act = () => msg.Complete("{}", Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Fail_TransitionsToFailed()
    {
        var msg = AgentA2AMessage.Send(TenantId, SessionId, null, ToAgent, "t", "do", "{}", Now);
        msg.Claim(Now);

        msg.Fail("timeout error", Now.AddSeconds(10));

        msg.Status.Should().Be("failed");
        msg.Error.Should().Be("timeout error");
        msg.ProcessedAt.Should().Be(Now.AddSeconds(10));
    }

    [Fact]
    public void Fail_ThrowsOnCompletedMessage()
    {
        var msg = AgentA2AMessage.Send(TenantId, SessionId, null, ToAgent, "t", "do", "{}", Now);
        msg.Claim(Now);
        msg.Complete("{}", Now.AddSeconds(5));

        var act = () => msg.Fail("err", Now.AddSeconds(10));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Cancel_TransitionsToCancelled()
    {
        var msg = AgentA2AMessage.Send(TenantId, SessionId, null, ToAgent, "t", "do", "{}", Now);

        msg.Cancel(Now.AddSeconds(5));

        msg.Status.Should().Be("cancelled");
        msg.ProcessedAt.Should().Be(Now.AddSeconds(5));
    }

    [Fact]
    public void Cancel_ThrowsOnCompletedMessage()
    {
        var msg = AgentA2AMessage.Send(TenantId, SessionId, null, ToAgent, "t", "do", "{}", Now);
        msg.Claim(Now);
        msg.Complete("{}", Now.AddSeconds(5));

        var act = () => msg.Cancel(Now.AddSeconds(10));

        act.Should().Throw<InvalidOperationException>();
    }
}
