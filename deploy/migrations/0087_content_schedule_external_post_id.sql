-- 0087: persist the provider object id independently from the published URL.
-- Meta engagement sync needs the Facebook post id or Instagram media id even when a permalink is absent or changes format.
-- One SqlCommand, no GO.
IF COL_LENGTH('dbo.content_schedule', 'external_post_id') IS NULL
    ALTER TABLE dbo.content_schedule ADD external_post_id NVARCHAR(256) NULL;
