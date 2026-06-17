/*
  Sample content briefs for a Chinese-language course (HSK) tenant.
  Idempotent: MERGE on (tenant_id, platform, brief). Re-running refreshes status/updated_at.

  USAGE: set the target tenant slug below, then apply:
    sqlcmd -S localhost -U sa -P "<password>" -d clawbot -i deploy/seed/content-briefs.sql -C
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @tenant_slug NVARCHAR(64) = N'demo';   -- <-- CHANGE to the target tenant slug
DECLARE @tenant_id UNIQUEIDENTIFIER = (SELECT id FROM tenants WHERE slug = @tenant_slug);
DECLARE @now DATETIMEOFFSET = SYSDATETIMEOFFSET();

IF @tenant_id IS NULL
BEGIN
    RAISERROR(N'Tenant slug "%s" not found. Seed aborted.', 16, 1, @tenant_slug);
    RETURN;
END;

BEGIN TRANSACTION;

DECLARE @expected_rows INT = 5;

DECLARE @briefs TABLE (
    platform NVARCHAR(32) NOT NULL,
    brief NVARCHAR(MAX) NOT NULL
);

INSERT INTO @briefs (platform, brief)
VALUES
    (N'tiktok', N'Create a short HSK 3 listening drill about ordering drinks, with one hook, one example sentence, and a call to comment the missing word.'),
    (N'instagram', N'Create a carousel brief for Mandarin tone pairs: slide-by-slide idea, one learner mistake per slide, and a final save/share prompt.'),
    (N'facebook', N'Create a community post for busy office workers learning Chinese: empathy hook, 3 practical study tips, and a soft invite to book a placement test.'),
    (N'youtube', N'Create a 6-minute YouTube lesson outline for HSK 4 result complements, including intro hook, examples, practice prompts, and CTA.'),
    (N'zalo', N'Create a concise Zalo broadcast for an upcoming HSK mock test, including benefit, date placeholder, and reply keyword.');

MERGE content_briefs AS target
USING @briefs AS source
ON target.tenant_id = @tenant_id
    AND target.platform = source.platform
    AND target.brief = source.brief
WHEN MATCHED THEN
    UPDATE SET
        status = N'pending',
        updated_at = @now
WHEN NOT MATCHED THEN
    INSERT (id, tenant_id, platform, brief, status, created_by, created_at, updated_at)
    VALUES (NEWID(), @tenant_id, source.platform, source.brief, N'pending', NULL, @now, @now);

DECLARE @actual_rows INT;

SELECT @actual_rows = COUNT(*)
FROM content_briefs AS seeded
WHERE seeded.tenant_id = @tenant_id
  AND EXISTS (
      SELECT 1
      FROM @briefs AS expected
      WHERE expected.platform = seeded.platform
        AND expected.brief = seeded.brief
  );

IF @actual_rows <> @expected_rows
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(N'Expected %d content_briefs rows for tenant "%s"; found %d. Seed aborted.', 16, 1, @expected_rows, @tenant_slug, @actual_rows);
    RETURN;
END;

COMMIT TRANSACTION;

PRINT N'content_briefs seed applied for tenant: ' + @tenant_slug;
