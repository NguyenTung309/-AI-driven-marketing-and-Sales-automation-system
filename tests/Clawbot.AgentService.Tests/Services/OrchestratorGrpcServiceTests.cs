using Clawbot.AgentService.Services;
using Clawbot.Agents.Contracts.Orchestrator;
using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Orchestrator;
using FluentAssertions;
using Grpc.Core;
using NSubstitute;

namespace Clawbot.AgentService.Tests.Services;

public sealed class OrchestratorGrpcServiceTests
{
    [Fact]
    public async Task Plan_and_trace_stream_planned_events_for_session()
    {
        var orchestrator = new PlanningOrchestrator(new AgentRegistry([Agent("chat")]));
        var service = new OrchestratorGrpcService(orchestrator);

        var plan = await service.Plan(new PlanRequest
        {
            TenantId = "tenant-1",
            Goal = "chat with learner",
        }, TestServerCallContext.Create());

        plan.Tasks.Should().ContainSingle(task => task.Agent == "chat");

        var stream = new CapturingTraceStream();
        await service.Trace(new TraceRequest { SessionId = plan.SessionId }, stream, TestServerCallContext.Create());

        stream.Messages.Should().ContainSingle();
        stream.Messages[0].Phase.Should().Be("planned");
        stream.Messages[0].Message.Should().Contain("chat");
    }

    private static IAgent Agent(string name)
    {
        var agent = Substitute.For<IAgent>();
        agent.Name.Returns(name);
        return agent;
    }

    private sealed class CapturingTraceStream : IServerStreamWriter<TraceEvent>
    {
        public List<TraceEvent> Messages { get; } = [];
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(TraceEvent message)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }
}
