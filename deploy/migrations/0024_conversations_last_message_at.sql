-- 0024: Reconcile conversations.LastMessageAt column name with EF snake_case conventions.
-- 0001 originally used last_msg_at, while EF maps LastMessageAt to last_message_at.

IF COL_LENGTH(N'dbo.conversations', N'last_message_at') IS NULL
    EXEC(N'ALTER TABLE conversations ADD last_message_at DATETIMEOFFSET;');

IF COL_LENGTH(N'dbo.conversations', N'last_msg_at') IS NOT NULL
    AND COL_LENGTH(N'dbo.conversations', N'last_message_at') IS NOT NULL
    EXEC(N'UPDATE conversations SET last_message_at = last_msg_at WHERE last_message_at IS NULL AND last_msg_at IS NOT NULL;');
