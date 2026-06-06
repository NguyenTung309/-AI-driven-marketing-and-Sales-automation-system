namespace Clawbot.Gateway.Configuration;


public class GatewayOptions
{
    public PancakeOptions Pancake { get; set; } = new();
    public RateLimitOptions RateLimit { get; set; } = new();
}


public class PancakeOptions
{
    public string WebhookSecret { get; set; } = string.Empty;
}


public class RateLimitOptions
{
    public int WebhookPermitLimit { get; set; } = 100;
    public int WebhookWindowSeconds { get; set; } = 10;
    public int ApiPermitLimit { get; set; } = 200;
    public int ApiWindowSeconds { get; set; } = 10;
}
