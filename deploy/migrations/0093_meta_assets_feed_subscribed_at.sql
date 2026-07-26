-- 0093: mark when a Page feed webhook subscription last succeeded.
-- NULL means the subscription never went through (usually pages_manage_metadata is not granted),
-- so comments only arrive through the reconciliation job — surfaced in the admin Meta panel.
IF COL_LENGTH('dbo.meta_assets', 'feed_subscribed_at') IS NULL
    ALTER TABLE dbo.meta_assets ADD feed_subscribed_at DATETIMEOFFSET NULL;
