using Clawbot.Agents.Contracts.Orchestrator;
using Clawbot.Agents.Core.Orchestrator;
using Google.Protobuf.WellKnownTypes;
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

    public override async Task Trace(TraceRequest request, IServerStreamWriter<TraceEvent> responseStream, ServerCallContext context)
    {
        var traces = orchestrator.GetTrace(request.SessionId);
        if (traces.Count == 0)
        {
            await responseStream.WriteAsync(new TraceEvent
            {
                Phase = "missing",
                Message = $"No orchestrator trace found for session {request.SessionId}.",
                At = Timestamp.FromDateTime(DateTime.UtcNow),
            });
            return;
        }

        foreach (var trace in traces)
        {
            await responseStream.WriteAsync(new TraceEvent
            {
                TaskId = trace.TaskId,
                Phase = trace.Phase,
                Message = trace.Message,
                At = Timestamp.FromDateTime(trace.At.UtcDateTime),
            });
        }
    }
}
