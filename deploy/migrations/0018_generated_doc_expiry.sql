-- 0018: Docs-1 — generated quote/brochure download links expire after 7 days.
ALTER TABLE generated_documents ADD expires_at DATETIMEOFFSET NULL;
