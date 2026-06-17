using System.Text;
using System.Threading.RateLimiting;
using Clawbot.Gateway.Configuration;
using Clawbot.Gateway.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// 1. Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();

// 2. Add CORS
var allowedOrigins = builder.Configuration
    .GetSection("Gateway:AllowedOrigins")
    .Get<string[]>()
    ?? ["http://localhost:5173"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("gateway-cors", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// 3. Add YARP reverse proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// 4. Configure GatewayOptions from appsettings.json
builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection("Gateway"));

// SPEC-11 D4: Gateway only VERIFIES the JWT (signature + lifetime) and forwards — no DB/Redis,
// no permission lookup, no X-Permissions. ADR-007 stays intact: JwtBearer is host-level infra
// auth, not a project reference. SigningKey/Issuer/Audience must match the backend's Jwt config.
var jwt = builder.Configuration.GetSection("Jwt");
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
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["SigningKey"] ?? string.Empty)),
            // SPEC-11 D10: must equal the backend's AuthPolicy.ClockSkewSeconds (30). The Gateway
            // cannot reference AuthPolicy (ADR-007 zero refs), so the value is duplicated here.
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        // SPEC-11 D9: WebSocket /hubs connections cannot set the Authorization header — accept
        // the access token from the ?access_token= query string.
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    ctx.Token = accessToken;
                return Task.CompletedTask;
            },
        };
    });

// Named policy referenced by api/hubs routes in appsettings (auth/webhook routes use "anonymous").
builder.Services.AddAuthorization(options =>
    options.AddPolicy("authenticated", p => p.RequireAuthenticatedUser()));

// 5. Add rate limiting
builder.Services.AddRateLimiter(options =>
{
    var gatewayOptions = builder.Configuration.GetSection("Gateway").Get<GatewayOptions>()
                        ?? new GatewayOptions();

    // Webhook rate limit policy
    options.AddFixedWindowLimiter("webhook-limit", config =>
    {
        config.PermitLimit = gatewayOptions.RateLimit.WebhookPermitLimit;
        config.Window = TimeSpan.FromSeconds(gatewayOptions.RateLimit.WebhookWindowSeconds);
        config.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        config.QueueLimit = 0;
    });

    // API rate limit policy
    options.AddFixedWindowLimiter("api-limit", config =>
    {
        config.PermitLimit = gatewayOptions.RateLimit.ApiPermitLimit;
        config.Window = TimeSpan.FromSeconds(gatewayOptions.RateLimit.ApiWindowSeconds);
        config.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        config.QueueLimit = 0;
    });
});

var app = builder.Build();

// Configure HTTP request pipeline
// Order matters: CORS -> TraceId -> HMAC -> RateLimiter -> AuthN/AuthZ -> ReverseProxy

// 1. CORS — must be first so preflight OPTIONS requests are handled before auth
app.UseCors("gateway-cors");

// 2. TraceId middleware
app.UseMiddleware<TraceIdMiddleware>();

// 3. HMAC validation middleware (before routing)
app.UseMiddleware<PancakeHmacMiddleware>();

// 4. Rate limiting
app.UseRateLimiter();

// 5. AuthN/AuthZ — verify JWT before proxying routes that declare an AuthorizationPolicy.
app.UseAuthentication();
app.UseAuthorization();

// 6. Gateway-local health endpoint (not proxied)
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));

// 7. Map reverse proxy
app.MapReverseProxy();

app.Run();