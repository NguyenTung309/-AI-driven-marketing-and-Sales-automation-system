-- 0062: index cho duong gom nhom (cot them bang ALTER phai o file rieng — quy uoc repo).
-- One SqlCommand, no GO.
IF COL_LENGTH('dbo.notifications', 'group_key') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_notifications_group' AND object_id = OBJECT_ID('dbo.notifications'))
    CREATE INDEX IX_notifications_group ON dbo.notifications (tenant_id, user_id, group_key, is_read);
