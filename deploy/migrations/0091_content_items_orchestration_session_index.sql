-- Keep orphan-content cleanup bounded to one tenant/session/status lookup.
-- Separate from 0090 because SQL Server requires an index on a newly added column in a later migration file.
-- One SqlCommand; do not add GO.
IF COL_LENGTH(N'dbo.content_items', N'orchestration_session_id') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_content_items_tenant_orchestration_session_status'
         AND object_id = OBJECT_ID(N'dbo.content_items'))
BEGIN
    CREATE INDEX IX_content_items_tenant_orchestration_session_status
        ON dbo.content_items (tenant_id, orchestration_session_id, status);
END
