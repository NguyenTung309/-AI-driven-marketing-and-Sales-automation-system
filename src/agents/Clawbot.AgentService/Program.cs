using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.AgentService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddSingleton<AgentRegistry>(_ => new AgentRegistry(Array.Empty<IAgent>()));
builder.Services.AddSingleton<PlanningOrchestrator>();

var app = builder.Build();

app.MapGrpcService<OrchestratorGrpcService>();
app.MapGrpcService<ChatAgentGrpcService>();
app.MapGrpcService<ContentAgentGrpcService>();
app.MapGrpcService<LeadAgentGrpcService>();
app.MapGet("/", () => "ClawBot Agent Service — use a gRPC client to call services.");

app.Run();
