using Clawbot.Agents.Contracts.Orchestrator;
using Clawbot.Agents.Core.Orchestrator;
using Grpc.Core;

namespace Clawbot.AgentService.Services;

public sealed class OrchestratorGrpcService(PlanningOrchestrator orchestrator) : Orchestrator.OrchestratorBase
{
    public override Task<PlanResponse> Plan(PlanRequest request, ServerCallContext context)
    {
        var plan = orchestrator.Plan(request.TenantId, request.Goal);
        var response = new PlanResponse { SessionId = plan.SessionId };
        response.Tasks.AddRange(plan.Tasks.Select(t => new PlannedTask
        {
            Id = t.Id,
            Agent = t.AgentName,
            Description = t.Description,
        }));
        return Task.FromResult(response);
    }

    public override Task Trace(TraceRequest request, IServerStreamWriter<TraceEvent> responseStream, ServerCallContext context)
    {
        // TODO(student): stream live trace events from the orchestrator.
        _ = request;
        return Task.CompletedTask;
    }
}
