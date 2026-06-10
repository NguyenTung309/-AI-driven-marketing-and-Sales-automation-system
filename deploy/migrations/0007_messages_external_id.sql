-- Add external_message_id for strict dedup of inbound messages.
-- Pancake provides a unique message ID per platform message; using it eliminates
-- the fragile (conversationId, content, sentAt, direction) heuristic.

ALTER TABLE messages
    ADD external_message_id NVARCHAR(256) NULL;

CREATE UNIQUE INDEX ix_messages_external_id
    ON messages (tenant_id, external_message_id)
    WHERE external_message_id IS NOT NULL;
