namespace Clawbot.SharedKernel.Demo;

/// <summary>Runtime-stored configuration for the demo session (survives env override).</summary>
public sealed class DemoRuntimeConfig
{
    public string? PancakeAccessToken { get; set; }
    public string? PancakeWebhookSecret { get; set; }
    public string? PancakePageId { get; set; }
    public string? PancakeBaseUrl { get; set; }
    public string? PancakePageAccessToken { get; set; }
    public string? AutoReplyText { get; set; }

    public bool IsTokenConfigured => !string.IsNullOrEmpty(PancakeAccessToken);
    public bool IsSecretConfigured => !string.IsNullOrEmpty(PancakeWebhookSecret);
    public bool IsPageTokenConfigured => !string.IsNullOrEmpty(PancakePageAccessToken);

    public string EffectiveAutoReplyText =>
        AutoReplyText ?? "Cảm ơn bạn đã liên hệ, chúng tôi sẽ phản hồi sớm";
}
