using System.Globalization;
using System.Text;
using Clawbot.Api.Auth;
using Clawbot.Api.Endpoints;
using Clawbot.Api.Hubs;
using Clawbot.Api.Middleware;
using Clawbot.Api.Services;
using Clawbot.Application;
using Clawbot.Infrastructure;
using Clawbot.Infrastructure.Identity;
using Clawbot.Infrastructure.Jobs;
using Clawbot.Infrastructure.Observability;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Inbox;
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
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddClawbotJobs(builder.Configuration);
builder.Services.AddClawbotTelemetry(builder.Configuration, "clawbot-api");

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
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
        };
    })
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddAuthorizationBuilder()
    .SetDefaultPolicy(new AuthorizationPolicyBuilder(
            JwtBearerDefaults.AuthenticationScheme,
            ApiKeyAuthenticationHandler.SchemeName)
        .RequireAuthenticatedUser()
        .Build())
    .AddPolicy("perm:kb.read", p => p.AddRequirements(new PermissionRequirement("kb.read")))
    .AddPolicy("perm:kb.write", p => p.AddRequirements(new PermissionRequirement("kb.write")))
    .AddPolicy("perm:kb.deploy", p => p.AddRequirements(new PermissionRequirement("kb.deploy")))
    .AddPolicy("perm:inbox.read", p => p.AddRequirements(new PermissionRequirement("inbox.read")))
    .AddPolicy("perm:inbox.assign", p => p.AddRequirements(new PermissionRequirement("inbox.assign")))
    .AddPolicy("perm:lead.read", p => p.AddRequirements(new PermissionRequirement("lead.read")))
    .AddPolicy("perm:lead.write", p => p.AddRequirements(new PermissionRequirement("lead.write")))
    .AddPolicy("perm:content.read", p => p.AddRequirements(new PermissionRequirement("content.read")))
    .AddPolicy("perm:content.write", p => p.AddRequirements(new PermissionRequirement("content.write")))
    .AddPolicy("perm:content.approve", p => p.AddRequirements(new PermissionRequirement("content.approve")))
    .AddPolicy("perm:docs.generate", p => p.AddRequirements(new PermissionRequirement("docs.generate")))
    .AddPolicy("perm:ads.read", p => p.AddRequirements(new PermissionRequirement("ads.read")))
    .AddPolicy("perm:ads.manage", p => p.AddRequirements(new PermissionRequirement("ads.manage")))
    .AddPolicy("perm:analytics.read", p => p.AddRequirements(new PermissionRequirement("analytics.read")))
    .AddPolicy("perm:admin.system", p => p.AddRequirements(new PermissionRequirement("admin.system")))
    .AddPolicy("perm:admin.audit", p => p.AddRequirements(new PermissionRequirement("admin.audit")));
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddClawbotRateLimiting();
builder.Services.AddSignalR();
builder.Services.AddScoped<IInboxNotifier, SignalRInboxNotifier>();
builder.Services.AddScoped<IContentNotifier, SignalRContentNotifier>();
builder.Services.AddScoped<INotificationPublisher, Clawbot.Api.Hubs.DbNotificationPublisher>();
// Document storage for avatar upload (M23): Local by default, MinIO presigned (7d) when configured.
var docsStorage = builder.Configuration.GetSection(Clawbot.Agents.Core.Docs.DocsStorageOptions.SectionName)
    .Get<Clawbot.Agents.Core.Docs.DocsStorageOptions>() ?? new Clawbot.Agents.Core.Docs.DocsStorageOptions();
builder.Services.AddSingleton(docsStorage);
builder.Services.AddSingleton<Clawbot.Agents.Core.Docs.IDocumentStorage, Clawbot.Agents.Core.Docs.LocalDocumentStorage>();
if (!string.IsNullOrWhiteSpace(builder.Configuration["Docs:Storage:Minio:Endpoint"]))
    builder.Services.AddSingleton<Clawbot.Agents.Core.Docs.IDocumentStorage, Clawbot.Infrastructure.Documents.MinioDocumentStorage>();
builder.Services.AddScoped<AnalyticsAggregationService>();
builder.Services.AddScoped<AnalyticsExportService>();

var agentServiceUrl = builder.Configuration["AgentService:Url"] ?? "http://localhost:5050";
builder.Services.AddGrpcClient<Clawbot.Agents.Contracts.SaleAssist.SaleAssistAgent.SaleAssistAgentClient>(o =>
{
    o.Address = new Uri(agentServiceUrl);
});
builder.Services.AddGrpcClient<Clawbot.Agents.Contracts.Docs.DocsAgent.DocsAgentClient>(o =>
{
    o.Address = new Uri(agentServiceUrl);
});
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
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(c =>
    c.AddDefaultPolicy(p =>
        p.WithOrigins("http://localhost:5173")
         .AllowAnyMethod()
         .AllowAnyHeader()
         .AllowCredentials()));

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

app.MapHealth();
app.MapAuth();
app.MapRoles();
app.MapApiKeys();
app.MapKb();
app.MapInbox();
app.MapSaleAssist();
app.MapContent();
app.MapAds();
app.MapAnalytics();
app.MapDocuments();
app.MapLeads();
app.MapChatScenarios();
app.MapChannels();
app.MapWebhooks();
app.MapContacts();
app.MapAdmin();
app.MapAdminUsers();
app.MapProfile();
app.MapNotifications();
app.MapAgents();
app.MapCompetitors();
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

app.Run();

public partial class Program { }
