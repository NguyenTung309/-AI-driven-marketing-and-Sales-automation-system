-- 0097: enforce overlap=skip across concurrent AgentService instances.
-- Reap historical duplicate active runs before installing the filtered unique index.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @now DATETIMEOFFSET = SYSUTCDATETIME();
DECLARE @reaped_sessions TABLE (session_id UNIQUEIDENTIFIER NULL);

;WITH ranked_active_runs AS
(
    SELECT id,
           ROW_NUMBER() OVER (PARTITION BY schedule_id ORDER BY started_at DESC, id DESC) AS row_number
    FROM dbo.agent_schedule_runs
    WHERE status = N'started'
      AND finished_at IS NULL
)
UPDATE run_row
SET status = N'failed',
    error = N'duplicate_active_run_reaped (0097)',
    finished_at = @now
OUTPUT inserted.session_id INTO @reaped_sessions(session_id)
FROM dbo.agent_schedule_runs AS run_row
INNER JOIN ranked_active_runs AS ranked ON ranked.id = run_row.id
WHERE ranked.row_number > 1;

UPDATE session_row
SET status = N'failed',
    finished_at = @now
FROM dbo.agent_sessions AS session_row
INNER JOIN @reaped_sessions AS reaped ON reaped.session_id = session_row.id
WHERE session_row.status = N'running';

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_agent_schedule_runs_single_active'
      AND object_id = OBJECT_ID(N'dbo.agent_schedule_runs')
)
    CREATE UNIQUE INDEX UX_agent_schedule_runs_single_active
        ON dbo.agent_schedule_runs (schedule_id)
        WHERE status = N'started' AND finished_at IS NULL;

COMMIT TRANSACTION;
