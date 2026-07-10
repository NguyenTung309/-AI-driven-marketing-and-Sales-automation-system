-- 0050: Review-gate P1 — tenant flag + content review columns.
-- tenants.require_content_review: khi bật, publish/schedule đòi chữ ký reviewer agent (approved_by_agent_id).
-- content_items.created_by_agent_id: agent sinh item (reviewer-independence check).
-- content_items.rejected_reason: lý do reject từ reviewer/human (G10).
-- One SqlCommand, no GO (run-all replays each file as a single command).
IF COL_LENGTH('dbo.tenants', 'require_content_review') IS NULL
    ALTER TABLE dbo.tenants ADD require_content_review BIT NOT NULL CONSTRAINT DF_tenants_require_content_review DEFAULT 0;
IF COL_LENGTH('dbo.content_items', 'created_by_agent_id') IS NULL
    ALTER TABLE dbo.content_items ADD created_by_agent_id UNIQUEIDENTIFIER NULL;
IF COL_LENGTH('dbo.content_items', 'rejected_reason') IS NULL
    ALTER TABLE dbo.content_items ADD rejected_reason NVARCHAR(1024) NULL;
