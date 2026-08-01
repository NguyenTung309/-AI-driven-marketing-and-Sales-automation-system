-- Repair active inbox collaboration tables that EF and API endpoints require. One SqlCommand, no GO.
SET QUOTED_IDENTIFIER ON;
SET ARITHABORT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.tenants', N'U') IS NULL
       OR OBJECT_ID(N'dbo.users', N'U') IS NULL
       OR OBJECT_ID(N'dbo.conversations', N'U') IS NULL
       OR OBJECT_ID(N'dbo.inboxes', N'U') IS NULL
    BEGIN
        THROW 51095, 'inbox_collaboration_parent_schema_missing', 1;
    END;

    IF EXISTS (
        SELECT 1
        FROM sys.indexes i
        WHERE i.object_id = OBJECT_ID(N'dbo.inboxes')
          AND i.name = N'UX_inboxes_tenant_platform_external_active'
          AND (
              i.is_unique <> 1
              OR i.has_filter <> 1
              OR i.filter_definition <> N'([is_active]=(1) AND [deleted_at] IS NULL)'
              OR (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) <> 3
              OR NOT EXISTS (
                  SELECT 1 FROM sys.index_columns ic
                  INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                  WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND c.name = N'tenant_id'
              )
              OR NOT EXISTS (
                  SELECT 1 FROM sys.index_columns ic
                  INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                  WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 2 AND c.name = N'platform'
              )
              OR NOT EXISTS (
                  SELECT 1 FROM sys.index_columns ic
                  INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                  WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 3 AND c.name = N'external_page_id'
              )
          )
    )
        THROW 51095, 'inbox_active_identity_index_malformed', 1;

    IF (
        NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.inboxes')
              AND name = N'UX_inboxes_tenant_platform_external_active'
        )
        OR EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.inboxes')
              AND name = N'UX_inboxes_tenant_platform_external_active'
              AND is_disabled = 1
        )
    )
       AND EXISTS (
           SELECT 1
           FROM dbo.inboxes
           WHERE is_active = 1 AND deleted_at IS NULL
           GROUP BY tenant_id, platform, external_page_id
           HAVING COUNT(*) > 1
       )
        THROW 51095, 'inbox_active_identity_duplicates_exist', 1;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.inboxes')
          AND name = N'UX_inboxes_tenant_platform_external_active'
    )
        CREATE UNIQUE INDEX UX_inboxes_tenant_platform_external_active
            ON dbo.inboxes (tenant_id, platform, external_page_id)
            WHERE is_active = 1 AND deleted_at IS NULL;

    IF EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.inboxes')
          AND name = N'UX_inboxes_tenant_platform_external_active'
          AND is_disabled = 1
    )
        ALTER INDEX UX_inboxes_tenant_platform_external_active ON dbo.inboxes REBUILD;

    IF OBJECT_ID(N'dbo.labels', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.labels (
            id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_labels PRIMARY KEY DEFAULT NEWID(),
            tenant_id UNIQUEIDENTIFIER NOT NULL,
            name NVARCHAR(128) NOT NULL,
            color NVARCHAR(32) NOT NULL CONSTRAINT DF_labels_color DEFAULT N'#6366f1',
            created_at DATETIMEOFFSET NOT NULL CONSTRAINT DF_labels_created_at DEFAULT SYSUTCDATETIME(),
            deleted_at DATETIMEOFFSET NULL,
            CONSTRAINT FK_labels_tenants FOREIGN KEY (tenant_id) REFERENCES dbo.tenants(id)
        );
    END;

    IF EXISTS (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.labels')
          AND name = N'color'
          AND system_type_id <> 231
    )
    BEGIN
        THROW 51095, 'inbox_collaboration_labels_color_type_invalid', 1;
    END;

    IF EXISTS (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.labels')
          AND name = N'color'
          AND max_length <> 64
    )
    BEGIN
        EXEC(N'ALTER TABLE dbo.labels ALTER COLUMN color NVARCHAR(32) NOT NULL;');
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys fk
        INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
        INNER JOIN sys.columns parent_column
            ON parent_column.object_id = fkc.parent_object_id
           AND parent_column.column_id = fkc.parent_column_id
        INNER JOIN sys.columns referenced_column
            ON referenced_column.object_id = fkc.referenced_object_id
           AND referenced_column.column_id = fkc.referenced_column_id
        WHERE fk.parent_object_id = OBJECT_ID(N'dbo.labels')
          AND fk.referenced_object_id = OBJECT_ID(N'dbo.tenants')
          AND parent_column.name = N'tenant_id'
          AND referenced_column.name = N'id'
    )
    BEGIN
        IF OBJECT_ID(N'dbo.FK_labels_tenants', N'F') IS NOT NULL
            THROW 51095, 'inbox_collaboration_labels_tenant_fk_malformed', 1;

        ALTER TABLE dbo.labels WITH CHECK
            ADD CONSTRAINT FK_labels_tenants FOREIGN KEY (tenant_id) REFERENCES dbo.tenants(id);
    END;

    IF EXISTS (
        SELECT 1
        FROM sys.indexes i
        WHERE i.object_id = OBJECT_ID(N'dbo.labels')
          AND i.name = N'ix_labels_tenant_name'
          AND (
              i.is_unique <> 1
              OR i.has_filter <> 1
              OR i.filter_definition <> N'([deleted_at] IS NULL)'
              OR (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) <> 2
              OR NOT EXISTS (
                  SELECT 1
                  FROM sys.index_columns ic
                  INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                  WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                    AND ic.key_ordinal = 1 AND c.name = N'tenant_id'
              )
              OR NOT EXISTS (
                  SELECT 1
                  FROM sys.index_columns ic
                  INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                  WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                    AND ic.key_ordinal = 2 AND c.name = N'name'
              )
          )
    )
    BEGIN
        THROW 51095, 'inbox_collaboration_labels_index_malformed', 1;
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.labels')
          AND name = N'ix_labels_tenant_name'
    )
    BEGIN
        CREATE UNIQUE INDEX ix_labels_tenant_name
            ON dbo.labels (tenant_id, name)
            WHERE deleted_at IS NULL;
    END;

    IF EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.labels')
          AND name = N'ix_labels_tenant_name'
          AND is_disabled = 1
    )
        ALTER INDEX ix_labels_tenant_name ON dbo.labels REBUILD;

    IF OBJECT_ID(N'dbo.conversation_labels', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.conversation_labels (
            conversation_id UNIQUEIDENTIFIER NOT NULL,
            label_id UNIQUEIDENTIFIER NOT NULL,
            created_at DATETIMEOFFSET NOT NULL CONSTRAINT DF_conversation_labels_created_at DEFAULT SYSUTCDATETIME(),
            CONSTRAINT PK_conversation_labels PRIMARY KEY (conversation_id, label_id),
            CONSTRAINT FK_conversation_labels_conversations FOREIGN KEY (conversation_id) REFERENCES dbo.conversations(id),
            CONSTRAINT FK_conversation_labels_labels FOREIGN KEY (label_id) REFERENCES dbo.labels(id)
        );
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_key_columns fkc
        INNER JOIN sys.columns parent_column
            ON parent_column.object_id = fkc.parent_object_id
           AND parent_column.column_id = fkc.parent_column_id
        INNER JOIN sys.columns referenced_column
            ON referenced_column.object_id = fkc.referenced_object_id
           AND referenced_column.column_id = fkc.referenced_column_id
        WHERE fkc.parent_object_id = OBJECT_ID(N'dbo.conversation_labels')
          AND fkc.referenced_object_id = OBJECT_ID(N'dbo.conversations')
          AND parent_column.name = N'conversation_id'
          AND referenced_column.name = N'id'
    )
    BEGIN
        IF OBJECT_ID(N'dbo.FK_conversation_labels_conversations', N'F') IS NOT NULL
            THROW 51095, 'inbox_collaboration_conversation_labels_conversation_fk_malformed', 1;

        ALTER TABLE dbo.conversation_labels WITH CHECK
            ADD CONSTRAINT FK_conversation_labels_conversations
            FOREIGN KEY (conversation_id) REFERENCES dbo.conversations(id);
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_key_columns fkc
        INNER JOIN sys.columns parent_column
            ON parent_column.object_id = fkc.parent_object_id
           AND parent_column.column_id = fkc.parent_column_id
        INNER JOIN sys.columns referenced_column
            ON referenced_column.object_id = fkc.referenced_object_id
           AND referenced_column.column_id = fkc.referenced_column_id
        WHERE fkc.parent_object_id = OBJECT_ID(N'dbo.conversation_labels')
          AND fkc.referenced_object_id = OBJECT_ID(N'dbo.labels')
          AND parent_column.name = N'label_id'
          AND referenced_column.name = N'id'
    )
    BEGIN
        IF OBJECT_ID(N'dbo.FK_conversation_labels_labels', N'F') IS NOT NULL
            THROW 51095, 'inbox_collaboration_conversation_labels_label_fk_malformed', 1;

        ALTER TABLE dbo.conversation_labels WITH CHECK
            ADD CONSTRAINT FK_conversation_labels_labels
            FOREIGN KEY (label_id) REFERENCES dbo.labels(id);
    END;

    IF EXISTS (
        SELECT 1
        FROM sys.indexes i
        WHERE i.object_id = OBJECT_ID(N'dbo.conversation_labels')
          AND i.name = N'ix_conv_labels_label'
          AND (
              i.is_unique <> 0
              OR i.has_filter <> 0
              OR (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) <> 1
              OR NOT EXISTS (
                  SELECT 1
                  FROM sys.index_columns ic
                  INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                  WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                    AND ic.key_ordinal = 1 AND c.name = N'label_id'
              )
          )
    )
    BEGIN
        THROW 51095, 'inbox_collaboration_conversation_labels_index_malformed', 1;
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.conversation_labels')
          AND name = N'ix_conv_labels_label'
    )
    BEGIN
        CREATE INDEX ix_conv_labels_label ON dbo.conversation_labels (label_id);
    END;

    IF EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.conversation_labels')
          AND name = N'ix_conv_labels_label'
          AND is_disabled = 1
    )
        ALTER INDEX ix_conv_labels_label ON dbo.conversation_labels REBUILD;

    IF OBJECT_ID(N'dbo.conversation_notes', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.conversation_notes (
            id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_conversation_notes PRIMARY KEY DEFAULT NEWID(),
            tenant_id UNIQUEIDENTIFIER NOT NULL,
            conversation_id UNIQUEIDENTIFIER NOT NULL,
            created_by_user_id UNIQUEIDENTIFIER NOT NULL,
            created_by_display_name NVARCHAR(256) NULL,
            content NVARCHAR(2000) NOT NULL,
            type NVARCHAR(32) NOT NULL CONSTRAINT DF_conversation_notes_type DEFAULT N'private',
            created_at DATETIMEOFFSET NOT NULL CONSTRAINT DF_conversation_notes_created_at DEFAULT SYSUTCDATETIME(),
            updated_at DATETIMEOFFSET NOT NULL CONSTRAINT DF_conversation_notes_updated_at DEFAULT SYSUTCDATETIME(),
            CONSTRAINT FK_conversation_notes_tenants FOREIGN KEY (tenant_id) REFERENCES dbo.tenants(id),
            CONSTRAINT FK_conversation_notes_conversations FOREIGN KEY (conversation_id) REFERENCES dbo.conversations(id),
            CONSTRAINT FK_conversation_notes_users FOREIGN KEY (created_by_user_id) REFERENCES dbo.users(id)
        );
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_key_columns fkc
        INNER JOIN sys.columns parent_column
            ON parent_column.object_id = fkc.parent_object_id
           AND parent_column.column_id = fkc.parent_column_id
        INNER JOIN sys.columns referenced_column
            ON referenced_column.object_id = fkc.referenced_object_id
           AND referenced_column.column_id = fkc.referenced_column_id
        WHERE fkc.parent_object_id = OBJECT_ID(N'dbo.conversation_notes')
          AND fkc.referenced_object_id = OBJECT_ID(N'dbo.tenants')
          AND parent_column.name = N'tenant_id'
          AND referenced_column.name = N'id'
    )
    BEGIN
        IF OBJECT_ID(N'dbo.FK_conversation_notes_tenants', N'F') IS NOT NULL
            THROW 51095, 'inbox_collaboration_notes_tenant_fk_malformed', 1;

        ALTER TABLE dbo.conversation_notes WITH CHECK
            ADD CONSTRAINT FK_conversation_notes_tenants FOREIGN KEY (tenant_id) REFERENCES dbo.tenants(id);
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_key_columns fkc
        INNER JOIN sys.columns parent_column
            ON parent_column.object_id = fkc.parent_object_id
           AND parent_column.column_id = fkc.parent_column_id
        INNER JOIN sys.columns referenced_column
            ON referenced_column.object_id = fkc.referenced_object_id
           AND referenced_column.column_id = fkc.referenced_column_id
        WHERE fkc.parent_object_id = OBJECT_ID(N'dbo.conversation_notes')
          AND fkc.referenced_object_id = OBJECT_ID(N'dbo.conversations')
          AND parent_column.name = N'conversation_id'
          AND referenced_column.name = N'id'
    )
    BEGIN
        IF OBJECT_ID(N'dbo.FK_conversation_notes_conversations', N'F') IS NOT NULL
            THROW 51095, 'inbox_collaboration_notes_conversation_fk_malformed', 1;

        ALTER TABLE dbo.conversation_notes WITH CHECK
            ADD CONSTRAINT FK_conversation_notes_conversations
            FOREIGN KEY (conversation_id) REFERENCES dbo.conversations(id);
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_key_columns fkc
        INNER JOIN sys.columns parent_column
            ON parent_column.object_id = fkc.parent_object_id
           AND parent_column.column_id = fkc.parent_column_id
        INNER JOIN sys.columns referenced_column
            ON referenced_column.object_id = fkc.referenced_object_id
           AND referenced_column.column_id = fkc.referenced_column_id
        WHERE fkc.parent_object_id = OBJECT_ID(N'dbo.conversation_notes')
          AND fkc.referenced_object_id = OBJECT_ID(N'dbo.users')
          AND parent_column.name = N'created_by_user_id'
          AND referenced_column.name = N'id'
    )
    BEGIN
        IF OBJECT_ID(N'dbo.FK_conversation_notes_users', N'F') IS NOT NULL
            THROW 51095, 'inbox_collaboration_notes_user_fk_malformed', 1;

        ALTER TABLE dbo.conversation_notes WITH CHECK
            ADD CONSTRAINT FK_conversation_notes_users FOREIGN KEY (created_by_user_id) REFERENCES dbo.users(id);
    END;

    IF EXISTS (
        SELECT 1
        FROM sys.indexes i
        WHERE i.object_id = OBJECT_ID(N'dbo.conversation_notes')
          AND i.name = N'ix_notes_conv'
          AND (
              i.is_unique <> 0
              OR i.has_filter <> 0
              OR (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) <> 1
              OR NOT EXISTS (
                  SELECT 1
                  FROM sys.index_columns ic
                  INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                  WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                    AND ic.key_ordinal = 1 AND c.name = N'conversation_id'
              )
          )
    )
    BEGIN
        THROW 51095, 'inbox_collaboration_notes_index_malformed', 1;
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.conversation_notes')
          AND name = N'ix_notes_conv'
    )
    BEGIN
        CREATE INDEX ix_notes_conv ON dbo.conversation_notes (conversation_id);
    END;

    IF EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.conversation_notes')
          AND name = N'ix_notes_conv'
          AND is_disabled = 1
    )
        ALTER INDEX ix_notes_conv ON dbo.conversation_notes REBUILD;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
