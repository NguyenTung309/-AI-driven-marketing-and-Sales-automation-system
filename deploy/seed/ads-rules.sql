-- Default ads rules per platform (idempotent MERGE)
-- CPL multipliers: 1.5× target = pause, 0.7× target = scale up
-- Absolute: frequency > 2 → rotate, CTR < 0.8% → pause, spend > 90% → alert

DECLARE @tenant_id UNIQUEIDENTIFIER = (SELECT TOP 1 id FROM tenants WHERE slug = 'default');
IF @tenant_id IS NOT NULL
BEGIN
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
    WHEN NOT MATCHED THEN
        INSERT (id, tenant_id, platform, metric, comparator, threshold, action, is_active, created_at, updated_at)
        VALUES (NEWID(), s.tenant_id, s.platform, s.metric, s.comparator, s.threshold, s.action, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
END
ELSE
    PRINT 'WARNING: No tenant with slug=default found. Ads rules not seeded.';
