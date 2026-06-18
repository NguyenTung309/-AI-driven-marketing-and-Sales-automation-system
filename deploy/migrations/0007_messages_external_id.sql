-- Add external_message_id for strict dedup of inbound messages.
-- Pancake provides a unique message ID per platform message; using it eliminates
-- the fragile (conversationId, content, sentAt, direction) heuristic.

IF COL_LENGTH('dbo.messages', 'external_message_id') IS NULL
BEGIN
    ALTER TABLE dbo.messages
        ADD external_message_id NVARCHAR(256) NULL;
END
