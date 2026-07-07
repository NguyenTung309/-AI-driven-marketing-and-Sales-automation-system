-- 0047: per-tenant LLM spend cap. NULL = dùng mặc định hệ thống ($200/tháng).
IF COL_LENGTH('dbo.tenants', 'monthly_cost_cap_usd') IS NULL
BEGIN
    ALTER TABLE dbo.tenants ADD monthly_cost_cap_usd DECIMAL(12, 2) NULL;
END
