-- One-time data repair for drafts stranded after their durable review task became terminal.
-- Run after the normal schema migrations and before starting the updated review worker.
-- Idempotent: only pending draft items with a failed/canceled task for their current revision change.
-- This file is one SqlCommand; do not add GO.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

IF OBJECT_ID(N'dbo.content_items', N'U') IS NULL
    OR OBJECT_ID(N'dbo.content_review_tasks', N'U') IS NULL
    RETURN;

UPDATE item
SET agent_review_status = N'needs_human',
    agent_reviewed_revision = item.content_revision,
    reviewed_by_agent_id = NULL,
    agent_review_started_at = NULL,
    agent_reviewed_at = SYSDATETIMEOFFSET(),
    agent_review_reason = N'content_review_attempt_limit_reached',
    image_review_status = N'failed',
    reviewed_image_count = 0,
    human_approval_requirement_reason = CASE
        WHEN item.human_approval_requirement_reason = N'migration_cutover'
            THEN N'migration_cutover'
        ELSE N'agent_non_pass'
    END,
    updated_at = SYSDATETIMEOFFSET()
FROM dbo.content_items AS item
WHERE item.deleted_at IS NULL
  AND item.status = N'draft'
  AND item.active_publish_attempt_id IS NULL
  AND item.agent_review_status = N'pending'
  AND EXISTS (
      SELECT 1
      FROM dbo.content_review_tasks AS task
      WHERE task.tenant_id = item.tenant_id
        AND task.content_item_id = item.id
        AND task.content_revision = item.content_revision
        AND task.status IN (N'failed', N'canceled_stale'));

SELECT @@ROWCOUNT AS repaired_content_item_count;
