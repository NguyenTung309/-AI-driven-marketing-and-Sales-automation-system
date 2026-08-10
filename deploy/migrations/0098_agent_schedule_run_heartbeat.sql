-- 0098: only reap schedule executions whose owner has stopped renewing its lease heartbeat.
-- One SQL command/batch; no GO.
IF COL_LENGTH(N'dbo.agent_schedule_runs', N'last_heartbeat_at') IS NULL
    ALTER TABLE dbo.agent_schedule_runs
        ADD last_heartbeat_at DATETIMEOFFSET NULL;
