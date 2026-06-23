-- Tao bang Labels, ConversationLabels, ConversationNotes
-- Chay sau 0028_backfill_conversation_inboxid.sql
BEGIN TRANSACTION;

CREATE TABLE labels (
    id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id),
    name NVARCHAR(128) NOT NULL,
    color NVARCHAR(7) NOT NULL DEFAULT '#6366f1',
    created_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
    deleted_at DATETIMEOFFSET NULL
);
CREATE UNIQUE INDEX ix_labels_tenant_name ON labels (tenant_id, name) WHERE deleted_at IS NULL;

CREATE TABLE conversation_labels (
    conversation_id UNIQUEIDENTIFIER NOT NULL REFERENCES conversations(id),
    label_id UNIQUEIDENTIFIER NOT NULL REFERENCES labels(id),
    created_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
    PRIMARY KEY (conversation_id, label_id)
);
CREATE INDEX ix_conv_labels_label ON conversation_labels (label_id);

CREATE TABLE conversation_notes (
    id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id),
    conversation_id UNIQUEIDENTIFIER NOT NULL REFERENCES conversations(id),
    created_by_user_id UNIQUEIDENTIFIER NOT NULL REFERENCES users(id),
    created_by_display_name NVARCHAR(256) NULL,
    content NVARCHAR(2000) NOT NULL,
    type NVARCHAR(32) NOT NULL DEFAULT 'private',
    created_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX ix_notes_conv ON conversation_notes (conversation_id);

COMMIT;