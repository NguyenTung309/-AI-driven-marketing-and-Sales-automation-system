-- Captures the authenticated creator whose current grants govern each scheduled LLM run.
-- Nullable for pre-existing rows, which must fail closed until recreated or reauthorized.
-- One SqlCommand; do not add GO.
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.agent_schedules', N'U') IS NULL
    THROW 51000, 'dbo.agent_schedules is missing; cannot add initiator_user_id.', 1;

IF COL_LENGTH(N'dbo.agent_schedules', N'initiator_user_id') IS NULL
    ALTER TABLE dbo.agent_schedules ADD initiator_user_id UNIQUEIDENTIFIER NULL;
