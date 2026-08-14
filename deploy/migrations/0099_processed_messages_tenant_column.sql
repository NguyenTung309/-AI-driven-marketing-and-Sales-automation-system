-- Migration: 0099_processed_messages_tenant_column
-- Description: Restore tenant ownership required by processed-message deduplication.

IF OBJECT_ID(N'dbo.processed_messages', N'U') IS NOT NULL
    AND COL_LENGTH(N'dbo.processed_messages', N'tenant_id') IS NULL
    ALTER TABLE dbo.processed_messages ADD tenant_id UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT DF_processed_messages_tenant_id DEFAULT '00000000-0000-0000-0000-000000000000';
