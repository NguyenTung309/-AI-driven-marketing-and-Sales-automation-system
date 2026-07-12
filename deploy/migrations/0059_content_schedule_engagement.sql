-- 0059: engagement counts fetched back from the platform after publishing (FB Graph likes/comments).
-- like_count/comment_count nullable (NULL = chưa sync), engagement_synced_at = lần fetch gần nhất.
-- One SqlCommand, no GO.
IF COL_LENGTH('dbo.content_schedule', 'like_count') IS NULL
    ALTER TABLE dbo.content_schedule ADD like_count INT NULL;
IF COL_LENGTH('dbo.content_schedule', 'comment_count') IS NULL
    ALTER TABLE dbo.content_schedule ADD comment_count INT NULL;
IF COL_LENGTH('dbo.content_schedule', 'engagement_synced_at') IS NULL
    ALTER TABLE dbo.content_schedule ADD engagement_synced_at DATETIMEOFFSET NULL;
