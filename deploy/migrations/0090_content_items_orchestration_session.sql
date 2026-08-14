-- Store the orchestration session that generated a content draft so replan/failure cleanup is tenant-safe.
-- One SqlCommand; do not add GO.
IF COL_LENGTH(N'dbo.content_items', N'orchestration_session_id') IS NULL
    ALTER TABLE dbo.content_items ADD orchestration_session_id UNIQUEIDENTIFIER NULL;
