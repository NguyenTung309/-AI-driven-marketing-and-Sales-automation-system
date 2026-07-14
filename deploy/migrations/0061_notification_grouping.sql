-- 0061: gom nhom thong bao kieu Facebook — 5 su kien cung nhom = 1 dong "count=5", khong phai 5 dong.
-- group_key NULL = thong bao le nhu cu (khong breaking).
-- One SqlCommand, no GO.
IF COL_LENGTH('dbo.notifications', 'group_key') IS NULL
    ALTER TABLE dbo.notifications ADD group_key NVARCHAR(128) NULL;
IF COL_LENGTH('dbo.notifications', 'occurrence_count') IS NULL
    ALTER TABLE dbo.notifications ADD occurrence_count INT NOT NULL CONSTRAINT DF_notifications_occurrence_count DEFAULT 1;
IF COL_LENGTH('dbo.notifications', 'last_occurred_at') IS NULL
    ALTER TABLE dbo.notifications ADD last_occurred_at DATETIMEOFFSET NULL;
