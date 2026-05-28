using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Agents.Core.Skills;
using Clawbot.AgentService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddSingleton<AgentRegistry>(_ => new AgentRegistry(Array.Empty<IAgent>()));
builder.Services.AddSingleton<PlanningOrchestrator>();
builder.Services.AddClawbotSkills();

var app = builder.Build();

app.MapGrpcService<OrchestratorGrpcService>();
app.MapGrpcService<ChatAgentGrpcService>();
app.MapGrpcService<ContentAgentGrpcService>();
app.MapGrpcService<LeadAgentGrpcService>();
app.MapGrpcService<SaleAssistAgentGrpcService>();
app.MapGrpcService<DocsAgentGrpcService>();
app.MapGrpcService<AdsAgentGrpcService>();
app.MapGrpcService<ReportAgentGrpcService>();
app.MapGrpcService<ResearchAgentGrpcService>();
app.MapGet("/", () => "ClawBot Agent Service — use a gRPC client to call services.");

app.Run();
