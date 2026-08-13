-- 0024: Reconcile conversations.LastMessageAt column name with EF snake_case conventions.
-- 0001 originally used last_msg_at, while EF maps LastMessageAt to last_message_at.

IF COL_LENGTH(N'dbo.conversations', N'last_message_at') IS NULL
    EXEC(N'ALTER TABLE conversations ADD last_message_at DATETIMEOFFSET;');

IF COL_LENGTH(N'dbo.conversations', N'last_msg_at') IS NOT NULL
    AND COL_LENGTH(N'dbo.conversations', N'last_message_at') IS NOT NULL
    EXEC(N'UPDATE conversations SET last_message_at = last_msg_at WHERE last_message_at IS NULL AND last_msg_at IS NOT NULL;');

IF COL_LENGTH(N'dbo.conversations', N'last_message_at') IS NOT NULL
    AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_conversations_tenant_id_status_last_message_at' AND object_id = OBJECT_ID(N'dbo.conversations'))
    EXEC(N'CREATE INDEX ix_conversations_tenant_id_status_last_message_at ON conversations (tenant_id, status, last_message_at DESC);');
