-- Lead scoring rules seed (idempotent MERGE per tenant)
-- Default rules for ClawBot CRM — weights calibrated for Chinese-language tutoring center.

SET XACT_ABORT ON;
SET NOCOUNT ON;

DECLARE @tenant_slug NVARCHAR(64) = N'$(TenantSlug)';   -- <-- CHANGE to the target tenant slug
DECLARE @tenant_id UNIQUEIDENTIFIER = (SELECT id FROM tenants WHERE slug = @tenant_slug);

IF @tenant_id IS NULL
BEGIN
    RAISERROR(N'Tenant slug "%s" not found. Seed aborted.', 16, 1, @tenant_slug);
    RETURN;
END;

BEGIN TRANSACTION;

DECLARE @expected_rows INT = 16;

MERGE INTO lead_scoring_rules AS target
USING (VALUES
    -- Pricing intent
    ('asks_price',      NULL,       10, 'Customer asks about pricing or tuition fees'),
    ('asks_discount',   NULL,       15, 'Customer asks for discount or promotion'),
    -- Contact sharing
    ('shares_phone',    NULL,       20, 'Customer shares phone number'),
    ('shares_email',    NULL,       15, 'Customer shares email address'),
    -- Engagement depth
    ('books_trial',     NULL,       30, 'Customer books a trial class'),
    ('attends_trial',   NULL,       40, 'Customer attends trial class'),
    ('asks_schedule',   NULL,       10, 'Customer asks about class schedule'),
    ('asks_curriculum', NULL,       10, 'Customer asks about curriculum or course content'),
    -- Platform-specific
    ('sends_dm',        'facebook',  5, 'Customer sends DM on Facebook'),
    ('sends_dm',        'zalo',      5, 'Customer sends DM on Zalo'),
    ('sends_dm',        'tiktok',    3, 'Customer sends DM on TikTok'),
    ('comments_post',   NULL,        3, 'Customer comments on a post'),
    -- Negative signals
    ('unsubscribe',     NULL,      -20, 'Customer unsubscribes or asks to stop'),
    ('no_reply_7d',     NULL,       -5, 'No reply for 7 days'),
    -- Conversion
    ('sends_payment',   NULL,       50, 'Customer sends payment or proof of transfer'),
    ('confirms_enroll', NULL,       50, 'Customer confirms enrollment')
) AS source (event_code, platform, weight, description)
ON target.tenant_id = @tenant_id
    AND target.event_code = source.event_code
    AND (target.platform = source.platform OR (target.platform IS NULL AND source.platform IS NULL))
    AND target.is_active = 1
WHEN MATCHED THEN
    UPDATE SET
        weight = source.weight,
        description = source.description,
        updated_at = SYSDATETIMEOFFSET()
WHEN NOT MATCHED THEN
    INSERT (id, tenant_id, event_code, platform, weight, description, is_active, created_at, updated_at)
    VALUES (NEWID(), @tenant_id, source.event_code, source.platform, source.weight, source.description, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());

DECLARE @actual_rows INT;

SELECT @actual_rows = COUNT(*)
FROM lead_scoring_rules
WHERE tenant_id = @tenant_id
  AND is_active = 1
  AND (
      (event_code = N'sends_dm' AND platform IN (N'facebook', N'zalo', N'tiktok'))
      OR (
          platform IS NULL
          AND event_code IN (
              N'asks_price',
              N'asks_discount',
              N'shares_phone',
              N'shares_email',
              N'books_trial',
              N'attends_trial',
              N'asks_schedule',
              N'asks_curriculum',
              N'comments_post',
              N'unsubscribe',
              N'no_reply_7d',
              N'sends_payment',
              N'confirms_enroll'
          )
      )
  );

IF @actual_rows <> @expected_rows
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(N'Expected %d lead_scoring_rules rows for tenant "%s"; found %d. Seed aborted.', 16, 1, @expected_rows, @tenant_slug, @actual_rows);
    RETURN;
END;

COMMIT TRANSACTION;

PRINT N'lead_scoring_rules seed applied for tenant: ' + @tenant_slug;
