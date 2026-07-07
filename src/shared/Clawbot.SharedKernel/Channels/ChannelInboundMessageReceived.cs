namespace Clawbot.SharedKernel.Channels;

// Bus contract: a channel message picked up by polling (or a webhook) and queued for ingestion.
// Published to RabbitMQ so ingestion (DB writes, PII redaction, notifier) runs decoupled from the
// poll loop; the consumer is idempotent via the ingestor's external_message_id dedup.
public sealed record ChannelInboundMessageReceived(Guid TenantId, ChannelMessage Message);
