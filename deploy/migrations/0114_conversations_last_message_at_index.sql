-- 0114: Create index on conversations.last_message_at (split from 0024 to satisfy SqlServerFixture batch constraint).

IF COL_LENGTH(N'dbo.conversations', N'last_message_at') IS NOT NULL
    AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_conversations_tenant_id_status_last_message_at' AND object_id = OBJECT_ID(N'dbo.conversations'))
    EXEC(N'CREATE INDEX ix_conversations_tenant_id_status_last_message_at ON conversations (tenant_id, status, last_message_at DESC);');
