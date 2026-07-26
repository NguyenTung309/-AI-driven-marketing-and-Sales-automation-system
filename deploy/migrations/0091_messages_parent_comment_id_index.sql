-- 0091: index per-tenant comment handling lookups for anti-spam/idempotency caps.
-- Kept separate from 0090 because SQL Server cannot compile an index on a column added in the same batch.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_messages_tenant_parent_comment_id'
      AND object_id = OBJECT_ID(N'dbo.messages')
)
    CREATE INDEX IX_messages_tenant_parent_comment_id
        ON dbo.messages (tenant_id, parent_comment_id)
        WHERE parent_comment_id IS NOT NULL;
