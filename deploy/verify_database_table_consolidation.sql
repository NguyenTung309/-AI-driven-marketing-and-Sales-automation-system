-- Verify the canonical schema after legacy-table consolidation. One SqlCommand, no GO.
SET NOCOUNT ON;

DECLARE @labels_contract BIT = CASE WHEN
    OBJECT_ID(N'dbo.labels', N'U') IS NOT NULL
    AND (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.labels')) = 6
    AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.labels') AND name = N'id' AND system_type_id = 36 AND max_length = 16 AND is_nullable = 0)
    AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.labels') AND name = N'tenant_id' AND system_type_id = 36 AND max_length = 16 AND is_nullable = 0)
    AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.labels') AND name = N'name' AND system_type_id = 231 AND max_length = 256 AND is_nullable = 0)
    AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.labels') AND name = N'color' AND system_type_id = 231 AND max_length = 64 AND is_nullable = 0)
    AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.labels') AND name = N'created_at' AND system_type_id = 43 AND is_nullable = 0)
    AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.labels') AND name = N'deleted_at' AND system_type_id = 43 AND is_nullable = 1)
    AND EXISTS (
        SELECT 1
        FROM sys.indexes i
        WHERE i.object_id = OBJECT_ID(N'dbo.labels')
          AND i.is_primary_key = 1
          AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 1
          AND EXISTS (
              SELECT 1
              FROM sys.index_columns ic
              INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                AND ic.key_ordinal = 1 AND c.name = N'id'
          )
    )
    AND EXISTS (
        SELECT 1
        FROM sys.foreign_keys fk
        INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
        INNER JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
        INNER JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
        WHERE fk.parent_object_id = OBJECT_ID(N'dbo.labels')
          AND fk.referenced_object_id = OBJECT_ID(N'dbo.tenants')
          AND pc.name = N'tenant_id'
          AND rc.name = N'id'
          AND fk.is_disabled = 0
          AND fk.is_not_trusted = 0
          AND fk.delete_referential_action = 0
    )
    AND EXISTS (
        SELECT 1
        FROM sys.indexes i
        WHERE i.object_id = OBJECT_ID(N'dbo.labels')
          AND i.name = N'ix_labels_tenant_name'
          AND i.is_disabled = 0
          AND i.is_unique = 1
          AND i.has_filter = 1
          AND i.filter_definition = N'([deleted_at] IS NULL)'
          AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 2
          AND EXISTS (
              SELECT 1 FROM sys.index_columns ic
              INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND c.name = N'tenant_id'
          )
          AND EXISTS (
              SELECT 1 FROM sys.index_columns ic
              INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 2 AND c.name = N'name'
          )
    )
THEN 1 ELSE 0 END;

DECLARE @conversation_labels_contract BIT = CASE WHEN
    OBJECT_ID(N'dbo.conversation_labels', N'U') IS NOT NULL
    AND (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.conversation_labels')) = 3
    AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.conversation_labels') AND name = N'conversation_id' AND system_type_id = 36 AND max_length = 16 AND is_nullable = 0)
    AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.conversation_labels') AND name = N'label_id' AND system_type_id = 36 AND max_length = 16 AND is_nullable = 0)
    AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.conversation_labels') AND name = N'created_at' AND system_type_id = 43 AND is_nullable = 0)
    AND EXISTS (
        SELECT 1
        FROM sys.indexes i
        WHERE i.object_id = OBJECT_ID(N'dbo.conversation_labels')
          AND i.is_primary_key = 1
          AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 2
          AND EXISTS (
              SELECT 1 FROM sys.index_columns ic
              INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND c.name = N'conversation_id'
          )
          AND EXISTS (
              SELECT 1 FROM sys.index_columns ic
              INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 2 AND c.name = N'label_id'
          )
    )
    AND EXISTS (
        SELECT 1 FROM sys.foreign_keys fk
        INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
        INNER JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
        INNER JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
        WHERE fk.parent_object_id = OBJECT_ID(N'dbo.conversation_labels')
          AND fk.referenced_object_id = OBJECT_ID(N'dbo.conversations')
          AND pc.name = N'conversation_id' AND rc.name = N'id'
          AND fk.is_disabled = 0 AND fk.is_not_trusted = 0 AND fk.delete_referential_action = 0
    )
    AND EXISTS (
        SELECT 1 FROM sys.foreign_keys fk
        INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
        INNER JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
        INNER JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
        WHERE fk.parent_object_id = OBJECT_ID(N'dbo.conversation_labels')
          AND fk.referenced_object_id = OBJECT_ID(N'dbo.labels')
          AND pc.name = N'label_id' AND rc.name = N'id'
          AND fk.is_disabled = 0 AND fk.is_not_trusted = 0 AND fk.delete_referential_action = 0
    )
    AND EXISTS (
        SELECT 1 FROM sys.indexes i
        WHERE i.object_id = OBJECT_ID(N'dbo.conversation_labels')
          AND i.name = N'ix_conv_labels_label'
          AND i.is_disabled = 0
          AND i.is_unique = 0
          AND i.has_filter = 0
          AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 1
          AND EXISTS (
              SELECT 1 FROM sys.index_columns ic
              INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND c.name = N'label_id'
          )
    )
THEN 1 ELSE 0 END;

DECLARE @conversation_notes_contract BIT = CASE WHEN
    OBJECT_ID(N'dbo.conversation_notes', N'U') IS NOT NULL
    AND (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.conversation_notes')) = 9
    AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.conversation_notes') AND name = N'id' AND system_type_id = 36 AND max_length = 16 AND is_nullable = 0)
    AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.conversation_notes') AND name = N'tenant_id' AND system_type_id = 36 AND max_length = 16 AND is_nullable = 0)
    AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.conversation_notes') AND name = N'conversation_id' AND system_type_id = 36 AND max_length = 16 AND is_nullable = 0)
    AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.conversation_notes') AND name = N'created_by_user_id' AND system_type_id = 36 AND max_length = 16 AND is_nullable = 0)
    AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.conversation_notes') AND name = N'created_by_display_name' AND system_type_id = 231 AND max_length = 512 AND is_nullable = 1)
    AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.conversation_notes') AND name = N'content' AND system_type_id = 231 AND max_length = 4000 AND is_nullable = 0)
    AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.conversation_notes') AND name = N'type' AND system_type_id = 231 AND max_length = 64 AND is_nullable = 0)
    AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.conversation_notes') AND name = N'created_at' AND system_type_id = 43 AND is_nullable = 0)
    AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.conversation_notes') AND name = N'updated_at' AND system_type_id = 43 AND is_nullable = 0)
    AND EXISTS (
        SELECT 1 FROM sys.indexes i
        WHERE i.object_id = OBJECT_ID(N'dbo.conversation_notes')
          AND i.is_primary_key = 1
          AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 1
          AND EXISTS (
              SELECT 1 FROM sys.index_columns ic
              INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND c.name = N'id'
          )
    )
    AND EXISTS (
        SELECT 1 FROM sys.foreign_keys fk
        INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
        INNER JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
        INNER JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
        WHERE fk.parent_object_id = OBJECT_ID(N'dbo.conversation_notes')
          AND fk.referenced_object_id = OBJECT_ID(N'dbo.tenants')
          AND pc.name = N'tenant_id' AND rc.name = N'id'
          AND fk.is_disabled = 0 AND fk.is_not_trusted = 0 AND fk.delete_referential_action = 0
    )
    AND EXISTS (
        SELECT 1 FROM sys.foreign_keys fk
        INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
        INNER JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
        INNER JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
        WHERE fk.parent_object_id = OBJECT_ID(N'dbo.conversation_notes')
          AND fk.referenced_object_id = OBJECT_ID(N'dbo.conversations')
          AND pc.name = N'conversation_id' AND rc.name = N'id'
          AND fk.is_disabled = 0 AND fk.is_not_trusted = 0 AND fk.delete_referential_action = 0
    )
    AND EXISTS (
        SELECT 1 FROM sys.foreign_keys fk
        INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
        INNER JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
        INNER JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
        WHERE fk.parent_object_id = OBJECT_ID(N'dbo.conversation_notes')
          AND fk.referenced_object_id = OBJECT_ID(N'dbo.users')
          AND pc.name = N'created_by_user_id' AND rc.name = N'id'
          AND fk.is_disabled = 0 AND fk.is_not_trusted = 0 AND fk.delete_referential_action = 0
    )
    AND EXISTS (
        SELECT 1 FROM sys.indexes i
        WHERE i.object_id = OBJECT_ID(N'dbo.conversation_notes')
          AND i.name = N'ix_notes_conv'
          AND i.is_disabled = 0
          AND i.is_unique = 0
          AND i.has_filter = 0
          AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 1
          AND EXISTS (
              SELECT 1 FROM sys.index_columns ic
              INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND c.name = N'conversation_id'
          )
    )
THEN 1 ELSE 0 END;

DECLARE @inbox_identity_contract BIT = CASE WHEN
    OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL
    AND EXISTS (
        SELECT 1
        FROM sys.indexes i
        WHERE i.object_id = OBJECT_ID(N'dbo.inboxes')
          AND i.name = N'UX_inboxes_tenant_platform_external_active'
          AND i.is_disabled = 0
          AND i.is_unique = 1
          AND i.has_filter = 1
          AND i.filter_definition = N'([is_active]=(1) AND [deleted_at] IS NULL)'
          AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 3
          AND EXISTS (
              SELECT 1 FROM sys.index_columns ic
              INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND c.name = N'tenant_id'
          )
          AND EXISTS (
              SELECT 1 FROM sys.index_columns ic
              INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 2 AND c.name = N'platform'
          )
          AND EXISTS (
              SELECT 1 FROM sys.index_columns ic
              INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 3 AND c.name = N'external_page_id'
          )
    )
THEN 1 ELSE 0 END;

DECLARE @flags NVARCHAR(32) = CONCAT(
    CASE WHEN OBJECT_ID(N'dbo.user_roles', N'U') IS NULL
              AND OBJECT_ID(N'dbo.AspNetRoles', N'U') IS NOT NULL
              AND OBJECT_ID(N'dbo.AspNetUserRoles', N'U') IS NOT NULL
         THEN 1 ELSE 0 END,
    CASE WHEN OBJECT_ID(N'dbo.channel_tokens', N'U') IS NULL THEN 1 ELSE 0 END,
    CASE WHEN OBJECT_ID(N'dbo.conversation_read_state', N'U') IS NULL THEN 1 ELSE 0 END,
    CASE WHEN OBJECT_ID(N'dbo.pancake_pages', N'U') IS NULL THEN 1 ELSE 0 END,
    CASE WHEN OBJECT_ID(N'dbo.labels', N'U') IS NOT NULL THEN 1 ELSE 0 END,
    CASE WHEN OBJECT_ID(N'dbo.conversation_labels', N'U') IS NOT NULL THEN 1 ELSE 0 END,
    CASE WHEN OBJECT_ID(N'dbo.conversation_notes', N'U') IS NOT NULL THEN 1 ELSE 0 END,
    CASE WHEN COL_LENGTH(N'dbo.inboxes', N'encrypted_access_token') IS NOT NULL
              AND @inbox_identity_contract = 1
         THEN 1 ELSE 0 END,
    CASE WHEN EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.inboxes')
          AND name = N'encrypted_access_token'
          AND system_type_id = 231
          AND max_length = -1
          AND is_nullable = 1
    ) THEN 1 ELSE 0 END,
    CASE WHEN EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.inboxes')
          AND name = N'encrypted_refresh_token'
          AND system_type_id = 231
          AND max_length = -1
          AND is_nullable = 1
    ) THEN 1 ELSE 0 END,
    CASE WHEN EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.inboxes')
          AND name = N'encrypted_webhook_secret'
          AND system_type_id = 231
          AND max_length = -1
          AND is_nullable = 1
    ) THEN 1 ELSE 0 END,
    CASE WHEN (
        SELECT COUNT(*) FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.inboxes')
          AND name IN (N'token_expires_at', N'page_token_minted_at')
          AND system_type_id = 43
          AND is_nullable = 1
    ) = 2 THEN 1 ELSE 0 END,
    @labels_contract,
    @conversation_labels_contract,
    @conversation_notes_contract
);

DECLARE @dbo_table_count INT = (
    SELECT COUNT(*) FROM sys.tables
    WHERE is_ms_shipped = 0 AND schema_id = SCHEMA_ID(N'dbo')
);
DECLARE @table_count INT = (
    SELECT COUNT(*) FROM sys.tables
    WHERE is_ms_shipped = 0
);

-- HangFire creates its 11 tables when the API host starts, after run-all's pre-start schema gate.
SELECT CONCAT(@flags, N'|', @dbo_table_count, N'|', @table_count);

IF @flags <> N'111111111111111'
    THROW 51096, 'database_table_consolidation_verification_failed', 1;
