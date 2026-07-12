namespace Clawbot.Infrastructure.Ads;

public sealed class MetaAdsOptions
{
    public const string SectionName = "Ads:Meta";

    public bool Enabled { get; set; }
    public string AccessToken { get; set; } = string.Empty;
    public string AdAccountId { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class TikTokAdsOptions
{
    public const string SectionName = "Ads:TikTok";

    public bool Enabled { get; set; }
    public string Endpoint { get; set; } = "https://business-api.tiktok.com/open_api/v1.3";
    public string AccessToken { get; set; } = string.Empty;
    public string AdvertiserId { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
}
