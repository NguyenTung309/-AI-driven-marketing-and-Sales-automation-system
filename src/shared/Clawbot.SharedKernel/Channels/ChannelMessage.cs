namespace Clawbot.SharedKernel.Channels;

public sealed record ChannelMessage(
    string Channel,
    string ExternalThreadId,
    string ExternalUserId,
    string Text,
    DateTimeOffset SentAt,
    IReadOnlyDictionary<string, string> Metadata);
