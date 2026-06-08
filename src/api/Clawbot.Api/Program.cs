using System.Globalization;
using System.Text;
using Clawbot.Api.Auth;
using Clawbot.Api.Endpoints;
using Clawbot.Api.Hubs;
using Clawbot.Api.Middleware;
using Clawbot.Application;
using Clawbot.Infrastructure;
using Clawbot.Infrastructure.Identity;
using Clawbot.Infrastructure.Jobs;
using Clawbot.SharedKernel.Inbox;
using Clawbot.SharedKernel.Security;
using Hangfire;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddClawbotJobs(builder.Configuration);
// jwt options — SigningKey/Issuer/Audience come from config (secret); the timing is forced
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
    });

builder.Services.AddAuthorization();
builder.Services.AddClawbotRateLimiting();
builder.Services.AddSignalR();
builder.Services.AddScoped<IInboxNotifier, SignalRInboxNotifier>();

var agentServiceUrl = builder.Configuration["AgentService:Url"] ?? "http://localhost:5050";
builder.Services.AddGrpcClient<Clawbot.Agents.Contracts.SaleAssist.SaleAssistAgent.SaleAssistAgentClient>(o =>
{
    o.Address = new Uri(agentServiceUrl);
});
builder.Services.AddGrpcClient<Clawbot.Agents.Contracts.Docs.DocsAgent.DocsAgentClient>(o =>
{
    o.Address = new Uri(agentServiceUrl);
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
    // Bounded-context stubs (BoundedContextEndpoints) overlap real handlers on the same
    // path (e.g. /api/kb/accuracy vs the stub's /api/kb/accuracy/), which breaks OpenAPI
    // generation. Pick the first action so Swagger renders; both routes still work at runtime.
    c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First()));
builder.Services.AddCors(c =>
    c.AddDefaultPolicy(p =>
        p.WithOrigins("http://localhost:5173")
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

app.MapHealth();
app.MapAuth();
app.MapRoles();
app.MapApiKeys();
app.MapKb();
app.MapInbox();
app.MapSaleAssist();
app.MapDocuments();
app.MapLeads();
app.MapChatScenarios();
app.MapChannels();
app.MapWebhooks();
app.MapBoundedContexts();
app.MapHub<DashboardHub>("/hubs/dashboard");
app.MapHub<InboxHub>("/hubs/inbox");
app.MapHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = Array.Empty<Hangfire.Dashboard.IDashboardAuthorizationFilter>(),
});

HangfireModule.ScheduleClawbotJobs(app.Services);

await RbacSeeder.SeedAsync(app.Services).ConfigureAwait(false);

if (app.Environment.IsDevelopment())
{
    await DevDataSeeder.SeedAdminAsync(app.Services).ConfigureAwait(false);
}

app.Run();

public partial class Program { }
