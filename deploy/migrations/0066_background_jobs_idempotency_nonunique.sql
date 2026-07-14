-- 0066: idempotency_key KHONG con unique.
-- Ly do: chi tai dung job dang chay/cho (job cu da xong thi phai cho chay lai) — giu unique thi
-- lan chay thu 2 cung key se vi pham rang buoc. Index van giu de tra cuu nhanh.
-- One SqlCommand, no GO.
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_background_jobs_idempotency' AND object_id = OBJECT_ID('dbo.background_jobs'))
    DROP INDEX UX_background_jobs_idempotency ON dbo.background_jobs;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_background_jobs_idempotency' AND object_id = OBJECT_ID('dbo.background_jobs'))
    CREATE INDEX IX_background_jobs_idempotency ON dbo.background_jobs (tenant_id, idempotency_key, status) WHERE idempotency_key IS NOT NULL;
