-- Durable cancellation/failure intent while an external content publication is in flight.
-- One SqlCommand; do not add GO.
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.agent_sessions', N'U') IS NULL
    THROW 51000, 'dbo.agent_sessions is missing; cannot add terminal intent columns.', 1;

IF COL_LENGTH(N'dbo.agent_sessions', N'pending_terminal_generation') IS NULL
    ALTER TABLE dbo.agent_sessions ADD pending_terminal_generation INT NULL;

IF COL_LENGTH(N'dbo.agent_sessions', N'pending_terminal_requested_at') IS NULL
    ALTER TABLE dbo.agent_sessions ADD pending_terminal_requested_at DATETIMEOFFSET NULL;

IF COL_LENGTH(N'dbo.agent_sessions', N'pending_terminal_reason') IS NULL
    ALTER TABLE dbo.agent_sessions ADD pending_terminal_reason NVARCHAR(1024) NULL;
