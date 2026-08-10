-- Partition orchestration-created drafts by durable replan generation and preserve human takeover.
-- One SqlCommand; do not add GO.
IF COL_LENGTH(N'dbo.content_items', N'orchestration_plan_generation') IS NULL
    ALTER TABLE dbo.content_items ADD orchestration_plan_generation INT NULL;

IF COL_LENGTH(N'dbo.content_items', N'orchestration_ownership_claimed_at') IS NULL
    ALTER TABLE dbo.content_items ADD orchestration_ownership_claimed_at DATETIMEOFFSET NULL;

IF COL_LENGTH(N'dbo.content_items', N'orchestration_ownership_claimed_by') IS NULL
    ALTER TABLE dbo.content_items ADD orchestration_ownership_claimed_by UNIQUEIDENTIFIER NULL;

UPDATE dbo.content_items
SET orchestration_plan_generation = 0
WHERE orchestration_session_id IS NOT NULL
  AND orchestration_plan_generation IS NULL;

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CK_content_items_orchestration_provenance'
      AND parent_object_id = OBJECT_ID(N'dbo.content_items'))
BEGIN
    ALTER TABLE dbo.content_items WITH CHECK
        ADD CONSTRAINT CK_content_items_orchestration_provenance
        CHECK (
            (orchestration_session_id IS NULL AND orchestration_plan_generation IS NULL)
            OR (orchestration_session_id IS NOT NULL AND orchestration_plan_generation >= 0));
END

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CK_content_items_orchestration_ownership'
      AND parent_object_id = OBJECT_ID(N'dbo.content_items'))
BEGIN
    ALTER TABLE dbo.content_items WITH CHECK
        ADD CONSTRAINT CK_content_items_orchestration_ownership
        CHECK (
            orchestration_ownership_claimed_at IS NULL
            OR orchestration_session_id IS NOT NULL);
END

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_content_items_orchestration_session'
      AND parent_object_id = OBJECT_ID(N'dbo.content_items'))
BEGIN
    ALTER TABLE dbo.content_items WITH CHECK
        ADD CONSTRAINT FK_content_items_orchestration_session
        FOREIGN KEY (tenant_id, orchestration_session_id)
        REFERENCES dbo.agent_sessions (tenant_id, id)
        ON DELETE NO ACTION;
END
