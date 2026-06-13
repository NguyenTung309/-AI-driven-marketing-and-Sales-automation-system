namespace Clawbot.SharedKernel.Channels;

public sealed record ChannelMessage(
    string Channel,
    string ExternalThreadId,
    string ExternalUserId,
    string Text,
    DateTimeOffset SentAt,
    IReadOnlyDictionary<string, string> Metadata,
    // Chat-2: distinguishes a public comment (under a post) from a direct message, and links the
    // comment to its post. Populated by the channel adapter once the Pancake comment-webhook spike
    // confirms the payload; defaults keep DM ingestion unchanged.
    string MessageType = "text",
    string? ParentPostId = null);
