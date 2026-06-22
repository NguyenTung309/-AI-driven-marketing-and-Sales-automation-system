-- 0030: Agent session orchestration listing index
-- Separate file because the indexed status column is part of the orchestration state path.

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_agent_sessions_tenant_status_started_at' AND object_id = OBJECT_ID(N'dbo.agent_sessions'))
    EXEC(N'CREATE INDEX IX_agent_sessions_tenant_status_started_at ON dbo.agent_sessions (tenant_id, status, started_at);');
