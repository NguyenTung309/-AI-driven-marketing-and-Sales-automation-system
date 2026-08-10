using Clawbot.Agents.Core;
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
using Clawbot.SharedKernel.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Console + file log (logs/agent-*.log): loi runtime (auto-reply 9112, channel send...) phai doc lai duoc
// sau khi cua so console dong — dong bo cach cau hinh voi Clawbot.Api.
// SystemLogs sink: Warning+ → dbo.system_logs (admin "Lỗi hệ thống" tab).
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture)
    .WriteTo.SystemLogs(ctx.Configuration.GetConnectionString("SqlServer"), "agent-service"));

builder.Services.AddGrpc(o => o.Interceptors.Add<LlmConfigGrpcInterceptor>());
var agentServiceAuthentication = builder.Configuration
    .GetSection(AgentServiceAuthenticationOptions.SectionName)
    .Get<AgentServiceAuthenticationOptions>() ?? new AgentServiceAuthenticationOptions();
var agentServiceSigningKey = AgentServiceAuthenticationOptions.GetSigningKeyBytes(
    agentServiceAuthentication.SigningKey);
AgentServiceAuthenticationOptions.EnsureGrpcTransportSecurity(
    builder.Configuration["Kestrel:Endpoints:Grpc:Url"],
    builder.Configuration["Kestrel:Endpoints:Grpc:Certificate:Path"],
    builder.Environment.IsDevelopment());
if (agentServiceAuthentication.TokenLifetimeMinutes is < 1 or > 5)
    throw new InvalidOperationException("agent_service_auth_token_lifetime_invalid");
builder.Services.Configure<AgentServiceAuthenticationOptions>(
    builder.Configuration.GetSection(AgentServiceAuthenticationOptions.SectionName));
builder.Services.AddAuthentication()
    .AddJwtBearer("AgentService", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = AgentServiceAuthenticationOptions.Issuer,
            ValidAudience = AgentServiceAuthenticationOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                agentServiceSigningKey),
            ClockSkew = TimeSpan.FromSeconds(15),
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("orchestrator-service", policy => policy
        .AddAuthenticationSchemes("AgentService")
        .RequireAuthenticatedUser()
        .RequireClaim("client_id", AgentServiceAuthenticationOptions.ClientId));
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
builder.Services.AddScoped<Clawbot.AgentService.Services.OrchestratorCallerAuthorizer>();
builder.Services.AddScoped<Clawbot.AgentService.Services.IOrchestratorCallerAuthorizer>(sp =>
    sp.GetRequiredService<Clawbot.AgentService.Services.OrchestratorCallerAuthorizer>());
builder.Services.AddScoped<Clawbot.AgentService.Services.IOrchestratorPermissionResolver>(sp =>
    sp.GetRequiredService<Clawbot.AgentService.Services.OrchestratorCallerAuthorizer>());
// SPEC-16 P4-4: tenant high-risk approval toggle resolver (reads Tenant.RequireOrchestrationApproval).
builder.Services.AddScoped<Clawbot.Agents.Core.Orchestrator.IOrchestrationApprovalResolver, Clawbot.Infrastructure.Agents.EfOrchestrationApprovalResolver>();
// Chính sách khi task lỗi (Tenant.OrchestratorFailurePolicy): mặc định dừng chờ người thay vì auto-replan.
builder.Services.AddScoped<Clawbot.Agents.Core.Orchestrator.IOrchestrationFailurePolicyResolver, Clawbot.Infrastructure.Agents.EfOrchestrationFailurePolicyResolver>();
// Review-gate P1: tenant RequireContentReview flag — dùng bởi content.schedule/content.publish tools + Review RPC.
builder.Services.AddScoped<Clawbot.SharedKernel.Content.IContentReviewPolicyResolver, Clawbot.Infrastructure.Agents.EfContentReviewPolicyResolver>();
// Review-gate P3 manual-mode: tenant RequireChatReplyApproval — ChatAgentGrpcService hold-all khi bật.
builder.Services.AddScoped<Clawbot.SharedKernel.Inbox.IChatApprovalPolicyResolver, Clawbot.Infrastructure.Agents.EfChatApprovalPolicyResolver>();
// Autonomous orchestration options bound from config (MaxRounds, transient-retry caps) so they are tunable without redeploy.
var autonomousOptions = builder.Configuration.GetSection("AutonomousOrchestration").Get<AutonomousOrchestratorOptions>()
    ?? new AutonomousOrchestratorOptions();
builder.Services.AddSingleton(autonomousOptions);
// Tool registry wraps the real DI agent adapters (content/ads/lead/report/...) as callable tools for the ReAct worker.
// Scoped because the adapters are scoped; built from IEnumerable<IAgent> so it tracks the registered hands.
// Explicit IAgentTool registrations (content persist/approve — AgentService-layer, need AppDbContext) override
// adapter-wrapped tools of the same name, so the content tool persists drafts instead of returning text only.
builder.Services.AddScoped<IAgentTool, ContentGenerateTool>();
// content.list: bước tra cứu read-only reviewer-agent cần trước khi gọi content.review (vốn đòi content_id cụ thể).
builder.Services.AddScoped<IAgentTool, ContentListTool>();
builder.Services.AddScoped<IAgentTool, ContentApproveTool>();
builder.Services.AddScoped<IAgentTool, ContentScheduleTool>();
builder.Services.AddScoped<IAgentTool, ContentPublishTool>();
// web.search: tra cuu web qua SearXNG self-host (Searxng:BaseUrl); timeout ngan de ReAct loop khong treo
builder.Services.AddScoped<IAgentTool, WebSearchTool>();
builder.Services.AddHttpClient(WebSearchTool.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<ToolRegistry>(sp => ToolRegistryFactory.Build(
    sp.GetRequiredService<IEnumerable<IAgent>>(),
    sp.GetRequiredService<IEnumerable<IAgentTool>>()));
builder.Services.AddScoped<IAutonomousOrchestrator>(sp => new AutonomousOrchestrator(
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
    sp.GetRequiredService<Clawbot.Agents.Core.Orchestrator.IOrchestrationApprovalResolver>(),
    sp.GetRequiredService<Clawbot.Agents.Core.Orchestrator.IOrchestrationFailurePolicyResolver>()));
// Tenant trend scan (settings-aware, persists briefs): used by the gRPC research endpoint and by
// "[trend-scan]" schedules, which bypass the LLM orchestrator entirely.
builder.Services.AddScoped<Clawbot.AgentService.Services.ITenantTrendScanner, Clawbot.AgentService.Services.TrendScanService>();
builder.Services.AddScoped<Clawbot.AgentService.Services.IAgentScheduleLeaseProvider,
    Clawbot.AgentService.Services.AgentScheduleLeaseProvider>();
builder.Services.AddScoped<Clawbot.AgentService.Services.AgentScheduleRunner>();
builder.Services.AddHostedService<Clawbot.AgentService.Services.AgentScheduleWorker>();
builder.Services.AddHostedService<Clawbot.AgentService.Services.OrchestrationTerminalIntentWorker>();
builder.Services.Configure<Clawbot.AgentService.Services.ContentReviewWorkerOptions>(
    builder.Configuration.GetSection(Clawbot.AgentService.Services.ContentReviewWorkerOptions.SectionName));
builder.Services.AddSingleton<Clawbot.Agents.Core.Content.ILlmVisionCapabilityResolver,
    Clawbot.Agents.Core.Content.LlmVisionCapabilityResolver>();
builder.Services.AddSingleton<Clawbot.Agents.Core.Content.IContentReviewCompletionClientFactory>(sp =>
{
    var env = sp.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>();
    var baseUrlOptions = sp.GetService<Microsoft.Extensions.Options.IOptions<Clawbot.Agents.Core.Chat.LlmBaseUrlOptions>>()?.Value
        ?? new Clawbot.Agents.Core.Chat.LlmBaseUrlOptions();
    var allowPrivate = env.IsDevelopment() && baseUrlOptions.AllowPrivate;
    return new Clawbot.Agents.Core.Content.ContentReviewCompletionClientFactory(allowPrivate);
});
builder.Services.AddScoped<Clawbot.Agents.Core.Content.ContentReviewer>();
builder.Services.AddScoped<Clawbot.AgentService.Services.IContentReviewExecutor,
    Clawbot.AgentService.Services.ContentReviewExecutor>();
builder.Services.AddScoped<Clawbot.AgentService.Services.IContentReviewCoordinator,
    Clawbot.AgentService.Services.ContentReviewCoordinator>();
// Refine (P6, §4.7): reviewer reject => chạy lại L3+L4 kèm lý do, sửa bài tại chỗ, chấm lại đúng 1 vòng.
builder.Services.AddScoped<Clawbot.AgentService.Services.IContentRefiner,
    Clawbot.AgentService.Services.ContentRefiner>();
builder.Services.Configure<Clawbot.Agents.Core.Content.ContentAssetReaderOptions>(
    builder.Configuration.GetSection(Clawbot.Agents.Core.Content.ContentAssetReaderOptions.SectionName));
builder.Services.AddScoped<Clawbot.Agents.Core.Content.IContentAssetRepository,
    Clawbot.AgentService.Services.EfContentAssetRepository>();
builder.Services.AddScoped<Clawbot.Agents.Core.Content.IContentAssetReader>(sp =>
    new Clawbot.Agents.Core.Content.ContentAssetReader(
        sp.GetRequiredService<Clawbot.Agents.Core.Content.IContentAssetRepository>(),
        sp.GetRequiredService<Clawbot.Agents.Core.Docs.IDocumentStorage>(),
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Clawbot.Agents.Core.Content.ContentAssetReaderOptions>>().Value));
builder.Services.AddScoped<Clawbot.AgentService.Services.IContentPublishingApprovalPolicyResolver,
    Clawbot.AgentService.Services.LockedContentPublishingApprovalPolicyResolver>();
builder.Services.AddScoped<Clawbot.AgentService.Services.ReviewTenantWorker>();
builder.Services.AddScoped<Clawbot.AgentService.Services.IReviewTenantRunner>(sp =>
    sp.GetRequiredService<Clawbot.AgentService.Services.ReviewTenantWorker>());
builder.Services.AddHostedService<Clawbot.AgentService.Services.ContentReviewDispatchWorker>();
builder.Services.AddHostedService<Clawbot.AgentService.Services.ChatSessionRecoveryService>();
builder.Services.AddScoped<IAgent, ChatAgentAdapter>();
builder.Services.AddScoped<IAgent, ContentAgentAdapter>();
builder.Services.AddScoped<IAgent, ResearchAgentAdapter>();
builder.Services.AddScoped<IAgent, DocsAgentAdapter>();
builder.Services.AddScoped<IAgent, SaleAssistAgentAdapter>();
builder.Services.AddScoped<LeadAgentRunner>();
builder.Services.AddScoped<ReportAgentRunner>();
// Part C.2: LLM-backed lead-signal classifier (keyword fallback) + per-message auto-scorer.
builder.Services.AddSingleton<Clawbot.Agents.Core.Skills.Lead.KeywordLeadSignalClassifier>();
builder.Services.AddSingleton<Clawbot.Agents.Core.Skills.Lead.ILeadSignalClassifier,
    Clawbot.Agents.Core.Skills.Lead.ClaudeLeadSignalClassifier>();
builder.Services.AddScoped<LeadAutoScorer>();
// LeadBatchRescorer registered via Infrastructure DI (shared with API rescore endpoint).
builder.Services.AddScoped<IAgent, LeadOrchestrationAdapter>();
builder.Services.AddScoped<IAgent, ReportOrchestrationAdapter>();
// M25: persist Claude cost to claude_cost_ledger (overrides in-memory tracker from the skills module).
builder.Services.RemoveAll<Clawbot.Agents.Core.Skills.Ops.ILlmCostTracker>();
builder.Services.AddSingleton<Clawbot.Agents.Core.Skills.Ops.ILlmCostTracker, Clawbot.Infrastructure.Agents.DbLlmCostTracker>();
// Prompt chaining (P1): ghi telemetry chuỗi vào content_generation_traces (scope riêng như cost tracker).
// Core chỉ đăng ký chuỗi + mắt xích; sink là EF nên phải vá ở host có AppDbContext.
builder.Services.AddSingleton<Clawbot.Agents.Core.Content.Chain.IContentChainTraceSink, Clawbot.Infrastructure.Content.EfContentChainTraceSink>();
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

app.UseAuthentication();
app.UseAuthorization();
app.MapGrpcService<OrchestratorGrpcService>().RequireAuthorization("orchestrator-service");
app.MapGrpcService<ChatAgentGrpcService>();
app.MapGrpcService<ContentAgentGrpcService>();
app.MapGrpcService<LeadAgentGrpcService>();
app.MapGrpcService<SaleAssistAgentGrpcService>();
app.MapGrpcService<DocsAgentGrpcService>();
app.MapGrpcService<ReportAgentGrpcService>();
app.MapGrpcService<ResearchAgentGrpcService>();
app.MapGet("/", () => "ClawBot Agent Service — use a gRPC client to call services.");
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" }));

// SPEC-16 Module M-1: bootstrap the Pancake page token from env vars into the encrypted inbox credential store,
// so the live token never lives in appsettings.json. Best-effort (never crashes startup); no-op when env absent.
await Clawbot.Infrastructure.Channels.Pancake.PancakeBootstrapSeeder.BootstrapAsync(app.Services, builder.Configuration).ConfigureAwait(false);

app.Run();
