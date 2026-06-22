-- 0029: Tenant orchestration approval toggle
-- Default false means orchestration plans auto-run unless a tenant opts into approval gating.

IF COL_LENGTH(N'dbo.tenants', N'require_orchestration_approval') IS NULL
    EXEC(N'ALTER TABLE dbo.tenants ADD require_orchestration_approval BIT NOT NULL CONSTRAINT DF_tenants_require_orchestration_approval DEFAULT 0;');
