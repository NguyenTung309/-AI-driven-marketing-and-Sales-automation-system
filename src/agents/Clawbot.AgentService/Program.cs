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

builder.Services.AddGrpc(o => o.Interceptors.Add<LlmConfigGrpcInterceptor>());
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddClawbotTelemetry(builder.Configuration, "clawbot-agent");
builder.Services.AddSingleton<AgentRegistry>(_ => DefaultAgentRegistry.Create());
builder.Services.AddClawbotSkills(builder.Configuration);
// Dynamic agent orchestration (SK planner + lifecycle).
builder.Services.AddScoped<IAgentCatalog, Clawbot.Infrastructure.Agents.DbAgentCatalog>();
builder.Services.AddScoped<Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService, ClawbotChatCompletionService>();
builder.Services.AddScoped<SemanticKernelPlanGenerator>();
builder.Services.AddSingleton<OrchestratorCostGuard>();
// Dynamic agent orchestration v2: data-defined sub-agents + A2A mailbox + autonomous coordinator.
builder.Services.AddScoped<Clawbot.Agents.Core.Orchestrator.IAgentDefinitionCatalog, Clawbot.Infrastructure.Agents.AgentDefinitionCatalog>();
builder.Services.AddScoped<Clawbot.Agents.Core.Orchestrator.IA2AMailbox, Clawbot.Infrastructure.Agents.EfA2AMailbox>();
builder.Services.AddScoped<Clawbot.Agents.Core.Orchestrator.IAutonomousPlanner, Clawbot.AgentService.Services.AutonomousPlanner>();
builder.Services.AddScoped<Clawbot.Agents.Core.Orchestrator.IAutonomousRunSink, Clawbot.AgentService.Services.AutonomousRunSink>();
// SPEC-16 P4-4: tenant high-risk approval toggle resolver (reads Tenant.RequireOrchestrationApproval).
builder.Services.AddScoped<Clawbot.Agents.Core.Orchestrator.IOrchestrationApprovalResolver, Clawbot.Infrastructure.Agents.EfOrchestrationApprovalResolver>();
// Autonomous orchestration options bound from config (MaxRounds, transient-retry caps) so they are tunable without redeploy.
var autonomousOptions = builder.Configuration.GetSection("AutonomousOrchestration").Get<AutonomousOrchestratorOptions>()
    ?? new AutonomousOrchestratorOptions();
builder.Services.AddSingleton(autonomousOptions);
// Tool registry wraps the real DI agent adapters (content/ads/lead/report/...) as callable tools for the ReAct worker.
// Scoped because the adapters are scoped; built from IEnumerable<IAgent> so it tracks the registered hands.
// Explicit IAgentTool registrations (content persist/approve — AgentService-layer, need AppDbContext) override
// adapter-wrapped tools of the same name, so the content tool persists drafts instead of returning text only.
builder.Services.AddScoped<IAgentTool, ContentGenerateTool>();
builder.Services.AddScoped<IAgentTool, ContentApproveTool>();
builder.Services.AddScoped<IAgentTool, ContentScheduleTool>();
builder.Services.AddScoped<IAgentTool, ContentPublishTool>();
// web.search: tra cuu web qua SearXNG self-host (Searxng:BaseUrl); timeout ngan de ReAct loop khong treo
builder.Services.AddScoped<IAgentTool, WebSearchTool>();
builder.Services.AddHttpClient(WebSearchTool.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<ToolRegistry>(sp => ToolRegistryFactory.Build(
    sp.GetRequiredService<IEnumerable<IAgent>>(),
    sp.GetRequiredService<IEnumerable<IAgentTool>>()));
builder.Services.AddScoped<AutonomousOrchestrator>(sp => new AutonomousOrchestrator(
    sp.GetRequiredService<Clawbot.Agents.Core.Orchestrator.IAutonomousPlanner>(),
    sp.GetRequiredService<Clawbot.Agents.Core.Orchestrator.IAgentDefinitionCatalog>(),
    sp.GetRequiredService<AgentRegistry>(),
    sp.GetRequiredService<Clawbot.Agents.Core.Orchestrator.IA2AMailbox>(),
    sp.GetRequiredService<OrchestratorCostGuard>(),
    sp.GetRequiredService<ILlmCallScope>(),
    sp.GetRequiredService<Clawbot.Agents.Core.Orchestrator.IAutonomousRunSink>(),
    sp.GetRequiredService<IRagRetriever>(),
    sp.GetRequiredService<IClaudeChatClient>(),
    sp.GetRequiredService<Clawbot.SharedKernel.Time.IClock>(),
    sp.GetRequiredService<AutonomousOrchestratorOptions>(),
    sp.GetRequiredService<ToolRegistry>(),
    sp.GetRequiredService<Clawbot.Agents.Core.Orchestrator.IOrchestrationApprovalResolver>()));
// Tenant trend scan (settings-aware, persists briefs): used by the gRPC research endpoint and by
// "[trend-scan]" schedules, which bypass the LLM orchestrator entirely.
builder.Services.AddScoped<Clawbot.AgentService.Services.ITenantTrendScanner, Clawbot.AgentService.Services.TrendScanService>();
builder.Services.AddScoped<Clawbot.AgentService.Services.AgentScheduleRunner>();
builder.Services.AddHostedService<Clawbot.AgentService.Services.AgentScheduleWorker>();
builder.Services.AddScoped<IAgent, ChatAgentAdapter>();
builder.Services.AddScoped<IAgent, ContentAgentAdapter>();
builder.Services.AddScoped<IAgent, ResearchAgentAdapter>();
builder.Services.AddScoped<IAgent, DocsAgentAdapter>();
builder.Services.AddScoped<IAgent, AdsAgentAdapter>();
builder.Services.AddScoped<IAgent, SaleAssistAgentAdapter>();
builder.Services.AddScoped<LeadAgentRunner>();
builder.Services.AddScoped<ReportAgentRunner>();
// Part C.2: LLM-backed lead-signal classifier (keyword fallback) + per-message auto-scorer.
builder.Services.AddSingleton<Clawbot.Agents.Core.Skills.Lead.KeywordLeadSignalClassifier>();
builder.Services.AddSingleton<Clawbot.Agents.Core.Skills.Lead.ILeadSignalClassifier,
    Clawbot.Agents.Core.Skills.Lead.ClaudeLeadSignalClassifier>();
builder.Services.AddScoped<LeadAutoScorer>();
builder.Services.AddScoped<IAgent, LeadOrchestrationAdapter>();
builder.Services.AddScoped<IAgent, ReportOrchestrationAdapter>();
// M25: persist Claude cost to claude_cost_ledger (overrides in-memory tracker from the skills module).
builder.Services.RemoveAll<Clawbot.Agents.Core.Skills.Ops.ILlmCostTracker>();
builder.Services.AddSingleton<Clawbot.Agents.Core.Skills.Ops.ILlmCostTracker, Clawbot.Infrastructure.Agents.DbLlmCostTracker>();
// M25: chat agent honors per-tenant enable/disable (AgentConfig.Status).
builder.Services.RemoveAll<Clawbot.Agents.Core.Chat.IAgentToggleGate>();
builder.Services.AddSingleton<Clawbot.Agents.Core.Chat.IAgentToggleGate, Clawbot.Infrastructure.Agents.DbAgentToggleGate>();
// Persist-only notification publisher (no SignalR hub in AgentService); FE polls unread count.
// Persist + publish to Redis so the API-side relay pushes realtime through NotificationHub
// (run failed / pending approval reach the bell + toast without F5).
builder.Services.AddSingleton<Clawbot.SharedKernel.Notifications.INotificationPublisher, Clawbot.Infrastructure.Notifications.RedisBridgeNotificationPublisher>();
builder.Services.TryAddSingleton<Clawbot.SharedKernel.Inbox.IInboxNotifier, Clawbot.Infrastructure.Notifications.NoopInboxNotifier>();
builder.Services.AddClawbotRag(builder.Configuration);
builder.Services.AddClawbotChat(builder.Configuration, builder.Environment);
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

// SPEC-16 Module M-1: bootstrap the Pancake page token from env vars into the encrypted pancake_pages store,
// so the live token never lives in appsettings.json. Best-effort (never crashes startup); no-op when env absent.
await Clawbot.Infrastructure.Channels.Pancake.PancakeBootstrapSeeder.BootstrapAsync(app.Services, builder.Configuration).ConfigureAwait(false);

app.Run();
