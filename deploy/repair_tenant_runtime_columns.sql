-- Runtime repair for existing local DBs that skip full migration replay.
-- One SqlCommand, no GO. Idempotent via COL_LENGTH guards.
SET QUOTED_IDENTIFIER ON;
SET ARITHABORT ON;

IF OBJECT_ID(N'dbo.tenants', N'U') IS NULL
BEGIN
    RAISERROR(N'dbo.tenants is missing; cannot repair tenant runtime columns.', 16, 1);
    RETURN;
END;

IF COL_LENGTH(N'dbo.tenants', N'monthly_cost_cap_usd') IS NULL
    ALTER TABLE dbo.tenants ADD monthly_cost_cap_usd DECIMAL(12, 2) NULL;

IF COL_LENGTH(N'dbo.tenants', N'require_content_review') IS NULL
    ALTER TABLE dbo.tenants ADD require_content_review BIT NOT NULL
        CONSTRAINT DF_tenants_require_content_review DEFAULT 0;

IF COL_LENGTH(N'dbo.tenants', N'require_chat_reply_approval') IS NULL
    ALTER TABLE dbo.tenants ADD require_chat_reply_approval BIT NOT NULL
        CONSTRAINT DF_tenants_require_chat_reply_approval DEFAULT 0;

IF COL_LENGTH(N'dbo.tenants', N'require_kb_human_review') IS NULL
    ALTER TABLE dbo.tenants ADD require_kb_human_review BIT NOT NULL
        CONSTRAINT DF_tenants_require_kb_human_review DEFAULT 0;

IF COL_LENGTH(N'dbo.tenants', N'ai_auto_reply_resume_minutes') IS NULL
    ALTER TABLE dbo.tenants ADD ai_auto_reply_resume_minutes INT NOT NULL
        CONSTRAINT DF_tenants_ai_resume_minutes DEFAULT 5;

IF COL_LENGTH(N'dbo.tenants', N'skip_chat_reply_review') IS NULL
    ALTER TABLE dbo.tenants ADD skip_chat_reply_review BIT NOT NULL
        CONSTRAINT DF_tenants_skip_chat_reply_review DEFAULT 0;
