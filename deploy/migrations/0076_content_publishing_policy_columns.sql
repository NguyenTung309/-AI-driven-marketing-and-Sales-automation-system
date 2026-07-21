-- Content publishing approval policy: additive tenant, item and schedule columns.
-- One SqlCommand, no GO. Safe to re-run. Dependent constraints/indexes are in 0077.

IF OBJECT_ID(N'dbo.tenants', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.tenants', N'content_publishing_approval_policy') IS NULL
        ALTER TABLE dbo.tenants ADD content_publishing_approval_policy NVARCHAR(32) NOT NULL
            CONSTRAINT DF_tenants_content_publishing_policy DEFAULT N'human_required';

    IF COL_LENGTH(N'dbo.tenants', N'content_publishing_policy_version') IS NULL
        ALTER TABLE dbo.tenants ADD content_publishing_policy_version BIGINT NOT NULL
            CONSTRAINT DF_tenants_content_publishing_policy_version DEFAULT 1;

    IF COL_LENGTH(N'dbo.tenants', N'content_publishing_policy_updated_at') IS NULL
        ALTER TABLE dbo.tenants ADD content_publishing_policy_updated_at DATETIMEOFFSET NOT NULL
            CONSTRAINT DF_tenants_content_publishing_policy_updated_at DEFAULT SYSDATETIMEOFFSET();
END

IF OBJECT_ID(N'dbo.content_items', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.content_items', N'content_revision') IS NULL
        ALTER TABLE dbo.content_items ADD content_revision INT NOT NULL
            CONSTRAINT DF_content_items_content_revision DEFAULT 1;

    IF COL_LENGTH(N'dbo.content_items', N'agent_review_status') IS NULL
        ALTER TABLE dbo.content_items ADD agent_review_status NVARCHAR(24) NOT NULL
            CONSTRAINT DF_content_items_agent_review_status DEFAULT N'pending';

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
        ALTER TABLE dbo.content_items ADD image_review_status NVARCHAR(24) NOT NULL
            CONSTRAINT DF_content_items_image_review_status DEFAULT N'pending';

    IF COL_LENGTH(N'dbo.content_items', N'reviewed_image_count') IS NULL
        ALTER TABLE dbo.content_items ADD reviewed_image_count INT NOT NULL
            CONSTRAINT DF_content_items_reviewed_image_count DEFAULT 0;

    IF COL_LENGTH(N'dbo.content_items', N'agent_review_attempt_count') IS NULL
        ALTER TABLE dbo.content_items ADD agent_review_attempt_count INT NOT NULL
            CONSTRAINT DF_content_items_agent_review_attempt_count DEFAULT 0;

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

IF OBJECT_ID(N'dbo.content_schedule', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.content_schedule', N'content_revision') IS NULL
        ALTER TABLE dbo.content_schedule ADD content_revision INT NULL;

    IF COL_LENGTH(N'dbo.content_schedule', N'active_revision_slot') IS NULL
        ALTER TABLE dbo.content_schedule ADD active_revision_slot INT NULL;

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

IF OBJECT_ID(N'dbo.audit_logs', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.audit_logs', N'event_key') IS NULL
        ALTER TABLE dbo.audit_logs ADD event_key NVARCHAR(256) NULL;

    IF COL_LENGTH(N'dbo.audit_logs', N'state_sequence') IS NULL
        ALTER TABLE dbo.audit_logs ADD state_sequence BIGINT NULL;
END

IF OBJECT_ID(N'dbo.content_review_tasks', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.content_review_tasks (
        id UNIQUEIDENTIFIER NOT NULL,
        tenant_id UNIQUEIDENTIFIER NOT NULL,
        content_item_id UNIQUEIDENTIFIER NOT NULL,
        content_revision INT NOT NULL,
        status NVARCHAR(24) NOT NULL
            CONSTRAINT DF_content_review_tasks_status DEFAULT N'pending',
        lease_token UNIQUEIDENTIFIER NULL,
        lease_expires_at DATETIMEOFFSET NULL,
        attempt_count INT NOT NULL
            CONSTRAINT DF_content_review_tasks_attempt_count DEFAULT 0,
        next_attempt_at DATETIMEOFFSET NOT NULL,
        last_error_code NVARCHAR(128) NULL,
        created_at DATETIMEOFFSET NOT NULL,
        started_at DATETIMEOFFSET NULL,
        completed_at DATETIMEOFFSET NULL,
        row_version ROWVERSION NOT NULL,
        CONSTRAINT PK_content_review_tasks PRIMARY KEY (id)
    );
END

IF OBJECT_ID(N'dbo.content_assets', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.content_assets (
        id UNIQUEIDENTIFIER NOT NULL,
        tenant_id UNIQUEIDENTIFIER NOT NULL,
        content_item_id UNIQUEIDENTIFIER NOT NULL,
        storage_key NVARCHAR(256) NOT NULL,
        status NVARCHAR(24) NOT NULL
            CONSTRAINT DF_content_assets_status DEFAULT N'uploading',
        sha256 BINARY(32) NULL,
        size_bytes BIGINT NULL,
        content_type NVARCHAR(128) NULL,
        original_file_name NVARCHAR(255) NULL,
        sort_order INT NOT NULL
            CONSTRAINT DF_content_assets_sort_order DEFAULT 0,
        created_at DATETIMEOFFSET NOT NULL,
        ready_at DATETIMEOFFSET NULL,
        deleted_at DATETIMEOFFSET NULL,
        last_error_code NVARCHAR(128) NULL,
        row_version ROWVERSION NOT NULL,
        CONSTRAINT PK_content_assets PRIMARY KEY (id)
    );
END

IF OBJECT_ID(N'dbo.content_publish_attempts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.content_publish_attempts (
        id UNIQUEIDENTIFIER NOT NULL,
        tenant_id UNIQUEIDENTIFIER NOT NULL,
        schedule_id UNIQUEIDENTIFIER NOT NULL,
        content_item_id UNIQUEIDENTIFIER NOT NULL,
        content_revision INT NOT NULL,
        publish_target_id UNIQUEIDENTIFIER NOT NULL,
        platform NVARCHAR(32) NOT NULL,
        attempt_token UNIQUEIDENTIFIER NOT NULL,
        lease_token UNIQUEIDENTIFIER NULL,
        lease_expires_at DATETIMEOFFSET NULL,
        idempotency_key NVARCHAR(160) NOT NULL,
        snapshot_schema_version INT NOT NULL
            CONSTRAINT DF_content_publish_attempts_snapshot_schema DEFAULT 1,
        body_snapshot NVARCHAR(MAX) NOT NULL,
        assets_snapshot_json NVARCHAR(MAX) NOT NULL,
        snapshot_sha256 BINARY(32) NOT NULL,
        status NVARCHAR(24) NOT NULL
            CONSTRAINT DF_content_publish_attempts_status DEFAULT N'claimed',
        provider_request_id NVARCHAR(256) NULL,
        external_post_id NVARCHAR(256) NULL,
        claimed_at DATETIMEOFFSET NOT NULL,
        transmitted_at DATETIMEOFFSET NULL,
        completed_at DATETIMEOFFSET NULL,
        last_error_code NVARCHAR(128) NULL,
        row_version ROWVERSION NOT NULL,
        CONSTRAINT PK_content_publish_attempts PRIMARY KEY (id)
    );
END

IF OBJECT_ID(N'dbo.content_workflow_metrics_hourly', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.content_workflow_metrics_hourly (
        id BIGINT IDENTITY(1,1) NOT NULL,
        tenant_id UNIQUEIDENTIFIER NOT NULL,
        hour_utc DATETIMEOFFSET NOT NULL,
        review_passed_count BIGINT NOT NULL CONSTRAINT DF_content_metrics_review_passed DEFAULT 0,
        review_rejected_count BIGINT NOT NULL CONSTRAINT DF_content_metrics_review_rejected DEFAULT 0,
        review_needs_human_count BIGINT NOT NULL CONSTRAINT DF_content_metrics_review_needs_human DEFAULT 0,
        review_failed_count BIGINT NOT NULL CONSTRAINT DF_content_metrics_review_failed DEFAULT 0,
        image_reviewed_count BIGINT NOT NULL CONSTRAINT DF_content_metrics_image_reviewed DEFAULT 0,
        image_not_applicable_count BIGINT NOT NULL CONSTRAINT DF_content_metrics_image_not_applicable DEFAULT 0,
        image_skipped_unsupported_count BIGINT NOT NULL CONSTRAINT DF_content_metrics_image_skipped DEFAULT 0,
        image_failed_count BIGINT NOT NULL CONSTRAINT DF_content_metrics_image_failed DEFAULT 0,
        human_fallback_count BIGINT NOT NULL CONSTRAINT DF_content_metrics_human_fallback DEFAULT 0,
        human_override_count BIGINT NOT NULL CONSTRAINT DF_content_metrics_human_override DEFAULT 0,
        human_reject_count BIGINT NOT NULL CONSTRAINT DF_content_metrics_human_reject DEFAULT 0,
        held_schedule_count BIGINT NOT NULL CONSTRAINT DF_content_metrics_held_schedule DEFAULT 0,
        publish_succeeded_count BIGINT NOT NULL CONSTRAINT DF_content_metrics_publish_succeeded DEFAULT 0,
        publish_failed_count BIGINT NOT NULL CONSTRAINT DF_content_metrics_publish_failed DEFAULT 0,
        publish_outcome_unknown_count BIGINT NOT NULL CONSTRAINT DF_content_metrics_publish_unknown DEFAULT 0,
        review_latency_ms_sum BIGINT NOT NULL CONSTRAINT DF_content_metrics_review_latency_sum DEFAULT 0,
        review_latency_sample_count BIGINT NOT NULL CONSTRAINT DF_content_metrics_review_latency_count DEFAULT 0,
        publish_latency_ms_sum BIGINT NOT NULL CONSTRAINT DF_content_metrics_publish_latency_sum DEFAULT 0,
        publish_latency_sample_count BIGINT NOT NULL CONSTRAINT DF_content_metrics_publish_latency_count DEFAULT 0,
        llm_input_tokens BIGINT NOT NULL CONSTRAINT DF_content_metrics_llm_input DEFAULT 0,
        llm_output_tokens BIGINT NOT NULL CONSTRAINT DF_content_metrics_llm_output DEFAULT 0,
        llm_cost_usd DECIMAL(18,6) NOT NULL CONSTRAINT DF_content_metrics_llm_cost DEFAULT 0,
        CONSTRAINT PK_content_workflow_metrics_hourly PRIMARY KEY (id)
    );
END
