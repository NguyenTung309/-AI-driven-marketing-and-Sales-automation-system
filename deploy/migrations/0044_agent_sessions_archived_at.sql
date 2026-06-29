SET XACT_ABORT ON;

IF COL_LENGTH(N'dbo.agent_sessions', N'archived_at') IS NULL
    ALTER TABLE dbo.agent_sessions ADD archived_at DATETIMEOFFSET NULL;
