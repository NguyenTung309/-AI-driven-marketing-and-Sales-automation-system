-- Manual content publishing cutover classification v1.
-- Run only while API, AgentService, Hangfire publication entry points and provider egress are paused.
-- Requires migrations through 0077. Do not add this file to automatic migration or repair execution.
-- Run with a dedicated sqlcmd connection. One SqlCommand, no GO; safe to re-run after a marked execution.

SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;

IF @@TRANCOUNT <> 0
    THROW 52000, 'content_cutover_caller_transaction_not_allowed', 1;

SET XACT_ABORT ON;
SET LOCK_TIMEOUT 15000;

DECLARE @marker NVARCHAR(260) = N'manual_content_publishing_cutover_classification_v1.sql';
DECLARE @completionMarker NVARCHAR(260) = N'manual_content_publishing_cutover_classification_v1.completed';
DECLARE @cutoverAt DATETIMEOFFSET;
DECLARE @completionAt DATETIMEOFFSET;
DECLARE @applicationLockResult INT;
DECLARE @publishedCount INT = 0;
DECLARE @unpublishedCount INT = 0;
DECLARE @tasksInserted INT = 0;
DECLARE @tasksReset INT = 0;
DECLARE @tasksCanceled INT = 0;
DECLARE @schedulesHeld INT = 0;
DECLARE @auditEventsInserted INT = 0;

BEGIN TRY
    BEGIN TRANSACTION;

    EXEC @applicationLockResult = sys.sp_getapplock
        @Resource = N'clawbot:content-publishing-cutover:v1',
        @LockMode = N'Exclusive',
        @LockOwner = N'Transaction',
        @LockTimeout = 0;
    IF @applicationLockResult < 0
        THROW 52001, 'content_cutover_lock_unavailable', 1;

    IF OBJECT_ID(N'dbo.schema_migrations', N'U') IS NULL
       OR OBJECT_ID(N'dbo.tenants', N'U') IS NULL
       OR OBJECT_ID(N'dbo.content_items', N'U') IS NULL
       OR OBJECT_ID(N'dbo.content_schedule', N'U') IS NULL
       OR OBJECT_ID(N'dbo.content_review_tasks', N'U') IS NULL
       OR OBJECT_ID(N'dbo.content_assets', N'U') IS NULL
       OR OBJECT_ID(N'dbo.content_publish_attempts', N'U') IS NULL
       OR OBJECT_ID(N'dbo.audit_logs', N'U') IS NULL
        THROW 52002, 'content_cutover_schema_missing', 1;

    IF COL_LENGTH(N'dbo.tenants', N'content_publishing_approval_policy') IS NULL
       OR COL_LENGTH(N'dbo.tenants', N'content_publishing_policy_version') IS NULL
       OR COL_LENGTH(N'dbo.tenants', N'content_publishing_policy_updated_at') IS NULL
       OR COL_LENGTH(N'dbo.content_items', N'content_revision') IS NULL
       OR COL_LENGTH(N'dbo.content_items', N'agent_review_status') IS NULL
       OR COL_LENGTH(N'dbo.content_items', N'agent_reviewed_revision') IS NULL
       OR COL_LENGTH(N'dbo.content_items', N'reviewed_by_agent_id') IS NULL
       OR COL_LENGTH(N'dbo.content_items', N'agent_review_started_at') IS NULL
       OR COL_LENGTH(N'dbo.content_items', N'agent_reviewed_at') IS NULL
       OR COL_LENGTH(N'dbo.content_items', N'agent_review_reason') IS NULL
       OR COL_LENGTH(N'dbo.content_items', N'image_review_status') IS NULL
       OR COL_LENGTH(N'dbo.content_items', N'reviewed_image_count') IS NULL
       OR COL_LENGTH(N'dbo.content_items', N'agent_review_attempt_count') IS NULL
       OR COL_LENGTH(N'dbo.content_items', N'publishing_policy_applied') IS NULL
       OR COL_LENGTH(N'dbo.content_items', N'publishing_policy_version_applied') IS NULL
       OR COL_LENGTH(N'dbo.content_items', N'human_approval_requirement_reason') IS NULL
       OR COL_LENGTH(N'dbo.content_items', N'approved_revision') IS NULL
       OR COL_LENGTH(N'dbo.content_items', N'approval_mode') IS NULL
       OR COL_LENGTH(N'dbo.content_items', N'approval_reason') IS NULL
       OR COL_LENGTH(N'dbo.content_items', N'active_publish_attempt_id') IS NULL
       OR COL_LENGTH(N'dbo.content_items', N'approved_by_agent_id') IS NULL
       OR COL_LENGTH(N'dbo.content_items', N'created_by_agent_id') IS NULL
       OR COL_LENGTH(N'dbo.content_items', N'rejected_reason') IS NULL
       OR COL_LENGTH(N'dbo.content_schedule', N'content_revision') IS NULL
       OR COL_LENGTH(N'dbo.content_schedule', N'active_revision_slot') IS NULL
       OR COL_LENGTH(N'dbo.content_schedule', N'approval_mode') IS NULL
       OR COL_LENGTH(N'dbo.content_schedule', N'publishing_policy_version_applied') IS NULL
       OR COL_LENGTH(N'dbo.content_schedule', N'next_attempt_at') IS NULL
       OR COL_LENGTH(N'dbo.content_schedule', N'last_error_code') IS NULL
       OR COL_LENGTH(N'dbo.audit_logs', N'event_key') IS NULL
       OR COL_LENGTH(N'dbo.audit_logs', N'state_sequence') IS NULL
        THROW 52003, 'content_cutover_columns_missing', 1;

    DECLARE @RequiredIndexes TABLE (
        table_name SYSNAME NOT NULL,
        index_name SYSNAME NOT NULL,
        key_columns NVARCHAR(512) NOT NULL,
        filter_definition NVARCHAR(256) NULL,
        PRIMARY KEY (table_name, index_name));

    INSERT INTO @RequiredIndexes (table_name, index_name, key_columns, filter_definition)
    VALUES
        (N'content_items', N'UX_content_items_tenant_id_id', N'tenant_id,id', NULL),
        (N'content_schedule', N'UX_content_schedule_tenant_id_id', N'tenant_id,id', NULL),
        (N'content_schedule', N'UX_content_schedule_publish_attempt_scope', N'tenant_id,id,content_item_id,content_revision,publish_target_id', NULL),
        (N'content_schedule', N'UX_content_schedule_active_revision', N'tenant_id,content_item_id,active_revision_slot', N'([active_revision_slot] IS NOT NULL)'),
        (N'content_schedule', N'ix_content_schedule_pending_item', N'content_item_id', N'([status]=''pending'')'),
        (N'content_schedule', N'UX_content_schedule_legacy_pending_item', N'tenant_id,content_item_id', N'([status]=N''pending'' AND [active_revision_slot] IS NULL)'),
        (N'content_review_tasks', N'UX_content_review_tasks_item_revision', N'tenant_id,content_item_id,content_revision', NULL),
        (N'content_assets', N'UX_content_assets_storage_key', N'storage_key', NULL),
        (N'content_assets', N'UX_content_assets_ready_order', N'tenant_id,content_item_id,sort_order', N'([status]=N''ready'')'),
        (N'content_publish_attempts', N'UX_content_publish_attempts_token', N'attempt_token', NULL),
        (N'content_publish_attempts', N'UX_content_publish_attempts_idempotency', N'tenant_id,idempotency_key', NULL),
        (N'content_publish_attempts', N'UX_content_publish_attempts_operation', N'tenant_id,schedule_id,content_item_id,content_revision,publish_target_id', NULL),
        (N'audit_logs', N'UX_audit_logs_tenant_event_key', N'tenant_id,event_key', N'([event_key] IS NOT NULL)');

    IF EXISTS (
        SELECT 1
        FROM @RequiredIndexes AS requiredIndex
        LEFT JOIN (
            SELECT
                indexDefinition.object_id,
                indexDefinition.name AS index_name,
                indexDefinition.is_unique,
                indexDefinition.is_disabled,
                indexDefinition.has_filter,
                indexDefinition.filter_definition,
                STRING_AGG(CONVERT(NVARCHAR(MAX), columnDefinition.name), N',')
                    WITHIN GROUP (ORDER BY indexColumn.key_ordinal) AS key_columns
            FROM sys.indexes AS indexDefinition
            INNER JOIN sys.index_columns AS indexColumn
                ON indexColumn.object_id = indexDefinition.object_id
               AND indexColumn.index_id = indexDefinition.index_id
               AND indexColumn.key_ordinal > 0
            INNER JOIN sys.columns AS columnDefinition
                ON columnDefinition.object_id = indexColumn.object_id
               AND columnDefinition.column_id = indexColumn.column_id
            WHERE indexDefinition.is_hypothetical = 0
            GROUP BY
                indexDefinition.object_id,
                indexDefinition.name,
                indexDefinition.is_unique,
                indexDefinition.is_disabled,
                indexDefinition.has_filter,
                indexDefinition.filter_definition) AS actualIndex
            ON actualIndex.object_id = OBJECT_ID(N'dbo.' + requiredIndex.table_name)
           AND actualIndex.index_name = requiredIndex.index_name
        WHERE actualIndex.index_name IS NULL
           OR actualIndex.is_unique <> 1
           OR actualIndex.is_disabled <> 0
           OR actualIndex.key_columns <> requiredIndex.key_columns
           OR (requiredIndex.filter_definition IS NULL
               AND (actualIndex.has_filter <> 0 OR actualIndex.filter_definition IS NOT NULL))
           OR (requiredIndex.filter_definition IS NOT NULL
               AND (actualIndex.has_filter <> 1
                    OR actualIndex.filter_definition IS NULL
                    OR actualIndex.filter_definition <> requiredIndex.filter_definition)))
        THROW 52004, 'content_cutover_indexes_invalid', 1;

    DECLARE @RequiredChecks TABLE (
        table_name SYSNAME NOT NULL,
        constraint_name SYSNAME NOT NULL,
        definition_hash VARBINARY(32) NOT NULL,
        PRIMARY KEY (table_name, constraint_name));

    INSERT INTO @RequiredChecks (table_name, constraint_name, definition_hash)
    VALUES
        (N'tenants', N'CK_tenants_content_publishing_policy', 0x5F94BC050840DC2437C0D2BF4698530BE7CC238B0D159C3D9A99F115CE4C13C7),
        (N'tenants', N'CK_tenants_content_publishing_policy_version', 0xBEA19D4EBA10A98A9A401C1B1DF170618A0D287DBD9F6C5E7D4F19148F95C588),
        (N'content_items', N'CK_content_items_content_revision', 0xCDAB7266766DBD3E97F684C5E7CBE20630BAF6FAF182BA8FFE6FD3BF754B65D8),
        (N'content_items', N'CK_content_items_agent_review_status', 0xAD973C001547715D772098A4302683CEBC2E10CF90ADA4BBC6FB1DD81F0D22A1),
        (N'content_items', N'CK_content_items_image_review_status', 0x0192078E8779F8A93CCB3F1B165D943C20CCA1DDBAE1EA816C081AC3E35E58C5),
        (N'content_items', N'CK_content_items_approval_mode', 0x2C094DF8FBBB935DF90C9EF8526FD53F291BA19135240163C7143006E42A5466),
        (N'content_schedule', N'CK_content_schedule_content_revision', 0x944E6020D52FA0D8AC23B96747D704F49B2DB532FD3AC373F48ACA7A31CC6F0D),
        (N'content_schedule', N'CK_content_schedule_status', 0x4566B6FE7B3AF3DBBE96B04E0312B253CC9527A410AD0D59CD06645777BC89C7),
        (N'content_schedule', N'CK_content_schedule_active_revision_slot_v2', 0x1303402B80A5FA16EDCDC8EFFCCCC6DC01A00E1502B6044A244030D417E1CC9F),
        (N'content_review_tasks', N'CK_content_review_tasks_revision', 0x97B642F8BF262CF5D1417A9B60CDE5B744521662050C449A1B2221A27BA5BDAD),
        (N'content_review_tasks', N'CK_content_review_tasks_status', 0x4EECC13F70307072634B4D7773A8F772663EDD569BF3C82CCE0764ABCEDA882B),
        (N'content_review_tasks', N'CK_content_review_tasks_state', 0xB09A5AA3D225CC101DE06708716F0BB2B806E0AA6B1D589AB4238437E3CD5CA3),
        (N'content_assets', N'CK_content_assets_status', 0x5A5DEFE5DDAC546A8AB6A234464AB13987631D95C059CC0EF32FD41A8613C16E),
        (N'content_assets', N'CK_content_assets_metadata', 0xE0280B00267349AA4E664C43BEEB596D803EEE936CE3FD8D7B47DE2002AAA89A),
        (N'content_publish_attempts', N'CK_content_publish_attempts_revision', 0x91CF557D888CE993503575A74FDEC055F224D488165E871E2256C1E92B6DECA8),
        (N'content_publish_attempts', N'CK_content_publish_attempts_status', 0xE836B238495B731C648AE7E72833D9F0AD942D953BE0CFDABEDD4AE66E54B015),
        (N'content_publish_attempts', N'CK_content_publish_attempts_snapshot_json', 0x0EAF2335FD8958BDBE39F12712456BBD606D0B5E7129C0F48E868F30572D23D9),
        (N'content_publish_attempts', N'CK_content_publish_attempts_state', 0x6C82DD735E80BA1F8A746048A902C83BE00C4D659F892E9EBB3BF2DED6C21C76),
        (N'audit_logs', N'CK_audit_logs_state_sequence', 0x936E5CF3828F40CCD3F63A77DEB61E28AEA437F4C25E2AB8303CD73CCF531F0D);

    IF EXISTS (
        SELECT 1
        FROM @RequiredChecks AS requiredCheck
        LEFT JOIN sys.check_constraints AS actualCheck
            ON actualCheck.parent_object_id = OBJECT_ID(N'dbo.' + requiredCheck.table_name)
           AND actualCheck.name = requiredCheck.constraint_name
        WHERE actualCheck.object_id IS NULL
           OR actualCheck.is_disabled = 1
           OR actualCheck.is_not_trusted = 1
           OR actualCheck.is_not_for_replication = 1
           OR actualCheck.definition IS NULL
           OR HASHBYTES(
                'SHA2_256',
                CONVERT(NVARCHAR(MAX), actualCheck.definition)) <> requiredCheck.definition_hash)
        THROW 52005, 'content_cutover_check_constraint_invalid', 1;

    DECLARE @RequiredForeignKeys TABLE (
        constraint_name SYSNAME NOT NULL PRIMARY KEY,
        parent_schema SYSNAME NOT NULL,
        parent_table SYSNAME NOT NULL,
        referenced_schema SYSNAME NOT NULL,
        referenced_table SYSNAME NOT NULL,
        parent_columns NVARCHAR(512) NOT NULL,
        referenced_columns NVARCHAR(512) NOT NULL,
        delete_action NVARCHAR(60) NOT NULL,
        update_action NVARCHAR(60) NOT NULL);

    INSERT INTO @RequiredForeignKeys (
        constraint_name,
        parent_schema,
        parent_table,
        referenced_schema,
        referenced_table,
        parent_columns,
        referenced_columns,
        delete_action,
        update_action)
    VALUES
        (N'FK_content_schedule_content_items_tenant_item', N'dbo', N'content_schedule', N'dbo', N'content_items', N'tenant_id,content_item_id', N'tenant_id,id', N'NO_ACTION', N'NO_ACTION'),
        (N'FK_content_review_tasks_content_items_tenant_item', N'dbo', N'content_review_tasks', N'dbo', N'content_items', N'tenant_id,content_item_id', N'tenant_id,id', N'NO_ACTION', N'NO_ACTION'),
        (N'FK_content_assets_content_items_tenant_item', N'dbo', N'content_assets', N'dbo', N'content_items', N'tenant_id,content_item_id', N'tenant_id,id', N'NO_ACTION', N'NO_ACTION'),
        (N'FK_content_publish_attempts_content_schedule_tenant_schedule', N'dbo', N'content_publish_attempts', N'dbo', N'content_schedule', N'tenant_id,schedule_id', N'tenant_id,id', N'NO_ACTION', N'NO_ACTION'),
        (N'FK_content_publish_attempts_content_items_tenant_item', N'dbo', N'content_publish_attempts', N'dbo', N'content_items', N'tenant_id,content_item_id', N'tenant_id,id', N'NO_ACTION', N'NO_ACTION'),
        (N'FK_content_publish_attempts_content_schedule_scope', N'dbo', N'content_publish_attempts', N'dbo', N'content_schedule', N'tenant_id,schedule_id,content_item_id,content_revision,publish_target_id', N'tenant_id,id,content_item_id,content_revision,publish_target_id', N'NO_ACTION', N'NO_ACTION');

    IF EXISTS (
        SELECT 1
        FROM @RequiredForeignKeys AS requiredForeignKey
        LEFT JOIN (
            SELECT
                foreignKey.name AS constraint_name,
                OBJECT_SCHEMA_NAME(foreignKey.parent_object_id) AS parent_schema,
                OBJECT_NAME(foreignKey.parent_object_id) AS parent_table,
                OBJECT_SCHEMA_NAME(foreignKey.referenced_object_id) AS referenced_schema,
                OBJECT_NAME(foreignKey.referenced_object_id) AS referenced_table,
                foreignKey.delete_referential_action_desc AS delete_action,
                foreignKey.update_referential_action_desc AS update_action,
                foreignKey.is_disabled,
                foreignKey.is_not_trusted,
                foreignKey.is_not_for_replication,
                STRING_AGG(CONVERT(NVARCHAR(MAX), parentColumn.name), N',')
                    WITHIN GROUP (ORDER BY foreignKeyColumn.constraint_column_id) AS parent_columns,
                STRING_AGG(CONVERT(NVARCHAR(MAX), referencedColumn.name), N',')
                    WITHIN GROUP (ORDER BY foreignKeyColumn.constraint_column_id) AS referenced_columns
            FROM sys.foreign_keys AS foreignKey
            INNER JOIN sys.foreign_key_columns AS foreignKeyColumn
                ON foreignKeyColumn.constraint_object_id = foreignKey.object_id
            INNER JOIN sys.columns AS parentColumn
                ON parentColumn.object_id = foreignKey.parent_object_id
               AND parentColumn.column_id = foreignKeyColumn.parent_column_id
            INNER JOIN sys.columns AS referencedColumn
                ON referencedColumn.object_id = foreignKey.referenced_object_id
               AND referencedColumn.column_id = foreignKeyColumn.referenced_column_id
            GROUP BY
                foreignKey.name,
                foreignKey.parent_object_id,
                foreignKey.referenced_object_id,
                foreignKey.delete_referential_action_desc,
                foreignKey.update_referential_action_desc,
                foreignKey.is_disabled,
                foreignKey.is_not_trusted,
                foreignKey.is_not_for_replication) AS actualForeignKey
            ON actualForeignKey.constraint_name = requiredForeignKey.constraint_name
           AND actualForeignKey.parent_schema = requiredForeignKey.parent_schema
           AND actualForeignKey.parent_table = requiredForeignKey.parent_table
        WHERE actualForeignKey.constraint_name IS NULL
           OR actualForeignKey.parent_schema <> requiredForeignKey.parent_schema
           OR actualForeignKey.parent_table <> requiredForeignKey.parent_table
           OR actualForeignKey.referenced_schema <> requiredForeignKey.referenced_schema
           OR actualForeignKey.referenced_table <> requiredForeignKey.referenced_table
           OR actualForeignKey.parent_columns <> requiredForeignKey.parent_columns
           OR actualForeignKey.referenced_columns <> requiredForeignKey.referenced_columns
           OR actualForeignKey.delete_action COLLATE DATABASE_DEFAULT <> requiredForeignKey.delete_action
           OR actualForeignKey.update_action COLLATE DATABASE_DEFAULT <> requiredForeignKey.update_action
           OR actualForeignKey.is_disabled = 1
           OR actualForeignKey.is_not_trusted = 1
           OR actualForeignKey.is_not_for_replication = 1)
        THROW 52007, 'content_cutover_foreign_key_invalid', 1;

    SELECT TOP (0) 1 FROM dbo.tenants WITH (TABLOCKX, HOLDLOCK);
    SELECT TOP (0) 1 FROM dbo.content_items WITH (TABLOCKX, HOLDLOCK);
    SELECT TOP (0) 1 FROM dbo.content_schedule WITH (TABLOCKX, HOLDLOCK);
    SELECT TOP (0) 1 FROM dbo.content_publish_attempts WITH (TABLOCKX, HOLDLOCK);
    SELECT TOP (0) 1 FROM dbo.content_review_tasks WITH (TABLOCKX, HOLDLOCK);

    SELECT @cutoverAt = applied_at
    FROM dbo.schema_migrations WITH (UPDLOCK, HOLDLOCK)
    WHERE filename = @marker;

    SELECT @completionAt = applied_at
    FROM dbo.schema_migrations WITH (UPDLOCK, HOLDLOCK)
    WHERE filename = @completionMarker;

    IF (@cutoverAt IS NULL AND @completionAt IS NOT NULL)
       OR (@cutoverAt IS NOT NULL AND (
            @completionAt IS NULL
            OR @completionAt <> @cutoverAt))
        THROW 52008, 'content_cutover_completion_marker_mismatch', 1;

    IF @cutoverAt IS NOT NULL
    BEGIN
        IF EXISTS (
            SELECT 1
            FROM dbo.tenants AS tenant
            LEFT JOIN dbo.audit_logs AS boundaryAudit
                ON boundaryAudit.tenant_id = tenant.id
               AND boundaryAudit.event_key = N'content-cutover:v1:boundary'
               AND boundaryAudit.occurred_at = @cutoverAt
            OUTER APPLY (
                SELECT COUNT_BIG(*) AS event_count
                FROM dbo.audit_logs AS itemAudit
                WHERE itemAudit.tenant_id = tenant.id
                  AND itemAudit.occurred_at = @cutoverAt
                  AND itemAudit.event_key LIKE N'content-cutover:v1:item:%'
                  AND itemAudit.resource_type = N'content_item'
                  AND itemAudit.resource_id IS NOT NULL
                  AND itemAudit.event_key = N'content-cutover:v1:item:' + LOWER(REPLACE(CONVERT(CHAR(36), itemAudit.resource_id), N'-', N''))
                  AND itemAudit.action IN (
                      N'content.migration_cutover.legacy_exempt',
                      N'content.migration_cutover.review_required')) AS itemEvents
            OUTER APPLY (
                SELECT COUNT_BIG(*) AS event_count
                FROM dbo.audit_logs AS scheduleAudit
                WHERE scheduleAudit.tenant_id = tenant.id
                  AND scheduleAudit.occurred_at = @cutoverAt
                  AND scheduleAudit.event_key LIKE N'content-cutover:v1:schedule:%'
                  AND scheduleAudit.resource_type = N'content_schedule'
                  AND scheduleAudit.resource_id IS NOT NULL
                  AND scheduleAudit.event_key = N'content-cutover:v1:schedule:' + LOWER(REPLACE(CONVERT(CHAR(36), scheduleAudit.resource_id), N'-', N''))
                  AND scheduleAudit.action = N'content.migration_cutover.schedule_held') AS scheduleEvents
            WHERE (tenant.created_at <= @cutoverAt
                    OR boundaryAudit.id IS NOT NULL
                    OR EXISTS (
                        SELECT 1
                        FROM dbo.content_items AS boundaryItem
                        WHERE boundaryItem.tenant_id = tenant.id
                          AND boundaryItem.created_at <= @cutoverAt)
                    OR EXISTS (
                        SELECT 1
                        FROM dbo.content_schedule AS boundarySchedule
                        WHERE boundarySchedule.tenant_id = tenant.id
                          AND boundarySchedule.created_at <= @cutoverAt))
              AND (boundaryAudit.id IS NULL
                   OR boundaryAudit.user_id IS NOT NULL
                   OR boundaryAudit.action <> N'content.migration_cutover.boundary'
                   OR boundaryAudit.resource_type <> N'content_cutover'
                   OR boundaryAudit.resource_id IS NOT NULL
                   OR boundaryAudit.state_sequence IS NULL
                   OR boundaryAudit.state_sequence <> 1
                   OR boundaryAudit.diff_json IS NULL
                   OR ISJSON(boundaryAudit.diff_json) <> 1
                   OR COALESCE(
                        JSON_VALUE(
                            CASE WHEN ISJSON(boundaryAudit.diff_json) = 1
                                THEN boundaryAudit.diff_json ELSE N'{}' END,
                            N'$.classification'),
                        N'') <> N'boundary'
                   OR COALESCE(
                        TRY_CONVERT(
                            INT,
                            JSON_VALUE(
                                CASE WHEN ISJSON(boundaryAudit.diff_json) = 1
                                    THEN boundaryAudit.diff_json ELSE N'{}' END,
                                N'$.version')),
                        0) <> 1
                   OR COALESCE(
                        TRY_CONVERT(
                            BIGINT,
                            JSON_VALUE(
                                CASE WHEN ISJSON(boundaryAudit.diff_json) = 1
                                    THEN boundaryAudit.diff_json ELSE N'{}' END,
                                N'$.itemAuditCount')),
                        -1) <> itemEvents.event_count
                   OR COALESCE(
                        TRY_CONVERT(
                            BIGINT,
                            JSON_VALUE(
                                CASE WHEN ISJSON(boundaryAudit.diff_json) = 1
                                    THEN boundaryAudit.diff_json ELSE N'{}' END,
                                N'$.scheduleAuditCount')),
                        -1) <> scheduleEvents.event_count))
            THROW 52008, 'content_cutover_marker_audit_mismatch', 1;

        IF EXISTS (
            SELECT 1
            FROM dbo.content_items AS item
            WHERE item.created_at <= @cutoverAt
              AND NOT EXISTS (
                  SELECT 1
                  FROM dbo.audit_logs AS itemAudit
                  WHERE itemAudit.tenant_id = item.tenant_id
                    AND itemAudit.resource_type = N'content_item'
                    AND itemAudit.resource_id = item.id
                    AND itemAudit.event_key = N'content-cutover:v1:item:' + LOWER(REPLACE(CONVERT(CHAR(36), item.id), N'-', N''))
                    AND itemAudit.action IN (
                        N'content.migration_cutover.legacy_exempt',
                        N'content.migration_cutover.review_required')
                    AND itemAudit.state_sequence = 1
                    AND itemAudit.occurred_at = @cutoverAt))
            THROW 52008, 'content_cutover_marker_item_audit_missing', 1;

        IF EXISTS (
            SELECT 1
            FROM dbo.audit_logs AS audit
            WHERE audit.occurred_at = @cutoverAt
              AND audit.event_key LIKE N'content-cutover:v1:%'
              AND (audit.state_sequence IS NULL
                   OR audit.state_sequence <> 1
                   OR audit.user_id IS NOT NULL
                   OR (audit.event_key = N'content-cutover:v1:boundary'
                       AND (audit.action <> N'content.migration_cutover.boundary'
                            OR audit.resource_type <> N'content_cutover'
                            OR audit.resource_id IS NOT NULL))
                   OR (audit.event_key LIKE N'content-cutover:v1:item:%'
                       AND (audit.resource_type <> N'content_item'
                            OR audit.resource_id IS NULL
                            OR audit.event_key <> N'content-cutover:v1:item:' + LOWER(REPLACE(CONVERT(CHAR(36), audit.resource_id), N'-', N''))
                            OR audit.diff_json IS NULL
                            OR ISJSON(audit.diff_json) <> 1
                            OR NOT (
                                (audit.action = N'content.migration_cutover.legacy_exempt'
                                 AND audit.diff_json = N'{"classification":"legacy_exempt","historyOnly":true}')
                                OR (audit.action = N'content.migration_cutover.review_required'
                                    AND COALESCE(
                                        JSON_VALUE(
                                            CASE WHEN ISJSON(audit.diff_json) = 1
                                                THEN audit.diff_json ELSE N'{}' END,
                                            N'$.classification'),
                                        N'') = N'review_required'
                                    AND COALESCE(
                                        JSON_VALUE(
                                            CASE WHEN ISJSON(audit.diff_json) = 1
                                                THEN audit.diff_json ELSE N'{}' END,
                                            N'$.humanApprovalRequirementReason'),
                                        N'') = N'migration_cutover'
                                    AND COALESCE(
                                        TRY_CONVERT(
                                            INT,
                                            JSON_VALUE(
                                                CASE WHEN ISJSON(audit.diff_json) = 1
                                                    THEN audit.diff_json ELSE N'{}' END,
                                                N'$.contentRevision')),
                                        0) > 0))))
                   OR (audit.event_key LIKE N'content-cutover:v1:schedule:%'
                       AND (audit.action <> N'content.migration_cutover.schedule_held'
                            OR audit.resource_type <> N'content_schedule'
                            OR audit.resource_id IS NULL
                            OR audit.event_key <> N'content-cutover:v1:schedule:' + LOWER(REPLACE(CONVERT(CHAR(36), audit.resource_id), N'-', N''))
                            OR audit.diff_json IS NULL
                            OR ISJSON(audit.diff_json) <> 1
                            OR COALESCE(
                                JSON_VALUE(
                                    CASE WHEN ISJSON(audit.diff_json) = 1
                                        THEN audit.diff_json ELSE N'{}' END,
                                    N'$.classification'),
                                N'') <> N'held'
                            OR COALESCE(
                                JSON_VALUE(
                                    CASE WHEN ISJSON(audit.diff_json) = 1
                                        THEN audit.diff_json ELSE N'{}' END,
                                    N'$.reason'),
                                N'') <> N'migration_cutover'
                            OR COALESCE(
                                TRY_CONVERT(
                                    INT,
                                    JSON_VALUE(
                                        CASE WHEN ISJSON(audit.diff_json) = 1
                                            THEN audit.diff_json ELSE N'{}' END,
                                        N'$.contentRevision')),
                                0) <= 0))
                   OR (audit.event_key <> N'content-cutover:v1:boundary'
                       AND audit.event_key NOT LIKE N'content-cutover:v1:item:%'
                       AND audit.event_key NOT LIKE N'content-cutover:v1:schedule:%')))
            THROW 52008, 'content_cutover_marker_audit_mismatch', 1;

        COMMIT TRANSACTION;
        SET LOCK_TIMEOUT -1;
        SET XACT_ABORT OFF;
        SELECT
            @marker AS marker,
            @cutoverAt AS cutover_at,
            CAST(1 AS BIT) AS already_applied;
        RETURN;
    END

    SET @cutoverAt = TODATETIMEOFFSET(SYSUTCDATETIME(), N'+00:00');

    IF EXISTS (SELECT 1 FROM dbo.content_items WHERE status NOT IN (N'draft', N'approved', N'scheduled', N'published', N'rejected'))
        THROW 52009, 'content_cutover_unknown_content_status', 1;

    IF EXISTS (SELECT 1 FROM dbo.content_schedule WHERE status IN (N'publishing', N'outcome_unknown'))
        THROW 52010, 'content_cutover_schedule_in_flight', 1;

    IF EXISTS (SELECT 1 FROM dbo.content_publish_attempts WHERE status IN (N'claimed', N'transmitted', N'outcome_unknown'))
        THROW 52011, 'content_cutover_publish_attempt_in_flight', 1;

    IF EXISTS (SELECT 1 FROM dbo.content_items WHERE active_publish_attempt_id IS NOT NULL)
        THROW 52012, 'content_cutover_active_publish_attempt_present', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.content_schedule
        WHERE status IN (N'pending', N'held', N'publishing', N'outcome_unknown')
        GROUP BY tenant_id, content_item_id
        HAVING COUNT_BIG(*) > 1)
        THROW 52013, 'content_cutover_duplicate_active_schedule', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.content_schedule AS schedule
        LEFT JOIN dbo.content_items AS item
            ON item.tenant_id = schedule.tenant_id
           AND item.id = schedule.content_item_id
        WHERE schedule.status IN (N'pending', N'held', N'publishing', N'outcome_unknown')
          AND (item.id IS NULL
            OR item.status = N'published'
            OR (schedule.content_revision IS NOT NULL AND schedule.content_revision <> item.content_revision)
            OR schedule.posted_at IS NOT NULL
            OR schedule.post_url IS NOT NULL
            OR EXISTS (
                SELECT 1
                FROM dbo.content_schedule AS postedSchedule
                WHERE postedSchedule.tenant_id = schedule.tenant_id
                  AND postedSchedule.content_item_id = schedule.content_item_id
                  AND postedSchedule.status = N'posted')))
        THROW 52014, 'content_cutover_active_schedule_anomaly', 1;

    DECLARE @Published TABLE (
        tenant_id UNIQUEIDENTIFIER NOT NULL,
        content_item_id UNIQUEIDENTIFIER NOT NULL,
        content_revision INT NOT NULL,
        PRIMARY KEY (tenant_id, content_item_id));
    DECLARE @Unpublished TABLE (
        tenant_id UNIQUEIDENTIFIER NOT NULL,
        content_item_id UNIQUEIDENTIFIER NOT NULL,
        content_revision INT NOT NULL,
        PRIMARY KEY (tenant_id, content_item_id));
    DECLARE @ActiveSchedules TABLE (
        tenant_id UNIQUEIDENTIFIER NOT NULL,
        schedule_id UNIQUEIDENTIFIER NOT NULL,
        content_item_id UNIQUEIDENTIFIER NOT NULL,
        content_revision INT NOT NULL,
        PRIMARY KEY (tenant_id, schedule_id));

    INSERT INTO @Published (tenant_id, content_item_id, content_revision)
    SELECT item.tenant_id, item.id, item.content_revision
    FROM dbo.content_items AS item
    WHERE item.status = N'published'
       OR EXISTS (
            SELECT 1
            FROM dbo.content_schedule AS schedule
            WHERE schedule.tenant_id = item.tenant_id
              AND schedule.content_item_id = item.id
              AND schedule.status = N'posted');
    SET @publishedCount = @@ROWCOUNT;

    INSERT INTO @Unpublished (tenant_id, content_item_id, content_revision)
    SELECT item.tenant_id, item.id, item.content_revision
    FROM dbo.content_items AS item
    WHERE NOT EXISTS (
        SELECT 1
        FROM @Published AS published
        WHERE published.tenant_id = item.tenant_id
          AND published.content_item_id = item.id);
    SET @unpublishedCount = @@ROWCOUNT;

    INSERT INTO @ActiveSchedules (tenant_id, schedule_id, content_item_id, content_revision)
    SELECT schedule.tenant_id, schedule.id, schedule.content_item_id, unpublished.content_revision
    FROM dbo.content_schedule AS schedule
    INNER JOIN @Unpublished AS unpublished
        ON unpublished.tenant_id = schedule.tenant_id
       AND unpublished.content_item_id = schedule.content_item_id
    WHERE schedule.status IN (N'pending', N'held');

    UPDATE task
    SET status = N'canceled_stale',
        lease_token = NULL,
        lease_expires_at = NULL,
        last_error_code = N'published_history_only',
        completed_at = @cutoverAt
    FROM dbo.content_review_tasks AS task
    INNER JOIN @Published AS published
        ON published.tenant_id = task.tenant_id
       AND published.content_item_id = task.content_item_id
    WHERE task.status IN (N'pending', N'leased');
    SET @tasksCanceled += @@ROWCOUNT;

    UPDATE schedule
    SET status = N'held',
        content_revision = activeSchedule.content_revision,
        active_revision_slot = activeSchedule.content_revision,
        approval_mode = NULL,
        publishing_policy_version_applied = NULL,
        retry_count = 0,
        next_attempt_at = NULL,
        last_error_code = N'migration_cutover',
        last_error = N'migration_cutover',
        updated_at = @cutoverAt
    FROM dbo.content_schedule AS schedule
    INNER JOIN @ActiveSchedules AS activeSchedule
        ON activeSchedule.tenant_id = schedule.tenant_id
       AND activeSchedule.schedule_id = schedule.id;
    SET @schedulesHeld = @@ROWCOUNT;

    UPDATE task
    SET status = N'canceled_stale',
        lease_token = NULL,
        lease_expires_at = NULL,
        last_error_code = N'stale_content_revision',
        completed_at = @cutoverAt
    FROM dbo.content_review_tasks AS task
    INNER JOIN @Unpublished AS unpublished
        ON unpublished.tenant_id = task.tenant_id
       AND unpublished.content_item_id = task.content_item_id
    WHERE task.content_revision <> unpublished.content_revision
      AND task.status IN (N'pending', N'leased');
    SET @tasksCanceled += @@ROWCOUNT;

    UPDATE task
    SET status = N'pending',
        lease_token = NULL,
        lease_expires_at = NULL,
        attempt_count = 0,
        next_attempt_at = @cutoverAt,
        last_error_code = NULL,
        started_at = NULL,
        completed_at = NULL,
        created_at = @cutoverAt
    FROM dbo.content_review_tasks AS task
    INNER JOIN @Unpublished AS unpublished
        ON unpublished.tenant_id = task.tenant_id
       AND unpublished.content_item_id = task.content_item_id
       AND unpublished.content_revision = task.content_revision;
    SET @tasksReset = @@ROWCOUNT;

    INSERT INTO dbo.content_review_tasks (
        id,
        tenant_id,
        content_item_id,
        content_revision,
        status,
        lease_token,
        lease_expires_at,
        attempt_count,
        next_attempt_at,
        last_error_code,
        created_at,
        started_at,
        completed_at)
    SELECT
        NEWID(),
        unpublished.tenant_id,
        unpublished.content_item_id,
        unpublished.content_revision,
        N'pending',
        NULL,
        NULL,
        0,
        @cutoverAt,
        NULL,
        @cutoverAt,
        NULL,
        NULL
    FROM @Unpublished AS unpublished
    WHERE NOT EXISTS (
        SELECT 1
        FROM dbo.content_review_tasks AS task
        WHERE task.tenant_id = unpublished.tenant_id
          AND task.content_item_id = unpublished.content_item_id
          AND task.content_revision = unpublished.content_revision);
    SET @tasksInserted = @@ROWCOUNT;

    UPDATE item
    SET status = N'draft',
        agent_review_status = N'pending',
        agent_reviewed_revision = NULL,
        reviewed_by_agent_id = NULL,
        agent_review_started_at = NULL,
        agent_reviewed_at = NULL,
        agent_review_reason = NULL,
        image_review_status = N'pending',
        reviewed_image_count = 0,
        agent_review_attempt_count = 0,
        publishing_policy_applied = NULL,
        publishing_policy_version_applied = NULL,
        human_approval_requirement_reason = N'migration_cutover',
        approved_revision = NULL,
        approval_mode = NULL,
        approval_reason = NULL,
        approved_by = NULL,
        approved_by_agent_id = NULL,
        approved_at = NULL,
        rejected_reason = NULL,
        last_review_alert_at = NULL,
        updated_at = @cutoverAt
    FROM dbo.content_items AS item
    INNER JOIN @Unpublished AS unpublished
        ON unpublished.tenant_id = item.tenant_id
       AND unpublished.content_item_id = item.id;

    UPDATE item
    SET status = N'published',
        agent_review_status = N'legacy_exempt',
        agent_reviewed_revision = NULL,
        reviewed_by_agent_id = NULL,
        agent_review_started_at = NULL,
        agent_reviewed_at = NULL,
        agent_review_reason = NULL,
        image_review_status = N'not_applicable',
        reviewed_image_count = 0,
        agent_review_attempt_count = 0,
        publishing_policy_applied = NULL,
        publishing_policy_version_applied = NULL,
        human_approval_requirement_reason = NULL,
        approved_revision = NULL,
        approval_mode = NULL,
        approval_reason = NULL,
        updated_at = @cutoverAt
    FROM dbo.content_items AS item
    INNER JOIN @Published AS published
        ON published.tenant_id = item.tenant_id
       AND published.content_item_id = item.id;

    INSERT INTO dbo.audit_logs (
        id, tenant_id, user_id, action, resource_type, resource_id,
        diff_json, event_key, state_sequence, occurred_at)
    SELECT
        NEWID(), tenant.id, NULL,
        N'content.migration_cutover.boundary', N'content_cutover', NULL,
        CONCAT(
            N'{"classification":"boundary","version":1,"itemAuditCount":',
            (SELECT COUNT_BIG(*) FROM @Published AS publishedCount WHERE publishedCount.tenant_id = tenant.id)
                + (SELECT COUNT_BIG(*) FROM @Unpublished AS unpublishedCount WHERE unpublishedCount.tenant_id = tenant.id),
            N',"scheduleAuditCount":',
            (SELECT COUNT_BIG(*) FROM @ActiveSchedules AS scheduleCount WHERE scheduleCount.tenant_id = tenant.id),
            N'}'),
        N'content-cutover:v1:boundary', 1, @cutoverAt
    FROM dbo.tenants AS tenant
    WHERE NOT EXISTS (
          SELECT 1 FROM dbo.audit_logs AS audit
          WHERE audit.tenant_id = tenant.id
            AND audit.event_key = N'content-cutover:v1:boundary');
    SET @auditEventsInserted += @@ROWCOUNT;

    INSERT INTO dbo.audit_logs (
        id, tenant_id, user_id, action, resource_type, resource_id,
        diff_json, event_key, state_sequence, occurred_at)
    SELECT
        NEWID(), published.tenant_id, NULL,
        N'content.migration_cutover.legacy_exempt', N'content_item', published.content_item_id,
        N'{"classification":"legacy_exempt","historyOnly":true}',
        N'content-cutover:v1:item:' + LOWER(REPLACE(CONVERT(CHAR(36), published.content_item_id), N'-', N'')),
        1, @cutoverAt
    FROM @Published AS published
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.audit_logs AS audit
        WHERE audit.tenant_id = published.tenant_id
          AND audit.event_key = N'content-cutover:v1:item:' + LOWER(REPLACE(CONVERT(CHAR(36), published.content_item_id), N'-', N'')));
    SET @auditEventsInserted += @@ROWCOUNT;

    INSERT INTO dbo.audit_logs (
        id, tenant_id, user_id, action, resource_type, resource_id,
        diff_json, event_key, state_sequence, occurred_at)
    SELECT
        NEWID(), unpublished.tenant_id, NULL,
        N'content.migration_cutover.review_required', N'content_item', unpublished.content_item_id,
        CONCAT(N'{"classification":"review_required","humanApprovalRequirementReason":"migration_cutover","contentRevision":', unpublished.content_revision, N'}'),
        N'content-cutover:v1:item:' + LOWER(REPLACE(CONVERT(CHAR(36), unpublished.content_item_id), N'-', N'')),
        1, @cutoverAt
    FROM @Unpublished AS unpublished
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.audit_logs AS audit
        WHERE audit.tenant_id = unpublished.tenant_id
          AND audit.event_key = N'content-cutover:v1:item:' + LOWER(REPLACE(CONVERT(CHAR(36), unpublished.content_item_id), N'-', N'')));
    SET @auditEventsInserted += @@ROWCOUNT;

    INSERT INTO dbo.audit_logs (
        id, tenant_id, user_id, action, resource_type, resource_id,
        diff_json, event_key, state_sequence, occurred_at)
    SELECT
        NEWID(), activeSchedule.tenant_id, NULL,
        N'content.migration_cutover.schedule_held', N'content_schedule', activeSchedule.schedule_id,
        CONCAT(N'{"classification":"held","reason":"migration_cutover","contentRevision":', activeSchedule.content_revision, N'}'),
        N'content-cutover:v1:schedule:' + LOWER(REPLACE(CONVERT(CHAR(36), activeSchedule.schedule_id), N'-', N'')),
        1, @cutoverAt
    FROM @ActiveSchedules AS activeSchedule
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.audit_logs AS audit
        WHERE audit.tenant_id = activeSchedule.tenant_id
          AND audit.event_key = N'content-cutover:v1:schedule:' + LOWER(REPLACE(CONVERT(CHAR(36), activeSchedule.schedule_id), N'-', N'')));
    SET @auditEventsInserted += @@ROWCOUNT;

    IF EXISTS (
        SELECT 1
        FROM @Published AS published
        INNER JOIN dbo.content_items AS item
            ON item.tenant_id = published.tenant_id
           AND item.id = published.content_item_id
        WHERE item.status IS NULL
           OR item.status <> N'published'
           OR item.agent_review_status IS NULL
           OR item.agent_review_status <> N'legacy_exempt'
           OR item.agent_reviewed_revision IS NOT NULL
           OR item.reviewed_by_agent_id IS NOT NULL
           OR item.approved_revision IS NOT NULL
           OR item.approval_mode IS NOT NULL
           OR item.human_approval_requirement_reason IS NOT NULL)
        THROW 52015, 'content_cutover_published_assertion_failed', 1;

    IF EXISTS (
        SELECT 1
        FROM @Unpublished AS unpublished
        INNER JOIN dbo.content_items AS item
            ON item.tenant_id = unpublished.tenant_id
           AND item.id = unpublished.content_item_id
        WHERE item.status IS NULL
           OR item.status <> N'draft'
           OR item.agent_review_status IS NULL
           OR item.agent_review_status <> N'pending'
           OR item.image_review_status IS NULL
           OR item.image_review_status <> N'pending'
           OR item.human_approval_requirement_reason IS NULL
           OR item.human_approval_requirement_reason <> N'migration_cutover'
           OR item.agent_reviewed_revision IS NOT NULL
           OR item.reviewed_by_agent_id IS NOT NULL
           OR item.approved_revision IS NOT NULL
           OR item.approval_mode IS NOT NULL
           OR item.approved_by IS NOT NULL
           OR item.approved_by_agent_id IS NOT NULL
           OR item.approved_at IS NOT NULL)
        THROW 52016, 'content_cutover_unpublished_assertion_failed', 1;

    IF EXISTS (
        SELECT 1
        FROM @Unpublished AS unpublished
        OUTER APPLY (
            SELECT COUNT_BIG(*) AS task_count,
                   SUM(CASE WHEN task.status = N'pending'
                              AND task.lease_token IS NULL
                              AND task.lease_expires_at IS NULL
                              AND task.completed_at IS NULL
                            THEN 1 ELSE 0 END) AS ready_count
            FROM dbo.content_review_tasks AS task
            WHERE task.tenant_id = unpublished.tenant_id
              AND task.content_item_id = unpublished.content_item_id
              AND task.content_revision = unpublished.content_revision) AS taskState
        WHERE taskState.task_count <> 1 OR taskState.ready_count <> 1)
        THROW 52017, 'content_cutover_review_task_assertion_failed', 1;

    IF EXISTS (
        SELECT 1
        FROM @ActiveSchedules AS activeSchedule
        INNER JOIN dbo.content_schedule AS schedule
            ON schedule.tenant_id = activeSchedule.tenant_id
           AND schedule.id = activeSchedule.schedule_id
        WHERE schedule.status IS NULL
           OR schedule.status <> N'held'
           OR schedule.content_revision IS NULL
           OR schedule.content_revision <> activeSchedule.content_revision
           OR schedule.active_revision_slot IS NULL
           OR schedule.active_revision_slot <> activeSchedule.content_revision
           OR schedule.approval_mode IS NOT NULL
           OR schedule.publishing_policy_version_applied IS NOT NULL
           OR schedule.last_error_code IS NULL
           OR schedule.last_error_code <> N'migration_cutover')
        THROW 52018, 'content_cutover_schedule_assertion_failed', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.tenants AS tenant
        WHERE NOT EXISTS (
            SELECT 1
            FROM dbo.audit_logs AS audit
            WHERE audit.tenant_id = tenant.id
              AND audit.user_id IS NULL
              AND audit.action = N'content.migration_cutover.boundary'
              AND audit.resource_type = N'content_cutover'
              AND audit.resource_id IS NULL
              AND audit.event_key = N'content-cutover:v1:boundary'
              AND audit.state_sequence = 1
              AND audit.occurred_at = @cutoverAt
              AND audit.diff_json = CONCAT(
                    N'{"classification":"boundary","version":1,"itemAuditCount":',
                    (SELECT COUNT_BIG(*) FROM @Published AS publishedCount WHERE publishedCount.tenant_id = tenant.id)
                        + (SELECT COUNT_BIG(*) FROM @Unpublished AS unpublishedCount WHERE unpublishedCount.tenant_id = tenant.id),
                    N',"scheduleAuditCount":',
                    (SELECT COUNT_BIG(*) FROM @ActiveSchedules AS scheduleCount WHERE scheduleCount.tenant_id = tenant.id),
                    N'}')))
        THROW 52019, 'content_cutover_boundary_audit_assertion_failed', 1;

    IF EXISTS (
        SELECT 1
        FROM @Published AS published
        WHERE NOT EXISTS (
            SELECT 1
            FROM dbo.audit_logs AS audit
            WHERE audit.tenant_id = published.tenant_id
              AND audit.user_id IS NULL
              AND audit.action = N'content.migration_cutover.legacy_exempt'
              AND audit.resource_type = N'content_item'
              AND audit.resource_id = published.content_item_id
              AND audit.diff_json = N'{"classification":"legacy_exempt","historyOnly":true}'
              AND audit.event_key = N'content-cutover:v1:item:' + LOWER(REPLACE(CONVERT(CHAR(36), published.content_item_id), N'-', N''))
              AND audit.state_sequence = 1
              AND audit.occurred_at = @cutoverAt))
        THROW 52019, 'content_cutover_item_audit_assertion_failed', 1;

    IF EXISTS (
        SELECT 1
        FROM @Unpublished AS unpublished
        WHERE NOT EXISTS (
            SELECT 1
            FROM dbo.audit_logs AS audit
            WHERE audit.tenant_id = unpublished.tenant_id
              AND audit.user_id IS NULL
              AND audit.action = N'content.migration_cutover.review_required'
              AND audit.resource_type = N'content_item'
              AND audit.resource_id = unpublished.content_item_id
              AND audit.diff_json = CONCAT(
                    N'{"classification":"review_required","humanApprovalRequirementReason":"migration_cutover","contentRevision":',
                    unpublished.content_revision,
                    N'}')
              AND audit.event_key = N'content-cutover:v1:item:' + LOWER(REPLACE(CONVERT(CHAR(36), unpublished.content_item_id), N'-', N''))
              AND audit.state_sequence = 1
              AND audit.occurred_at = @cutoverAt))
        THROW 52019, 'content_cutover_item_audit_assertion_failed', 1;

    IF (SELECT COUNT_BIG(*) FROM dbo.audit_logs
        WHERE action IN (
            N'content.migration_cutover.legacy_exempt',
            N'content.migration_cutover.review_required')
          AND occurred_at = @cutoverAt) <> @publishedCount + @unpublishedCount
        THROW 52019, 'content_cutover_item_audit_assertion_failed', 1;

    IF EXISTS (
        SELECT 1
        FROM @ActiveSchedules AS activeSchedule
        WHERE NOT EXISTS (
            SELECT 1
            FROM dbo.audit_logs AS audit
            WHERE audit.tenant_id = activeSchedule.tenant_id
              AND audit.user_id IS NULL
              AND audit.action = N'content.migration_cutover.schedule_held'
              AND audit.resource_type = N'content_schedule'
              AND audit.resource_id = activeSchedule.schedule_id
              AND audit.diff_json = CONCAT(
                    N'{"classification":"held","reason":"migration_cutover","contentRevision":',
                    activeSchedule.content_revision,
                    N'}')
              AND audit.event_key = N'content-cutover:v1:schedule:' + LOWER(REPLACE(CONVERT(CHAR(36), activeSchedule.schedule_id), N'-', N''))
              AND audit.state_sequence = 1
              AND audit.occurred_at = @cutoverAt))
        THROW 52020, 'content_cutover_schedule_audit_assertion_failed', 1;

    IF (SELECT COUNT_BIG(*) FROM dbo.audit_logs
        WHERE action = N'content.migration_cutover.schedule_held'
          AND occurred_at = @cutoverAt) <> @schedulesHeld
        THROW 52020, 'content_cutover_schedule_audit_assertion_failed', 1;

    IF (SELECT COUNT_BIG(*) FROM dbo.audit_logs
        WHERE action = N'content.migration_cutover.boundary'
          AND occurred_at = @cutoverAt) <> (SELECT COUNT_BIG(*) FROM dbo.tenants)
        THROW 52019, 'content_cutover_boundary_audit_assertion_failed', 1;

    INSERT INTO dbo.schema_migrations (filename, applied_at)
    VALUES
        (@completionMarker, @cutoverAt),
        (@marker, @cutoverAt);

    COMMIT TRANSACTION;
    SET LOCK_TIMEOUT -1;
    SET XACT_ABORT OFF;

    SELECT
        @marker AS marker,
        @cutoverAt AS cutover_at,
        CAST(0 AS BIT) AS already_applied,
        @publishedCount AS published_legacy_exempt,
        @unpublishedCount AS unpublished_review_required,
        @tasksInserted AS review_tasks_inserted,
        @tasksReset AS review_tasks_reset,
        @tasksCanceled AS review_tasks_canceled,
        @schedulesHeld AS schedules_held,
        @auditEventsInserted AS audit_events_inserted;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    SET LOCK_TIMEOUT -1;
    SET XACT_ABORT OFF;
    THROW;
END CATCH;
