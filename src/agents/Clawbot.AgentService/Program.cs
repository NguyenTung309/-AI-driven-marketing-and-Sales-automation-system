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
using Clawbot.Infrastructure.Documents;
using Clawbot.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddClawbotTelemetry(builder.Configuration, "clawbot-agent");
builder.Services.AddSingleton<AgentRegistry>(_ => DefaultAgentRegistry.Create());
builder.Services.AddSingleton<PlanningOrchestrator>();
builder.Services.AddClawbotSkills(builder.Configuration);
// M25: persist Claude cost to claude_cost_ledger (overrides in-memory tracker from the skills module).
builder.Services.RemoveAll<Clawbot.Agents.Core.Skills.Ops.IClaudeCostTracker>();
builder.Services.AddSingleton<Clawbot.Agents.Core.Skills.Ops.IClaudeCostTracker, Clawbot.Infrastructure.Agents.DbClaudeCostTracker>();
// M25: chat agent honors per-tenant enable/disable (AgentConfig.Status).
builder.Services.RemoveAll<Clawbot.Agents.Core.Chat.IAgentToggleGate>();
builder.Services.AddSingleton<Clawbot.Agents.Core.Chat.IAgentToggleGate, Clawbot.Infrastructure.Agents.DbAgentToggleGate>();
// Persist-only notification publisher (no SignalR hub in AgentService); FE polls unread count.
builder.Services.AddSingleton<Clawbot.SharedKernel.Notifications.INotificationPublisher, Clawbot.Infrastructure.Notifications.DbOnlyNotificationPublisher>();
builder.Services.TryAddSingleton<Clawbot.SharedKernel.Inbox.IInboxNotifier, Clawbot.Infrastructure.Notifications.NoopInboxNotifier>();
builder.Services.AddClawbotRag(builder.Configuration);
builder.Services.AddClawbotChat(builder.Configuration);
builder.Services.AddClawbotContent(builder.Configuration);
builder.Services.AddClawbotResearch(builder.Configuration);
builder.Services.AddClawbotSaleAssist();
builder.Services.AddClawbotLead();
builder.Services.AddClawbotDocs(builder.Configuration);
// MinIO storage overrides LocalDocumentStorage when configured (7-day signed URLs).
if (!string.IsNullOrWhiteSpace(builder.Configuration["Docs:Storage:Minio:Endpoint"]))
    builder.Services.AddSingleton<IDocumentStorage, MinioDocumentStorage>();

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
