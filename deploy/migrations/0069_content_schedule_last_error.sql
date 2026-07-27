-- 0069: persist last publish/hold/stale error on content_schedule for calendar UX + manual retry.
-- One SqlCommand, no GO.
IF COL_LENGTH('dbo.content_schedule', 'last_error') IS NULL
    ALTER TABLE dbo.content_schedule ADD last_error NVARCHAR(1024) NULL;
