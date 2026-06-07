-- Ads campaign target CPL + dayparting support
-- Adds target_cpl for relative threshold evaluation and daypart_paused for quiet-hour tracking.

ALTER TABLE ads_campaigns
    ADD target_cpl DECIMAL(12,2) NULL;

ALTER TABLE ads_campaigns
    ADD daypart_paused BIT NOT NULL DEFAULT 0;
