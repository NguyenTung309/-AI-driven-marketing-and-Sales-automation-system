-- 0052: Review-gate P3 manual-mode — tenants.require_chat_reply_approval: khi bật, mọi AI reply
-- hold thành messages.status='pending_approval' chờ người duyệt (tin sale gõ tay miễn, QĐ5).
-- One SqlCommand, no GO.
IF COL_LENGTH('dbo.tenants', 'require_chat_reply_approval') IS NULL
    ALTER TABLE dbo.tenants ADD require_chat_reply_approval BIT NOT NULL CONSTRAINT DF_tenants_require_chat_reply_approval DEFAULT 0;
