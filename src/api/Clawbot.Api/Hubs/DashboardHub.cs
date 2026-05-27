using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Clawbot.Api.Hubs;

[Authorize]
public sealed class DashboardHub : Hub
{
    // TODO(student): emit orchestrator plan updates, conversation events, agent traces.
}
