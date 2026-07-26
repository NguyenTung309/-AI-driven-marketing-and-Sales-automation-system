-- 0089: persist the Meta comment reconciliation attempt watermark independently from engagement counts.
-- This prevents the oldest 100 schedules from starving newer posts when a Graph page is unavailable.
-- One SqlCommand, no GO.
IF COL_LENGTH('dbo.content_schedule', 'meta_comments_synced_at') IS NULL
    ALTER TABLE dbo.content_schedule ADD meta_comments_synced_at DATETIMEOFFSET NULL;
