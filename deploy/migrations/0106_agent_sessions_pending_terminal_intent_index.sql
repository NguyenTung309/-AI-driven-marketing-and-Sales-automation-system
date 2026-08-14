-- Must remain separate from 0105: SQL Server compiles a file as one batch.
-- One SqlCommand; do not add GO.
SET XACT_ABORT ON;

IF COL_LENGTH(N'dbo.agent_sessions', N'pending_terminal_requested_at') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_agent_sessions_status_pending_terminal_requested_at'
         AND object_id = OBJECT_ID(N'dbo.agent_sessions'))
BEGIN
    CREATE INDEX IX_agent_sessions_status_pending_terminal_requested_at
        ON dbo.agent_sessions (status, pending_terminal_requested_at)
        WHERE pending_terminal_requested_at IS NOT NULL;
END
