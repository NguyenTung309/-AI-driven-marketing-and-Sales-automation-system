-- 0026: Sale gui tay -> AI tam nhuong bao lau (phut) roi tu bat lai. Cau hinh per-tenant, mac dinh 5.
IF COL_LENGTH(N'dbo.tenants', N'ai_auto_reply_resume_minutes') IS NULL
    EXEC(N'ALTER TABLE tenants ADD ai_auto_reply_resume_minutes INT NOT NULL CONSTRAINT DF_tenants_ai_resume_minutes DEFAULT 5;');
