SET QUOTED_IDENTIFIER ON;
SET ARITHABORT ON;

-- Review-gate P1 backfill (QĐ1: default OFF + backfill + opt-in): stamp existing approved/scheduled
-- content items with the tenant's reviewer-agent definition id so flipping require_content_review ON
-- does not halt items that were human-approved before the gate existed. One-shot via data_patches.

IF OBJECT_ID(N'dbo.data_patches', N'U') IS NULL
    CREATE TABLE dbo.data_patches (
        patch_id NVARCHAR(64) NOT NULL CONSTRAINT PK_data_patches PRIMARY KEY,
        applied_at DATETIMEOFFSET NOT NULL CONSTRAINT DF_data_patches_applied_at DEFAULT SYSUTCDATETIME()
    );

IF NOT EXISTS (SELECT 1 FROM dbo.data_patches WHERE patch_id = N'2026-07-10-backfill-content-agent-review')
BEGIN
    IF COL_LENGTH(N'dbo.content_items', N'approved_by_agent_id') IS NULL
        OR OBJECT_ID(N'dbo.agent_definitions', N'U') IS NULL
    BEGIN
        PRINT 'content_items.approved_by_agent_id / agent_definitions missing - patch skipped, retries next run.';
        RETURN;
    END

    DECLARE @stamped INT = 0;

    UPDATE ci
    SET approved_by_agent_id = ad.id
    FROM dbo.content_items ci
    INNER JOIN dbo.agent_definitions ad
        ON ad.tenant_id = ci.tenant_id AND ad.code = N'reviewer-agent' AND ad.deleted_at IS NULL
    WHERE ci.status IN (N'approved', N'scheduled', N'published')
      AND ci.approved_by_agent_id IS NULL
      AND ci.deleted_at IS NULL;
    SET @stamped = @@ROWCOUNT;

    INSERT INTO dbo.data_patches (patch_id) VALUES (N'2026-07-10-backfill-content-agent-review');
    PRINT CONCAT('backfill-content-agent-review applied: ', @stamped, ' items stamped.');
END
ELSE
    PRINT 'backfill-content-agent-review already applied - skipped.';
