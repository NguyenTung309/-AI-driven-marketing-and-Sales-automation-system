-- Split messages.content into original + redacted for PII compliance.
-- original_content: raw inbound text (retained for 30 days, then purged).
-- redacted_content: PII-masked version (retained indefinitely for analytics).

ALTER TABLE messages
    ADD original_content NVARCHAR(MAX) NULL;

ALTER TABLE messages
    ADD redacted_content NVARCHAR(MAX) NULL;

-- Backfill: copy existing content to both columns for historical rows.
UPDATE messages SET original_content = content, redacted_content = content WHERE original_content IS NULL;
