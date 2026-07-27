-- Verify exact content_render_tasks schema definitions. One SqlCommand, no GO.
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ARITHABORT ON;

IF OBJECT_ID(N'dbo.content_render_tasks', N'U') IS NULL
BEGIN
    SELECT N'0000000000000';
    RETURN;
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

DECLARE @expectedRevisionCheck NVARCHAR(MAX);
DECLARE @expectedStatusCheck NVARCHAR(MAX);
DECLARE @expectedPresetCheck NVARCHAR(MAX);
DECLARE @expectedPayloadCheck NVARCHAR(MAX);
DECLARE @expectedStateCheck NVARCHAR(MAX);
SELECT @expectedRevisionCheck = definition FROM tempdb.sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'tempdb..#content_render_task_expected_checks') AND definition LIKE N'%source_revision%' AND definition NOT LIKE N'%completed_revision%';
SELECT @expectedStatusCheck = definition FROM tempdb.sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'tempdb..#content_render_task_expected_checks') AND definition LIKE N'%canceled_stale%' AND definition NOT LIKE N'%lease_token%';
SELECT @expectedPresetCheck = definition FROM tempdb.sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'tempdb..#content_render_task_expected_checks') AND definition LIKE N'%1200x630%';
SELECT @expectedPayloadCheck = definition FROM tempdb.sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'tempdb..#content_render_task_expected_checks') AND definition LIKE N'%SHA2_256%';
SELECT @expectedStateCheck = definition FROM tempdb.sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'tempdb..#content_render_task_expected_checks') AND definition LIKE N'%lease_token%';

SELECT CONCAT(
    1,
    CASE WHEN EXISTS (
        SELECT 1 FROM sys.columns c INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
        WHERE c.object_id = OBJECT_ID(N'dbo.content_render_tasks') AND c.name = N'row_version'
          AND c.system_type_id = 189 AND t.system_type_id = 189 AND c.max_length = 8 AND c.is_nullable = 0) THEN 1 ELSE 0 END,
    CASE WHEN EXISTS (
        SELECT 1 FROM sys.foreign_keys fk
        WHERE fk.name = N'FK_content_render_tasks_content_items_tenant_item'
          AND fk.parent_object_id = OBJECT_ID(N'dbo.content_render_tasks')
          AND fk.referenced_object_id = OBJECT_ID(N'dbo.content_items')
          AND fk.delete_referential_action_desc = N'NO_ACTION'
          AND fk.update_referential_action_desc = N'NO_ACTION'
          AND fk.is_disabled = 0 AND fk.is_not_trusted = 0 AND fk.is_not_for_replication = 0
          AND (SELECT COUNT(*) FROM sys.foreign_key_columns fkc WHERE fkc.constraint_object_id = fk.object_id) = 2
          AND EXISTS (SELECT 1 FROM sys.foreign_key_columns fkc WHERE fkc.constraint_object_id = fk.object_id AND fkc.constraint_column_id = 1 AND COL_NAME(fkc.parent_object_id, fkc.parent_column_id) = N'tenant_id' AND COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) = N'tenant_id')
          AND EXISTS (SELECT 1 FROM sys.foreign_key_columns fkc WHERE fkc.constraint_object_id = fk.object_id AND fkc.constraint_column_id = 2 AND COL_NAME(fkc.parent_object_id, fkc.parent_column_id) = N'content_item_id' AND COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) = N'id')) THEN 1 ELSE 0 END,
    CASE WHEN EXISTS (
        SELECT 1 FROM sys.indexes i WHERE i.name = N'UX_content_render_tasks_item_revision' AND i.object_id = OBJECT_ID(N'dbo.content_render_tasks')
          AND i.is_unique = 1 AND i.type_desc = N'NONCLUSTERED' AND i.is_disabled = 0 AND i.has_filter = 0
          AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 3
          AND NOT EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
          AND EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND COL_NAME(ic.object_id, ic.column_id) = N'tenant_id')
          AND EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 2 AND COL_NAME(ic.object_id, ic.column_id) = N'content_item_id')
          AND EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 3 AND COL_NAME(ic.object_id, ic.column_id) = N'source_revision')) THEN 1 ELSE 0 END,
    CASE WHEN EXISTS (
        SELECT 1 FROM sys.indexes i WHERE i.name = N'IX_content_render_tasks_due' AND i.object_id = OBJECT_ID(N'dbo.content_render_tasks')
          AND i.is_unique = 0 AND i.type_desc = N'NONCLUSTERED' AND i.is_disabled = 0 AND i.has_filter = 0
          AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 4
          AND EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND COL_NAME(ic.object_id, ic.column_id) = N'tenant_id')
          AND EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 2 AND COL_NAME(ic.object_id, ic.column_id) = N'status')
          AND EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 3 AND COL_NAME(ic.object_id, ic.column_id) = N'next_attempt_at')
          AND EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 4 AND COL_NAME(ic.object_id, ic.column_id) = N'created_at')) THEN 1 ELSE 0 END,
    CASE WHEN EXISTS (
        SELECT 1 FROM sys.indexes i WHERE i.name = N'IX_content_render_tasks_expired_lease' AND i.object_id = OBJECT_ID(N'dbo.content_render_tasks')
          AND i.is_unique = 0 AND i.type_desc = N'NONCLUSTERED' AND i.is_disabled = 0 AND i.has_filter = 0
          AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 3
          AND EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND COL_NAME(ic.object_id, ic.column_id) = N'tenant_id')
          AND EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 2 AND COL_NAME(ic.object_id, ic.column_id) = N'status')
          AND EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 3 AND COL_NAME(ic.object_id, ic.column_id) = N'lease_expires_at')) THEN 1 ELSE 0 END,
    CASE WHEN EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.content_render_tasks') AND name = N'CK_content_render_tasks_revision' AND is_disabled = 0 AND is_not_trusted = 0 AND definition = @expectedRevisionCheck)
           AND EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.content_render_tasks') AND name = N'CK_content_render_tasks_status' AND is_disabled = 0 AND is_not_trusted = 0 AND definition = @expectedStatusCheck)
           AND EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.content_render_tasks') AND name = N'CK_content_render_tasks_preset' AND is_disabled = 0 AND is_not_trusted = 0 AND definition = @expectedPresetCheck)
           AND EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.content_render_tasks') AND name = N'CK_content_render_tasks_payload' AND is_disabled = 0 AND is_not_trusted = 0 AND definition = @expectedPayloadCheck)
           AND EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.content_render_tasks') AND name = N'CK_content_render_tasks_state' AND is_disabled = 0 AND is_not_trusted = 0 AND definition = @expectedStateCheck) THEN 1 ELSE 0 END,
    CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.content_render_tasks') AND name = N'canonical_slots_json' AND system_type_id = 231 AND max_length = -1 AND is_nullable = 0 AND default_object_id = 0) THEN 1 ELSE 0 END,
    CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.content_render_tasks') AND name = N'slots_hash' AND system_type_id = 231 AND max_length = 128 AND is_nullable = 0 AND default_object_id = 0) THEN 1 ELSE 0 END,
    CASE WHEN EXISTS (
        SELECT 1 FROM sys.indexes i WHERE i.name = N'UX_content_items_tenant_id_id' AND i.object_id = OBJECT_ID(N'dbo.content_items')
          AND i.is_unique = 1 AND i.type_desc = N'NONCLUSTERED' AND i.is_disabled = 0 AND i.has_filter = 0
          AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 2
          AND EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND COL_NAME(ic.object_id, ic.column_id) = N'tenant_id')
          AND EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 2 AND COL_NAME(ic.object_id, ic.column_id) = N'id'))
          AND NOT EXISTS (
              SELECT 1 FROM sys.default_constraints dc INNER JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
              WHERE dc.parent_object_id = OBJECT_ID(N'dbo.content_render_tasks')
                AND c.name IN (N'source_revision', N'template_id', N'template_version', N'template_hash', N'preset', N'canonical_slots_json', N'slots_hash')) THEN 1 ELSE 0 END,
    CASE WHEN NOT EXISTS (
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
          )) THEN 1 ELSE 0 END,
    CASE WHEN NOT EXISTS (
        SELECT 1 FROM dbo.content_render_tasks
        WHERE id IS NULL OR tenant_id IS NULL OR content_item_id IS NULL OR source_revision IS NULL
           OR template_id IS NULL OR template_version IS NULL OR template_hash IS NULL OR preset IS NULL
           OR canonical_slots_json IS NULL OR slots_hash IS NULL OR status IS NULL OR attempt_count IS NULL
           OR next_attempt_at IS NULL OR created_at IS NULL) THEN 1 ELSE 0 END,
    CASE WHEN EXISTS (
        SELECT 1
        FROM sys.key_constraints kc
        INNER JOIN sys.indexes i ON i.object_id = kc.parent_object_id AND i.index_id = kc.unique_index_id
        WHERE kc.parent_object_id = OBJECT_ID(N'dbo.content_render_tasks')
          AND kc.type = N'PK'
          AND kc.name = N'PK_content_render_tasks'
          AND i.name = N'PK_content_render_tasks'
          AND i.is_primary_key = 1 AND i.is_unique = 1 AND i.type_desc = N'CLUSTERED'
          AND i.is_disabled = 0 AND i.is_hypothetical = 0 AND i.has_filter = 0
          AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 1
          AND NOT EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1)
          AND EXISTS (SELECT 1 FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND ic.is_descending_key = 0 AND COL_NAME(ic.object_id, ic.column_id) = N'id')) THEN 1 ELSE 0 END
);

DROP TABLE #content_render_task_expected_columns;
DROP TABLE #content_render_task_expected_checks;
