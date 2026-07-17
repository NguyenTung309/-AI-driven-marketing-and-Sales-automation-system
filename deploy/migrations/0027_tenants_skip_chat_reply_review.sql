-- 0027: Bypass review-gate P2 cho AI chat reply (QĐ user 2026-07-16). Default 0 = fail-closed nhu cu.
IF COL_LENGTH(N'dbo.tenants', N'skip_chat_reply_review') IS NULL
    EXEC(N'ALTER TABLE tenants ADD skip_chat_reply_review BIT NOT NULL CONSTRAINT DF_tenants_skip_chat_reply_review DEFAULT 0;');
