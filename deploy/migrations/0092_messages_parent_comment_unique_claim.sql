-- 0092: serialize one bot public reply and one bot DM claim per source comment.
-- A pending_send row is inserted before the external POST; concurrent scans then fail the unique claim
-- instead of sending duplicate non-idempotent replies.
-- send_failed nam ngoai index: mot lan gui hong khong duoc khoa vinh vien viec thu lai comment do.
IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'UX_messages_bot_parent_comment_type'
      AND object_id = OBJECT_ID(N'dbo.messages')
      AND (filter_definition IS NULL OR CHARINDEX(N'send_failed', filter_definition) = 0)
)
    DROP INDEX UX_messages_bot_parent_comment_type ON dbo.messages;
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'UX_messages_bot_parent_comment_type'
      AND object_id = OBJECT_ID(N'dbo.messages')
)
    CREATE UNIQUE INDEX UX_messages_bot_parent_comment_type
        ON dbo.messages (tenant_id, parent_comment_id, message_type)
        WHERE parent_comment_id IS NOT NULL
          AND direction = N'out'
          AND sender_type = N'bot'
          AND status <> N'send_failed';
