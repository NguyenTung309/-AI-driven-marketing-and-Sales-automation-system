-- Ads creative inventory for rotation (freq>2 fatigue mitigation)
-- Each campaign should have ≥3 creatives (1 active, rest standby).

CREATE TABLE ads_creatives (
    id                    UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id             UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    campaign_id           UNIQUEIDENTIFIER NOT NULL REFERENCES ads_campaigns(id) ON DELETE NO ACTION,
    external_creative_id  NVARCHAR(128) NOT NULL,
    status                NVARCHAR(16) NOT NULL DEFAULT 'active',  -- active|standby
    created_at            DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    updated_at            DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
CREATE INDEX ix_ads_creatives_campaign ON ads_creatives (campaign_id, status);
