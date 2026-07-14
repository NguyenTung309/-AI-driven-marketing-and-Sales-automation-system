-- 0063: notification_preferences — user tu bat/tat tung loai thong bao (in_app / push / email).
-- Khong co dong = dung mac dinh trong code. severity=warning (job fail) LUON push, khong cho tat.
-- One SqlCommand, no GO.
IF OBJECT_ID('dbo.notification_preferences', 'U') IS NULL
CREATE TABLE dbo.notification_preferences (
    id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_notification_preferences PRIMARY KEY,
    tenant_id UNIQUEIDENTIFIER NOT NULL,
    user_id UNIQUEIDENTIFIER NOT NULL,
    type NVARCHAR(64) NOT NULL,
    in_app BIT NOT NULL CONSTRAINT DF_notification_preferences_in_app DEFAULT 1,
    push BIT NOT NULL CONSTRAINT DF_notification_preferences_push DEFAULT 1,
    email BIT NOT NULL CONSTRAINT DF_notification_preferences_email DEFAULT 0,
    updated_at DATETIMEOFFSET NOT NULL
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_notification_preferences' AND object_id = OBJECT_ID('dbo.notification_preferences'))
    CREATE UNIQUE INDEX UX_notification_preferences ON dbo.notification_preferences (tenant_id, user_id, type);
