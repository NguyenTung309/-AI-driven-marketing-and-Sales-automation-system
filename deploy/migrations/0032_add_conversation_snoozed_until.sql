-- 0032_add_conversation_snoozed_until.sql
-- Add snoozed_until column to conversations table to match EF Core Domain model
IF COL_LENGTH(N'dbo.conversations', N'snoozed_until') IS NULL
BEGIN
    ALTER TABLE conversations ADD snoozed_until DATETIMEOFFSET NULL;
END
