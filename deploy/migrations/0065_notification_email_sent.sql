-- 0065: email fallback — job_failed chua doc sau 30 phut thi gui email; cot nay chan gui lap.
-- One SqlCommand, no GO.
IF COL_LENGTH('dbo.notifications', 'email_sent_at') IS NULL
    ALTER TABLE dbo.notifications ADD email_sent_at DATETIMEOFFSET NULL;
