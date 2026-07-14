-- 0064: push_subscriptions — Web Push endpoint cua tung trinh duyet (dong tab van nhan duoc thong bao).
-- endpoint la URL do push service cap; p256dh/auth la khoa ma hoa cua client.
-- One SqlCommand, no GO.
IF OBJECT_ID('dbo.push_subscriptions', 'U') IS NULL
CREATE TABLE dbo.push_subscriptions (
    id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_push_subscriptions PRIMARY KEY,
    tenant_id UNIQUEIDENTIFIER NOT NULL,
    user_id UNIQUEIDENTIFIER NOT NULL,
    endpoint NVARCHAR(512) NOT NULL,
    p256dh NVARCHAR(256) NOT NULL,
    auth NVARCHAR(128) NOT NULL,
    created_at DATETIMEOFFSET NOT NULL
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_push_subscriptions_endpoint' AND object_id = OBJECT_ID('dbo.push_subscriptions'))
    CREATE UNIQUE INDEX UX_push_subscriptions_endpoint ON dbo.push_subscriptions (endpoint);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_push_subscriptions_tenant_user' AND object_id = OBJECT_ID('dbo.push_subscriptions'))
    CREATE INDEX IX_push_subscriptions_tenant_user ON dbo.push_subscriptions (tenant_id, user_id);
