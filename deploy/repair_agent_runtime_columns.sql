-- Repair cot runtime cua llm_configs/agents/agent_sessions/pancake_configs/embedding_configs/skill_files.
-- Idempotent: moi cau lenh tu kiem tra truoc khi doi schema.
-- Chay bang: type <file> ^| docker exec -i clawbot-sqlserver sqlcmd ... -b
-- KHONG them GO: ca file duoc gui nhu mot batch duy nhat.
SET QUOTED_IDENTIFIER ON;
SET ARITHABORT ON;
IF COL_LENGTH(N'dbo.llm_configs', N'timeout_seconds') IS NULL ALTER TABLE dbo.llm_configs ADD timeout_seconds INT NULL;
IF COL_LENGTH(N'dbo.llm_configs', N'max_output_tokens') IS NULL ALTER TABLE dbo.llm_configs ADD max_output_tokens INT NULL;
IF COL_LENGTH(N'dbo.llm_configs', N'supports_vision') IS NULL ALTER TABLE dbo.llm_configs ADD supports_vision BIT NULL;
IF COL_LENGTH(N'dbo.agents', N'llm_config_id') IS NULL ALTER TABLE dbo.agents ADD llm_config_id UNIQUEIDENTIFIER NULL;
IF COL_LENGTH(N'dbo.agents', N'llm_config_id') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_agents_llm_config_id' AND object_id = OBJECT_ID(N'dbo.agents')) EXEC(N'CREATE INDEX ix_agents_llm_config_id ON agents (llm_config_id);');
IF COL_LENGTH(N'dbo.agents', N'llm_config_id') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_agents_llm_configs_llm_config_id') EXEC(N'ALTER TABLE agents ADD CONSTRAINT fk_agents_llm_configs_llm_config_id FOREIGN KEY (llm_config_id) REFERENCES llm_configs (id) ON DELETE NO ACTION;');
IF COL_LENGTH(N'dbo.agent_sessions', N'requires_approval') IS NULL ALTER TABLE dbo.agent_sessions ADD requires_approval BIT NOT NULL CONSTRAINT DF_agent_sessions_requires_approval DEFAULT 0;
IF COL_LENGTH(N'dbo.agent_sessions', N'replan_count') IS NULL ALTER TABLE dbo.agent_sessions ADD replan_count INT NOT NULL CONSTRAINT DF_agent_sessions_replan_count DEFAULT 0;
IF COL_LENGTH(N'dbo.agent_sessions', N'row_version') IS NULL ALTER TABLE dbo.agent_sessions ADD row_version ROWVERSION;
IF COL_LENGTH(N'dbo.agent_sessions', N'archived_at') IS NULL ALTER TABLE dbo.agent_sessions ADD archived_at DATETIMEOFFSET NULL;
IF COL_LENGTH(N'dbo.agent_sessions', N'pending_terminal_generation') IS NULL ALTER TABLE dbo.agent_sessions ADD pending_terminal_generation INT NULL;
IF COL_LENGTH(N'dbo.agent_sessions', N'pending_terminal_requested_at') IS NULL ALTER TABLE dbo.agent_sessions ADD pending_terminal_requested_at DATETIMEOFFSET NULL;
IF COL_LENGTH(N'dbo.agent_sessions', N'pending_terminal_reason') IS NULL ALTER TABLE dbo.agent_sessions ADD pending_terminal_reason NVARCHAR(1024) NULL;
IF COL_LENGTH(N'dbo.tenants', N'require_orchestration_approval') IS NULL ALTER TABLE dbo.tenants ADD require_orchestration_approval BIT NOT NULL CONSTRAINT DF_tenants_require_orchestration_approval DEFAULT 0;
IF COL_LENGTH(N'dbo.processed_messages', N'tenant_id') IS NULL ALTER TABLE dbo.processed_messages ADD tenant_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_processed_messages_tenant_id DEFAULT '00000000-0000-0000-0000-000000000000';
IF COL_LENGTH(N'dbo.pancake_configs', N'channel') IS NOT NULL BEGIN
IF EXISTS (SELECT tenant_id FROM dbo.pancake_configs GROUP BY tenant_id HAVING COUNT(*) > 1)
    THROW 51000, 'Cannot consolidate pancake_configs while a tenant has multiple channel-specific rows.', 1;
DECLARE @pcuq nvarchar(200);
SELECT @pcuq = name FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.pancake_configs') AND type = 'UQ';
IF @pcuq IS NOT NULL EXEC(N'ALTER TABLE pancake_configs DROP CONSTRAINT ' + @pcuq);
DECLARE @pcdf nvarchar(200);
SELECT @pcdf = dc.name FROM sys.default_constraints dc INNER JOIN sys.columns c ON c.default_object_id = dc.object_id WHERE dc.parent_object_id = OBJECT_ID(N'dbo.pancake_configs') AND c.name = N'channel';
IF @pcdf IS NOT NULL EXEC(N'ALTER TABLE pancake_configs DROP CONSTRAINT ' + @pcdf);
EXEC(N'ALTER TABLE dbo.pancake_configs DROP COLUMN channel');
END;
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.pancake_configs') AND type = 'UQ') ALTER TABLE dbo.pancake_configs ADD CONSTRAINT UQ_pancake_configs_tenant_id UNIQUE (tenant_id);
IF COL_LENGTH(N'dbo.pancake_configs', N'base_url') IS NULL ALTER TABLE dbo.pancake_configs ADD base_url NVARCHAR(256) NOT NULL CONSTRAINT DF_pancake_configs_base_url DEFAULT N'https://pancake.vn/api/v1';
IF COL_LENGTH(N'dbo.pancake_configs', N'signature_header') IS NULL ALTER TABLE dbo.pancake_configs ADD signature_header NVARCHAR(64) NOT NULL CONSTRAINT DF_pancake_configs_signature_header DEFAULT N'x-pancake-signature';
IF COL_LENGTH(N'dbo.pancake_configs', N'signature_algo') IS NULL ALTER TABLE dbo.pancake_configs ADD signature_algo NVARCHAR(32) NOT NULL CONSTRAINT DF_pancake_configs_signature_algo DEFAULT N'hmac-sha256';
IF COL_LENGTH(N'dbo.pancake_configs', N'signature_encoding') IS NULL ALTER TABLE dbo.pancake_configs ADD signature_encoding NVARCHAR(16) NOT NULL CONSTRAINT DF_pancake_configs_signature_encoding DEFAULT N'hex';
IF COL_LENGTH(N'dbo.pancake_configs', N'send_path_template') IS NULL ALTER TABLE dbo.pancake_configs ADD send_path_template NVARCHAR(512) NOT NULL CONSTRAINT DF_pancake_configs_send_path_template DEFAULT N'/pages/{page_id}/conversations/{thread_id}/messages';
IF COL_LENGTH(N'dbo.pancake_configs', N'auth_mode') IS NULL ALTER TABLE dbo.pancake_configs ADD auth_mode NVARCHAR(16) NOT NULL CONSTRAINT DF_pancake_configs_auth_mode DEFAULT N'query';
IF COL_LENGTH(N'dbo.agent_definitions', N'kb_module_code') IS NULL ALTER TABLE dbo.agent_definitions ADD kb_module_code NVARCHAR(64) NULL;
IF OBJECT_ID(N'dbo.embedding_configs', N'U') IS NULL CREATE TABLE dbo.embedding_configs (id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, tenant_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.tenants(id) ON DELETE CASCADE, provider NVARCHAR(32) NOT NULL, model_id NVARCHAR(128) NOT NULL, display_name NVARCHAR(128) NULL, api_key_encrypted NVARCHAR(MAX) NOT NULL, base_url NVARCHAR(512) NULL, dimension INT NOT NULL CONSTRAINT df_embedding_configs_dimension DEFAULT 1536, is_active BIT NOT NULL CONSTRAINT df_embedding_configs_is_active DEFAULT 1, created_at DATETIMEOFFSET NOT NULL, updated_at DATETIMEOFFSET NOT NULL);
IF OBJECT_ID(N'dbo.embedding_configs', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_embedding_configs_tenant_id_is_active' AND object_id = OBJECT_ID(N'dbo.embedding_configs')) CREATE INDEX IX_embedding_configs_tenant_id_is_active ON dbo.embedding_configs (tenant_id, is_active);
IF COL_LENGTH(N'dbo.users', N'pancake_access_token_encrypted') IS NULL ALTER TABLE dbo.users ADD pancake_access_token_encrypted NVARCHAR(2048) NULL;
IF COL_LENGTH(N'dbo.users', N'pancake_access_token_updated_at') IS NULL ALTER TABLE dbo.users ADD pancake_access_token_updated_at DATETIMEOFFSET NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_agent_sessions_tenant_status_started_at' AND object_id = OBJECT_ID(N'dbo.agent_sessions')) EXEC(N'CREATE INDEX IX_agent_sessions_tenant_status_started_at ON agent_sessions (tenant_id, status, started_at);');
IF COL_LENGTH(N'dbo.agent_sessions', N'archived_at') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_agent_sessions_tenant_archived_started_at' AND object_id = OBJECT_ID(N'dbo.agent_sessions')) EXEC(N'CREATE INDEX IX_agent_sessions_tenant_archived_started_at ON agent_sessions (tenant_id, archived_at, started_at);');
IF COL_LENGTH(N'dbo.agent_sessions', N'pending_terminal_requested_at') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_agent_sessions_status_pending_terminal_requested_at' AND object_id = OBJECT_ID(N'dbo.agent_sessions')) EXEC(N'CREATE INDEX IX_agent_sessions_status_pending_terminal_requested_at ON agent_sessions (status, pending_terminal_requested_at) WHERE pending_terminal_requested_at IS NOT NULL;');
IF COL_LENGTH(N'dbo.agent_schedules', N'initiator_user_id') IS NULL ALTER TABLE dbo.agent_schedules ADD initiator_user_id UNIQUEIDENTIFIER NULL;
IF COL_LENGTH(N'dbo.agent_schedule_runs', N'initiator_user_id') IS NULL ALTER TABLE dbo.agent_schedule_runs ADD initiator_user_id UNIQUEIDENTIFIER NULL;
IF COL_LENGTH(N'dbo.agent_schedules', N'initiator_user_id') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_agent_schedules_tenant_initiator_user' AND object_id = OBJECT_ID(N'dbo.agent_schedules')) EXEC(N'CREATE INDEX IX_agent_schedules_tenant_initiator_user ON agent_schedules (tenant_id, initiator_user_id) WHERE initiator_user_id IS NOT NULL;');
IF COL_LENGTH(N'dbo.agent_schedules', N'trigger_type') IS NULL ALTER TABLE dbo.agent_schedules ADD trigger_type NVARCHAR(16) NOT NULL CONSTRAINT DF_agent_schedules_trigger_type DEFAULT N'cadence';
IF COL_LENGTH(N'dbo.agent_schedules', N'event_key') IS NULL ALTER TABLE dbo.agent_schedules ADD event_key NVARCHAR(64) NULL;
IF COL_LENGTH(N'dbo.tenants', N'monthly_cost_cap_usd') IS NULL ALTER TABLE dbo.tenants ADD monthly_cost_cap_usd DECIMAL(12,2) NULL;
IF COL_LENGTH(N'dbo.claude_cost_ledger', N'session_id') IS NULL ALTER TABLE dbo.claude_cost_ledger ADD session_id UNIQUEIDENTIFIER NULL;
IF OBJECT_ID(N'dbo.claude_cost_ledger', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_claude_cost_ledger_session_id' AND object_id = OBJECT_ID(N'dbo.claude_cost_ledger')) EXEC(N'CREATE INDEX IX_claude_cost_ledger_session_id ON claude_cost_ledger (session_id);');
IF OBJECT_ID(N'dbo.skill_files', N'U') IS NULL AND OBJECT_ID(N'dbo.tenants', N'U') IS NOT NULL CREATE TABLE dbo.skill_files (id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_skill_files PRIMARY KEY DEFAULT NEWID(), tenant_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.tenants(id) ON DELETE CASCADE, name NVARCHAR(128) NOT NULL, description NVARCHAR(512) NULL, content_md NVARCHAR(MAX) NOT NULL, created_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(), updated_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(), deleted_at DATETIMEOFFSET NULL);
IF OBJECT_ID(N'dbo.skill_files', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_skill_files_tenant_name' AND object_id = OBJECT_ID(N'dbo.skill_files')) EXEC(N'CREATE UNIQUE INDEX ix_skill_files_tenant_name ON dbo.skill_files (tenant_id, name) WHERE deleted_at IS NULL;');
