-- 0056: AI tu hoc — bang staging kb_suggestions + tenant flag require_kb_human_review.
-- kb_suggestions: de xuat tri thuc do job chung cat sinh; KHONG dung kb_versions cho toi khi qua gate
-- (nguoi duyet, hoac auto khi verdict approve + accuracy khong giam). Noi dung da PII-redact.
-- tenants.require_kb_human_review: default 0 = AI tu duyet khi rail dat; bat = moi de xuat cho nguoi.
-- One SqlCommand, no GO (run-all replays each file as a single command).
IF OBJECT_ID('dbo.kb_suggestions', 'U') IS NULL
CREATE TABLE dbo.kb_suggestions (
    id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_kb_suggestions PRIMARY KEY,
    tenant_id UNIQUEIDENTIFIER NOT NULL,
    op NVARCHAR(16) NOT NULL,
    target_kb_module_id UNIQUEIDENTIFIER NULL,
    title NVARCHAR(256) NOT NULL,
    content_md NVARCHAR(MAX) NOT NULL,
    rationale NVARCHAR(MAX) NULL,
    evidence_json NVARCHAR(MAX) NULL,
    dedup_hash NVARCHAR(64) NOT NULL,
    reviewer_verdict NVARCHAR(16) NULL,
    reviewer_notes NVARCHAR(MAX) NULL,
    accuracy_before DECIMAL(5,2) NULL,
    accuracy_after DECIMAL(5,2) NULL,
    status NVARCHAR(16) NOT NULL CONSTRAINT DF_kb_suggestions_status DEFAULT 'pending',
    approval_mode NVARCHAR(8) NULL,
    rejected_reason NVARCHAR(1024) NULL,
    decided_by UNIQUEIDENTIFIER NULL,
    created_at DATETIMEOFFSET NOT NULL,
    decided_at DATETIMEOFFSET NULL,
    CONSTRAINT UQ_kb_suggestions_tenant_dedup UNIQUE (tenant_id, dedup_hash)
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_kb_suggestions_tenant_id_status' AND object_id = OBJECT_ID('dbo.kb_suggestions'))
    CREATE INDEX IX_kb_suggestions_tenant_id_status ON dbo.kb_suggestions (tenant_id, status);
IF COL_LENGTH('dbo.tenants', 'require_kb_human_review') IS NULL
    ALTER TABLE dbo.tenants ADD require_kb_human_review BIT NOT NULL CONSTRAINT DF_tenants_require_kb_human_review DEFAULT 0;
