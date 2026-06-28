-- Backfill inbox_id cho conversation cu (chay SAU 0026 + 0027)
BEGIN TRANSACTION;

UPDATE c
SET c.inbox_id = (
    SELECT TOP 1 i.id
    FROM inboxes i
    WHERE i.tenant_id = c.tenant_id
      AND i.platform = c.platform
      AND i.is_active = 1
      AND (i.external_page_id = c.external_thread_id
           OR c.external_thread_id LIKE i.external_page_id + '%')
)
FROM conversations c
WHERE c.inbox_id IS NULL
  AND c.deleted_at IS NULL;

-- Fallback: conversation khong tim duoc inbox_id -> default inbox cung platform
UPDATE c
SET c.inbox_id = fallback.id
FROM conversations c
CROSS APPLY (
    SELECT TOP 1 id FROM inboxes
    WHERE tenant_id = c.tenant_id AND platform = c.platform AND is_active = 1
    ORDER BY created_at
) fallback
WHERE c.inbox_id IS NULL
  AND c.deleted_at IS NULL;

COMMIT;
