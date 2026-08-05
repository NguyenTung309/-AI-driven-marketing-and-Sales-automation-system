-- Repair cot runtime cua inbox/conversation/message/contact tren schema da ton tai.
-- Idempotent: moi cau lenh tu kiem tra truoc khi doi schema.
-- Chay bang: type <file> ^| docker exec -i clawbot-sqlserver sqlcmd ... -b
-- KHONG them GO: ca file duoc gui nhu mot batch duy nhat.
SET QUOTED_IDENTIFIER ON;
SET ARITHABORT ON;
IF OBJECT_ID(N'dbo.inboxes', N'U') IS NULL AND OBJECT_ID(N'dbo.tenants', N'U') IS NOT NULL CREATE TABLE dbo.inboxes (id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_inboxes PRIMARY KEY DEFAULT NEWID(), tenant_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.tenants(id), name NVARCHAR(256) NOT NULL, platform NVARCHAR(32) NOT NULL, external_page_id NVARCHAR(128) NOT NULL, avatar_url NVARCHAR(512) NULL, encrypted_access_token NVARCHAR(MAX) NULL, encrypted_refresh_token NVARCHAR(MAX) NULL, encrypted_webhook_secret NVARCHAR(MAX) NULL, token_expires_at DATETIMEOFFSET NULL, page_token_minted_at DATETIMEOFFSET NULL, sender_id NVARCHAR(128) NULL, is_active BIT NOT NULL DEFAULT 1, created_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(), updated_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(), deleted_at DATETIMEOFFSET NULL);
IF OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.inboxes', N'encrypted_access_token') IS NULL ALTER TABLE dbo.inboxes ADD encrypted_access_token NVARCHAR(MAX) NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.inboxes') AND name = N'encrypted_access_token' AND (max_length <> -1 OR is_nullable = 0)) ALTER TABLE dbo.inboxes ALTER COLUMN encrypted_access_token NVARCHAR(MAX) NULL;
IF OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.inboxes', N'encrypted_refresh_token') IS NULL ALTER TABLE dbo.inboxes ADD encrypted_refresh_token NVARCHAR(MAX) NULL;
IF OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.inboxes', N'encrypted_webhook_secret') IS NULL ALTER TABLE dbo.inboxes ADD encrypted_webhook_secret NVARCHAR(MAX) NULL;
IF OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.inboxes', N'token_expires_at') IS NULL ALTER TABLE dbo.inboxes ADD token_expires_at DATETIMEOFFSET NULL;
IF OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.inboxes', N'page_token_minted_at') IS NULL ALTER TABLE dbo.inboxes ADD page_token_minted_at DATETIMEOFFSET NULL;
IF OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.inboxes', N'sender_id') IS NULL ALTER TABLE dbo.inboxes ADD sender_id NVARCHAR(128) NULL;
IF OBJECT_ID(N'dbo.inbox_members', N'U') IS NULL AND OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.users', N'U') IS NOT NULL CREATE TABLE dbo.inbox_members (inbox_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.inboxes(id), agent_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.users(id), tenant_id UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.tenants(id), CONSTRAINT PK_inbox_members PRIMARY KEY (inbox_id, agent_id));
IF OBJECT_ID(N'dbo.conversations', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.conversations', N'inbox_id') IS NULL ALTER TABLE dbo.conversations ADD inbox_id UNIQUEIDENTIFIER NULL REFERENCES dbo.inboxes(id);
IF OBJECT_ID(N'dbo.conversations', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.conversations', N'row_version') IS NULL ALTER TABLE dbo.conversations ADD row_version ROWVERSION;
IF OBJECT_ID(N'dbo.conversations', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.conversations', N'snoozed_until') IS NULL ALTER TABLE dbo.conversations ADD snoozed_until DATETIMEOFFSET NULL;
IF OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_inboxes_external' AND object_id = OBJECT_ID(N'dbo.inboxes')) CREATE INDEX ix_inboxes_external ON dbo.inboxes (tenant_id, platform, external_page_id) WHERE is_active = 1;
IF OBJECT_ID(N'dbo.conversations', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.conversations', N'ai_auto_reply_enabled') IS NULL ALTER TABLE dbo.conversations ADD ai_auto_reply_enabled BIT NOT NULL CONSTRAINT DF_conversations_ai_auto_reply_enabled DEFAULT 1;
IF OBJECT_ID(N'dbo.conversations', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.conversations', N'ai_auto_reply_resume_at') IS NULL ALTER TABLE dbo.conversations ADD ai_auto_reply_resume_at DATETIMEOFFSET NULL;
IF OBJECT_ID(N'dbo.messages', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.messages', N'sender_display_name') IS NULL ALTER TABLE dbo.messages ADD sender_display_name NVARCHAR(256) NULL;
IF OBJECT_ID(N'dbo.messages', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.messages', N'sender_avatar_url') IS NULL ALTER TABLE dbo.messages ADD sender_avatar_url NVARCHAR(512) NULL;
IF OBJECT_ID(N'dbo.messages', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.messages', N'attachment_url') IS NULL ALTER TABLE dbo.messages ADD attachment_url NVARCHAR(2048) NULL;
IF OBJECT_ID(N'dbo.contacts', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.contacts', N'avatar_url') IS NULL ALTER TABLE dbo.contacts ADD avatar_url NVARCHAR(512) NULL;
