-- 0025: AI auto-reply tam tat khi sale gui tay, hen tu bat lai (ai_auto_reply_resume_at).
-- Null = tat vinh vien (toggle tay/escalate); co gia tri = moc thoi gian AI tu bat lai o lan khach nhan tiep theo.

IF COL_LENGTH(N'dbo.conversations', N'ai_auto_reply_resume_at') IS NULL
    EXEC(N'ALTER TABLE conversations ADD ai_auto_reply_resume_at DATETIMEOFFSET NULL;');
