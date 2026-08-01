-- 0094: consolidate obsolete role/channel tables into their canonical stores.
-- Historical migrations remain unchanged; every supported runner must supply the transaction wrapper.

-- Required for DML against tables with filtered indexes under every sqlcmd/ADO.NET session default.
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;

IF @@TRANCOUNT = 0
    THROW 51094, 'database_consolidation_transaction_required', 1;

DECLARE @migration_lock_result INT;
EXEC @migration_lock_result = sys.sp_getapplock
    @Resource = N'clawbot:database-table-consolidation:0094',
    @LockMode = N'Exclusive',
    @LockOwner = N'Transaction',
    @LockTimeout = 60000,
    @DbPrincipal = N'public';
IF @migration_lock_result < 0
    THROW 51094, 'database_consolidation_migration_lock_failed', 1;

-- Capture the exact legacy objects this execution is authorized to process. The final drop phase
-- rejects a table that appears or is replaced after this point instead of dropping unprocessed data.
DECLARE @legacy_user_roles_object_id INT = OBJECT_ID(N'dbo.user_roles', N'U');
DECLARE @legacy_channel_tokens_object_id INT = OBJECT_ID(N'dbo.channel_tokens', N'U');
DECLARE @legacy_read_state_object_id INT = OBJECT_ID(N'dbo.conversation_read_state', N'U');
DECLARE @legacy_pancake_pages_object_id INT = OBJECT_ID(N'dbo.pancake_pages', N'U');

IF OBJECT_ID(N'dbo.inboxes', N'U') IS NULL
    THROW 51094, 'database_consolidation_inboxes_missing', 1;

IF OBJECT_ID(N'dbo.AspNetRoles', N'U') IS NULL
   OR OBJECT_ID(N'dbo.AspNetUserRoles', N'U') IS NULL
    THROW 51094, 'database_consolidation_identity_role_tables_missing', 1;

-- Migration 0030 and the existing-schema repair path must establish this canonical column first.
-- Fail closed instead of compiling later statements against a column added in the same SQL Server batch.
IF COL_LENGTH(N'dbo.inboxes', N'encrypted_access_token') IS NULL
    THROW 51094, 'database_consolidation_access_token_column_missing', 1;

-- Existing databases can have drifted credential definitions. Normalize the full contract before
-- copying or dropping any legacy source so a truncating or non-nullable target cannot lose data.
EXEC sys.sp_executesql N'
    ALTER TABLE dbo.inboxes
    ALTER COLUMN encrypted_access_token NVARCHAR(MAX) NULL;';

IF COL_LENGTH(N'dbo.inboxes', N'encrypted_refresh_token') IS NULL
    EXEC sys.sp_executesql N'
        ALTER TABLE dbo.inboxes
        ADD encrypted_refresh_token NVARCHAR(MAX) NULL;';
ELSE
    EXEC sys.sp_executesql N'
        ALTER TABLE dbo.inboxes
        ALTER COLUMN encrypted_refresh_token NVARCHAR(MAX) NULL;';

IF COL_LENGTH(N'dbo.inboxes', N'encrypted_webhook_secret') IS NULL
    EXEC sys.sp_executesql N'
        ALTER TABLE dbo.inboxes
        ADD encrypted_webhook_secret NVARCHAR(MAX) NULL;';
ELSE
    EXEC sys.sp_executesql N'
        ALTER TABLE dbo.inboxes
        ALTER COLUMN encrypted_webhook_secret NVARCHAR(MAX) NULL;';

IF COL_LENGTH(N'dbo.inboxes', N'token_expires_at') IS NULL
    EXEC sys.sp_executesql N'
        ALTER TABLE dbo.inboxes
        ADD token_expires_at DATETIMEOFFSET NULL;';
ELSE
    EXEC sys.sp_executesql N'
        ALTER TABLE dbo.inboxes
        ALTER COLUMN token_expires_at DATETIMEOFFSET NULL;';

-- The legacy page map recorded when a page token was minted. Without a canonical column that value
-- is lost and callers fall back to updated_at, which any rename or lifecycle change also moves.
IF COL_LENGTH(N'dbo.inboxes', N'page_token_minted_at') IS NULL
    EXEC sys.sp_executesql N'
        ALTER TABLE dbo.inboxes
        ADD page_token_minted_at DATETIMEOFFSET NULL;';
ELSE
    EXEC sys.sp_executesql N'
        ALTER TABLE dbo.inboxes
        ALTER COLUMN page_token_minted_at DATETIMEOFFSET NULL;';

IF EXISTS (
    SELECT 1
    FROM sys.columns column_definition
    INNER JOIN sys.types column_type
        ON column_type.user_type_id = column_definition.user_type_id
    WHERE column_definition.object_id = OBJECT_ID(N'dbo.inboxes')
      AND column_definition.name IN (
          N'encrypted_access_token',
          N'encrypted_refresh_token',
          N'encrypted_webhook_secret')
    GROUP BY column_definition.object_id
    HAVING COUNT(*) <> 3
       OR SUM(CASE
           WHEN column_type.name = N'nvarchar'
            AND column_definition.max_length = -1
            AND column_definition.is_nullable = 1
               THEN 1
           ELSE 0
       END) <> 3
)
    THROW 51094, 'database_consolidation_credential_column_contract_invalid', 1;

IF NOT EXISTS (
    SELECT 1
    FROM sys.columns column_definition
    INNER JOIN sys.types column_type
        ON column_type.user_type_id = column_definition.user_type_id
    WHERE column_definition.object_id = OBJECT_ID(N'dbo.inboxes')
      AND column_definition.name IN (N'token_expires_at', N'page_token_minted_at')
      AND column_type.name = N'datetimeoffset'
      AND column_definition.is_nullable = 1
    GROUP BY column_definition.object_id
    HAVING COUNT(*) = 2
)
    THROW 51094, 'database_consolidation_token_expiry_column_contract_invalid', 1;

-- Hold the canonical target stable through copy/drop/commit. This also closes the race where an
-- old process inserts a duplicate after the validation below but before the unique-index repair.
DECLARE @canonical_inbox_count BIGINT;
SELECT @canonical_inbox_count = COUNT_BIG(*)
FROM dbo.inboxes WITH (TABLOCKX, HOLDLOCK);

IF EXISTS (
    SELECT 1
    FROM dbo.inboxes
    WHERE platform IS NULL
       OR LEN(LTRIM(RTRIM(platform))) = 0
       OR LEN(LTRIM(RTRIM(platform))) > 32
       OR UNICODE(LEFT(LTRIM(RTRIM(platform)), 1)) IN (
            9, 10, 11, 12, 13, 133, 160, 5760,
            8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
            8232, 8233, 8239, 8287, 12288)
       OR UNICODE(RIGHT(LTRIM(RTRIM(platform)), 1)) IN (
            9, 10, 11, 12, 13, 133, 160, 5760,
            8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
            8232, 8233, 8239, 8287, 12288)
)
    THROW 51094, 'database_consolidation_canonical_platform_invalid', 1;

IF EXISTS (
    SELECT 1
    FROM dbo.inboxes
    WHERE external_page_id IS NULL
       OR LEN(LTRIM(RTRIM(external_page_id))) = 0
       OR LEN(LTRIM(RTRIM(external_page_id))) > 128
       OR UNICODE(LEFT(LTRIM(RTRIM(external_page_id)), 1)) IN (
            9, 10, 11, 12, 13, 133, 160, 5760,
            8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
            8232, 8233, 8239, 8287, 12288)
       OR UNICODE(RIGHT(LTRIM(RTRIM(external_page_id)), 1)) IN (
            9, 10, 11, 12, 13, 133, 160, 5760,
            8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
            8232, 8233, 8239, 8287, 12288)
)
    THROW 51094, 'database_consolidation_canonical_page_id_invalid', 1;

-- Compare the normalized identity before updating any row so normalization cannot collide with an
-- existing active target even when the expected filtered unique index is absent or disabled.
IF EXISTS (
    SELECT 1
    FROM dbo.inboxes
    WHERE is_active = 1
      AND deleted_at IS NULL
    GROUP BY
        tenant_id,
        LOWER(LTRIM(RTRIM(platform))),
        LTRIM(RTRIM(external_page_id))
    HAVING COUNT(*) > 1
)
    THROW 51094, 'database_consolidation_duplicate_active_inbox_identity', 1;

UPDATE dbo.inboxes
SET
    platform = LOWER(LTRIM(RTRIM(platform))),
    external_page_id = LTRIM(RTRIM(external_page_id))
WHERE DATALENGTH(platform) <> DATALENGTH(LOWER(LTRIM(RTRIM(platform))))
   OR CONVERT(VARBINARY(MAX), platform)
        <> CONVERT(VARBINARY(MAX), LOWER(LTRIM(RTRIM(platform))))
   OR DATALENGTH(external_page_id) <> DATALENGTH(LTRIM(RTRIM(external_page_id)))
   OR CONVERT(VARBINARY(MAX), external_page_id)
        <> CONVERT(VARBINARY(MAX), LTRIM(RTRIM(external_page_id)));

DECLARE @legacy_user_role_count INT = 0;
DECLARE @legacy_pancake_page_count INT = 0;
DECLARE @legacy_channel_token_count INT = 0;
DECLARE @legacy_read_state_count INT = 0;
DECLARE @legacy_role_map TABLE (
    user_id UNIQUEIDENTIFIER NOT NULL,
    role_id UNIQUEIDENTIFIER NOT NULL,
    PRIMARY KEY (user_id, role_id)
);

IF @legacy_user_roles_object_id IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.AspNetUserRoles', N'U') IS NULL
       OR OBJECT_ID(N'dbo.AspNetRoles', N'U') IS NULL
       OR OBJECT_ID(N'dbo.users', N'U') IS NULL
       OR OBJECT_ID(N'dbo.roles', N'U') IS NULL
    BEGIN
        THROW 51094, 'database_consolidation_identity_schema_missing', 1;
    END;

    -- Lock the legacy source for the transaction so an old application instance cannot
    -- add an assignment after reconciliation and before the table is dropped.
    SELECT @legacy_user_role_count = COUNT(*)
    FROM dbo.user_roles WITH (TABLOCKX, HOLDLOCK);

    -- Keep both canonical role definitions and assignments stable through validation/drop/commit.
    DECLARE @identity_role_count INT;
    DECLARE @identity_assignment_count INT;
    SELECT @identity_role_count = COUNT(*)
    FROM dbo.AspNetRoles WITH (TABLOCKX, HOLDLOCK);
    SELECT @identity_assignment_count = COUNT(*)
    FROM dbo.AspNetUserRoles WITH (TABLOCKX, HOLDLOCK);

    -- Tenant and legacy-role metadata participate in the authorization decision. Keep them stable
    -- through the final source drop so a concurrent rename/reassignment cannot preserve stale access.
    DECLARE @user_metadata_count BIGINT;
    DECLARE @role_metadata_count BIGINT;
    SELECT @user_metadata_count = COUNT_BIG(*)
    FROM dbo.users WITH (TABLOCKX, HOLDLOCK);
    SELECT @role_metadata_count = COUNT_BIG(*)
    FROM dbo.roles WITH (TABLOCKX, HOLDLOCK);

    DECLARE @supported_identity_roles TABLE (
        name NVARCHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL PRIMARY KEY,
        normalized_name NVARCHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL UNIQUE
    );
    INSERT INTO @supported_identity_roles (name, normalized_name)
    VALUES
        (N'Admin', N'ADMIN'),
        (N'SalesLead', N'SALESLEAD'),
        (N'Sale', N'SALE'),
        (N'Marketer', N'MARKETER'),
        (N'QA', N'QA'),
        (N'Viewer', N'VIEWER');

    -- A legacy assignment may map only to one fixed system Identity role in the same tenant.
    IF EXISTS (
        SELECT 1
        FROM dbo.user_roles ur
        LEFT JOIN dbo.users u ON u.id = ur.user_id
        LEFT JOIN dbo.roles r ON r.id = ur.role_id AND r.tenant_id = u.tenant_id
        LEFT JOIN @supported_identity_roles supported_role
            ON DATALENGTH(supported_role.name) = DATALENGTH(r.name)
           AND CONVERT(VARBINARY(MAX), supported_role.name)
               = CONVERT(VARBINARY(MAX), r.name)
        LEFT JOIN (
            SELECT
                id,
                name,
                normalized_name,
                COUNT(*) OVER (PARTITION BY normalized_name) AS match_count
            FROM dbo.AspNetRoles
        ) identity_role
            ON DATALENGTH(identity_role.normalized_name)
               = DATALENGTH(supported_role.normalized_name)
           AND CONVERT(VARBINARY(MAX), identity_role.normalized_name)
               = CONVERT(VARBINARY(MAX), supported_role.normalized_name)
        WHERE u.id IS NULL
           OR r.id IS NULL
           OR r.is_system <> 1
           OR supported_role.name IS NULL
           OR identity_role.id IS NULL
           OR identity_role.match_count <> 1
           OR DATALENGTH(identity_role.name) <> DATALENGTH(supported_role.name)
           OR CONVERT(VARBINARY(MAX), identity_role.name)
              <> CONVERT(VARBINARY(MAX), supported_role.name)
           OR DATALENGTH(identity_role.normalized_name)
              <> DATALENGTH(supported_role.normalized_name)
           OR CONVERT(VARBINARY(MAX), identity_role.normalized_name)
              <> CONVERT(VARBINARY(MAX), supported_role.normalized_name)
    )
    BEGIN
        THROW 51094, 'database_consolidation_unmappable_or_ambiguous_user_role', 1;
    END;

    INSERT INTO @legacy_role_map (user_id, role_id)
    SELECT ur.user_id, identity_role.id
    FROM dbo.user_roles ur
    INNER JOIN dbo.users u ON u.id = ur.user_id
    INNER JOIN dbo.roles r ON r.id = ur.role_id AND r.tenant_id = u.tenant_id
    INNER JOIN @supported_identity_roles supported_role
        ON DATALENGTH(supported_role.name) = DATALENGTH(r.name)
       AND CONVERT(VARBINARY(MAX), supported_role.name)
           = CONVERT(VARBINARY(MAX), r.name)
    INNER JOIN (
        SELECT
            id,
            name,
            normalized_name,
            COUNT(*) OVER (PARTITION BY normalized_name) AS match_count
        FROM dbo.AspNetRoles
    ) identity_role
        ON DATALENGTH(identity_role.normalized_name)
           = DATALENGTH(supported_role.normalized_name)
       AND CONVERT(VARBINARY(MAX), identity_role.normalized_name)
           = CONVERT(VARBINARY(MAX), supported_role.normalized_name)
       AND identity_role.match_count = 1
       AND DATALENGTH(identity_role.name) = DATALENGTH(supported_role.name)
       AND CONVERT(VARBINARY(MAX), identity_role.name)
           = CONVERT(VARBINARY(MAX), supported_role.name)
    WHERE r.is_system = 1;

    IF (SELECT COUNT(*) FROM @legacy_role_map) <> @legacy_user_role_count
        THROW 51094, 'database_consolidation_user_role_map_incomplete', 1;

    -- Identity assignments are authoritative. Exact equality is required even when a user has
    -- zero canonical roles, because an empty set may represent an intentional full revocation.
    IF EXISTS (
        SELECT 1
        FROM (SELECT DISTINCT user_id FROM @legacy_role_map) legacy_user
        WHERE EXISTS (
            SELECT 1
            FROM @legacy_role_map mapped_role
            WHERE mapped_role.user_id = legacy_user.user_id
              AND NOT EXISTS (
                  SELECT 1
                  FROM dbo.AspNetUserRoles current_role
                  WHERE current_role.user_id = mapped_role.user_id
                    AND current_role.role_id = mapped_role.role_id
              )
        )
        OR EXISTS (
            SELECT 1
            FROM dbo.AspNetUserRoles current_role
            WHERE current_role.user_id = legacy_user.user_id
              AND NOT EXISTS (
                  SELECT 1
                  FROM @legacy_role_map mapped_role
                  WHERE mapped_role.user_id = current_role.user_id
                    AND mapped_role.role_id = current_role.role_id
              )
        )
    )
    BEGIN
        THROW 51094, 'database_consolidation_conflicting_user_role_assignments', 1;
    END;
END;

-- The inbox-linked legacy token wins over the page-map token when both source tables have a value.
IF @legacy_channel_tokens_object_id IS NOT NULL
BEGIN
    -- Freeze token rotation in the legacy store until consolidation commits.
    SELECT @legacy_channel_token_count = COUNT(*)
    FROM dbo.channel_tokens WITH (TABLOCKX, HOLDLOCK);

    IF EXISTS (
        SELECT 1
        FROM dbo.channel_tokens
        GROUP BY inbox_id
        HAVING COUNT(*) > 1
    )
        THROW 51094, 'database_consolidation_duplicate_channel_token_source', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.channel_tokens token
        LEFT JOIN dbo.inboxes inbox ON inbox.id = token.inbox_id
        WHERE inbox.id IS NULL
    )
    BEGIN
        THROW 51094, 'database_consolidation_orphan_channel_token', 1;
    END;

    EXEC sys.sp_executesql N'
        IF EXISTS (
            SELECT 1
            FROM dbo.channel_tokens token
            INNER JOIN dbo.inboxes inbox ON inbox.id = token.inbox_id
            WHERE (DATALENGTH(inbox.encrypted_access_token) > 0
                   AND CONVERT(VARBINARY(MAX), inbox.encrypted_access_token)
                       <> CONVERT(VARBINARY(MAX), token.access_token_encrypted))
               OR (DATALENGTH(inbox.encrypted_refresh_token) > 0
                   AND DATALENGTH(token.refresh_token_encrypted) > 0
                   AND CONVERT(VARBINARY(MAX), inbox.encrypted_refresh_token)
                       <> CONVERT(VARBINARY(MAX), token.refresh_token_encrypted))
               OR (DATALENGTH(inbox.encrypted_webhook_secret) > 0
                   AND DATALENGTH(token.webhook_secret_encrypted) > 0
                   AND CONVERT(VARBINARY(MAX), inbox.encrypted_webhook_secret)
                       <> CONVERT(VARBINARY(MAX), token.webhook_secret_encrypted))
        )
            THROW 51094, ''database_consolidation_conflicting_channel_credentials'', 1;

        UPDATE inbox
        SET
            encrypted_access_token = CASE
                WHEN inbox.encrypted_access_token IS NULL OR DATALENGTH(inbox.encrypted_access_token) = 0
                    THEN token.access_token_encrypted
                ELSE inbox.encrypted_access_token
            END,
            encrypted_refresh_token = CASE
                WHEN inbox.encrypted_refresh_token IS NULL OR DATALENGTH(inbox.encrypted_refresh_token) = 0
                    THEN token.refresh_token_encrypted
                ELSE inbox.encrypted_refresh_token
            END,
            encrypted_webhook_secret = CASE
                WHEN inbox.encrypted_webhook_secret IS NULL OR DATALENGTH(inbox.encrypted_webhook_secret) = 0
                    THEN token.webhook_secret_encrypted
                ELSE inbox.encrypted_webhook_secret
            END,
            token_expires_at = CASE
                WHEN inbox.encrypted_access_token IS NULL
                  OR DATALENGTH(inbox.encrypted_access_token) = 0
                    THEN token.token_expires_at
                ELSE COALESCE(inbox.token_expires_at, token.token_expires_at)
            END,
            is_active = CASE
                WHEN (inbox.encrypted_access_token IS NULL OR DATALENGTH(inbox.encrypted_access_token) = 0)
                     AND token.is_active = 0
                    THEN 0
                ELSE inbox.is_active
            END,
            updated_at = CASE
                WHEN token.updated_at > inbox.updated_at THEN token.updated_at
                ELSE inbox.updated_at
            END
        FROM dbo.inboxes inbox
        INNER JOIN dbo.channel_tokens token ON token.inbox_id = inbox.id;

        IF EXISTS (
            SELECT 1
            FROM dbo.channel_tokens token
            INNER JOIN dbo.inboxes inbox ON inbox.id = token.inbox_id
            WHERE inbox.encrypted_access_token IS NULL
               OR DATALENGTH(inbox.encrypted_access_token) = 0
        )
        BEGIN
            THROW 51094, ''database_consolidation_channel_token_copy_incomplete'', 1;
        END;';
END;

IF @legacy_pancake_pages_object_id IS NOT NULL
BEGIN
    -- Freeze legacy page writes until credentials are copied and the source table is dropped.
    SELECT @legacy_pancake_page_count = COUNT(*)
    FROM dbo.pancake_pages WITH (TABLOCKX, HOLDLOCK);

    IF EXISTS (
        SELECT 1
        FROM dbo.pancake_pages page
        WHERE page.platform IS NULL
           OR LEN(LTRIM(RTRIM(page.platform))) = 0
           OR LEN(LTRIM(RTRIM(page.platform))) > 32
           OR UNICODE(LEFT(LTRIM(RTRIM(page.platform)), 1)) IN (
                9, 10, 11, 12, 13, 133, 160, 5760,
                8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                8232, 8233, 8239, 8287, 12288)
           OR UNICODE(RIGHT(LTRIM(RTRIM(page.platform)), 1)) IN (
                9, 10, 11, 12, 13, 133, 160, 5760,
                8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                8232, 8233, 8239, 8287, 12288)
    )
        THROW 51094, 'database_consolidation_pancake_platform_invalid', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.pancake_pages page
        WHERE page.page_id IS NULL
           OR LEN(LTRIM(RTRIM(page.page_id))) = 0
           OR LEN(LTRIM(RTRIM(page.page_id))) > 128
           OR UNICODE(LEFT(LTRIM(RTRIM(page.page_id)), 1)) IN (
                9, 10, 11, 12, 13, 133, 160, 5760,
                8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                8232, 8233, 8239, 8287, 12288)
           OR UNICODE(RIGHT(LTRIM(RTRIM(page.page_id)), 1)) IN (
                9, 10, 11, 12, 13, 133, 160, 5760,
                8192, 8193, 8194, 8195, 8196, 8197, 8198, 8199, 8200, 8201, 8202,
                8232, 8233, 8239, 8287, 12288)
    )
        THROW 51094, 'database_consolidation_pancake_page_id_invalid', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.pancake_pages page
        WHERE page.page_access_token_encrypted IS NULL
           OR DATALENGTH(page.page_access_token_encrypted) = 0
    )
        THROW 51094, 'database_consolidation_pancake_credential_missing', 1;

    -- Multiple legacy rows for one normalized canonical identity cannot be collapsed without
    -- choosing which credential and lifecycle state survives. Require operator reconciliation.
    IF EXISTS (
        SELECT 1
        FROM dbo.pancake_pages page
        GROUP BY
            page.tenant_id,
            LOWER(LTRIM(RTRIM(page.platform))),
            LTRIM(RTRIM(page.page_id))
        HAVING COUNT(*) > 1
    )
        THROW 51094, 'database_consolidation_duplicate_pancake_source', 1;

    -- An active source must have at most one active canonical target for its full provider identity.
    IF EXISTS (
        SELECT page.id
        FROM dbo.pancake_pages page
        INNER JOIN dbo.inboxes inbox
            ON inbox.tenant_id = page.tenant_id
           AND inbox.platform = LEFT(
               LOWER(LTRIM(RTRIM(page.platform))),
               32)
           AND inbox.external_page_id = LTRIM(RTRIM(page.page_id))
           AND inbox.is_active = 1
           AND inbox.deleted_at IS NULL
        WHERE page.is_active = 1
          AND page.deleted_at IS NULL
        GROUP BY page.id
        HAVING COUNT(*) > 1
    )
    BEGIN
        THROW 51094, 'database_consolidation_ambiguous_active_pancake_inbox', 1;
    END;

    -- Never discard an alternate legacy credential merely because a canonical row is non-empty.
    -- Ciphertexts can differ even for the same plaintext, so an operator must reconcile this conflict.
    IF EXISTS (
        SELECT 1
        FROM dbo.pancake_pages page
        WHERE EXISTS (
            SELECT 1
            FROM dbo.inboxes inbox
            WHERE inbox.tenant_id = page.tenant_id
              AND inbox.platform = LEFT(
                  LOWER(LTRIM(RTRIM(page.platform))),
                  32)
              AND inbox.external_page_id = LTRIM(RTRIM(page.page_id))
              AND DATALENGTH(inbox.encrypted_access_token) > 0
              AND CONVERT(VARBINARY(MAX), inbox.encrypted_access_token)
                  <> CONVERT(VARBINARY(MAX), page.page_access_token_encrypted)
        )
          AND NOT EXISTS (
              SELECT 1
              FROM dbo.inboxes inbox
              WHERE inbox.tenant_id = page.tenant_id
                AND inbox.platform = LEFT(
                    LOWER(LTRIM(RTRIM(page.platform))),
                    32)
                AND inbox.external_page_id = LTRIM(RTRIM(page.page_id))
                AND CONVERT(VARBINARY(MAX), inbox.encrypted_access_token)
                = CONVERT(VARBINARY(MAX), page.page_access_token_encrypted)
          )
    )
        THROW 51094, 'database_consolidation_conflicting_pancake_credentials', 1;

    -- A stored ciphertext that exactly matches a Pancake page token represents a non-expiring
    -- credential. Dynamic SQL is required because token_expires_at may have been added above in
    -- this same migration batch.
    EXEC sys.sp_executesql N'
        UPDATE inbox
        SET token_expires_at = NULL
        FROM dbo.inboxes inbox
        INNER JOIN dbo.pancake_pages page
            ON inbox.tenant_id = page.tenant_id
           AND inbox.platform = LEFT(
               LOWER(LTRIM(RTRIM(page.platform))),
               32)
           AND inbox.external_page_id = LTRIM(RTRIM(page.page_id))
           AND CONVERT(VARBINARY(MAX), inbox.encrypted_access_token)
               = CONVERT(VARBINARY(MAX), page.page_access_token_encrypted)
        WHERE inbox.token_expires_at IS NOT NULL;

        UPDATE inbox
        SET
            encrypted_access_token = CASE
                WHEN inbox.encrypted_access_token IS NULL OR DATALENGTH(inbox.encrypted_access_token) = 0
                    THEN page.page_access_token_encrypted
                ELSE inbox.encrypted_access_token
            END,
            token_expires_at = CASE
                WHEN inbox.encrypted_access_token IS NULL OR DATALENGTH(inbox.encrypted_access_token) = 0
                    THEN NULL
                ELSE inbox.token_expires_at
            END,
            updated_at = CASE
                WHEN page.updated_at > inbox.updated_at THEN page.updated_at
                ELSE inbox.updated_at
            END
        FROM dbo.inboxes inbox
        INNER JOIN dbo.pancake_pages page
            ON inbox.tenant_id = page.tenant_id
           AND inbox.platform = LEFT(
               LOWER(LTRIM(RTRIM(page.platform))),
               32)
           AND inbox.external_page_id = LTRIM(RTRIM(page.page_id))
        WHERE inbox.is_active = 1
          AND inbox.deleted_at IS NULL;';

    -- A disconnected canonical inbox is authoritative even when the legacy page is still active.
    -- Preserve its credential without creating or reactivating another canonical inbox.
    EXEC sys.sp_executesql N'
        ;WITH disconnected_matches AS (
            SELECT
                page.id AS page_row_id,
                inbox.id AS inbox_id,
                ROW_NUMBER() OVER (
                    PARTITION BY page.id
                    ORDER BY inbox.created_at, inbox.id
                ) AS match_order
            FROM dbo.pancake_pages page
            INNER JOIN dbo.inboxes inbox
                ON inbox.tenant_id = page.tenant_id
               AND inbox.platform = LEFT(
                   LOWER(LTRIM(RTRIM(page.platform))),
                   32)
               AND inbox.external_page_id = LTRIM(RTRIM(page.page_id))
            WHERE (inbox.is_active = 0 OR inbox.deleted_at IS NOT NULL)
              AND NOT EXISTS (
                  SELECT 1
                  FROM dbo.inboxes active_inbox
                  WHERE active_inbox.tenant_id = page.tenant_id
                    AND active_inbox.platform = LEFT(
                        LOWER(LTRIM(RTRIM(page.platform))),
                        32)
                    AND active_inbox.external_page_id = LTRIM(RTRIM(page.page_id))
                    AND active_inbox.is_active = 1
                    AND active_inbox.deleted_at IS NULL
              )
        )
        UPDATE inbox
        SET
            encrypted_access_token = CASE
                WHEN inbox.encrypted_access_token IS NULL OR DATALENGTH(inbox.encrypted_access_token) = 0
                    THEN page.page_access_token_encrypted
                ELSE inbox.encrypted_access_token
            END,
            token_expires_at = CASE
                WHEN inbox.encrypted_access_token IS NULL OR DATALENGTH(inbox.encrypted_access_token) = 0
                    THEN NULL
                ELSE inbox.token_expires_at
            END,
            updated_at = CASE
                WHEN page.updated_at > inbox.updated_at THEN page.updated_at
                ELSE inbox.updated_at
            END
        FROM dbo.inboxes inbox
        INNER JOIN disconnected_matches match
            ON match.inbox_id = inbox.id
           AND match.match_order = 1
        INNER JOIN dbo.pancake_pages page ON page.id = match.page_row_id;';

    INSERT INTO dbo.inboxes (
        id,
        tenant_id,
        name,
        platform,
        external_page_id,
        avatar_url,
        is_active,
        created_at,
        updated_at,
        deleted_at,
        encrypted_access_token)
    SELECT
        NEWID(),
        page.tenant_id,
        COALESCE(NULLIF(LTRIM(RTRIM(page.name)), N''), LTRIM(RTRIM(page.page_id))),
        LEFT(LOWER(LTRIM(RTRIM(page.platform))), 32),
        LTRIM(RTRIM(page.page_id)),
        NULL,
        page.is_active,
        page.created_at,
        page.updated_at,
        page.deleted_at,
        page.page_access_token_encrypted
    FROM dbo.pancake_pages page
    WHERE page.is_active = 1
      AND page.deleted_at IS NULL
      AND NOT EXISTS (
          SELECT 1
          FROM dbo.inboxes inbox
          WHERE inbox.tenant_id = page.tenant_id
            AND inbox.platform = LEFT(
                LOWER(LTRIM(RTRIM(page.platform))),
                32)
            AND inbox.external_page_id = LTRIM(RTRIM(page.page_id))
      );

    INSERT INTO dbo.inboxes (
        id,
        tenant_id,
        name,
        platform,
        external_page_id,
        avatar_url,
        is_active,
        created_at,
        updated_at,
        deleted_at,
        encrypted_access_token)
    SELECT
        NEWID(),
        page.tenant_id,
        COALESCE(NULLIF(LTRIM(RTRIM(page.name)), N''), LTRIM(RTRIM(page.page_id))),
        LEFT(LOWER(LTRIM(RTRIM(page.platform))), 32),
        LTRIM(RTRIM(page.page_id)),
        NULL,
        page.is_active,
        page.created_at,
        page.updated_at,
        page.deleted_at,
        page.page_access_token_encrypted
    FROM dbo.pancake_pages page
    WHERE (page.is_active = 0 OR page.deleted_at IS NOT NULL)
      AND NOT EXISTS (
          SELECT 1
          FROM dbo.inboxes inbox
          WHERE inbox.tenant_id = page.tenant_id
            AND inbox.platform = LEFT(
                LOWER(LTRIM(RTRIM(page.platform))),
                32)
            AND inbox.external_page_id = LTRIM(RTRIM(page.page_id))
      );

    IF EXISTS (
        SELECT 1
        FROM dbo.pancake_pages page
        WHERE page.is_active = 1
          AND page.deleted_at IS NULL
          AND NOT EXISTS (
              SELECT 1
              FROM dbo.inboxes inbox
              WHERE inbox.tenant_id = page.tenant_id
                AND inbox.platform = LEFT(
                    LOWER(LTRIM(RTRIM(page.platform))),
                    32)
                AND inbox.external_page_id = LTRIM(RTRIM(page.page_id))
                AND CONVERT(VARBINARY(MAX), inbox.encrypted_access_token)
                    = CONVERT(VARBINARY(MAX), page.page_access_token_encrypted)
          )
    )
    BEGIN
        THROW 51094, 'database_consolidation_active_pancake_copy_incomplete', 1;
    END;

    IF EXISTS (
        SELECT 1
        FROM dbo.pancake_pages page
        WHERE (page.is_active = 0 OR page.deleted_at IS NOT NULL)
          AND NOT EXISTS (
              SELECT 1
              FROM dbo.inboxes inbox
              WHERE inbox.tenant_id = page.tenant_id
                AND inbox.platform = LEFT(
                    LOWER(LTRIM(RTRIM(page.platform))),
                    32)
                AND inbox.external_page_id = LTRIM(RTRIM(page.page_id))
                AND CONVERT(VARBINARY(MAX), inbox.encrypted_access_token)
                    = CONVERT(VARBINARY(MAX), page.page_access_token_encrypted)
          )
    )
    BEGIN
        THROW 51094, 'database_consolidation_inactive_pancake_copy_incomplete', 1;
    END;

    EXEC sys.sp_executesql N'
        IF EXISTS (
            SELECT 1
            FROM dbo.pancake_pages page
            WHERE NOT EXISTS (
                SELECT 1
                FROM dbo.inboxes inbox
                WHERE inbox.tenant_id = page.tenant_id
                  AND inbox.platform = LEFT(
                      LOWER(LTRIM(RTRIM(page.platform))),
                      32)
                  AND inbox.external_page_id = LTRIM(RTRIM(page.page_id))
                  AND CONVERT(VARBINARY(MAX), inbox.encrypted_access_token)
                      = CONVERT(VARBINARY(MAX), page.page_access_token_encrypted)
                  AND inbox.token_expires_at IS NULL
            )
        )
            THROW 51094, ''database_consolidation_pancake_expiry_inconsistent'', 1;';

    -- Carry the legacy mint timestamp onto the canonical row. A canonical value already present
    -- was written by the running application and is newer than anything the legacy map recorded.
    -- Dynamic SQL is required because page_token_minted_at may have been added in this same batch.
    EXEC sys.sp_executesql N'
        UPDATE inbox
        SET page_token_minted_at = page.page_token_minted_at
        FROM dbo.inboxes inbox
        INNER JOIN dbo.pancake_pages page
            ON inbox.tenant_id = page.tenant_id
           AND inbox.platform = LEFT(
               LOWER(LTRIM(RTRIM(page.platform))),
               32)
           AND inbox.external_page_id = LTRIM(RTRIM(page.page_id))
        WHERE inbox.page_token_minted_at IS NULL
          AND page.page_token_minted_at IS NOT NULL;

        IF EXISTS (
            SELECT 1
            FROM dbo.pancake_pages page
            WHERE page.page_token_minted_at IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1
                  FROM dbo.inboxes inbox
                  WHERE inbox.tenant_id = page.tenant_id
                    AND inbox.platform = LEFT(
                        LOWER(LTRIM(RTRIM(page.platform))),
                        32)
                    AND inbox.external_page_id = LTRIM(RTRIM(page.page_id))
                    AND inbox.page_token_minted_at IS NOT NULL
              )
        )
            THROW 51094, ''database_consolidation_pancake_mint_time_lost'', 1;';
END;

IF @legacy_read_state_object_id IS NOT NULL
BEGIN
    -- Hold an exclusive table lock until the enclosing transaction commits so no writer can
    -- insert a row between the emptiness check and the DROP TABLE statement.
    SELECT @legacy_read_state_count = COUNT(*)
    FROM dbo.conversation_read_state WITH (TABLOCKX, HOLDLOCK);

    IF @legacy_read_state_count > 0
        THROW 51094, 'database_consolidation_read_state_not_empty', 1;
END;

IF @legacy_channel_tokens_object_id IS NULL
BEGIN
    IF OBJECT_ID(N'dbo.channel_tokens', N'U') IS NOT NULL
        THROW 51094, 'database_consolidation_channel_tokens_appeared', 1;
END
ELSE
BEGIN
    IF OBJECT_ID(N'dbo.channel_tokens', N'U') IS NULL
       OR OBJECT_ID(N'dbo.channel_tokens', N'U') <> @legacy_channel_tokens_object_id
        THROW 51094, 'database_consolidation_channel_tokens_changed', 1;
    DROP TABLE dbo.channel_tokens;
END;

IF @legacy_read_state_object_id IS NULL
BEGIN
    IF OBJECT_ID(N'dbo.conversation_read_state', N'U') IS NOT NULL
        THROW 51094, 'database_consolidation_read_state_appeared', 1;
END
ELSE
BEGIN
    IF OBJECT_ID(N'dbo.conversation_read_state', N'U') IS NULL
       OR OBJECT_ID(N'dbo.conversation_read_state', N'U') <> @legacy_read_state_object_id
        THROW 51094, 'database_consolidation_read_state_changed', 1;
    DROP TABLE dbo.conversation_read_state;
END;

IF @legacy_pancake_pages_object_id IS NULL
BEGIN
    IF OBJECT_ID(N'dbo.pancake_pages', N'U') IS NOT NULL
        THROW 51094, 'database_consolidation_pancake_pages_appeared', 1;
END
ELSE
BEGIN
    IF OBJECT_ID(N'dbo.pancake_pages', N'U') IS NULL
       OR OBJECT_ID(N'dbo.pancake_pages', N'U') <> @legacy_pancake_pages_object_id
        THROW 51094, 'database_consolidation_pancake_pages_changed', 1;
    DROP TABLE dbo.pancake_pages;
END;

IF @legacy_user_roles_object_id IS NULL
BEGIN
    IF OBJECT_ID(N'dbo.user_roles', N'U') IS NOT NULL
        THROW 51094, 'database_consolidation_user_roles_appeared', 1;
END
ELSE
BEGIN
    IF OBJECT_ID(N'dbo.user_roles', N'U') IS NULL
       OR OBJECT_ID(N'dbo.user_roles', N'U') <> @legacy_user_roles_object_id
        THROW 51094, 'database_consolidation_user_roles_changed', 1;
    DROP TABLE dbo.user_roles;
END;

PRINT CONCAT(
    N'Database consolidation migrated user_roles=', @legacy_user_role_count,
    N', pancake_pages=', @legacy_pancake_page_count,
    N', channel_tokens=', @legacy_channel_token_count,
    N'; removed empty read_state_rows=', @legacy_read_state_count,
    N'.');
