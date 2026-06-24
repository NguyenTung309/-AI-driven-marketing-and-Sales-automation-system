-- 0031: Dynamic agent orchestration v2 tables
-- Sub-agents as data, A2A mailbox, recurring schedules, and idempotent schedule runs.

IF OBJECT_ID(N'dbo.agent_definitions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.agent_definitions (
        id                   UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_agent_definitions PRIMARY KEY,
        tenant_id            UNIQUEIDENTIFIER NOT NULL,
        code                 NVARCHAR(64) NOT NULL,
        display_name         NVARCHAR(256) NOT NULL,
        agent_type           NVARCHAR(32) NOT NULL,
        persona_prompt       NVARCHAR(MAX) NOT NULL,
        allowed_tools_json   NVARCHAR(MAX) NOT NULL CONSTRAINT DF_agent_definitions_allowed_tools_json DEFAULT N'[]',
        input_schema_json    NVARCHAR(MAX) NOT NULL CONSTRAINT DF_agent_definitions_input_schema_json DEFAULT N'{}',
        output_schema_json   NVARCHAR(MAX) NOT NULL CONSTRAINT DF_agent_definitions_output_schema_json DEFAULT N'{}',
        memory_scope         NVARCHAR(32) NOT NULL CONSTRAINT DF_agent_definitions_memory_scope DEFAULT N'none',
        llm_config_id        UNIQUEIDENTIFIER NULL,
        is_orchestratable    BIT NOT NULL CONSTRAINT DF_agent_definitions_is_orchestratable DEFAULT 1,
        version              INT NOT NULL CONSTRAINT DF_agent_definitions_version DEFAULT 1,
        created_at           DATETIMEOFFSET NOT NULL,
        updated_at           DATETIMEOFFSET NOT NULL,
        deleted_at           DATETIMEOFFSET NULL,
        CONSTRAINT FK_agent_definitions_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES dbo.tenants(id) ON DELETE CASCADE,
        CONSTRAINT FK_agent_definitions_llm_configs_llm_config_id FOREIGN KEY (llm_config_id) REFERENCES dbo.llm_configs(id) ON DELETE NO ACTION
    );
END

IF OBJECT_ID(N'dbo.agent_a2a_messages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.agent_a2a_messages (
        id                         UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_agent_a2a_messages PRIMARY KEY,
        tenant_id                  UNIQUEIDENTIFIER NOT NULL,
        session_id                 UNIQUEIDENTIFIER NOT NULL,
        from_agent_definition_id   UNIQUEIDENTIFIER NULL,
        to_agent_definition_id     UNIQUEIDENTIFIER NOT NULL,
        task_id                    NVARCHAR(128) NOT NULL,
        intent                     NVARCHAR(32) NOT NULL,
        payload_json               NVARCHAR(MAX) NOT NULL CONSTRAINT DF_agent_a2a_messages_payload_json DEFAULT N'{}',
        status                     NVARCHAR(32) NOT NULL CONSTRAINT DF_agent_a2a_messages_status DEFAULT N'pending',
        error                      NVARCHAR(1024) NULL,
        created_at                 DATETIMEOFFSET NOT NULL,
        processed_at               DATETIMEOFFSET NULL,
        CONSTRAINT FK_agent_a2a_messages_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES dbo.tenants(id) ON DELETE NO ACTION,
        CONSTRAINT FK_agent_a2a_messages_agent_sessions_session_id FOREIGN KEY (session_id) REFERENCES dbo.agent_sessions(id) ON DELETE CASCADE,
        CONSTRAINT FK_agent_a2a_messages_agent_definitions_from FOREIGN KEY (from_agent_definition_id) REFERENCES dbo.agent_definitions(id) ON DELETE NO ACTION,
        CONSTRAINT FK_agent_a2a_messages_agent_definitions_to FOREIGN KEY (to_agent_definition_id) REFERENCES dbo.agent_definitions(id) ON DELETE NO ACTION
    );
END

IF OBJECT_ID(N'dbo.agent_schedules', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.agent_schedules (
        id                    UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_agent_schedules PRIMARY KEY,
        tenant_id             UNIQUEIDENTIFIER NOT NULL,
        name                  NVARCHAR(128) NOT NULL,
        goal_template         NVARCHAR(MAX) NOT NULL,
        cadence               NVARCHAR(16) NOT NULL,
        cron_expression       NVARCHAR(128) NULL,
        timezone_id           NVARCHAR(128) NOT NULL,
        next_run_at           DATETIMEOFFSET NOT NULL,
        last_run_at           DATETIMEOFFSET NULL,
        overlap_policy        NVARCHAR(32) NOT NULL CONSTRAINT DF_agent_schedules_overlap_policy DEFAULT N'skip',
        misfire_policy        NVARCHAR(32) NOT NULL CONSTRAINT DF_agent_schedules_misfire_policy DEFAULT N'skip_missed',
        requires_approval     BIT NOT NULL CONSTRAINT DF_agent_schedules_requires_approval DEFAULT 0,
        approval_policy_json  NVARCHAR(MAX) NULL,
        is_active             BIT NOT NULL CONSTRAINT DF_agent_schedules_is_active DEFAULT 1,
        created_at            DATETIMEOFFSET NOT NULL,
        updated_at            DATETIMEOFFSET NOT NULL,
        deleted_at            DATETIMEOFFSET NULL,
        CONSTRAINT FK_agent_schedules_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES dbo.tenants(id) ON DELETE CASCADE
    );
END

IF OBJECT_ID(N'dbo.agent_schedule_runs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.agent_schedule_runs (
        id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_agent_schedule_runs PRIMARY KEY,
        tenant_id     UNIQUEIDENTIFIER NOT NULL,
        schedule_id   UNIQUEIDENTIFIER NOT NULL,
        session_id    UNIQUEIDENTIFIER NULL,
        window_key    NVARCHAR(128) NOT NULL,
        status        NVARCHAR(32) NOT NULL CONSTRAINT DF_agent_schedule_runs_status DEFAULT N'started',
        error         NVARCHAR(1024) NULL,
        started_at    DATETIMEOFFSET NOT NULL,
        finished_at   DATETIMEOFFSET NULL,
        CONSTRAINT FK_agent_schedule_runs_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES dbo.tenants(id) ON DELETE NO ACTION,
        CONSTRAINT FK_agent_schedule_runs_agent_schedules_schedule_id FOREIGN KEY (schedule_id) REFERENCES dbo.agent_schedules(id) ON DELETE CASCADE,
        CONSTRAINT FK_agent_schedule_runs_agent_sessions_session_id FOREIGN KEY (session_id) REFERENCES dbo.agent_sessions(id) ON DELETE NO ACTION
    );
END
