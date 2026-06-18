-- Split messages.content into original + redacted for PII compliance.
-- original_content: raw inbound text (retained for 30 days, then purged).
-- redacted_content: PII-masked version (retained indefinitely for analytics).

IF COL_LENGTH('dbo.messages', 'original_content') IS NULL
BEGIN
    ALTER TABLE dbo.messages
        ADD original_content NVARCHAR(MAX) NULL;
END

IF COL_LENGTH('dbo.messages', 'redacted_content') IS NULL
BEGIN
    ALTER TABLE dbo.messages
        ADD redacted_content NVARCHAR(MAX) NULL;
END

-- Backfill: copy existing content to both columns for historical rows.
EXEC(N'UPDATE dbo.messages SET original_content = content, redacted_content = content WHERE original_content IS NULL;');
