-- 0032: Dynamic agent orchestration v2 indexes
-- Separate from table creation for clearer local repair and replay.

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_agent_definitions_tenant_code' AND object_id = OBJECT_ID(N'dbo.agent_definitions'))
    CREATE UNIQUE INDEX UX_agent_definitions_tenant_code ON dbo.agent_definitions (tenant_id, code);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_agent_definitions_tenant_orchestratable' AND object_id = OBJECT_ID(N'dbo.agent_definitions'))
    CREATE INDEX IX_agent_definitions_tenant_orchestratable ON dbo.agent_definitions (tenant_id, is_orchestratable);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_agent_definitions_llm_config_id' AND object_id = OBJECT_ID(N'dbo.agent_definitions'))
    CREATE INDEX IX_agent_definitions_llm_config_id ON dbo.agent_definitions (llm_config_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_agent_a2a_messages_claim' AND object_id = OBJECT_ID(N'dbo.agent_a2a_messages'))
    CREATE INDEX IX_agent_a2a_messages_claim ON dbo.agent_a2a_messages (tenant_id, session_id, status, created_at);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_agent_a2a_messages_from_agent_definition_id' AND object_id = OBJECT_ID(N'dbo.agent_a2a_messages'))
    CREATE INDEX IX_agent_a2a_messages_from_agent_definition_id ON dbo.agent_a2a_messages (from_agent_definition_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_agent_a2a_messages_to_agent_definition_id' AND object_id = OBJECT_ID(N'dbo.agent_a2a_messages'))
    CREATE INDEX IX_agent_a2a_messages_to_agent_definition_id ON dbo.agent_a2a_messages (to_agent_definition_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_agent_schedules_due' AND object_id = OBJECT_ID(N'dbo.agent_schedules'))
    CREATE INDEX IX_agent_schedules_due ON dbo.agent_schedules (tenant_id, is_active, next_run_at);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_agent_schedules_tenant_name' AND object_id = OBJECT_ID(N'dbo.agent_schedules'))
    CREATE INDEX IX_agent_schedules_tenant_name ON dbo.agent_schedules (tenant_id, name);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_agent_schedule_runs_schedule_window' AND object_id = OBJECT_ID(N'dbo.agent_schedule_runs'))
    CREATE UNIQUE INDEX UX_agent_schedule_runs_schedule_window ON dbo.agent_schedule_runs (schedule_id, window_key);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_agent_schedule_runs_tenant_status_started_at' AND object_id = OBJECT_ID(N'dbo.agent_schedule_runs'))
    CREATE INDEX IX_agent_schedule_runs_tenant_status_started_at ON dbo.agent_schedule_runs (tenant_id, status, started_at);
