-- Ads campaign target CPL + dayparting support
-- Adds target_cpl for relative threshold evaluation and daypart_paused for quiet-hour tracking.

IF COL_LENGTH('dbo.ads_campaigns', 'target_cpl') IS NULL
BEGIN
    ALTER TABLE dbo.ads_campaigns
        ADD target_cpl DECIMAL(12,2) NULL;
END

IF COL_LENGTH('dbo.ads_campaigns', 'daypart_paused') IS NULL
BEGIN
    ALTER TABLE dbo.ads_campaigns
        ADD daypart_paused BIT NOT NULL DEFAULT 0;
END
