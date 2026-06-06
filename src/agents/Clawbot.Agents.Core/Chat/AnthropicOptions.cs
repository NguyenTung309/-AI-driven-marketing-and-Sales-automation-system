namespace Clawbot.Agents.Core.Chat;

public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "claude-sonnet-4-6";
    public string BaseUrl { get; init; } = "https://api.anthropic.com";
    public int MaxTokens { get; init; } = 1024;
    public decimal InputUsdPer1M { get; init; } = 3.00m;
    public decimal OutputUsdPer1M { get; init; } = 15.00m;
}
