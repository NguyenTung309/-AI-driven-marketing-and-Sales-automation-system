-- Migrate zalo conversations external_thread_id to match "page_id:thread_id" format
BEGIN TRANSACTION;

UPDATE c
SET c.external_thread_id = CONCAT(i.external_page_id, ':', c.external_thread_id)
FROM conversations c
INNER JOIN inboxes i ON c.inbox_id = i.id
WHERE c.platform = 'zalo'
  AND c.external_thread_id NOT LIKE '%:%';

COMMIT;
