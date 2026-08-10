-- 0096: close schedule runs abandoned by an AgentService process crash before terminal status.
-- A started run triggers overlap=skip, so leaving it open permanently disables its schedule.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @now DATETIMEOFFSET = SYSUTCDATETIME();

UPDATE dbo.agent_schedule_runs
SET status = N'failed',
    error = N'stale_run_reaped (0096)',
    finished_at = @now
WHERE status = N'started'
  AND finished_at IS NULL
  AND started_at < DATEADD(hour, -2, @now);

UPDATE session_row
SET status = N'failed',
    finished_at = @now
FROM dbo.agent_sessions AS session_row
INNER JOIN dbo.agent_schedule_runs AS run_row ON run_row.session_id = session_row.id
WHERE session_row.status = N'running'
  AND session_row.started_at < DATEADD(hour, -6, @now)
  AND run_row.error = N'stale_run_reaped (0096)';

COMMIT TRANSACTION;