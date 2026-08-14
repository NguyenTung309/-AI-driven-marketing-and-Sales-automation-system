-- Separate from 0107 because SQL Server compiles each migration file as a single batch.
-- One SqlCommand; do not add GO.
SET XACT_ABORT ON;

IF COL_LENGTH(N'dbo.agent_schedules', N'initiator_user_id') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_agent_schedules_tenant_initiator_user'
         AND object_id = OBJECT_ID(N'dbo.agent_schedules'))
BEGIN
    CREATE INDEX IX_agent_schedules_tenant_initiator_user
        ON dbo.agent_schedules (tenant_id, initiator_user_id)
        WHERE initiator_user_id IS NOT NULL;
END
