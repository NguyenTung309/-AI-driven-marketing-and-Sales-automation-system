IF COL_LENGTH(N'dbo.conversations', N'ai_auto_reply_enabled') IS NULL
BEGIN
    ALTER TABLE dbo.conversations ADD ai_auto_reply_enabled BIT NOT NULL CONSTRAINT DF_conversations_ai_auto_reply_enabled DEFAULT 1;
END
