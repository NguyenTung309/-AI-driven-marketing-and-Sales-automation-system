-- Support tenant-enforcing orchestration provenance foreign keys.
-- One SqlCommand; do not add GO.
IF OBJECT_ID(N'dbo.agent_sessions', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'UX_agent_sessions_tenant_id'
         AND object_id = OBJECT_ID(N'dbo.agent_sessions'))
BEGIN
    CREATE UNIQUE INDEX UX_agent_sessions_tenant_id
        ON dbo.agent_sessions (tenant_id, id);
END
