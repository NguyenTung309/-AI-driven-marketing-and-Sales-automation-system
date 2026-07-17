-- 0068: Nguong canh bao hoi thoai cho (phut) per-tenant, mac dinh 5. Escalate = 2x nguong.
IF COL_LENGTH(N'dbo.tenants', N'idle_alert_minutes') IS NULL
    EXEC(N'ALTER TABLE tenants ADD idle_alert_minutes INT NOT NULL CONSTRAINT DF_tenants_idle_alert_minutes DEFAULT 5;');
