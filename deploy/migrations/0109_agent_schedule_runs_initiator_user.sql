-- Captures the effective actor whose current grants authorize every scheduled side effect.
-- One SqlCommand; do not add GO.
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.agent_schedule_runs', N'U') IS NULL
    THROW 51000, 'dbo.agent_schedule_runs is missing; cannot add initiator_user_id.', 1;

IF COL_LENGTH(N'dbo.agent_schedule_runs', N'initiator_user_id') IS NULL
    ALTER TABLE dbo.agent_schedule_runs ADD initiator_user_id UNIQUEIDENTIFIER NULL;
