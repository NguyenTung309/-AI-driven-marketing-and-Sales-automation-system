-- Bound superseded-plan cleanup by tenant, session, plan generation, and human ownership.
-- Separate from 0103 because SQL Server requires an index on newly added columns in a later migration file.
-- One SqlCommand; do not add GO.
IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_content_items_tenant_orchestration_session_status'
      AND object_id = OBJECT_ID(N'dbo.content_items'))
BEGIN
    DROP INDEX IX_content_items_tenant_orchestration_session_status ON dbo.content_items;
END

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_content_items_orchestration_cleanup'
      AND object_id = OBJECT_ID(N'dbo.content_items'))
BEGIN
    CREATE INDEX IX_content_items_orchestration_cleanup
        ON dbo.content_items (
            tenant_id,
            orchestration_session_id,
            orchestration_plan_generation,
            status,
            orchestration_ownership_claimed_at);
END
