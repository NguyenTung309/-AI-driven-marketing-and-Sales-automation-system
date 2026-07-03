-- 0046: C2 — event-triggered schedules: trigger_type ('cadence' | 'event') + event_key.
-- Event schedules sleep at NextRunAt = 9999-12-31; the dispatcher pulls NextRunAt to now when the event fires.
IF COL_LENGTH('dbo.agent_schedules', 'trigger_type') IS NULL
BEGIN
    ALTER TABLE dbo.agent_schedules ADD trigger_type NVARCHAR(16) NOT NULL CONSTRAINT DF_agent_schedules_trigger_type DEFAULT N'cadence';
END
IF COL_LENGTH('dbo.agent_schedules', 'event_key') IS NULL
BEGIN
    ALTER TABLE dbo.agent_schedules ADD event_key NVARCHAR(64) NULL;
END
