using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Ads;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Content;
using Clawbot.Agents.Core.Docs;
using Clawbot.Agents.Core.Lead;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Agents.Core.Rag;
using Clawbot.Agents.Core.Research;
using Clawbot.Agents.Core.SaleAssist;
using Clawbot.Agents.Core.Skills;
using Clawbot.AgentService.Services;
using Clawbot.Application;
using Clawbot.Infrastructure;
using Clawbot.Infrastructure.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddClawbotTelemetry(builder.Configuration, "clawbot-agent");
builder.Services.AddSingleton<AgentRegistry>(_ => new AgentRegistry(Array.Empty<IAgent>()));
builder.Services.AddSingleton<PlanningOrchestrator>();
builder.Services.AddClawbotSkills(builder.Configuration);
builder.Services.AddClawbotRag(builder.Configuration);
builder.Services.AddClawbotChat(builder.Configuration);
builder.Services.AddClawbotContent(builder.Configuration);
builder.Services.AddClawbotResearch(builder.Configuration);
builder.Services.AddClawbotSaleAssist();
builder.Services.AddClawbotLead();
builder.Services.AddClawbotDocs(builder.Configuration);

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
