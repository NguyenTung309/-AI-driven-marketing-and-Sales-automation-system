SET QUOTED_IDENTIFIER ON;
SET ARITHABORT ON;

-- Repair duplicate outbound messages: sale manual sends (and AI replies) were persisted
-- locally with external_message_id NULL, then the Pancake poller re-ingested the channel
-- echo as a second row because the echo dedup compared pre-StripHtml text (fixed in
-- ChannelMessageIngestor). Deletes the echo row (has external_message_id) and keeps the
-- local row (has sender attribution). Auto-run by run-all.bat; data_patches makes it one-shot.

IF OBJECT_ID(N'dbo.data_patches', N'U') IS NULL
    CREATE TABLE dbo.data_patches (
        patch_id NVARCHAR(64) NOT NULL CONSTRAINT PK_data_patches PRIMARY KEY,
        applied_at DATETIMEOFFSET NOT NULL CONSTRAINT DF_data_patches_applied_at DEFAULT SYSUTCDATETIME()
    );

IF NOT EXISTS (SELECT 1 FROM dbo.data_patches WHERE patch_id = N'2026-07-09-fix-duplicate-outbound-echo')
BEGIN
    IF COL_LENGTH(N'dbo.messages', N'external_message_id') IS NULL
    BEGIN
        PRINT 'messages.external_message_id missing - patch skipped, retries next run.';
        RETURN;
    END

    DECLARE @deleted INT = 0;

    DELETE e
    FROM dbo.messages e
    WHERE e.direction = N'out'
      AND e.external_message_id IS NOT NULL
      AND EXISTS (
          SELECT 1
          FROM dbo.messages l
          WHERE l.conversation_id = e.conversation_id
            AND l.direction = N'out'
            AND l.external_message_id IS NULL
            AND l.content = e.content
            AND l.sent_at >= DATEADD(MINUTE, -10, e.sent_at)
            AND l.sent_at <= DATEADD(MINUTE, 10, e.sent_at)
            AND l.id <> e.id);
    SET @deleted = @@ROWCOUNT;

    INSERT INTO dbo.data_patches (patch_id) VALUES (N'2026-07-09-fix-duplicate-outbound-echo');
    PRINT CONCAT('fix-duplicate-outbound-echo applied: ', @deleted, ' echo rows deleted.');
END
ELSE
    PRINT 'fix-duplicate-outbound-echo already applied - skipped.';
