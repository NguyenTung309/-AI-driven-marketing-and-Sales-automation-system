SET XACT_ABORT ON;

IF COL_LENGTH(N'dbo.agent_sessions', N'archived_at') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_agent_sessions_tenant_archived_started_at' AND object_id = OBJECT_ID(N'dbo.agent_sessions'))
    CREATE INDEX IX_agent_sessions_tenant_archived_started_at ON dbo.agent_sessions (tenant_id, archived_at, started_at);
