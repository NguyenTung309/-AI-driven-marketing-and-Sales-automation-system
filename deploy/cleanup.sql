-- ==================================================
-- Cleanup chat data (keep config)
-- ==================================================
SET QUOTED_IDENTIFIER ON;
USE clawbot;
BEGIN TRANSACTION;

-- 1. Cleanup chat data
DELETE FROM messages;
DELETE FROM conversation_labels;
DELETE FROM conversation_notes;
IF OBJECT_ID(N'dbo.conversation_read_state', N'U') IS NOT NULL
    DELETE FROM conversation_read_state;
DELETE FROM conversations;
DELETE FROM processed_messages;
DELETE FROM contact_external_ids;
DELETE FROM contacts;
DELETE FROM leads;

-- 2. Migration: add tenant_id to processed_messages
IF COL_LENGTH('dbo.processed_messages', 'tenant_id') IS NULL
    ALTER TABLE dbo.processed_messages ADD tenant_id UNIQUEIDENTIFIER NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';

COMMIT;

SELECT 'Cleanup complete' AS status;