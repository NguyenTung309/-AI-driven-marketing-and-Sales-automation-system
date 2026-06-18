-- 0018: Docs-1 — generated quote/brochure download links expire after 7 days.
IF COL_LENGTH('dbo.generated_documents', 'expires_at') IS NULL
BEGIN
    ALTER TABLE dbo.generated_documents ADD expires_at DATETIMEOFFSET NULL;
END
