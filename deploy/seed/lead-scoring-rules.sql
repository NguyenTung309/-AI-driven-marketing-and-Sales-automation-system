-- Lead scoring rules seed (idempotent MERGE per tenant)
-- Default rules for ClawBot CRM — weights calibrated for Chinese-language tutoring center.

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
ON target.event_code = source.event_code
    AND (target.platform = source.platform OR (target.platform IS NULL AND source.platform IS NULL))
    AND target.is_active = 1
WHEN NOT MATCHED THEN
    INSERT (id, tenant_id, event_code, platform, weight, description, is_active, created_at, updated_at)
    VALUES (NEWID(), @tenant_id, source.event_code, source.platform, source.weight, source.description, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
