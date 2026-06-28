-- Tao bang inboxes, channel_tokens, inbox_members, conversation_read_state
-- Cac bang nay can thiet cho Agent Hub isolation
BEGIN TRANSACTION;

CREATE TABLE inboxes (
    id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id),
    name NVARCHAR(256) NOT NULL,
    platform NVARCHAR(32) NOT NULL,  -- facebook|zalo|web
    external_page_id NVARCHAR(128) NOT NULL,
    avatar_url NVARCHAR(512) NULL,
    is_active BIT NOT NULL DEFAULT 1,
    created_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
    deleted_at DATETIMEOFFSET NULL
);
CREATE INDEX ix_inboxes_external ON inboxes (tenant_id, platform, external_page_id) WHERE is_active = 1;

CREATE TABLE channel_tokens (
    inbox_id UNIQUEIDENTIFIER PRIMARY KEY REFERENCES inboxes(id),
    access_token_encrypted NVARCHAR(MAX) NOT NULL,
    refresh_token_encrypted NVARCHAR(MAX) NULL,
    webhook_secret_encrypted NVARCHAR(MAX) NOT NULL,
    token_expires_at DATETIMEOFFSET NULL,
    is_active BIT NOT NULL DEFAULT 1,
    created_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE inbox_members (
    inbox_id UNIQUEIDENTIFIER NOT NULL REFERENCES inboxes(id),
    agent_id UNIQUEIDENTIFIER NOT NULL REFERENCES users(id),
    tenant_id UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id),
    PRIMARY KEY (inbox_id, agent_id)
);

CREATE TABLE conversation_read_state (
    user_id UNIQUEIDENTIFIER NOT NULL REFERENCES users(id),
    conversation_id UNIQUEIDENTIFIER NOT NULL REFERENCES conversations(id),
    last_read_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
    PRIMARY KEY (user_id, conversation_id)
);
CREATE INDEX ix_convread_conv ON conversation_read_state (conversation_id);

-- Add inbox_id to conversations table
ALTER TABLE conversations ADD inbox_id UNIQUEIDENTIFIER NULL REFERENCES inboxes(id);

COMMIT;