-- 0088: prevent duplicate active Meta inboxes for the same tenant/page.
-- Provisioning is called by both OAuth page sync and webhook reconciliation; the unique filtered index
-- is the database-level race guard. Existing duplicate active rows must be cleaned up before applying.
IF EXISTS (
    SELECT 1
    FROM dbo.inboxes
    WHERE is_active = 1
      AND deleted_at IS NULL
    GROUP BY tenant_id, platform, external_page_id
    HAVING COUNT(*) > 1
)
    THROW 51088, 'meta_inbox_duplicate_identity', 1;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_inboxes_tenant_platform_external_active'
      AND object_id = OBJECT_ID(N'dbo.inboxes')
)
    CREATE UNIQUE INDEX UX_inboxes_tenant_platform_external_active
        ON dbo.inboxes (tenant_id, platform, external_page_id)
        WHERE is_active = 1 AND deleted_at IS NULL;
