-- Content publishing approval policy: dependent checks and revision-aware active schedule index.
-- One SqlCommand, no GO. Safe to re-run. Depends on 0076.

SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.tenants', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.tenants', N'content_publishing_approval_policy') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1 FROM sys.check_constraints
       WHERE name = N'CK_tenants_content_publishing_policy'
         AND parent_object_id = OBJECT_ID(N'dbo.tenants'))
BEGIN
    ALTER TABLE dbo.tenants WITH CHECK
        ADD CONSTRAINT CK_tenants_content_publishing_policy
        CHECK (content_publishing_approval_policy IN (N'automatic', N'human_required'));
END

IF OBJECT_ID(N'dbo.tenants', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.tenants', N'content_publishing_policy_version') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1 FROM sys.check_constraints
       WHERE name = N'CK_tenants_content_publishing_policy_version'
         AND parent_object_id = OBJECT_ID(N'dbo.tenants'))
BEGIN
    ALTER TABLE dbo.tenants WITH CHECK
        ADD CONSTRAINT CK_tenants_content_publishing_policy_version
        CHECK (content_publishing_policy_version > 0);
END

IF OBJECT_ID(N'dbo.content_items', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'CK_content_items_content_revision'
          AND parent_object_id = OBJECT_ID(N'dbo.content_items'))
    BEGIN
        ALTER TABLE dbo.content_items WITH CHECK
            ADD CONSTRAINT CK_content_items_content_revision CHECK (content_revision > 0);
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'CK_content_items_agent_review_status'
          AND parent_object_id = OBJECT_ID(N'dbo.content_items'))
    BEGIN
        ALTER TABLE dbo.content_items WITH CHECK
            ADD CONSTRAINT CK_content_items_agent_review_status
            CHECK (agent_review_status IN (
                N'pending', N'running', N'passed', N'rejected', N'needs_human', N'failed', N'legacy_exempt'));
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'CK_content_items_image_review_status'
          AND parent_object_id = OBJECT_ID(N'dbo.content_items'))
    BEGIN
        ALTER TABLE dbo.content_items WITH CHECK
            ADD CONSTRAINT CK_content_items_image_review_status
            CHECK (image_review_status IN (
                N'pending', N'running', N'reviewed', N'not_applicable', N'skipped_unsupported', N'failed'));
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'CK_content_items_approval_mode'
          AND parent_object_id = OBJECT_ID(N'dbo.content_items'))
    BEGIN
        ALTER TABLE dbo.content_items WITH CHECK
            ADD CONSTRAINT CK_content_items_approval_mode
            CHECK (approval_mode IS NULL OR approval_mode IN (N'automatic', N'human', N'human_override'));
    END
END

IF OBJECT_ID(N'dbo.content_schedule', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'CK_content_schedule_content_revision'
          AND parent_object_id = OBJECT_ID(N'dbo.content_schedule'))
    BEGIN
        ALTER TABLE dbo.content_schedule WITH CHECK
            ADD CONSTRAINT CK_content_schedule_content_revision
            CHECK (content_revision IS NULL OR content_revision > 0);
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'CK_content_schedule_status'
          AND parent_object_id = OBJECT_ID(N'dbo.content_schedule'))
    BEGIN
        ALTER TABLE dbo.content_schedule WITH CHECK
            ADD CONSTRAINT CK_content_schedule_status
            CHECK (status IN (N'pending', N'held', N'publishing', N'outcome_unknown', N'posted', N'failed', N'canceled'));
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'ix_content_schedule_pending_item'
          AND object_id = OBJECT_ID(N'dbo.content_schedule'))
    BEGIN
        CREATE UNIQUE INDEX ix_content_schedule_pending_item
            ON dbo.content_schedule (content_item_id)
            WHERE status = N'pending';
    END

    DECLARE @activeScheduleIndexIsCorrect BIT = 0;
    SELECT @activeScheduleIndexIsCorrect = 1
    FROM sys.indexes AS indexDefinition
    WHERE indexDefinition.object_id = OBJECT_ID(N'dbo.content_schedule')
      AND indexDefinition.name = N'UX_content_schedule_active_revision'
      AND indexDefinition.is_unique = 1
      AND indexDefinition.has_filter = 1
      AND REPLACE(REPLACE(REPLACE(REPLACE(
            indexDefinition.filter_definition,
            N'[', N''), N']', N''), N'(', N''), N')', N'') =
          N'active_revision_slot IS NOT NULL'
      AND (SELECT COUNT(*)
           FROM sys.index_columns AS indexColumn
           WHERE indexColumn.object_id = indexDefinition.object_id
             AND indexColumn.index_id = indexDefinition.index_id) = 3
      AND EXISTS (
          SELECT 1
          FROM sys.index_columns AS indexColumn
          INNER JOIN sys.columns AS columnDefinition
              ON columnDefinition.object_id = indexColumn.object_id
             AND columnDefinition.column_id = indexColumn.column_id
          WHERE indexColumn.object_id = indexDefinition.object_id
            AND indexColumn.index_id = indexDefinition.index_id
            AND indexColumn.key_ordinal = 1
            AND columnDefinition.name = N'tenant_id')
      AND EXISTS (
          SELECT 1
          FROM sys.index_columns AS indexColumn
          INNER JOIN sys.columns AS columnDefinition
              ON columnDefinition.object_id = indexColumn.object_id
             AND columnDefinition.column_id = indexColumn.column_id
          WHERE indexColumn.object_id = indexDefinition.object_id
            AND indexColumn.index_id = indexDefinition.index_id
            AND indexColumn.key_ordinal = 2
            AND columnDefinition.name = N'content_item_id')
      AND EXISTS (
          SELECT 1
          FROM sys.index_columns AS indexColumn
          INNER JOIN sys.columns AS columnDefinition
              ON columnDefinition.object_id = indexColumn.object_id
             AND columnDefinition.column_id = indexColumn.column_id
          WHERE indexColumn.object_id = indexDefinition.object_id
            AND indexColumn.index_id = indexDefinition.index_id
            AND indexColumn.key_ordinal = 3
            AND columnDefinition.name = N'active_revision_slot');

    IF @activeScheduleIndexIsCorrect = 0
       AND EXISTS (
           SELECT 1 FROM sys.indexes
           WHERE name = N'UX_content_schedule_active_revision'
             AND object_id = OBJECT_ID(N'dbo.content_schedule'))
    BEGIN
        DROP INDEX UX_content_schedule_active_revision ON dbo.content_schedule;
    END

    IF COL_LENGTH(N'dbo.content_schedule', N'active_revision_key') IS NOT NULL
    BEGIN
        EXEC(N'ALTER TABLE dbo.content_schedule DROP COLUMN active_revision_key;');
    END

    UPDATE dbo.content_schedule
    SET active_revision_slot = CASE
        WHEN content_revision IS NOT NULL
          AND status IN (N'pending', N'held', N'publishing', N'outcome_unknown')
        THEN content_revision
        ELSE NULL
    END
    WHERE active_revision_slot <> CASE
            WHEN content_revision IS NOT NULL
              AND status IN (N'pending', N'held', N'publishing', N'outcome_unknown')
            THEN content_revision
            ELSE NULL
        END
       OR (active_revision_slot IS NULL
           AND content_revision IS NOT NULL
           AND status IN (N'pending', N'held', N'publishing', N'outcome_unknown'))
       OR (active_revision_slot IS NOT NULL
           AND (content_revision IS NULL
             OR status NOT IN (N'pending', N'held', N'publishing', N'outcome_unknown')));

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'CK_content_schedule_active_revision_slot_v2'
          AND parent_object_id = OBJECT_ID(N'dbo.content_schedule'))
    BEGIN
        ALTER TABLE dbo.content_schedule WITH CHECK
            ADD CONSTRAINT CK_content_schedule_active_revision_slot_v2
            CHECK (
                (content_revision IS NOT NULL
                    AND status IN (N'pending', N'held', N'publishing', N'outcome_unknown')
                    AND active_revision_slot IS NOT NULL
                    AND active_revision_slot = content_revision)
                OR ((content_revision IS NULL
                        OR status NOT IN (N'pending', N'held', N'publishing', N'outcome_unknown'))
                    AND active_revision_slot IS NULL));
    END

    IF @activeScheduleIndexIsCorrect = 0
    BEGIN
        CREATE UNIQUE INDEX UX_content_schedule_active_revision
            ON dbo.content_schedule (tenant_id, content_item_id, active_revision_slot)
            WHERE active_revision_slot IS NOT NULL;
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'UX_content_schedule_legacy_pending_item'
          AND object_id = OBJECT_ID(N'dbo.content_schedule'))
    BEGIN
        CREATE UNIQUE INDEX UX_content_schedule_legacy_pending_item
            ON dbo.content_schedule (tenant_id, content_item_id)
            WHERE status = N'pending' AND active_revision_slot IS NULL;
    END
END

IF OBJECT_ID(N'dbo.content_items', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1 FROM sys.indexes
       WHERE name = N'UX_content_items_tenant_id_id'
         AND object_id = OBJECT_ID(N'dbo.content_items'))
BEGIN
    CREATE UNIQUE INDEX UX_content_items_tenant_id_id
        ON dbo.content_items (tenant_id, id);
END

IF OBJECT_ID(N'dbo.content_schedule', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1 FROM sys.indexes
       WHERE name = N'UX_content_schedule_tenant_id_id'
         AND object_id = OBJECT_ID(N'dbo.content_schedule'))
BEGIN
    CREATE UNIQUE INDEX UX_content_schedule_tenant_id_id
        ON dbo.content_schedule (tenant_id, id);
END

IF OBJECT_ID(N'dbo.content_schedule', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1 FROM sys.indexes
       WHERE name = N'UX_content_schedule_publish_attempt_scope'
         AND object_id = OBJECT_ID(N'dbo.content_schedule'))
BEGIN
    CREATE UNIQUE INDEX UX_content_schedule_publish_attempt_scope
        ON dbo.content_schedule (
            tenant_id,
            id,
            content_item_id,
            content_revision,
            publish_target_id);
END

IF OBJECT_ID(N'dbo.content_schedule', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.content_items', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1 FROM sys.foreign_keys
       WHERE name = N'FK_content_schedule_content_items_tenant_item')
BEGIN
    ALTER TABLE dbo.content_schedule WITH CHECK
        ADD CONSTRAINT FK_content_schedule_content_items_tenant_item
        FOREIGN KEY (tenant_id, content_item_id)
        REFERENCES dbo.content_items (tenant_id, id);
END

IF OBJECT_ID(N'dbo.content_review_tasks', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'CK_content_review_tasks_revision'
          AND parent_object_id = OBJECT_ID(N'dbo.content_review_tasks'))
    BEGIN
        ALTER TABLE dbo.content_review_tasks WITH CHECK
            ADD CONSTRAINT CK_content_review_tasks_revision
            CHECK (content_revision > 0 AND attempt_count >= 0);
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'CK_content_review_tasks_status'
          AND parent_object_id = OBJECT_ID(N'dbo.content_review_tasks'))
    BEGIN
        ALTER TABLE dbo.content_review_tasks WITH CHECK
            ADD CONSTRAINT CK_content_review_tasks_status
            CHECK (status = N'pending'
                OR status = N'leased'
                OR status = N'completed'
                OR status = N'failed'
                OR status = N'canceled_stale');
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'CK_content_review_tasks_state'
          AND parent_object_id = OBJECT_ID(N'dbo.content_review_tasks'))
    BEGIN
        ALTER TABLE dbo.content_review_tasks WITH CHECK
            ADD CONSTRAINT CK_content_review_tasks_state
            CHECK (
                (status = N'pending' AND lease_token IS NULL AND lease_expires_at IS NULL AND completed_at IS NULL)
                OR (status = N'leased' AND lease_token IS NOT NULL AND lease_expires_at IS NOT NULL AND completed_at IS NULL)
                OR (status IN (N'completed', N'failed', N'canceled_stale')
                    AND lease_token IS NULL AND lease_expires_at IS NULL AND completed_at IS NOT NULL));
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'UX_content_review_tasks_item_revision'
          AND object_id = OBJECT_ID(N'dbo.content_review_tasks'))
    BEGIN
        CREATE UNIQUE INDEX UX_content_review_tasks_item_revision
            ON dbo.content_review_tasks (tenant_id, content_item_id, content_revision);
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'IX_content_review_tasks_due'
          AND object_id = OBJECT_ID(N'dbo.content_review_tasks'))
    BEGIN
        CREATE INDEX IX_content_review_tasks_due
            ON dbo.content_review_tasks (tenant_id, status, next_attempt_at, created_at);
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'IX_content_review_tasks_expired_lease'
          AND object_id = OBJECT_ID(N'dbo.content_review_tasks'))
    BEGIN
        CREATE INDEX IX_content_review_tasks_expired_lease
            ON dbo.content_review_tasks (tenant_id, status, lease_expires_at);
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE name = N'FK_content_review_tasks_content_items_tenant_item')
    BEGIN
        ALTER TABLE dbo.content_review_tasks WITH CHECK
            ADD CONSTRAINT FK_content_review_tasks_content_items_tenant_item
            FOREIGN KEY (tenant_id, content_item_id)
            REFERENCES dbo.content_items (tenant_id, id);
    END
END

IF OBJECT_ID(N'dbo.content_assets', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'CK_content_assets_status'
          AND parent_object_id = OBJECT_ID(N'dbo.content_assets'))
    BEGIN
        ALTER TABLE dbo.content_assets WITH CHECK
            ADD CONSTRAINT CK_content_assets_status
            CHECK (status = N'uploading'
                OR status = N'ready'
                OR status = N'delete_pending'
                OR status = N'failed'
                OR status = N'deleted');
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'CK_content_assets_metadata'
          AND parent_object_id = OBJECT_ID(N'dbo.content_assets'))
    BEGIN
        ALTER TABLE dbo.content_assets WITH CHECK
            ADD CONSTRAINT CK_content_assets_metadata
            CHECK (sort_order >= 0
                AND (size_bytes IS NULL OR size_bytes > 0)
                AND ((status = N'ready'
                        AND sha256 IS NOT NULL
                        AND size_bytes IS NOT NULL
                        AND content_type IS NOT NULL
                        AND ready_at IS NOT NULL
                        AND deleted_at IS NULL)
                    OR (status = N'uploading'
                        AND sha256 IS NULL
                        AND size_bytes IS NULL
                        AND content_type IS NULL
                        AND ready_at IS NULL
                        AND deleted_at IS NULL)
                    OR (status IN (N'delete_pending', N'failed') AND deleted_at IS NULL)
                    OR (status = N'deleted' AND deleted_at IS NOT NULL)));
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'UX_content_assets_storage_key'
          AND object_id = OBJECT_ID(N'dbo.content_assets'))
    BEGIN
        CREATE UNIQUE INDEX UX_content_assets_storage_key
            ON dbo.content_assets (storage_key);
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'UX_content_assets_ready_order'
          AND object_id = OBJECT_ID(N'dbo.content_assets'))
    BEGIN
        CREATE UNIQUE INDEX UX_content_assets_ready_order
            ON dbo.content_assets (tenant_id, content_item_id, sort_order)
            WHERE status = N'ready';
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'IX_content_assets_item_status_order'
          AND object_id = OBJECT_ID(N'dbo.content_assets'))
    BEGIN
        CREATE INDEX IX_content_assets_item_status_order
            ON dbo.content_assets (tenant_id, content_item_id, status, sort_order);
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'IX_content_assets_cleanup'
          AND object_id = OBJECT_ID(N'dbo.content_assets'))
    BEGIN
        CREATE INDEX IX_content_assets_cleanup
            ON dbo.content_assets (status, created_at);
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE name = N'FK_content_assets_content_items_tenant_item')
    BEGIN
        ALTER TABLE dbo.content_assets WITH CHECK
            ADD CONSTRAINT FK_content_assets_content_items_tenant_item
            FOREIGN KEY (tenant_id, content_item_id)
            REFERENCES dbo.content_items (tenant_id, id);
    END
END

IF OBJECT_ID(N'dbo.content_publish_attempts', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'CK_content_publish_attempts_revision'
          AND parent_object_id = OBJECT_ID(N'dbo.content_publish_attempts'))
    BEGIN
        ALTER TABLE dbo.content_publish_attempts WITH CHECK
            ADD CONSTRAINT CK_content_publish_attempts_revision
            CHECK (content_revision > 0 AND snapshot_schema_version > 0);
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'CK_content_publish_attempts_status'
          AND parent_object_id = OBJECT_ID(N'dbo.content_publish_attempts'))
    BEGIN
        ALTER TABLE dbo.content_publish_attempts WITH CHECK
            ADD CONSTRAINT CK_content_publish_attempts_status
            CHECK (status = N'claimed'
                OR status = N'transmitted'
                OR status = N'succeeded'
                OR status = N'failed'
                OR status = N'outcome_unknown'
                OR status = N'reconciled');
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'CK_content_publish_attempts_snapshot_json'
          AND parent_object_id = OBJECT_ID(N'dbo.content_publish_attempts'))
    BEGIN
        ALTER TABLE dbo.content_publish_attempts WITH CHECK
            ADD CONSTRAINT CK_content_publish_attempts_snapshot_json
            CHECK (ISJSON(assets_snapshot_json) = 1
                AND LEFT(LTRIM(assets_snapshot_json), 1) = N'['
                AND DATALENGTH(assets_snapshot_json) <= 131072);
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'CK_content_publish_attempts_state'
          AND parent_object_id = OBJECT_ID(N'dbo.content_publish_attempts'))
    BEGIN
        ALTER TABLE dbo.content_publish_attempts WITH CHECK
            ADD CONSTRAINT CK_content_publish_attempts_state
            CHECK (
                (status = N'claimed'
                    AND transmitted_at IS NULL
                    AND completed_at IS NULL
                    AND lease_token IS NOT NULL
                    AND lease_expires_at IS NOT NULL)
                OR (status = N'transmitted'
                    AND transmitted_at IS NOT NULL
                    AND completed_at IS NULL
                    AND lease_token IS NOT NULL
                    AND lease_expires_at IS NOT NULL)
                OR (status IN (N'succeeded', N'failed', N'outcome_unknown', N'reconciled')
                    AND completed_at IS NOT NULL
                    AND lease_token IS NULL
                    AND lease_expires_at IS NULL));
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'UX_content_publish_attempts_token'
          AND object_id = OBJECT_ID(N'dbo.content_publish_attempts'))
    BEGIN
        CREATE UNIQUE INDEX UX_content_publish_attempts_token
            ON dbo.content_publish_attempts (attempt_token);
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'UX_content_publish_attempts_idempotency'
          AND object_id = OBJECT_ID(N'dbo.content_publish_attempts'))
    BEGIN
        CREATE UNIQUE INDEX UX_content_publish_attempts_idempotency
            ON dbo.content_publish_attempts (tenant_id, idempotency_key);
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'UX_content_publish_attempts_operation'
          AND object_id = OBJECT_ID(N'dbo.content_publish_attempts'))
    BEGIN
        CREATE UNIQUE INDEX UX_content_publish_attempts_operation
            ON dbo.content_publish_attempts (
                tenant_id,
                schedule_id,
                content_item_id,
                content_revision,
                publish_target_id);
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'IX_content_publish_attempts_status'
          AND object_id = OBJECT_ID(N'dbo.content_publish_attempts'))
    BEGIN
        CREATE INDEX IX_content_publish_attempts_status
            ON dbo.content_publish_attempts (tenant_id, status, claimed_at);
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'IX_content_publish_attempts_expired_lease'
          AND object_id = OBJECT_ID(N'dbo.content_publish_attempts'))
    BEGIN
        CREATE INDEX IX_content_publish_attempts_expired_lease
            ON dbo.content_publish_attempts (tenant_id, status, lease_expires_at);
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE name = N'FK_content_publish_attempts_content_schedule_tenant_schedule')
    BEGIN
        ALTER TABLE dbo.content_publish_attempts WITH CHECK
            ADD CONSTRAINT FK_content_publish_attempts_content_schedule_tenant_schedule
            FOREIGN KEY (tenant_id, schedule_id)
            REFERENCES dbo.content_schedule (tenant_id, id);
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE name = N'FK_content_publish_attempts_content_items_tenant_item')
    BEGIN
        ALTER TABLE dbo.content_publish_attempts WITH CHECK
            ADD CONSTRAINT FK_content_publish_attempts_content_items_tenant_item
            FOREIGN KEY (tenant_id, content_item_id)
            REFERENCES dbo.content_items (tenant_id, id);
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE name = N'FK_content_publish_attempts_content_schedule_scope')
    BEGIN
        ALTER TABLE dbo.content_publish_attempts WITH CHECK
            ADD CONSTRAINT FK_content_publish_attempts_content_schedule_scope
            FOREIGN KEY (
                tenant_id,
                schedule_id,
                content_item_id,
                content_revision,
                publish_target_id)
            REFERENCES dbo.content_schedule (
                tenant_id,
                id,
                content_item_id,
                content_revision,
                publish_target_id);
    END
END

IF OBJECT_ID(N'dbo.audit_logs', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'CK_audit_logs_state_sequence'
          AND parent_object_id = OBJECT_ID(N'dbo.audit_logs'))
    BEGIN
        ALTER TABLE dbo.audit_logs WITH CHECK
            ADD CONSTRAINT CK_audit_logs_state_sequence
            CHECK (state_sequence IS NULL OR state_sequence > 0);
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'UX_audit_logs_tenant_event_key'
          AND object_id = OBJECT_ID(N'dbo.audit_logs'))
    BEGIN
        CREATE UNIQUE INDEX UX_audit_logs_tenant_event_key
            ON dbo.audit_logs (tenant_id, event_key)
            WHERE event_key IS NOT NULL;
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'IX_audit_logs_resource_sequence'
          AND object_id = OBJECT_ID(N'dbo.audit_logs'))
    BEGIN
        CREATE INDEX IX_audit_logs_resource_sequence
            ON dbo.audit_logs (tenant_id, resource_id, state_sequence)
            WHERE state_sequence IS NOT NULL;
    END
END

IF OBJECT_ID(N'dbo.content_workflow_metrics_hourly', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'CK_content_workflow_metrics_hourly_nonnegative'
          AND parent_object_id = OBJECT_ID(N'dbo.content_workflow_metrics_hourly'))
    BEGIN
        ALTER TABLE dbo.content_workflow_metrics_hourly WITH CHECK
            ADD CONSTRAINT CK_content_workflow_metrics_hourly_nonnegative
            CHECK (
                review_passed_count >= 0
                AND review_rejected_count >= 0
                AND review_needs_human_count >= 0
                AND review_failed_count >= 0
                AND image_reviewed_count >= 0
                AND image_not_applicable_count >= 0
                AND image_skipped_unsupported_count >= 0
                AND image_failed_count >= 0
                AND human_fallback_count >= 0
                AND human_override_count >= 0
                AND human_reject_count >= 0
                AND held_schedule_count >= 0
                AND publish_succeeded_count >= 0
                AND publish_failed_count >= 0
                AND publish_outcome_unknown_count >= 0
                AND review_latency_ms_sum >= 0
                AND review_latency_sample_count >= 0
                AND publish_latency_ms_sum >= 0
                AND publish_latency_sample_count >= 0
                AND llm_input_tokens >= 0
                AND llm_output_tokens >= 0
                AND llm_cost_usd >= 0);
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'CK_content_workflow_metrics_hourly_bucket'
          AND parent_object_id = OBJECT_ID(N'dbo.content_workflow_metrics_hourly'))
    BEGIN
        ALTER TABLE dbo.content_workflow_metrics_hourly WITH CHECK
            ADD CONSTRAINT CK_content_workflow_metrics_hourly_bucket
            CHECK (DATEPART(TZOFFSET, hour_utc) = 0
                AND DATEPART(MINUTE, hour_utc) = 0
                AND DATEPART(SECOND, hour_utc) = 0);
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'UX_content_workflow_metrics_hourly_tenant_hour'
          AND object_id = OBJECT_ID(N'dbo.content_workflow_metrics_hourly'))
    BEGIN
        CREATE UNIQUE INDEX UX_content_workflow_metrics_hourly_tenant_hour
            ON dbo.content_workflow_metrics_hourly (tenant_id, hour_utc);
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'IX_content_workflow_metrics_hourly_retention'
          AND object_id = OBJECT_ID(N'dbo.content_workflow_metrics_hourly'))
    BEGIN
        CREATE INDEX IX_content_workflow_metrics_hourly_retention
            ON dbo.content_workflow_metrics_hourly (hour_utc, tenant_id);
    END
END

IF OBJECT_ID(N'dbo.tenants', N'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.tenants WITH CHECK CHECK CONSTRAINT CK_tenants_content_publishing_policy;
    ALTER TABLE dbo.tenants WITH CHECK CHECK CONSTRAINT CK_tenants_content_publishing_policy_version;
END

IF OBJECT_ID(N'dbo.content_items', N'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.content_items WITH CHECK CHECK CONSTRAINT CK_content_items_content_revision;
    ALTER TABLE dbo.content_items WITH CHECK CHECK CONSTRAINT CK_content_items_agent_review_status;
    ALTER TABLE dbo.content_items WITH CHECK CHECK CONSTRAINT CK_content_items_image_review_status;
    ALTER TABLE dbo.content_items WITH CHECK CHECK CONSTRAINT CK_content_items_approval_mode;
END

IF OBJECT_ID(N'dbo.content_schedule', N'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.content_schedule WITH CHECK CHECK CONSTRAINT CK_content_schedule_content_revision;
    ALTER TABLE dbo.content_schedule WITH CHECK CHECK CONSTRAINT CK_content_schedule_status;
    ALTER TABLE dbo.content_schedule WITH CHECK CHECK CONSTRAINT CK_content_schedule_active_revision_slot_v2;
    ALTER TABLE dbo.content_schedule WITH CHECK CHECK CONSTRAINT FK_content_schedule_content_items_tenant_item;
END

IF OBJECT_ID(N'dbo.content_review_tasks', N'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.content_review_tasks WITH CHECK CHECK CONSTRAINT CK_content_review_tasks_revision;
    ALTER TABLE dbo.content_review_tasks WITH CHECK CHECK CONSTRAINT CK_content_review_tasks_status;
    ALTER TABLE dbo.content_review_tasks WITH CHECK CHECK CONSTRAINT CK_content_review_tasks_state;
    ALTER TABLE dbo.content_review_tasks WITH CHECK CHECK CONSTRAINT FK_content_review_tasks_content_items_tenant_item;
END

IF OBJECT_ID(N'dbo.content_assets', N'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.content_assets WITH CHECK CHECK CONSTRAINT CK_content_assets_status;
    ALTER TABLE dbo.content_assets WITH CHECK CHECK CONSTRAINT CK_content_assets_metadata;
    ALTER TABLE dbo.content_assets WITH CHECK CHECK CONSTRAINT FK_content_assets_content_items_tenant_item;
END

IF OBJECT_ID(N'dbo.content_publish_attempts', N'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.content_publish_attempts WITH CHECK CHECK CONSTRAINT CK_content_publish_attempts_revision;
    ALTER TABLE dbo.content_publish_attempts WITH CHECK CHECK CONSTRAINT CK_content_publish_attempts_status;
    ALTER TABLE dbo.content_publish_attempts WITH CHECK CHECK CONSTRAINT CK_content_publish_attempts_snapshot_json;
    ALTER TABLE dbo.content_publish_attempts WITH CHECK CHECK CONSTRAINT CK_content_publish_attempts_state;
    ALTER TABLE dbo.content_publish_attempts WITH CHECK CHECK CONSTRAINT FK_content_publish_attempts_content_schedule_tenant_schedule;
    ALTER TABLE dbo.content_publish_attempts WITH CHECK CHECK CONSTRAINT FK_content_publish_attempts_content_items_tenant_item;
    ALTER TABLE dbo.content_publish_attempts WITH CHECK CHECK CONSTRAINT FK_content_publish_attempts_content_schedule_scope;
END

IF OBJECT_ID(N'dbo.audit_logs', N'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.audit_logs WITH CHECK CHECK CONSTRAINT CK_audit_logs_state_sequence;
END

IF OBJECT_ID(N'dbo.content_workflow_metrics_hourly', N'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.content_workflow_metrics_hourly WITH CHECK CHECK CONSTRAINT CK_content_workflow_metrics_hourly_nonnegative;
    ALTER TABLE dbo.content_workflow_metrics_hourly WITH CHECK CHECK CONSTRAINT CK_content_workflow_metrics_hourly_bucket;
END

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
