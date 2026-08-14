-- Chính sách xử lý task orchestration lỗi theo tenant. One SqlCommand, no GO. Safe to re-run.
-- 'pause'  = dừng phiên chờ người sửa output bước lỗi rồi chạy tiếp (mặc định, không tốn thêm LLM)
-- 'replan' = hành vi cũ: orchestrator sinh plan mới và chạy lại toàn bộ từ đầu
-- 'fail'   = dừng hẳn phiên

IF OBJECT_ID(N'dbo.tenants', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.tenants', N'orchestrator_failure_policy') IS NULL
        ALTER TABLE dbo.tenants ADD orchestrator_failure_policy NVARCHAR(20) NOT NULL
            CONSTRAINT DF_tenants_orchestrator_failure_policy DEFAULT N'pause';
END
