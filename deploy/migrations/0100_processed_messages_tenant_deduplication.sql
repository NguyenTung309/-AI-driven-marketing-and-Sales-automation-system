-- Migration: 0100_processed_messages_tenant_deduplication
-- Description: Scope inbound-message deduplication to its owning tenant.
-- Existing historical rows retain the zero tenant identifier because their owner cannot be recovered safely.

IF OBJECT_ID(N'dbo.processed_messages', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.processed_messages', N'tenant_id') IS NULL
        THROW 51000, 'processed_messages.tenant_id must exist before tenant-scoped deduplication can be applied.', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.processed_messages
        GROUP BY tenant_id, Platform, ExternalMessageId
        HAVING COUNT(*) > 1)
        THROW 51000, 'Cannot create tenant-scoped processed_messages deduplication because duplicate tenant message keys already exist.', 1;

    DECLARE @legacyUniqueName sysname;
    DECLARE @legacyIsConstraint bit;
    DECLARE @dropSql nvarchar(max);

    SELECT TOP (1)
        @legacyUniqueName = i.name,
        @legacyIsConstraint = CASE WHEN kc.object_id IS NULL THEN 0 ELSE 1 END
    FROM sys.indexes AS i
    LEFT JOIN sys.key_constraints AS kc
        ON kc.parent_object_id = i.object_id
        AND kc.unique_index_id = i.index_id
    WHERE i.object_id = OBJECT_ID(N'dbo.processed_messages')
        AND i.is_unique = 1
        AND i.is_primary_key = 0
        AND (SELECT COUNT(*) FROM sys.index_columns AS ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 2
        AND EXISTS (
            SELECT 1
            FROM sys.index_columns AS ic
            INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND c.name = N'Platform')
        AND EXISTS (
            SELECT 1
            FROM sys.index_columns AS ic
            INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 2 AND c.name = N'ExternalMessageId');

    IF @legacyUniqueName IS NOT NULL
    BEGIN
        DECLARE @quotedName nvarchar(128) = QUOTENAME(@legacyUniqueName);
        DECLARE @sql nvarchar(max);

        IF @legacyIsConstraint = 1
        BEGIN
            SET @dropSql = N'ALTER TABLE dbo.processed_messages DROP CONSTRAINT ' + QUOTENAME(@legacyUniqueName) + N';';
            EXEC(@dropSql);
        END;
        ELSE
        BEGIN
            SET @dropSql = N'DROP INDEX ' + QUOTENAME(@legacyUniqueName) + N' ON dbo.processed_messages;';
            EXEC(@dropSql);
        END;
            SET @sql = N'ALTER TABLE dbo.processed_messages DROP CONSTRAINT ' + @quotedName + N';';
        ELSE
            SET @sql = N'DROP INDEX ' + @quotedName + N' ON dbo.processed_messages;';

        EXEC(@sql);
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes AS i
        WHERE i.object_id = OBJECT_ID(N'dbo.processed_messages')
            AND i.name = N'IX_processed_messages_tenant_platform_external')
        CREATE UNIQUE INDEX IX_processed_messages_tenant_platform_external
            ON dbo.processed_messages (tenant_id, Platform, ExternalMessageId);
END;
