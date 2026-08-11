-- 0095: bảng lưu kết quả báo cáo do report-agent sinh ra, để có link mở lại được.
-- Output của orchestrator trước đây chỉ là text: số liệu nằm lẫn trong narrative, không có ID nào
-- để mở lại hay xuất file. Mỗi lần chạy report-agent giờ chốt một artifact bất biến ở đây.
-- Index khai báo inline để cả file vẫn là một câu lệnh (no GO).
IF OBJECT_ID(N'dbo.report_artifacts', N'U') IS NULL
CREATE TABLE dbo.report_artifacts (
    id UNIQUEIDENTIFIER NOT NULL CONSTRAINT pk_report_artifacts PRIMARY KEY,
    tenant_id UNIQUEIDENTIFIER NOT NULL,
    kind NVARCHAR(32) NOT NULL,
    title NVARCHAR(256) NOT NULL,
    platform NVARCHAR(32) NOT NULL,
    metric NVARCHAR(64) NULL,
    from_date DATE NOT NULL,
    to_date DATE NOT NULL,
    data_json NVARCHAR(MAX) NOT NULL,
    created_at DATETIMEOFFSET NOT NULL,
    INDEX ix_report_artifacts_tenant_created NONCLUSTERED (tenant_id, created_at DESC)
);
