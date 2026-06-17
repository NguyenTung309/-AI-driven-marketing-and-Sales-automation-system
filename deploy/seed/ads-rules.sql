-- Default ads rules per platform (idempotent MERGE)
-- CPL multipliers: 1.5× target = pause, 0.7× target = scale up
-- Absolute: frequency > 2 → rotate, CTR < 0.8% → pause, spend > 90% → alert

SET XACT_ABORT ON;
SET NOCOUNT ON;

DECLARE @tenant_slug NVARCHAR(64) = N'demo';   -- <-- CHANGE to the target tenant slug
DECLARE @tenant_id UNIQUEIDENTIFIER = (SELECT TOP 1 id FROM tenants WHERE slug = @tenant_slug);

IF @tenant_id IS NULL
BEGIN
    RAISERROR(N'Tenant slug "%s" not found. Seed aborted.', 16, 1, @tenant_slug);
    RETURN;
END;

BEGIN TRANSACTION;

DECLARE @expected_rows INT = 10;

-- Meta rules
MERGE ads_rules AS t
USING (VALUES
    (@tenant_id, 'meta', 'cpl',        'gt',  1.5,    'pause'),
    (@tenant_id, 'meta', 'cpl',        'lt',  0.7,    'scale_up'),
    (@tenant_id, 'meta', 'frequency',  'gt',  2.0,    'rotate'),
    (@tenant_id, 'meta', 'ctr',        'lt',  0.8,    'pause'),
    (@tenant_id, 'meta', 'spend',      'gt',  0.9,    'alert')
) AS s (tenant_id, platform, metric, comparator, threshold, action)
ON t.tenant_id = s.tenant_id AND t.platform = s.platform AND t.metric = s.metric AND t.comparator = s.comparator
WHEN MATCHED THEN
    UPDATE SET threshold = s.threshold, action = s.action, is_active = 1, updated_at = SYSDATETIMEOFFSET()
WHEN NOT MATCHED THEN
    INSERT (id, tenant_id, platform, metric, comparator, threshold, action, is_active, created_at, updated_at)
    VALUES (NEWID(), s.tenant_id, s.platform, s.metric, s.comparator, s.threshold, s.action, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());

-- TikTok rules
MERGE ads_rules AS t
USING (VALUES
    (@tenant_id, 'tiktok', 'cpl',        'gt',  1.5,    'pause'),
    (@tenant_id, 'tiktok', 'cpl',        'lt',  0.7,    'scale_up'),
    (@tenant_id, 'tiktok', 'frequency',  'gt',  2.0,    'rotate'),
    (@tenant_id, 'tiktok', 'ctr',        'lt',  0.8,    'pause'),
    (@tenant_id, 'tiktok', 'spend',      'gt',  0.9,    'alert')
) AS s (tenant_id, platform, metric, comparator, threshold, action)
ON t.tenant_id = s.tenant_id AND t.platform = s.platform AND t.metric = s.metric AND t.comparator = s.comparator
WHEN MATCHED THEN
    UPDATE SET threshold = s.threshold, action = s.action, is_active = 1, updated_at = SYSDATETIMEOFFSET()
WHEN NOT MATCHED THEN
    INSERT (id, tenant_id, platform, metric, comparator, threshold, action, is_active, created_at, updated_at)
    VALUES (NEWID(), s.tenant_id, s.platform, s.metric, s.comparator, s.threshold, s.action, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());

DECLARE @actual_rows INT;

SELECT @actual_rows = COUNT(*)
FROM ads_rules
WHERE tenant_id = @tenant_id
  AND is_active = 1
  AND platform IN (N'meta', N'tiktok')
  AND (
      (metric = N'cpl' AND comparator IN (N'gt', N'lt'))
      OR (metric = N'frequency' AND comparator = N'gt')
      OR (metric = N'ctr' AND comparator = N'lt')
      OR (metric = N'spend' AND comparator = N'gt')
  );

IF @actual_rows <> @expected_rows
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(N'Expected %d ads_rules rows for tenant "%s"; found %d. Seed aborted.', 16, 1, @expected_rows, @tenant_slug, @actual_rows);
    RETURN;
END;

COMMIT TRANSACTION;

PRINT N'ads_rules seed applied for tenant: ' + @tenant_slug;
