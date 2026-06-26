-- 0030_add_inbox_encrypted_token.sql
-- Add EncryptedAccessToken column to Inboxes table for storing Pancake page_access_token
ALTER TABLE Inboxes ADD encrypted_access_token NVARCHAR(1024) NULL;
