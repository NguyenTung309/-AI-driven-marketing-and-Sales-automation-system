-- 01_cleanup_duplicate_inbox_members.sql
-- Data cleanup: remove duplicate InboxMembers (1 inbox -> nhieu sale)
-- Chay BEFORE migration 0031_unique_inbox_members.sql
-- Idempotent: safe to run multiple times
BEGIN TRANSACTION;

-- 1. Count bao nhieu inbox bi duplicate
DECLARE @duplicateCount INT;
SELECT @duplicateCount = COUNT(*)
FROM (
    SELECT inbox_id
    FROM inbox_members
    GROUP BY inbox_id
    HAVING COUNT(*) > 1
) dup;
PRINT 'Inboxes with >1 member: ' + CAST(@duplicateCount AS NVARCHAR(10));

-- 2. Xoa duplicate members, keep 1 member/inbox (lowest agent_id)
WITH ranked AS (
    SELECT
        inbox_id,
        agent_id,
        ROW_NUMBER() OVER (PARTITION BY inbox_id ORDER BY agent_id) AS rn
    FROM inbox_members
)
DELETE FROM inbox_members
WHERE EXISTS (
    SELECT 1 FROM ranked
    WHERE ranked.inbox_id = inbox_members.inbox_id
      AND ranked.agent_id = inbox_members.agent_id
      AND ranked.rn > 1
);

-- 3. Assert: khong con inbox nao >1 member
DECLARE @remaining INT;
SELECT @remaining = COUNT(*)
FROM (
    SELECT inbox_id
    FROM inbox_members
    GROUP BY inbox_id
    HAVING COUNT(*) > 1
) dup;
IF @remaining > 0
BEGIN
    RAISERROR('Cleanup failed: %d inboxes still have multiple members', 16, 1, @remaining);
    ROLLBACK;
    RETURN;
END

PRINT 'Cleanup OK: All inboxes have 1 member. Safe to run migration 0031.';

COMMIT;
GO
