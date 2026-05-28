using Clawbot.Agents.Contracts.Report;
using Grpc.Core;

namespace Clawbot.AgentService.Services;

public sealed class ReportAgentGrpcService : ReportAgent.ReportAgentBase
{
    public override Task<DailySnapshotResponse> DailySnapshot(DailySnapshotRequest request, ServerCallContext context)
    {
        _ = request;
        // TODO(student): aggregate messages/leads/conversions per platform for the date, write kpi_daily, return rows.
        return Task.FromResult(new DailySnapshotResponse());
    }
}
