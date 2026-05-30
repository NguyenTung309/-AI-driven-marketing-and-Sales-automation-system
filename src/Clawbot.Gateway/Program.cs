using Clawbot.Gateway.Configuration;
using Clawbot.Gateway.Middleware;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// 1. Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();

// 2. Add YARP reverse proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// 3. Configure GatewayOptions from appsettings.json
builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection("Gateway"));

// 4. Add rate limiting
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
// Order matters: TraceId -> HMAC -> RateLimiter -> ReverseProxy

// 1. TraceId middleware (first, always)
app.UseMiddleware<TraceIdMiddleware>();

// 2. HMAC validation middleware (before routing)
app.UseMiddleware<PancakeHmacMiddleware>();

// 3. Rate limiting
app.UseRateLimiter();

// 4. Map reverse proxy
app.MapReverseProxy();

app.Run();
