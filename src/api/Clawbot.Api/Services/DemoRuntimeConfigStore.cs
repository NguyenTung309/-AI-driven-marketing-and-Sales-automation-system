using Clawbot.SharedKernel.Demo;
using Microsoft.Extensions.Options;

namespace Clawbot.Api.Services;

public sealed class DemoRuntimeConfigStore
{
    private readonly object _lock = new();
    private DemoRuntimeConfig _config;

    public DemoRuntimeConfigStore(IOptions<DemoOptions> opts)
    {
        var token = Environment.GetEnvironmentVariable("PANCAKE_ACCESS_TOKEN");
        var secret = Environment.GetEnvironmentVariable("PANCAKE_WEBHOOK_SECRET");
        var pageToken = Environment.GetEnvironmentVariable("PANCAKE_PAGE_ACCESS_TOKEN");
        var pageId = Environment.GetEnvironmentVariable("PANCAKE_PAGE_ID");
        _config = new DemoRuntimeConfig
        {
            PancakeAccessToken = token,
            PancakeWebhookSecret = secret,
            PancakePageAccessToken = pageToken,
            PancakePageId = pageId,
        };
    }

    public DemoRuntimeConfig Get()
    {
        lock (_lock) return new DemoRuntimeConfig
        {
            PancakeAccessToken = _config.PancakeAccessToken,
            PancakeWebhookSecret = _config.PancakeWebhookSecret,
            PancakePageId = _config.PancakePageId,
            PancakeBaseUrl = _config.PancakeBaseUrl,
            PancakePageAccessToken = _config.PancakePageAccessToken,
            AutoReplyText = _config.AutoReplyText,
        };
    }

    public void UpdateToken(string? token)
    {
        lock (_lock) _config.PancakeAccessToken = token;
    }

    public void UpdatePageAccessToken(string? token)
    {
        lock (_lock) _config.PancakePageAccessToken = token;
    }

    public void UpdateAutoReplyText(string? text)
    {
        lock (_lock) _config.AutoReplyText = text;
    }

    public void UpdatePageId(string? pageId)
    {
        lock (_lock) _config.PancakePageId = pageId;
    }

    public void UpdateBaseUrl(string? baseUrl)
    {
        lock (_lock) _config.PancakeBaseUrl = baseUrl;
    }

    public void UpdateSecret(string? secret)
    {
        lock (_lock) _config.PancakeWebhookSecret = secret;
    }

    public void Override(DemoRuntimeConfig cfg)
    {
        lock (_lock)
        {
            _config.PancakeAccessToken = cfg.PancakeAccessToken;
            _config.PancakeWebhookSecret = cfg.PancakeWebhookSecret;
            _config.PancakePageId = cfg.PancakePageId;
            _config.PancakeBaseUrl = cfg.PancakeBaseUrl;
            _config.PancakePageAccessToken = cfg.PancakePageAccessToken;
            _config.AutoReplyText = cfg.AutoReplyText;
        }
    }
}