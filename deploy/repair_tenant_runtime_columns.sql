-- Runtime repair for existing local DBs that skip full migration replay.
-- One SqlCommand, no GO. Idempotent via COL_LENGTH guards.
SET QUOTED_IDENTIFIER ON;
SET ARITHABORT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.tenants', N'U') IS NULL
BEGIN
    RAISERROR(N'dbo.tenants is missing; cannot repair tenant runtime columns.', 16, 1);
    RETURN;
END;

BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.tenants', N'monthly_cost_cap_usd') IS NULL
    ALTER TABLE dbo.tenants ADD monthly_cost_cap_usd DECIMAL(12, 2) NULL;

IF COL_LENGTH(N'dbo.tenants', N'require_content_review') IS NULL
    ALTER TABLE dbo.tenants ADD require_content_review BIT NOT NULL
        CONSTRAINT DF_tenants_require_content_review DEFAULT 0;

IF COL_LENGTH(N'dbo.tenants', N'content_publishing_approval_policy') IS NULL
    ALTER TABLE dbo.tenants ADD content_publishing_approval_policy NVARCHAR(32) NOT NULL
        CONSTRAINT DF_tenants_content_publishing_policy DEFAULT N'human_required';

IF COL_LENGTH(N'dbo.tenants', N'content_publishing_policy_version') IS NULL
    ALTER TABLE dbo.tenants ADD content_publishing_policy_version BIGINT NOT NULL
        CONSTRAINT DF_tenants_content_publishing_policy_version DEFAULT 1;

IF COL_LENGTH(N'dbo.tenants', N'content_publishing_policy_updated_at') IS NULL
    ALTER TABLE dbo.tenants ADD content_publishing_policy_updated_at DATETIMEOFFSET NOT NULL
        CONSTRAINT DF_tenants_content_publishing_policy_updated_at DEFAULT SYSDATETIMEOFFSET();

IF COL_LENGTH(N'dbo.tenants', N'require_chat_reply_approval') IS NULL
    ALTER TABLE dbo.tenants ADD require_chat_reply_approval BIT NOT NULL
        CONSTRAINT DF_tenants_require_chat_reply_approval DEFAULT 0;

IF COL_LENGTH(N'dbo.tenants', N'require_kb_human_review') IS NULL
    ALTER TABLE dbo.tenants ADD require_kb_human_review BIT NOT NULL
        CONSTRAINT DF_tenants_require_kb_human_review DEFAULT 0;

IF COL_LENGTH(N'dbo.tenants', N'ai_auto_reply_resume_minutes') IS NULL
    ALTER TABLE dbo.tenants ADD ai_auto_reply_resume_minutes INT NOT NULL
        CONSTRAINT DF_tenants_ai_resume_minutes DEFAULT 5;

IF COL_LENGTH(N'dbo.tenants', N'skip_chat_reply_review') IS NULL
    ALTER TABLE dbo.tenants ADD skip_chat_reply_review BIT NOT NULL
        CONSTRAINT DF_tenants_skip_chat_reply_review DEFAULT 0;

IF COL_LENGTH(N'dbo.tenants', N'idle_alert_minutes') IS NULL
    ALTER TABLE dbo.tenants ADD idle_alert_minutes INT NOT NULL
        CONSTRAINT DF_tenants_idle_alert_minutes DEFAULT 5;

IF COL_LENGTH(N'dbo.tenants', N'lead_lost_after_days') IS NULL
    ALTER TABLE dbo.tenants ADD lead_lost_after_days INT NOT NULL
        CONSTRAINT DF_tenants_lead_lost_after_days DEFAULT 60;

IF COL_LENGTH(N'dbo.tenants', N'auto_approve_lead_revenue') IS NULL
    ALTER TABLE dbo.tenants ADD auto_approve_lead_revenue BIT NOT NULL
        CONSTRAINT DF_tenants_auto_approve_lead_revenue DEFAULT 0;

-- Content publishing approval policy runtime columns (0076).
IF OBJECT_ID(N'dbo.content_items', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.content_items', N'content_revision') IS NULL
        ALTER TABLE dbo.content_items ADD content_revision INT NOT NULL CONSTRAINT DF_content_items_content_revision DEFAULT 1;
    IF COL_LENGTH(N'dbo.content_items', N'agent_review_status') IS NULL
        ALTER TABLE dbo.content_items ADD agent_review_status NVARCHAR(24) NOT NULL CONSTRAINT DF_content_items_agent_review_status DEFAULT N'pending';
    IF COL_LENGTH(N'dbo.content_items', N'agent_reviewed_revision') IS NULL
        ALTER TABLE dbo.content_items ADD agent_reviewed_revision INT NULL;
    IF COL_LENGTH(N'dbo.content_items', N'reviewed_by_agent_id') IS NULL
        ALTER TABLE dbo.content_items ADD reviewed_by_agent_id UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH(N'dbo.content_items', N'agent_review_started_at') IS NULL
        ALTER TABLE dbo.content_items ADD agent_review_started_at DATETIMEOFFSET NULL;
    IF COL_LENGTH(N'dbo.content_items', N'agent_reviewed_at') IS NULL
        ALTER TABLE dbo.content_items ADD agent_reviewed_at DATETIMEOFFSET NULL;
    IF COL_LENGTH(N'dbo.content_items', N'agent_review_reason') IS NULL
        ALTER TABLE dbo.content_items ADD agent_review_reason NVARCHAR(1024) NULL;
    IF COL_LENGTH(N'dbo.content_items', N'image_review_status') IS NULL
        ALTER TABLE dbo.content_items ADD image_review_status NVARCHAR(24) NOT NULL CONSTRAINT DF_content_items_image_review_status DEFAULT N'pending';
    IF COL_LENGTH(N'dbo.content_items', N'reviewed_image_count') IS NULL
        ALTER TABLE dbo.content_items ADD reviewed_image_count INT NOT NULL CONSTRAINT DF_content_items_reviewed_image_count DEFAULT 0;
    IF COL_LENGTH(N'dbo.content_items', N'agent_review_attempt_count') IS NULL
        ALTER TABLE dbo.content_items ADD agent_review_attempt_count INT NOT NULL CONSTRAINT DF_content_items_agent_review_attempt_count DEFAULT 0;
    IF COL_LENGTH(N'dbo.content_items', N'publishing_policy_applied') IS NULL
        ALTER TABLE dbo.content_items ADD publishing_policy_applied NVARCHAR(32) NULL;
    IF COL_LENGTH(N'dbo.content_items', N'publishing_policy_version_applied') IS NULL
        ALTER TABLE dbo.content_items ADD publishing_policy_version_applied BIGINT NULL;
    IF COL_LENGTH(N'dbo.content_items', N'human_approval_requirement_reason') IS NULL
        ALTER TABLE dbo.content_items ADD human_approval_requirement_reason NVARCHAR(32) NULL;
    IF COL_LENGTH(N'dbo.content_items', N'approved_revision') IS NULL
        ALTER TABLE dbo.content_items ADD approved_revision INT NULL;
    IF COL_LENGTH(N'dbo.content_items', N'approval_mode') IS NULL
        ALTER TABLE dbo.content_items ADD approval_mode NVARCHAR(16) NULL;
    IF COL_LENGTH(N'dbo.content_items', N'approval_reason') IS NULL
        ALTER TABLE dbo.content_items ADD approval_reason NVARCHAR(1024) NULL;
    IF COL_LENGTH(N'dbo.content_items', N'active_publish_attempt_id') IS NULL
        ALTER TABLE dbo.content_items ADD active_publish_attempt_id UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH(N'dbo.content_items', N'row_version') IS NULL
        ALTER TABLE dbo.content_items ADD row_version ROWVERSION NOT NULL;
END

IF OBJECT_ID(N'dbo.content_review_tasks', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.content_review_tasks', N'claimed_lease_token') IS NULL
    ALTER TABLE dbo.content_review_tasks ADD claimed_lease_token UNIQUEIDENTIFIER NULL;

IF OBJECT_ID(N'dbo.content_schedule', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.content_schedule', N'content_revision') IS NULL
        ALTER TABLE dbo.content_schedule ADD content_revision INT NULL;
    IF COL_LENGTH(N'dbo.content_schedule', N'publish_target_id') IS NULL
        ALTER TABLE dbo.content_schedule ADD publish_target_id UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH(N'dbo.content_schedule', N'approval_mode') IS NULL
        ALTER TABLE dbo.content_schedule ADD approval_mode NVARCHAR(16) NULL;
    IF COL_LENGTH(N'dbo.content_schedule', N'publishing_policy_version_applied') IS NULL
        ALTER TABLE dbo.content_schedule ADD publishing_policy_version_applied BIGINT NULL;
    IF COL_LENGTH(N'dbo.content_schedule', N'next_attempt_at') IS NULL
        ALTER TABLE dbo.content_schedule ADD next_attempt_at DATETIMEOFFSET NULL;
    IF COL_LENGTH(N'dbo.content_schedule', N'last_error_code') IS NULL
        ALTER TABLE dbo.content_schedule ADD last_error_code NVARCHAR(128) NULL;
    IF COL_LENGTH(N'dbo.content_schedule', N'row_version') IS NULL
        ALTER TABLE dbo.content_schedule ADD row_version ROWVERSION NOT NULL;
END

-- lead_revenues table + KPI revenue (0073/0074) + invariants (0075)
IF OBJECT_ID(N'dbo.lead_revenues', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.lead_revenues (
        id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_lead_revenues PRIMARY KEY,
        tenant_id UNIQUEIDENTIFIER NOT NULL,
        lead_id UNIQUEIDENTIFIER NOT NULL,
        amount DECIMAL(18,2) NOT NULL,
        currency NVARCHAR(8) NOT NULL CONSTRAINT DF_lead_revenues_currency DEFAULT N'VND',
        source NVARCHAR(16) NOT NULL,
        status NVARCHAR(16) NOT NULL,
        evidence NVARCHAR(1000) NULL,
        proposed_by UNIQUEIDENTIFIER NULL,
        decided_by UNIQUEIDENTIFIER NULL,
        created_at DATETIMEOFFSET NOT NULL,
        decided_at DATETIMEOFFSET NULL
    );
    CREATE INDEX IX_lead_revenues_tenant_status ON dbo.lead_revenues (tenant_id, status, created_at DESC);
    CREATE INDEX IX_lead_revenues_lead ON dbo.lead_revenues (lead_id);
END

-- kpi_daily có thể chưa tồn tại trên schema cực cũ — chỉ ALTER khi bảng đã có.
IF OBJECT_ID(N'dbo.kpi_daily', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.kpi_daily', N'revenue') IS NULL
    ALTER TABLE dbo.kpi_daily ADD revenue DECIMAL(18,2) NULL;

IF OBJECT_ID(N'dbo.lead_revenues', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.leads', N'U') IS NOT NULL
       AND NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE name = N'FK_lead_revenues_leads' AND parent_object_id = OBJECT_ID(N'dbo.lead_revenues'))
        ALTER TABLE dbo.lead_revenues WITH NOCHECK
            ADD CONSTRAINT FK_lead_revenues_leads
            FOREIGN KEY (lead_id) REFERENCES dbo.leads(id) ON DELETE CASCADE;

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'CK_lead_revenues_amount' AND parent_object_id = OBJECT_ID(N'dbo.lead_revenues'))
        ALTER TABLE dbo.lead_revenues WITH NOCHECK
            ADD CONSTRAINT CK_lead_revenues_amount
            CHECK (amount > 0 AND amount <= 10000000000 AND currency = N'VND');

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'UX_lead_revenues_one_active' AND object_id = OBJECT_ID(N'dbo.lead_revenues'))
        CREATE UNIQUE INDEX UX_lead_revenues_one_active
            ON dbo.lead_revenues (lead_id)
            WHERE status IN (N'pending', N'approved');
END


-- Phase 6.1 content workflow runtime gate (idempotent repair for existing local DBs).
IF OBJECT_ID(N'dbo.content_workflow_runtime_gate', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.content_workflow_runtime_gate (
        id TINYINT NOT NULL,
        publication_paused BIT NOT NULL CONSTRAINT DF_content_workflow_runtime_gate_paused DEFAULT (0),
        minimum_writer_version INT NOT NULL CONSTRAINT DF_content_workflow_runtime_gate_min_writer DEFAULT (0),
        updated_at DATETIMEOFFSET NOT NULL CONSTRAINT DF_content_workflow_runtime_gate_updated DEFAULT (SYSDATETIMEOFFSET()),
        updated_by NVARCHAR(128) NULL,
        notes NVARCHAR(1024) NULL,
        CONSTRAINT PK_content_workflow_runtime_gate PRIMARY KEY (id),
        CONSTRAINT CK_content_workflow_runtime_gate_singleton CHECK (id = 1),
        CONSTRAINT CK_content_workflow_runtime_gate_min_writer CHECK (minimum_writer_version >= 0)
    );
END;

IF OBJECT_ID(N'dbo.content_workflow_runtime_gate', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.content_workflow_runtime_gate WHERE id = 1)
BEGIN
    INSERT INTO dbo.content_workflow_runtime_gate (
        id, publication_paused, minimum_writer_version, updated_at, updated_by, notes)
    VALUES (
        1, 0, 0, SYSDATETIMEOFFSET(), N'system',
        N'Permissive default from repair path.');
END;

-- Phase 2.6: nullable vision capability override (existing DB repair path).
IF OBJECT_ID(N'dbo.llm_configs', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.llm_configs', N'supports_vision') IS NULL
    EXEC(N'ALTER TABLE dbo.llm_configs ADD supports_vision BIT NULL;');

-- Phase 6.1: recreate writer-gate triggers (same semantics as 0080).
IF OBJECT_ID(N'dbo.TR_content_publish_attempts_writer_gate', N'TR') IS NOT NULL
    DROP TRIGGER dbo.TR_content_publish_attempts_writer_gate;

IF OBJECT_ID(N'dbo.content_publish_attempts', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE TRIGGER dbo.TR_content_publish_attempts_writer_gate
ON dbo.content_publish_attempts
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) RETURN;

    DECLARE @paused BIT = 0;
    DECLARE @min_writer INT = 0;
    DECLARE @writer INT = TRY_CONVERT(INT, SESSION_CONTEXT(N''clawbot_content_writer_version''));

    SELECT
        @paused = publication_paused,
        @min_writer = minimum_writer_version
    FROM dbo.content_workflow_runtime_gate WITH (UPDLOCK, HOLDLOCK)
    WHERE id = 1;

    IF @paused = 1
    BEGIN
        THROW 53001, ''content_publication_paused'', 1;
    END;

    IF @min_writer > 0 AND (@writer IS NULL OR @writer < @min_writer)
    BEGIN
        THROW 53002, ''content_writer_version_too_low'', 1;
    END;
END');
END;

-- Instagram target snapshot repair. Dynamic SQL is required because this script may add and
-- reference provider_target_id in the same SqlCommand batch. Only idle active rows are held;
-- publishing/outcome_unknown rows retain their in-flight or reconciliation state.
DECLARE @repairScheduleWriterGateWasEnabled BIT = 0;
IF OBJECT_ID(N'dbo.TR_content_schedule_writer_gate', N'TR') IS NOT NULL
   AND OBJECTPROPERTYEX(OBJECT_ID(N'dbo.TR_content_schedule_writer_gate'), N'ExecIsTriggerDisabled') = 0
BEGIN
    SET @repairScheduleWriterGateWasEnabled = 1;
    DISABLE TRIGGER dbo.TR_content_schedule_writer_gate ON dbo.content_schedule;
END;

BEGIN TRY
    IF OBJECT_ID(N'dbo.content_schedule', N'U') IS NOT NULL
    BEGIN
        IF COL_LENGTH(N'dbo.content_schedule', N'provider_target_id') IS NULL
            EXEC(N'ALTER TABLE dbo.content_schedule ADD provider_target_id NVARCHAR(128) NULL;');

        IF COL_LENGTH(N'dbo.content_schedule', N'provider_target_id') IS NOT NULL
        BEGIN
            EXEC(N'
                UPDATE dbo.content_schedule
                SET status = N''held'',
                    next_attempt_at = NULL,
                    last_error_code = N''instagram_target_reselection_required'',
                    last_error = N''Instagram target must be reselected after the provider target snapshot repair.'',
                    updated_at = SYSDATETIMEOFFSET()
                WHERE LOWER(LTRIM(RTRIM(platform))) = N''instagram''
                  AND status IN (N''pending'', N''held'')
                  AND NULLIF(LTRIM(RTRIM(provider_target_id)), N'''') IS NULL
                  AND (status <> N''held''
                       OR ISNULL(last_error_code, N'''') <> N''instagram_target_reselection_required''
                       OR next_attempt_at IS NOT NULL);');
        END;
    END;
END TRY
BEGIN CATCH
    IF @repairScheduleWriterGateWasEnabled = 1
        ENABLE TRIGGER dbo.TR_content_schedule_writer_gate ON dbo.content_schedule;
    THROW;
END CATCH;

IF OBJECT_ID(N'dbo.TR_content_schedule_writer_gate', N'TR') IS NOT NULL
    DROP TRIGGER dbo.TR_content_schedule_writer_gate;

IF OBJECT_ID(N'dbo.content_schedule', N'U') IS NOT NULL
BEGIN
    EXEC(N'
CREATE TRIGGER dbo.TR_content_schedule_writer_gate
ON dbo.content_schedule
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) RETURN;

    IF NOT EXISTS (
        SELECT 1
        FROM inserted i
        WHERE i.status IN (N''publishing'', N''outcome_unknown'', N''pending'', N''held'', N''failed''))
    BEGIN
        RETURN;
    END;

    DECLARE @paused BIT = 0;
    DECLARE @min_writer INT = 0;
    DECLARE @writer INT = TRY_CONVERT(INT, SESSION_CONTEXT(N''clawbot_content_writer_version''));

    SELECT
        @paused = publication_paused,
        @min_writer = minimum_writer_version
    FROM dbo.content_workflow_runtime_gate WITH (UPDLOCK, HOLDLOCK)
    WHERE id = 1;

    IF @paused = 1
       AND EXISTS (
           SELECT 1
           FROM inserted i
           LEFT JOIN deleted d ON d.id = i.id
           WHERE i.status IN (N''publishing'', N''pending'', N''held'', N''failed'', N''outcome_unknown'')
             AND (d.id IS NULL OR d.status <> i.status OR ISNULL(d.next_attempt_at, ''1900-01-01'') <> ISNULL(i.next_attempt_at, ''1900-01-01'')))
    BEGIN
        THROW 53001, ''content_publication_paused'', 1;
    END;

    IF @min_writer > 0 AND (@writer IS NULL OR @writer < @min_writer)
    BEGIN
        THROW 53002, ''content_writer_version_too_low'', 1;
    END;
END');
END;

-- Durable content render task persistence (0082). Keep this repair path complete because existing
-- local databases can skip migration replay and may contain a partially provisioned task table.
IF OBJECT_ID(N'dbo.content_items', N'U') IS NULL
    THROW 50001, 'dbo.content_items is missing; cannot provision content_render_tasks.', 1;

IF OBJECT_ID(N'dbo.content_render_tasks', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.content_render_tasks (
        id UNIQUEIDENTIFIER NOT NULL,
        tenant_id UNIQUEIDENTIFIER NOT NULL,
        content_item_id UNIQUEIDENTIFIER NOT NULL,
        source_revision INT NOT NULL,
        template_id NVARCHAR(64) NOT NULL,
        template_version INT NOT NULL,
        template_hash NVARCHAR(64) NOT NULL,
        preset NVARCHAR(64) NOT NULL,
        canonical_slots_json NVARCHAR(MAX) NOT NULL,
        slots_hash NVARCHAR(64) NOT NULL,
        status NVARCHAR(24) NOT NULL CONSTRAINT DF_content_render_tasks_status DEFAULT N'pending',
        lease_token UNIQUEIDENTIFIER NULL,
        claimed_lease_token UNIQUEIDENTIFIER NULL,
        lease_expires_at DATETIMEOFFSET NULL,
        attempt_count INT NOT NULL CONSTRAINT DF_content_render_tasks_attempt_count DEFAULT 0,
        next_attempt_at DATETIMEOFFSET NOT NULL,
        last_error_code NVARCHAR(128) NULL,
        output_asset_id UNIQUEIDENTIFIER NULL,
        completed_revision INT NULL,
        created_at DATETIMEOFFSET NOT NULL,
        started_at DATETIMEOFFSET NULL,
        completed_at DATETIMEOFFSET NULL,
        row_version ROWVERSION NOT NULL,
        CONSTRAINT PK_content_render_tasks PRIMARY KEY CLUSTERED (id)
    );
END;

IF COL_LENGTH(N'dbo.content_render_tasks', N'id') IS NULL
    ALTER TABLE dbo.content_render_tasks ADD id UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_content_render_tasks_id DEFAULT NEWID();
IF COL_LENGTH(N'dbo.content_render_tasks', N'tenant_id') IS NULL
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.content_render_tasks)
        THROW 50002, 'Cannot add tenant_id to populated dbo.content_render_tasks.', 1;
    ALTER TABLE dbo.content_render_tasks ADD tenant_id UNIQUEIDENTIFIER NOT NULL;
END;
IF COL_LENGTH(N'dbo.content_render_tasks', N'content_item_id') IS NULL
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.content_render_tasks)
        THROW 50003, 'Cannot add content_item_id to populated dbo.content_render_tasks.', 1;
    ALTER TABLE dbo.content_render_tasks ADD content_item_id UNIQUEIDENTIFIER NOT NULL;
END;
IF COL_LENGTH(N'dbo.content_render_tasks', N'source_revision') IS NULL
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.content_render_tasks)
        THROW 50004, 'Cannot add source_revision to populated dbo.content_render_tasks; immutable payload cannot be reconstructed.', 1;
    ALTER TABLE dbo.content_render_tasks ADD source_revision INT NOT NULL;
END;
IF COL_LENGTH(N'dbo.content_render_tasks', N'template_id') IS NULL
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.content_render_tasks)
        THROW 50005, 'Cannot add template_id to populated dbo.content_render_tasks; immutable payload cannot be reconstructed.', 1;
    ALTER TABLE dbo.content_render_tasks ADD template_id NVARCHAR(64) NOT NULL;
END;
IF COL_LENGTH(N'dbo.content_render_tasks', N'template_version') IS NULL
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.content_render_tasks)
        THROW 50006, 'Cannot add template_version to populated dbo.content_render_tasks; immutable payload cannot be reconstructed.', 1;
    ALTER TABLE dbo.content_render_tasks ADD template_version INT NOT NULL;
END;
IF COL_LENGTH(N'dbo.content_render_tasks', N'template_hash') IS NULL
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.content_render_tasks)
        THROW 50007, 'Cannot add template_hash to populated dbo.content_render_tasks; immutable payload cannot be reconstructed.', 1;
    ALTER TABLE dbo.content_render_tasks ADD template_hash NVARCHAR(64) NOT NULL;
END;
IF COL_LENGTH(N'dbo.content_render_tasks', N'preset') IS NULL
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.content_render_tasks)
        THROW 50008, 'Cannot add preset to populated dbo.content_render_tasks; immutable payload cannot be reconstructed.', 1;
    ALTER TABLE dbo.content_render_tasks ADD preset NVARCHAR(64) NOT NULL;
END;

DECLARE @contentRenderTasksPopulated BIT =
    CASE WHEN EXISTS (SELECT 1 FROM dbo.content_render_tasks) THEN 1 ELSE 0 END;

IF @contentRenderTasksPopulated = 1
   AND EXISTS (
       SELECT 1
       FROM sys.default_constraints dc
       INNER JOIN sys.columns c
           ON c.object_id = dc.parent_object_id
          AND c.column_id = dc.parent_column_id
       WHERE dc.parent_object_id = OBJECT_ID(N'dbo.content_render_tasks')
         AND c.name IN (
             N'source_revision', N'template_id', N'template_version', N'template_hash', N'preset'))
    THROW 50010, 'content_render_task_immutable_defaults_unsafe', 1;

IF @contentRenderTasksPopulated = 1
   AND COL_LENGTH(N'dbo.content_render_tasks', N'canonical_slots_json') IS NULL
   AND COL_LENGTH(N'dbo.content_render_tasks', N'slots_hash') IS NOT NULL
    EXEC(N'IF EXISTS (
        SELECT 1 FROM dbo.content_render_tasks
        WHERE slots_hash IS NULL
           OR slots_hash <> N''4f53cda18c2baa0c0354bb5f9a3ecbe5ed12ab4d8e11ba873c2f11161202b945'')
        THROW 50009, ''content_render_task_slots_backfill_unsafe'', 1;');

IF @contentRenderTasksPopulated = 1
   AND COL_LENGTH(N'dbo.content_render_tasks', N'slots_hash') IS NULL
   AND COL_LENGTH(N'dbo.content_render_tasks', N'canonical_slots_json') IS NOT NULL
    EXEC(N'IF EXISTS (
        SELECT 1 FROM dbo.content_render_tasks
        WHERE canonical_slots_json IS NULL OR canonical_slots_json <> N''[]'')
        THROW 50009, ''content_render_task_slots_backfill_unsafe'', 1;');

IF COL_LENGTH(N'dbo.content_render_tasks', N'canonical_slots_json') IS NULL
BEGIN
    IF @contentRenderTasksPopulated = 1
        ALTER TABLE dbo.content_render_tasks ADD canonical_slots_json NVARCHAR(MAX) NOT NULL
            CONSTRAINT DF_content_render_tasks_slots_backfill DEFAULT N'[]' WITH VALUES;
    ELSE
        ALTER TABLE dbo.content_render_tasks ADD canonical_slots_json NVARCHAR(MAX) NOT NULL;
END;
IF COL_LENGTH(N'dbo.content_render_tasks', N'slots_hash') IS NULL
BEGIN
    IF @contentRenderTasksPopulated = 1
        ALTER TABLE dbo.content_render_tasks ADD slots_hash NVARCHAR(64) NOT NULL
            CONSTRAINT DF_content_render_tasks_slots_hash_backfill
            DEFAULT N'4f53cda18c2baa0c0354bb5f9a3ecbe5ed12ab4d8e11ba873c2f11161202b945' WITH VALUES;
    ELSE
        ALTER TABLE dbo.content_render_tasks ADD slots_hash NVARCHAR(64) NOT NULL;
END;

EXEC(N'IF EXISTS (
    SELECT 1
    FROM dbo.content_render_tasks
    WHERE ISJSON(canonical_slots_json, ARRAY) <> 1
       OR slots_hash <> LOWER(CONVERT(VARCHAR(64), HASHBYTES(
            ''SHA2_256'',
            CONVERT(VARCHAR(MAX), canonical_slots_json COLLATE Latin1_General_100_BIN2_UTF8)), 2)))
    THROW 50009, ''content_render_task_slots_backfill_unsafe'', 1;');

DECLARE @contentRenderPayloadDefault SYSNAME;
DECLARE @contentRenderPayloadDropSql NVARCHAR(MAX);
WHILE 1 = 1
BEGIN
    SET @contentRenderPayloadDefault = NULL;
    SELECT TOP (1) @contentRenderPayloadDefault = dc.name
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c
        ON c.object_id = dc.parent_object_id
       AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.content_render_tasks')
      AND c.name IN (
          N'source_revision', N'template_id', N'template_version', N'template_hash',
          N'preset', N'canonical_slots_json', N'slots_hash');

    IF @contentRenderPayloadDefault IS NULL
        BREAK;

    -- EXEC() chi nhan literal + bien; goi ham QUOTENAME truc tiep trong EXEC la loi cu phap.
    SET @contentRenderPayloadDropSql = N'ALTER TABLE dbo.content_render_tasks DROP CONSTRAINT '
        + QUOTENAME(@contentRenderPayloadDefault) + N';';
    EXEC (@contentRenderPayloadDropSql);
END;
IF COL_LENGTH(N'dbo.content_render_tasks', N'status') IS NULL
    ALTER TABLE dbo.content_render_tasks ADD status NVARCHAR(24) NOT NULL CONSTRAINT DF_content_render_tasks_status_repair DEFAULT N'pending';
IF COL_LENGTH(N'dbo.content_render_tasks', N'lease_token') IS NULL
    ALTER TABLE dbo.content_render_tasks ADD lease_token UNIQUEIDENTIFIER NULL;
IF COL_LENGTH(N'dbo.content_render_tasks', N'claimed_lease_token') IS NULL
    ALTER TABLE dbo.content_render_tasks ADD claimed_lease_token UNIQUEIDENTIFIER NULL;
IF COL_LENGTH(N'dbo.content_render_tasks', N'lease_expires_at') IS NULL
    ALTER TABLE dbo.content_render_tasks ADD lease_expires_at DATETIMEOFFSET NULL;
IF COL_LENGTH(N'dbo.content_render_tasks', N'attempt_count') IS NULL
    ALTER TABLE dbo.content_render_tasks ADD attempt_count INT NOT NULL CONSTRAINT DF_content_render_tasks_attempt_count_repair DEFAULT 0;
IF COL_LENGTH(N'dbo.content_render_tasks', N'next_attempt_at') IS NULL
    ALTER TABLE dbo.content_render_tasks ADD next_attempt_at DATETIMEOFFSET NOT NULL CONSTRAINT DF_content_render_tasks_next_attempt DEFAULT SYSDATETIMEOFFSET();
IF COL_LENGTH(N'dbo.content_render_tasks', N'last_error_code') IS NULL
    ALTER TABLE dbo.content_render_tasks ADD last_error_code NVARCHAR(128) NULL;
IF COL_LENGTH(N'dbo.content_render_tasks', N'output_asset_id') IS NULL
    ALTER TABLE dbo.content_render_tasks ADD output_asset_id UNIQUEIDENTIFIER NULL;
IF COL_LENGTH(N'dbo.content_render_tasks', N'completed_revision') IS NULL
    ALTER TABLE dbo.content_render_tasks ADD completed_revision INT NULL;
IF COL_LENGTH(N'dbo.content_render_tasks', N'created_at') IS NULL
    ALTER TABLE dbo.content_render_tasks ADD created_at DATETIMEOFFSET NOT NULL CONSTRAINT DF_content_render_tasks_created_at DEFAULT SYSDATETIMEOFFSET();
IF COL_LENGTH(N'dbo.content_render_tasks', N'started_at') IS NULL
    ALTER TABLE dbo.content_render_tasks ADD started_at DATETIMEOFFSET NULL;
IF COL_LENGTH(N'dbo.content_render_tasks', N'completed_at') IS NULL
    ALTER TABLE dbo.content_render_tasks ADD completed_at DATETIMEOFFSET NULL;
IF COL_LENGTH(N'dbo.content_render_tasks', N'row_version') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.columns c
       INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
       WHERE c.object_id = OBJECT_ID(N'dbo.content_render_tasks')
         AND c.name = N'row_version'
         AND c.system_type_id = 189
         AND t.system_type_id = 189
         AND c.max_length = 8
         AND c.is_nullable = 0)
BEGIN
    DECLARE @contentRenderRowVersionDefault SYSNAME;
    DECLARE @contentRenderRowVersionDropSql NVARCHAR(MAX);
    SELECT @contentRenderRowVersionDefault = dc.name
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c
        ON c.object_id = dc.parent_object_id
       AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.content_render_tasks')
      AND c.name = N'row_version';

    IF @contentRenderRowVersionDefault IS NOT NULL
    BEGIN
        SET @contentRenderRowVersionDropSql = N'ALTER TABLE dbo.content_render_tasks DROP CONSTRAINT ' + QUOTENAME(@contentRenderRowVersionDefault) + N';';
        EXEC (@contentRenderRowVersionDropSql);
    END;

    ALTER TABLE dbo.content_render_tasks DROP COLUMN row_version;
END;

IF COL_LENGTH(N'dbo.content_render_tasks', N'row_version') IS NULL
    ALTER TABLE dbo.content_render_tasks ADD row_version ROWVERSION NOT NULL;

EXEC(N'
IF EXISTS (
    SELECT 1
    FROM dbo.content_render_tasks
    WHERE id IS NULL
       OR tenant_id IS NULL
       OR content_item_id IS NULL
       OR source_revision IS NULL
       OR template_id IS NULL
       OR template_version IS NULL
       OR template_hash IS NULL
       OR preset IS NULL
       OR canonical_slots_json IS NULL
       OR slots_hash IS NULL
       OR status IS NULL
       OR attempt_count IS NULL
       OR next_attempt_at IS NULL
       OR created_at IS NULL
)
    THROW 50013, ''content_render_task_required_values_null'', 1;
');

CREATE TABLE #content_render_task_expected_columns (
    id UNIQUEIDENTIFIER NOT NULL,
    tenant_id UNIQUEIDENTIFIER NOT NULL,
    content_item_id UNIQUEIDENTIFIER NOT NULL,
    source_revision INT NOT NULL,
    template_id NVARCHAR(64) COLLATE DATABASE_DEFAULT NOT NULL,
    template_version INT NOT NULL,
    template_hash NVARCHAR(64) COLLATE DATABASE_DEFAULT NOT NULL,
    preset NVARCHAR(64) COLLATE DATABASE_DEFAULT NOT NULL,
    canonical_slots_json NVARCHAR(MAX) COLLATE DATABASE_DEFAULT NOT NULL,
    slots_hash NVARCHAR(64) COLLATE DATABASE_DEFAULT NOT NULL,
    status NVARCHAR(24) COLLATE DATABASE_DEFAULT NOT NULL,
    lease_token UNIQUEIDENTIFIER NULL,
    claimed_lease_token UNIQUEIDENTIFIER NULL,
    lease_expires_at DATETIMEOFFSET NULL,
    attempt_count INT NOT NULL,
    next_attempt_at DATETIMEOFFSET NOT NULL,
    last_error_code NVARCHAR(128) COLLATE DATABASE_DEFAULT NULL,
    output_asset_id UNIQUEIDENTIFIER NULL,
    completed_revision INT NULL,
    created_at DATETIMEOFFSET NOT NULL,
    started_at DATETIMEOFFSET NULL,
    completed_at DATETIMEOFFSET NULL,
    row_version ROWVERSION NOT NULL
);

IF EXISTS (
    SELECT 1
    FROM tempdb.sys.columns expected
    INNER JOIN tempdb.sys.types expected_type
        ON expected_type.user_type_id = expected.user_type_id
    LEFT JOIN sys.columns actual
        ON actual.object_id = OBJECT_ID(N'dbo.content_render_tasks')
       AND actual.name = expected.name
    LEFT JOIN sys.types actual_type
        ON actual_type.user_type_id = actual.user_type_id
    WHERE expected.object_id = OBJECT_ID(N'tempdb..#content_render_task_expected_columns')
      AND (
          actual.column_id IS NULL
          OR actual_type.user_type_id IS NULL
          OR actual.system_type_id <> expected.system_type_id
          OR actual_type.system_type_id <> expected_type.system_type_id
          OR actual.user_type_id <> actual.system_type_id
          OR actual_type.is_user_defined <> 0
          OR actual_type.is_assembly_type <> 0
          OR actual.max_length <> expected.max_length
          OR actual.precision <> expected.precision
          OR actual.scale <> expected.scale
          OR actual.is_nullable <> expected.is_nullable
          OR actual.is_identity <> expected.is_identity
          OR actual.is_computed <> expected.is_computed
          OR actual.is_sparse <> expected.is_sparse
          OR actual.is_column_set <> expected.is_column_set
          OR ISNULL(actual.collation_name, N'') COLLATE DATABASE_DEFAULT
             <> ISNULL(expected.collation_name, N'') COLLATE DATABASE_DEFAULT
      ))
    THROW 50014, 'content_render_task_column_contract_unsafe', 1;

DROP TABLE #content_render_task_expected_columns;

IF NOT EXISTS (
    SELECT 1
    FROM sys.key_constraints kc
    INNER JOIN sys.indexes i
        ON i.object_id = kc.parent_object_id
       AND i.index_id = kc.unique_index_id
    WHERE kc.parent_object_id = OBJECT_ID(N'dbo.content_render_tasks')
      AND kc.type = N'PK'
      AND kc.name = N'PK_content_render_tasks'
      AND i.name = N'PK_content_render_tasks'
      AND i.is_primary_key = 1
      AND i.is_unique = 1
      AND i.type_desc = N'CLUSTERED'
      AND i.is_disabled = 0
      AND i.is_hypothetical = 0
      AND i.has_filter = 0
      AND (SELECT COUNT(*) FROM sys.index_columns ic
           WHERE ic.object_id = i.object_id
             AND ic.index_id = i.index_id
             AND ic.key_ordinal > 0) = 1
      AND NOT EXISTS (
          SELECT 1 FROM sys.index_columns ic
          WHERE ic.object_id = i.object_id
            AND ic.index_id = i.index_id
            AND ic.is_included_column = 1)
      AND EXISTS (
          SELECT 1 FROM sys.index_columns ic
          WHERE ic.object_id = i.object_id
            AND ic.index_id = i.index_id
            AND ic.key_ordinal = 1
            AND ic.is_descending_key = 0
            AND COL_NAME(ic.object_id, ic.column_id) = N'id'))
BEGIN
    EXEC(N'
    IF EXISTS (
        SELECT id
        FROM dbo.content_render_tasks
        GROUP BY id
        HAVING COUNT_BIG(*) > 1
    )
        THROW 50015, ''content_render_task_primary_key_data_unsafe'', 1;
    ');

    DECLARE @contentRenderTaskPrimaryKey SYSNAME;
    DECLARE @contentRenderTaskPrimaryKeyDropSql NVARCHAR(MAX);
    SELECT @contentRenderTaskPrimaryKey = kc.name
    FROM sys.key_constraints kc
    WHERE kc.parent_object_id = OBJECT_ID(N'dbo.content_render_tasks')
      AND kc.type = N'PK';

    IF @contentRenderTaskPrimaryKey IS NOT NULL
    BEGIN
        SET @contentRenderTaskPrimaryKeyDropSql = N'ALTER TABLE dbo.content_render_tasks DROP CONSTRAINT ' + QUOTENAME(@contentRenderTaskPrimaryKey) + N';';
        EXEC (@contentRenderTaskPrimaryKeyDropSql);
    END;

    ALTER TABLE dbo.content_render_tasks
        ADD CONSTRAINT PK_content_render_tasks PRIMARY KEY CLUSTERED (id);
END;
CREATE TABLE #content_render_task_expected_checks (
    source_revision INT NOT NULL,
    template_id NVARCHAR(64) NOT NULL,
    template_version INT NOT NULL,
    template_hash NVARCHAR(64) NOT NULL,
    preset NVARCHAR(64) NOT NULL,
    canonical_slots_json NVARCHAR(MAX) NOT NULL,
    slots_hash NVARCHAR(64) NOT NULL,
    status NVARCHAR(24) NOT NULL,
    lease_token UNIQUEIDENTIFIER NULL,
    claimed_lease_token UNIQUEIDENTIFIER NULL,
    lease_expires_at DATETIMEOFFSET NULL,
    attempt_count INT NOT NULL,
    output_asset_id UNIQUEIDENTIFIER NULL,
    completed_revision INT NULL,
    completed_at DATETIMEOFFSET NULL,
    CHECK (source_revision > 0 AND source_revision < 2147483647 AND template_version > 0 AND attempt_count >= 0),
    CHECK (status IN (N'pending', N'leased', N'completed', N'failed', N'canceled_stale')),
    CHECK (preset IN (N'1200x630', N'1080x1080')),
    CHECK (LEN(template_id) BETWEEN 1 AND 64 AND LEN(template_hash) = 64 AND template_hash COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9a-f]%' AND LEN(slots_hash) = 64 AND slots_hash COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9a-f]%' AND ISJSON(canonical_slots_json, ARRAY) = 1 AND slots_hash = LOWER(CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', CONVERT(VARCHAR(MAX), canonical_slots_json COLLATE Latin1_General_100_BIN2_UTF8)), 2)) AND DATALENGTH(CONVERT(VARCHAR(MAX), canonical_slots_json COLLATE Latin1_General_100_BIN2_UTF8)) <= 131072),
    CHECK ((status = N'pending' AND lease_token IS NULL AND claimed_lease_token IS NULL AND lease_expires_at IS NULL AND completed_at IS NULL AND output_asset_id IS NULL AND completed_revision IS NULL) OR (status = N'leased' AND lease_token IS NOT NULL AND (claimed_lease_token IS NULL OR claimed_lease_token = lease_token) AND lease_expires_at IS NOT NULL AND completed_at IS NULL AND output_asset_id IS NULL AND completed_revision IS NULL) OR (status = N'completed' AND lease_token IS NULL AND claimed_lease_token IS NULL AND lease_expires_at IS NULL AND completed_at IS NOT NULL AND output_asset_id IS NOT NULL AND completed_revision = source_revision + 1) OR (status IN (N'failed', N'canceled_stale') AND lease_token IS NULL AND claimed_lease_token IS NULL AND lease_expires_at IS NULL AND completed_at IS NOT NULL AND output_asset_id IS NULL AND completed_revision IS NULL))
);

DECLARE @repairExpectedRevisionCheck NVARCHAR(MAX);
DECLARE @repairExpectedStatusCheck NVARCHAR(MAX);
DECLARE @repairExpectedPresetCheck NVARCHAR(MAX);
DECLARE @repairExpectedPayloadCheck NVARCHAR(MAX);
DECLARE @repairExpectedStateCheck NVARCHAR(MAX);
SELECT @repairExpectedRevisionCheck = definition FROM tempdb.sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'tempdb..#content_render_task_expected_checks') AND definition LIKE N'%source_revision%' AND definition NOT LIKE N'%completed_revision%';
SELECT @repairExpectedStatusCheck = definition FROM tempdb.sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'tempdb..#content_render_task_expected_checks') AND definition LIKE N'%canceled_stale%' AND definition NOT LIKE N'%lease_token%';
SELECT @repairExpectedPresetCheck = definition FROM tempdb.sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'tempdb..#content_render_task_expected_checks') AND definition LIKE N'%1200x630%';
SELECT @repairExpectedPayloadCheck = definition FROM tempdb.sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'tempdb..#content_render_task_expected_checks') AND definition LIKE N'%SHA2_256%';
SELECT @repairExpectedStateCheck = definition FROM tempdb.sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'tempdb..#content_render_task_expected_checks') AND definition LIKE N'%lease_token%';
IF @repairExpectedRevisionCheck IS NULL OR @repairExpectedStatusCheck IS NULL OR @repairExpectedPresetCheck IS NULL OR @repairExpectedPayloadCheck IS NULL OR @repairExpectedStateCheck IS NULL
    THROW 50012, 'Could not construct expected content_render_tasks CHECK definitions.', 1;

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_content_render_tasks_revision' AND parent_object_id = OBJECT_ID(N'dbo.content_render_tasks') AND (is_disabled = 1 OR is_not_trusted = 1 OR definition <> @repairExpectedRevisionCheck))
    ALTER TABLE dbo.content_render_tasks DROP CONSTRAINT CK_content_render_tasks_revision;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_content_render_tasks_status' AND parent_object_id = OBJECT_ID(N'dbo.content_render_tasks') AND (is_disabled = 1 OR is_not_trusted = 1 OR definition <> @repairExpectedStatusCheck))
    ALTER TABLE dbo.content_render_tasks DROP CONSTRAINT CK_content_render_tasks_status;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_content_render_tasks_preset' AND parent_object_id = OBJECT_ID(N'dbo.content_render_tasks') AND (is_disabled = 1 OR is_not_trusted = 1 OR definition <> @repairExpectedPresetCheck))
    ALTER TABLE dbo.content_render_tasks DROP CONSTRAINT CK_content_render_tasks_preset;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_content_render_tasks_payload' AND parent_object_id = OBJECT_ID(N'dbo.content_render_tasks') AND (is_disabled = 1 OR is_not_trusted = 1 OR definition <> @repairExpectedPayloadCheck))
    ALTER TABLE dbo.content_render_tasks DROP CONSTRAINT CK_content_render_tasks_payload;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_content_render_tasks_state' AND parent_object_id = OBJECT_ID(N'dbo.content_render_tasks') AND (is_disabled = 1 OR is_not_trusted = 1 OR definition <> @repairExpectedStateCheck))
    ALTER TABLE dbo.content_render_tasks DROP CONSTRAINT CK_content_render_tasks_state;

IF EXISTS (
    SELECT 1 FROM sys.foreign_keys fk
    WHERE fk.name = N'FK_content_render_tasks_content_items_tenant_item'
      AND fk.parent_object_id = OBJECT_ID(N'dbo.content_render_tasks')
      AND (fk.referenced_object_id <> OBJECT_ID(N'dbo.content_items') OR fk.delete_referential_action_desc <> N'NO_ACTION' OR fk.update_referential_action_desc <> N'NO_ACTION' OR fk.is_disabled = 1 OR fk.is_not_trusted = 1 OR fk.is_not_for_replication = 1
           OR (SELECT COUNT(*) FROM sys.foreign_key_columns fkc WHERE fkc.constraint_object_id = fk.object_id) <> 2
           OR NOT EXISTS (SELECT 1 FROM sys.foreign_key_columns fkc WHERE fkc.constraint_object_id = fk.object_id AND fkc.constraint_column_id = 1 AND COL_NAME(fkc.parent_object_id, fkc.parent_column_id) = N'tenant_id' AND COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) = N'tenant_id')
           OR NOT EXISTS (SELECT 1 FROM sys.foreign_key_columns fkc WHERE fkc.constraint_object_id = fk.object_id AND fkc.constraint_column_id = 2 AND COL_NAME(fkc.parent_object_id, fkc.parent_column_id) = N'content_item_id' AND COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) = N'id')))
    ALTER TABLE dbo.content_render_tasks DROP CONSTRAINT FK_content_render_tasks_content_items_tenant_item;

IF EXISTS (
    SELECT 1 FROM sys.indexes i
    WHERE i.name = N'UX_content_items_tenant_id_id' AND i.object_id = OBJECT_ID(N'dbo.content_items')
      AND (i.is_unique = 0 OR i.type_desc <> N'NONCLUSTERED' OR i.is_disabled = 1 OR i.has_filter = 1
           OR (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) <> 2
           OR EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
           OR NOT EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND COL_NAME(ic.object_id, ic.column_id) = N'tenant_id')
           OR NOT EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 2 AND COL_NAME(ic.object_id, ic.column_id) = N'id')))
    DROP INDEX UX_content_items_tenant_id_id ON dbo.content_items;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_content_items_tenant_id_id' AND object_id = OBJECT_ID(N'dbo.content_items'))
    EXEC(N'CREATE UNIQUE INDEX UX_content_items_tenant_id_id ON dbo.content_items (tenant_id, id);');

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_content_render_tasks_revision'
      AND parent_object_id = OBJECT_ID(N'dbo.content_render_tasks'))
    EXEC(N'ALTER TABLE dbo.content_render_tasks WITH CHECK ADD CONSTRAINT CK_content_render_tasks_revision CHECK (source_revision > 0 AND source_revision < 2147483647 AND template_version > 0 AND attempt_count >= 0);');
IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_content_render_tasks_status'
      AND parent_object_id = OBJECT_ID(N'dbo.content_render_tasks'))
    EXEC(N'ALTER TABLE dbo.content_render_tasks WITH CHECK ADD CONSTRAINT CK_content_render_tasks_status CHECK (status IN (N''pending'', N''leased'', N''completed'', N''failed'', N''canceled_stale''));');
IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_content_render_tasks_preset'
      AND parent_object_id = OBJECT_ID(N'dbo.content_render_tasks'))
    EXEC(N'ALTER TABLE dbo.content_render_tasks WITH CHECK ADD CONSTRAINT CK_content_render_tasks_preset CHECK (preset IN (N''1200x630'', N''1080x1080''));');
IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_content_render_tasks_payload'
      AND parent_object_id = OBJECT_ID(N'dbo.content_render_tasks'))
    EXEC(N'ALTER TABLE dbo.content_render_tasks WITH CHECK ADD CONSTRAINT CK_content_render_tasks_payload CHECK (LEN(template_id) BETWEEN 1 AND 64 AND LEN(template_hash) = 64 AND template_hash COLLATE Latin1_General_100_BIN2 NOT LIKE N''%[^0-9a-f]%'' AND LEN(slots_hash) = 64 AND slots_hash COLLATE Latin1_General_100_BIN2 NOT LIKE N''%[^0-9a-f]%'' AND ISJSON(canonical_slots_json, ARRAY) = 1 AND slots_hash = LOWER(CONVERT(VARCHAR(64), HASHBYTES(''SHA2_256'', CONVERT(VARCHAR(MAX), canonical_slots_json COLLATE Latin1_General_100_BIN2_UTF8)), 2)) AND DATALENGTH(CONVERT(VARCHAR(MAX), canonical_slots_json COLLATE Latin1_General_100_BIN2_UTF8)) <= 131072);');
IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_content_render_tasks_state'
      AND parent_object_id = OBJECT_ID(N'dbo.content_render_tasks'))
    EXEC(N'ALTER TABLE dbo.content_render_tasks WITH CHECK ADD CONSTRAINT CK_content_render_tasks_state CHECK ((status = N''pending'' AND lease_token IS NULL AND claimed_lease_token IS NULL AND lease_expires_at IS NULL AND completed_at IS NULL AND output_asset_id IS NULL AND completed_revision IS NULL) OR (status = N''leased'' AND lease_token IS NOT NULL AND (claimed_lease_token IS NULL OR claimed_lease_token = lease_token) AND lease_expires_at IS NOT NULL AND completed_at IS NULL AND output_asset_id IS NULL AND completed_revision IS NULL) OR (status = N''completed'' AND lease_token IS NULL AND claimed_lease_token IS NULL AND lease_expires_at IS NULL AND completed_at IS NOT NULL AND output_asset_id IS NOT NULL AND completed_revision = source_revision + 1) OR (status IN (N''failed'', N''canceled_stale'') AND lease_token IS NULL AND claimed_lease_token IS NULL AND lease_expires_at IS NULL AND completed_at IS NOT NULL AND output_asset_id IS NULL AND completed_revision IS NULL));');
IF EXISTS (
    SELECT 1 FROM sys.indexes i
    WHERE i.name = N'UX_content_render_tasks_item_revision' AND i.object_id = OBJECT_ID(N'dbo.content_render_tasks')
      AND (i.is_unique = 0 OR i.type_desc <> N'NONCLUSTERED' OR i.is_disabled = 1 OR i.has_filter = 1
           OR (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) <> 3
           OR EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
           OR NOT EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND COL_NAME(ic.object_id, ic.column_id) = N'tenant_id')
           OR NOT EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 2 AND COL_NAME(ic.object_id, ic.column_id) = N'content_item_id')
           OR NOT EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 3 AND COL_NAME(ic.object_id, ic.column_id) = N'source_revision')))
    DROP INDEX UX_content_render_tasks_item_revision ON dbo.content_render_tasks;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_content_render_tasks_item_revision' AND object_id = OBJECT_ID(N'dbo.content_render_tasks'))
    EXEC(N'CREATE UNIQUE INDEX UX_content_render_tasks_item_revision ON dbo.content_render_tasks (tenant_id, content_item_id, source_revision);');

IF EXISTS (
    SELECT 1 FROM sys.indexes i
    WHERE i.name = N'IX_content_render_tasks_due' AND i.object_id = OBJECT_ID(N'dbo.content_render_tasks')
      AND (i.is_unique = 1 OR i.type_desc <> N'NONCLUSTERED' OR i.is_disabled = 1 OR i.has_filter = 1
           OR (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) <> 4
           OR EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
           OR NOT EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND COL_NAME(ic.object_id, ic.column_id) = N'tenant_id')
           OR NOT EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 2 AND COL_NAME(ic.object_id, ic.column_id) = N'status')
           OR NOT EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 3 AND COL_NAME(ic.object_id, ic.column_id) = N'next_attempt_at')
           OR NOT EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 4 AND COL_NAME(ic.object_id, ic.column_id) = N'created_at')))
    DROP INDEX IX_content_render_tasks_due ON dbo.content_render_tasks;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_content_render_tasks_due' AND object_id = OBJECT_ID(N'dbo.content_render_tasks'))
    EXEC(N'CREATE INDEX IX_content_render_tasks_due ON dbo.content_render_tasks (tenant_id, status, next_attempt_at, created_at);');

IF EXISTS (
    SELECT 1 FROM sys.indexes i
    WHERE i.name = N'IX_content_render_tasks_expired_lease' AND i.object_id = OBJECT_ID(N'dbo.content_render_tasks')
      AND (i.is_unique = 1 OR i.type_desc <> N'NONCLUSTERED' OR i.is_disabled = 1 OR i.has_filter = 1
           OR (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) <> 3
           OR EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
           OR NOT EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND COL_NAME(ic.object_id, ic.column_id) = N'tenant_id')
           OR NOT EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 2 AND COL_NAME(ic.object_id, ic.column_id) = N'status')
           OR NOT EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 3 AND COL_NAME(ic.object_id, ic.column_id) = N'lease_expires_at')))
    DROP INDEX IX_content_render_tasks_expired_lease ON dbo.content_render_tasks;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_content_render_tasks_expired_lease' AND object_id = OBJECT_ID(N'dbo.content_render_tasks'))
    EXEC(N'CREATE INDEX IX_content_render_tasks_expired_lease ON dbo.content_render_tasks (tenant_id, status, lease_expires_at);');
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE name = N'FK_content_render_tasks_content_items_tenant_item'
      AND parent_object_id = OBJECT_ID(N'dbo.content_render_tasks'))
    EXEC(N'ALTER TABLE dbo.content_render_tasks WITH CHECK ADD CONSTRAINT FK_content_render_tasks_content_items_tenant_item FOREIGN KEY (tenant_id, content_item_id) REFERENCES dbo.content_items (tenant_id, id) ON DELETE NO ACTION;');

DROP TABLE #content_render_task_expected_checks;

COMMIT TRANSACTION;
