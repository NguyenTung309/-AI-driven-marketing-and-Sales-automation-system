-- Content schedule publish retry support
-- Adds retry_count to track transient failure retries before terminal failure.

IF COL_LENGTH('dbo.content_schedule', 'retry_count') IS NULL
BEGIN
    ALTER TABLE dbo.content_schedule
        ADD retry_count INT NOT NULL DEFAULT 0;
END

EXEC(N'UPDATE dbo.content_schedule SET retry_count = 0 WHERE retry_count IS NULL;');

-- Prevent duplicate pending schedules for the same content item.
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'ix_content_schedule_pending_item'
      AND object_id = OBJECT_ID(N'dbo.content_schedule')
)
BEGIN
    CREATE UNIQUE INDEX ix_content_schedule_pending_item
        ON dbo.content_schedule (content_item_id)
        WHERE status = 'pending';
END
