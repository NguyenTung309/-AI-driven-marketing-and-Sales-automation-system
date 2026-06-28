-- 0040: filtered index on agent_sessions.user_id (SPEC-16 P3-3).
-- Separate file from 0039's ALTER ADD because SQL Server parses a batch before executing it,
-- so an index referencing a just-added column must be its own batch.

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_agent_sessions_tenant_user_started')
    CREATE INDEX IX_agent_sessions_tenant_user_started ON dbo.agent_sessions (tenant_id, user_id, started_at)
    WHERE user_id IS NOT NULL;
