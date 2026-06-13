-- 0019: Research-2 — competitor monitoring. Admin-configured feeds (competitor_sources) scanned
-- by CompetitorScanJob; detected posts (competitor_posts) deduped by (source_id, content_hash).

CREATE TABLE competitor_sources (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id       UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    name            NVARCHAR(200) NOT NULL,
    url             NVARCHAR(1024) NOT NULL,
    source_type     NVARCHAR(16) NOT NULL DEFAULT 'rss',   -- rss|fanpage
    is_active       BIT NOT NULL DEFAULT 1,
    created_at      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    last_scanned_at DATETIMEOFFSET NULL,
    deleted_at      DATETIMEOFFSET NULL
);
CREATE INDEX ix_competitor_sources_tenant ON competitor_sources (tenant_id, is_active);

CREATE TABLE competitor_posts (
    id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    tenant_id       UNIQUEIDENTIFIER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    source_id       UNIQUEIDENTIFIER NOT NULL REFERENCES competitor_sources(id) ON DELETE CASCADE,
    url             NVARCHAR(1024) NOT NULL,
    title           NVARCHAR(512) NOT NULL,
    snippet         NVARCHAR(1024) NULL,
    published_at    DATETIMEOFFSET NOT NULL,
    detected_at     DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    content_hash    NVARCHAR(64) NOT NULL,
    CONSTRAINT uq_competitor_posts_source_hash UNIQUE (source_id, content_hash)
);
CREATE INDEX ix_competitor_posts_tenant_detected ON competitor_posts (tenant_id, detected_at DESC);
