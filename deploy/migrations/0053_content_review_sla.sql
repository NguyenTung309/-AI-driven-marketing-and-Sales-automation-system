-- 0053: Review-gate P4 (SLA) — content_items.desired_publish_at (deadline mong muốn, set lúc lên lịch
-- kể cả khi review-gate chặn) + last_review_alert_at (chống spam alert, mỗi tier notify 1 lần).
-- One SqlCommand, no GO.
IF COL_LENGTH('dbo.content_items', 'desired_publish_at') IS NULL
    ALTER TABLE dbo.content_items ADD desired_publish_at DATETIMEOFFSET NULL;
IF COL_LENGTH('dbo.content_items', 'last_review_alert_at') IS NULL
    ALTER TABLE dbo.content_items ADD last_review_alert_at DATETIMEOFFSET NULL;
