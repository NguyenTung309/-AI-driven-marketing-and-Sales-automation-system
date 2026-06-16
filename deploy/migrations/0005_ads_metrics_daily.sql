-- Daily metric snapshots per campaign for 3-day CPL streak evaluation + scaling cooldown.

CREATE TABLE ads_metrics_daily (
    id            UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id     UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    campaign_id   UNIQUEIDENTIFIER NOT NULL REFERENCES ads_campaigns(id) ON DELETE NO ACTION,
    metric_date   DATE NOT NULL,
    cpl           DECIMAL(12,2),
    frequency     DECIMAL(12,4),
    ctr           DECIMAL(12,4),
    spend         DECIMAL(12,2),
    created_at    DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    UNIQUE (campaign_id, metric_date)
);
CREATE INDEX ix_ads_metrics_daily_campaign ON ads_metrics_daily (campaign_id, metric_date DESC);
