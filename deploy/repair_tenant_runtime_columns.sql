-- Runtime repair for existing local DBs that skip full migration replay.
-- One SqlCommand, no GO. Idempotent via COL_LENGTH guards.
SET QUOTED_IDENTIFIER ON;
SET ARITHABORT ON;

IF OBJECT_ID(N'dbo.tenants', N'U') IS NULL
BEGIN
    RAISERROR(N'dbo.tenants is missing; cannot repair tenant runtime columns.', 16, 1);
    RETURN;
END;

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

IF OBJECT_ID(N'dbo.TR_content_schedule_writer_gate', N'TR') IS NOT NULL
    DROP TRIGGER dbo.TR_content_schedule_writer_gate;

-- Instagram target snapshot repair. Dynamic SQL is required because this script may add and
-- reference provider_target_id in the same SqlCommand batch.
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
              AND status IN (N''pending'', N''held'', N''publishing'', N''outcome_unknown'')
              AND NULLIF(LTRIM(RTRIM(provider_target_id)), N'''') IS NULL
              AND (status <> N''held''
                   OR ISNULL(last_error_code, N'''') <> N''instagram_target_reselection_required''
                   OR next_attempt_at IS NOT NULL);');
    END;
END;

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
