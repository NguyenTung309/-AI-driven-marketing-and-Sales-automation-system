-- 0028: Agent session orchestration state columns
-- Adds autonomy snapshot, bounded re-plan counter, and optimistic concurrency token.

IF COL_LENGTH(N'dbo.agent_sessions', N'requires_approval') IS NULL
    EXEC(N'ALTER TABLE dbo.agent_sessions ADD requires_approval BIT NOT NULL CONSTRAINT DF_agent_sessions_requires_approval DEFAULT 0;');

IF COL_LENGTH(N'dbo.agent_sessions', N'replan_count') IS NULL
    EXEC(N'ALTER TABLE dbo.agent_sessions ADD replan_count INT NOT NULL CONSTRAINT DF_agent_sessions_replan_count DEFAULT 0;');

IF COL_LENGTH(N'dbo.agent_sessions', N'row_version') IS NULL
    EXEC(N'ALTER TABLE dbo.agent_sessions ADD row_version ROWVERSION;');
