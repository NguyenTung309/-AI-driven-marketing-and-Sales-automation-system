namespace Clawbot.Gateway.Configuration;

/// <summary>
/// Root configuration options for the Gateway
/// </summary>
public class GatewayOptions
{
    public PancakeOptions Pancake { get; set; } = new();
    public RateLimitOptions RateLimit { get; set; } = new();
}

/// <summary>
/// Pancake webhook configuration (HMAC secret)
/// </summary>
public class PancakeOptions
{
    public string WebhookSecret { get; set; } = string.Empty;
}

/// <summary>
/// Rate limiting configuration
/// </summary>
public class RateLimitOptions
{
    public int WebhookPermitLimit { get; set; } = 100;
    public int WebhookWindowSeconds { get; set; } = 10;
    public int ApiPermitLimit { get; set; } = 200;
    public int ApiWindowSeconds { get; set; } = 10;
}
