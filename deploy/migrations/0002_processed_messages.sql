-- Migration: 0002_processed_messages
-- Description: Add processed_messages table for demo polling dedup

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'processed_messages')
BEGIN
    CREATE TABLE processed_messages (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        tenant_id UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT DF_processed_messages_tenant_id DEFAULT '00000000-0000-0000-0000-000000000000',
        Platform NVARCHAR(50) NOT NULL,
        ExternalMessageId NVARCHAR(255) NOT NULL,
        ConversationExternalId NVARCHAR(255) NOT NULL,
        ProcessedAt DATETIME2 NOT NULL,

        CONSTRAINT UQ_processed_messages_platform_external
            UNIQUE (Platform, ExternalMessageId)
    );

    CREATE INDEX IX_processed_messages_platform_external
        ON processed_messages (Platform, ExternalMessageId);

    CREATE INDEX IX_processed_messages_processed_at
        ON processed_messages (ProcessedAt);
END

-- Older DBs created the table without tenant_id (entity maps [Column("tenant_id")])
IF COL_LENGTH(N'dbo.processed_messages', N'tenant_id') IS NULL
    ALTER TABLE dbo.processed_messages ADD tenant_id UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT DF_processed_messages_tenant_id DEFAULT '00000000-0000-0000-0000-000000000000';
