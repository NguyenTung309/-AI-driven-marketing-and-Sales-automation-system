-- 0011: Notification center (M24)
-- Persists alerts (hot-lead, idle, anomaly, ads-budget, system) so they survive beyond
-- the ephemeral SignalR push and feed the FE Notification center.
-- user_id NULL = tenant-wide broadcast. (Per-user fan-out for broadcasts deferred until
-- the Identity↔DDL reconcile lands — enumerating users needs the user table.)
CREATE TABLE notifications (
    id          UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id   UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    user_id     UNIQUEIDENTIFIER,
    type        NVARCHAR(40)  NOT NULL,
    severity    NVARCHAR(10)  NOT NULL DEFAULT 'info',
    title       NVARCHAR(256) NOT NULL,
    body        NVARCHAR(MAX),
    link        NVARCHAR(256),
    is_read     BIT NOT NULL DEFAULT 0,
    read_at     DATETIMEOFFSET,
    created_at  DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
CREATE INDEX ix_notifications_tenant_user_read
    ON notifications (tenant_id, user_id, is_read, created_at DESC);
