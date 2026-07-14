-- 0060: background_jobs — run record chung cho moi tac vu AI chay ngam (P0 plan 2026-07-13).
-- Moi job do user kich: enqueue Hangfire -> row nay -> JobRunner notify khi xong/loi -> user click vao xem.
-- One SqlCommand, no GO.
IF OBJECT_ID('dbo.background_jobs', 'U') IS NULL
CREATE TABLE dbo.background_jobs (
    id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_background_jobs PRIMARY KEY,
    tenant_id UNIQUEIDENTIFIER NOT NULL,
    user_id UNIQUEIDENTIFIER NULL,
    type NVARCHAR(64) NOT NULL,
    title NVARCHAR(200) NOT NULL,
    status NVARCHAR(20) NOT NULL CONSTRAINT DF_background_jobs_status DEFAULT 'queued',
    progress INT NOT NULL CONSTRAINT DF_background_jobs_progress DEFAULT 0,
    progress_note NVARCHAR(200) NULL,
    payload_json NVARCHAR(MAX) NULL,
    result_link NVARCHAR(400) NULL,
    result_summary NVARCHAR(MAX) NULL,
    error NVARCHAR(1000) NULL,
    hangfire_job_id NVARCHAR(64) NULL,
    idempotency_key NVARCHAR(128) NULL,
    cancel_requested BIT NOT NULL CONSTRAINT DF_background_jobs_cancel_requested DEFAULT 0,
    created_at DATETIMEOFFSET NOT NULL,
    started_at DATETIMEOFFSET NULL,
    finished_at DATETIMEOFFSET NULL
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_background_jobs_tenant_created' AND object_id = OBJECT_ID('dbo.background_jobs'))
    CREATE INDEX IX_background_jobs_tenant_created ON dbo.background_jobs (tenant_id, created_at DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_background_jobs_tenant_user_created' AND object_id = OBJECT_ID('dbo.background_jobs'))
    CREATE INDEX IX_background_jobs_tenant_user_created ON dbo.background_jobs (tenant_id, user_id, created_at DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_background_jobs_idempotency' AND object_id = OBJECT_ID('dbo.background_jobs'))
    CREATE INDEX IX_background_jobs_idempotency ON dbo.background_jobs (tenant_id, idempotency_key, status) WHERE idempotency_key IS NOT NULL;
