-- 0039: agent_sessions.user_id — the user who initiated the orchestration run (SPEC-16 P3-3),
-- so terminal notifications can be targeted to that user and the run list can filter by user.
-- ALTER only; the filtered index on user_id lives in 0040 (per ADR-009: an index referencing an
-- ALTER-added column needs its own batch/file because SQL Server parses the whole batch first).

IF COL_LENGTH(N'dbo.agent_sessions', N'user_id') IS NULL
    ALTER TABLE dbo.agent_sessions ADD user_id UNIQUEIDENTIFIER NULL;
