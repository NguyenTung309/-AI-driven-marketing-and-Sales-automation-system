using Clawbot.Domain.Common;

namespace Clawbot.Domain.Channels;

public sealed class ChannelToken
{
    public Guid InboxId { get; private set; }
    public string AccessTokenEncrypted { get; private set; } = string.Empty;
    public string? RefreshTokenEncrypted { get; private set; }
    public string WebhookSecretEncrypted { get; private set; } = string.Empty;
    public DateTimeOffset? TokenExpiresAt { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ChannelToken() { }
}