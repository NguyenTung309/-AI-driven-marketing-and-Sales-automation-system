-- Content schedule publish retry support
-- Adds retry_count to track transient failure retries before terminal failure.

ALTER TABLE content_schedule
    ADD retry_count INT NOT NULL DEFAULT 0;

UPDATE content_schedule SET retry_count = 0 WHERE retry_count IS NULL;

-- Prevent duplicate pending schedules for the same content item.
CREATE UNIQUE INDEX ix_content_schedule_pending_item
    ON content_schedule (content_item_id)
    WHERE status = 'pending';
