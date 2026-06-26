using System.Globalization;
using System.Text;
using Clawbot.Api.Background;
using Clawbot.Api.Auth;
using Clawbot.Api.Endpoints;
using Clawbot.Api.Hubs;
using Clawbot.Api.Middleware;
using Clawbot.Api.Services;
using Clawbot.Application;
using Clawbot.Infrastructure;
using Clawbot.Agents.Core.Rag;
using Clawbot.Agents.Core.Skills;
using Clawbot.Infrastructure.Identity;
using Clawbot.Infrastructure.Jobs;
using Clawbot.Infrastructure.Notifications;
using Clawbot.Infrastructure.Observability;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Inbox;
using Clawbot.SharedKernel.Security;
using Clawbot.SharedKernel.Demo;
using Clawbot.SharedKernel.Notifications;
using Hangfire;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

builder.Services.AddApplication();
builder.Services.AddClawbotChatSupport(builder.Configuration);
builder.Services.AddClawbotForecasting();
Clawbot.Agents.Core.Chat.ChatModule.AddClawbotChat(builder.Services, builder.Configuration, builder.Environment);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddClawbotRag(builder.Configuration);
builder.Services.AddClawbotJobs(builder.Configuration);
builder.Services.AddClawbotTelemetry(builder.Configuration, "clawbot-api");
builder.Services.AddMemoryCache();

// jwt options - SigningKey/Issuer/Audience come from config (secret); the timing is forced
// from AuthPolicy via PostConfigure so appsettings cannot drift it (SPEC-11).
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.PostConfigure<JwtOptions>(o =>
{
    o.AccessTokenMinutes = AuthPolicy.AccessTokenMinutes;
    o.ClockSkewSeconds = AuthPolicy.ClockSkewSeconds;
});
builder.Services.AddSingleton<JwtTokenIssuer>();

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            // SPEC-11: explicit clock skew, identical on Gateway + backend (AuthPolicy).
            ClockSkew = TimeSpan.FromSeconds(AuthPolicy.ClockSkewSeconds),
        };

        // SPEC-11 D9: SignalR cannot set the Authorization header, so accept the access
        // token from the ?access_token= query string on hub connections.
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            },
        };
    })
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddAuthorizationBuilder()
    .SetDefaultPolicy(new AuthorizationPolicyBuilder(
            JwtBearerDefaults.AuthenticationScheme,
            ApiKeyAuthenticationHandler.SchemeName)
        .RequireAuthenticatedUser()
        .Build());
// Permission gating uses endpoint filter (RequirePermission) + IPermissionResolver at runtime.
// Policy-based "perm:xxx" approach is removed - those were dead code.
// PermissionAuthorizationHandler also removed for the same reason.

builder.Services.AddClawbotRateLimiting();
builder.Services.AddSignalR();
builder.Services.AddScoped<IInboxNotifier, SignalRInboxNotifier>();
builder.Services.AddScoped<INotificationPublisher, Clawbot.Api.Hubs.DbNotificationPublisher>();
builder.Services.AddScoped<SignalRContentNotifier>();
builder.Services.AddScoped<IContentNotifier>(sp => new PublishingContentNotifier(
    sp.GetRequiredService<SignalRContentNotifier>(),
    sp.GetRequiredService<INotificationPublisher>()));
// Document storage for avatar upload (M23): Local by default, MinIO presigned (7d) when configured.
var docsStorage = builder.Configuration.GetSection(Clawbot.Agents.Core.Docs.DocsStorageOptions.SectionName)
    .Get<Clawbot.Agents.Core.Docs.DocsStorageOptions>() ?? new Clawbot.Agents.Core.Docs.DocsStorageOptions();
builder.Services.AddSingleton(docsStorage);
if (!string.IsNullOrWhiteSpace(builder.Configuration["Docs:Storage:Minio:Endpoint"]))
    builder.Services.AddSingleton<Clawbot.Agents.Core.Docs.IDocumentStorage, Clawbot.Infrastructure.Documents.MinioDocumentStorage>();
builder.Services.AddScoped<AnalyticsAggregationService>();
builder.Services.AddScoped<AnalyticsExportService>();
builder.Services.AddScoped<ChannelHealthService>();
builder.Services.Configure<ReplicationOptions>(builder.Configuration.GetSection(ReplicationOptions.SectionName));
builder.Services.AddScoped<IReplicationLagProbe, SqlServerReplicationLagProbe>();
builder.Services.AddScoped<ReplicationHealthService>();
builder.Services.AddScoped<ContactDataExportService>();
builder.Services.AddScoped<ConversationExportService>();
builder.Services.AddScoped<InboxSearchService>();
builder.Services.AddScoped<KbTestRunnerService>();
builder.Services.AddScoped<LeadCsvService>();
builder.Services.AddScoped<Clawbot.Agents.Core.Skills.Content.IImagePromptGenerator, Clawbot.Agents.Core.Skills.Content.ClaudeImagePromptGenerator>();
builder.Services.AddSingleton<Clawbot.Agents.Core.Skills.Nlp.IPiiRedactor, Clawbot.Agents.Core.Skills.Nlp.RegexPiiRedactor>();
builder.Services.Configure<Clawbot.Agents.Core.Skills.Nlp.ToxicityOptions>(
    builder.Configuration.GetSection(Clawbot.Agents.Core.Skills.Nlp.ToxicityOptions.SectionName));
builder.Services.AddSingleton<Clawbot.Agents.Core.Skills.Nlp.IToxicityFilter, Clawbot.Agents.Core.Skills.Nlp.DetoxifyToxicityFilter>();
builder.Services.AddScoped<ContentImagePromptService>();
builder.Services.AddScoped<OutboundMessageSafetyService>();
builder.Services.AddScoped<ISaleAssistUpsellClient, GrpcSaleAssistUpsellClient>();
builder.Services.AddScoped<SaleAssistUpsellSuggestionService>();
builder.Services.AddScoped<SaleAssistDraftFeedbackService>();
builder.Services.AddScoped<DocumentDeliveryService>();
builder.Services.AddScoped<DocumentOpenReceiptService>();
builder.Services.AddScoped<ExperimentService>();
builder.Services.AddScoped<TenantBrandingService>();

var agentServiceUrl = builder.Configuration["AgentService:Url"] ?? "http://localhost:15875";
builder.Services.AddGrpcClient<Clawbot.Agents.Contracts.SaleAssist.SaleAssistAgent.SaleAssistAgentClient>(o =>
{
    o.Address = new Uri(agentServiceUrl);
});
builder.Services.AddGrpcClient<Clawbot.Agents.Contracts.Docs.DocsAgent.DocsAgentClient>(o =>
{
    o.Address = new Uri(agentServiceUrl);
});

// SPEC-12: Demo mode services
var demoOpts = builder.Configuration.GetSection(DemoOptions.Section).Get<DemoOptions>() ?? new DemoOptions();
if (demoOpts.Mode)
{
    builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection(DemoOptions.Section));
    builder.Services.AddHttpClient();
    builder.Services.AddSingleton<DemoRuntimeConfigStore>();
    builder.Services.AddSingleton<DemoTraceService>();
    builder.Services.AddHostedService<PancakePollingService>();
}

// gRPC agent clients (shared by demo and production modes)
builder.Services.AddGrpcClient<Clawbot.Agents.Contracts.Content.ContentAgent.ContentAgentClient>(o =>
{
    o.Address = new Uri(agentServiceUrl);
});
builder.Services.AddGrpcClient<Clawbot.Agents.Contracts.Research.ResearchAgent.ResearchAgentClient>(o =>
{
    o.Address = new Uri(agentServiceUrl);
});
builder.Services.AddGrpcClient<Clawbot.Agents.Contracts.Ads.AdsAgent.AdsAgentClient>(o =>
{
    o.Address = new Uri(agentServiceUrl);
});
builder.Services.AddGrpcClient<Clawbot.Agents.Contracts.Lead.LeadAgent.LeadAgentClient>(o =>
{
    o.Address = new Uri(agentServiceUrl);
});
builder.Services.AddGrpcClient<Clawbot.Agents.Contracts.Report.ReportAgent.ReportAgentClient>(o =>
{
    o.Address = new Uri(agentServiceUrl);
});
builder.Services.AddGrpcClient<Clawbot.Agents.Contracts.Orchestrator.Orchestrator.OrchestratorClient>(o =>
{
    o.Address = new Uri(agentServiceUrl);
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
    c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First()));
builder.Services.AddCors(c =>
    c.AddDefaultPolicy(p =>
        p.WithOrigins("http://localhost:15876")
         .AllowAnyMethod()
         .AllowAnyHeader()
         .AllowCredentials()));

// Dev only: the repo ships no EF migrations, so create the database + EF schema up front.
// Must run BEFORE builder.Build() because Hangfire installs its own schema during host
// build and fails if the database does not yet exist.
if (builder.Environment.IsDevelopment())
{
    var sqlConn = builder.Configuration.GetConnectionString("SqlServer")
        ?? throw new InvalidOperationException("ConnectionStrings:SqlServer is required.");
    await DevDataSeeder.EnsureSchemaAsync(sqlConn).ConfigureAwait(false);
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Map typed gRPC failures from the AgentService (e.g. llm_config_not_configured) → clean HTTP 4xx.
app.UseMiddleware<Clawbot.Api.Middleware.GrpcErrorTranslationMiddleware>();

// SPEC-12: Demo mode middleware must run early (before route mapping)
if (demoOpts.Mode)
{
    app.UseMiddleware<DemoModeMiddleware>();
}

app.MapHealth();

// SPEC-12: Register demo endpoints (only in demo mode)
if (demoOpts.Mode)
{
    app.MapDemo();
}

app.MapAuth();
app.MapRoles();
app.MapApiKeys();
app.MapKb();
app.MapInbox();
app.MapSaleAssist();
app.MapContent();
app.MapAds();
app.MapAnalytics();
app.MapExperiments();
app.MapTokens();
app.MapLogs();
app.MapPrompts();
app.MapDocuments();
app.MapLeads();
app.MapChatScenarios();
app.MapChannels();
app.MapWebhooks();
app.MapContacts();
app.MapAdmin();
app.MapTenantBranding();
app.MapAdminUsers();
app.MapProfile();
app.MapNotifications();
app.MapAgents();
app.MapOrchestration();
app.MapOrchestrationV2();
app.MapLlmConfigs();
app.MapCompetitors();
app.MapPublicWidget();
app.MapBoundedContexts();
app.MapHub<DashboardHub>("/hubs/dashboard");
app.MapHub<InboxHub>("/hubs/inbox");
app.MapHub<NotificationHub>("/hubs/notifications");
app.MapHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireAdminFilter()],
});

HangfireModule.ScheduleClawbotJobs(app.Services);

await RbacSeeder.SeedAsync(app.Services).ConfigureAwait(false);

if (app.Environment.IsDevelopment())
{
    await DevDataSeeder.SeedAdminAsync(app.Services).ConfigureAwait(false);
    await DevDataSeeder.SeedAutoReplyTemplateAsync(app.Services).ConfigureAwait(false);
    await DemoLlmConfigSeeder.SeedAsync(app.Services).ConfigureAwait(false);
}

app.Run();

public partial class Program { }
