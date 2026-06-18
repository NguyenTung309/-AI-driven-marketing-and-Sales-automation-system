-- 0019: Research-2 — competitor monitoring. Admin-configured feeds (competitor_sources) scanned
-- by CompetitorScanJob; detected posts (competitor_posts) deduped by (source_id, content_hash).

IF OBJECT_ID(N'dbo.competitor_sources', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.competitor_sources (
        id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        tenant_id       UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.tenants(id) ON DELETE CASCADE,
        name            NVARCHAR(200) NOT NULL,
        url             NVARCHAR(1024) NOT NULL,
        source_type     NVARCHAR(16) NOT NULL DEFAULT 'rss',   -- rss|fanpage
        is_active       BIT NOT NULL DEFAULT 1,
        created_at      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        last_scanned_at DATETIMEOFFSET NULL,
        deleted_at      DATETIMEOFFSET NULL
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_competitor_sources_tenant' AND object_id = OBJECT_ID(N'dbo.competitor_sources'))
    CREATE INDEX ix_competitor_sources_tenant ON dbo.competitor_sources (tenant_id, is_active);

IF OBJECT_ID(N'dbo.competitor_posts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.competitor_posts (
        id              UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        tenant_id       UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.tenants(id) ON DELETE CASCADE,
        source_id       UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.competitor_sources(id) ON DELETE NO ACTION,
        url             NVARCHAR(1024) NOT NULL,
        title           NVARCHAR(512) NOT NULL,
        snippet         NVARCHAR(1024) NULL,
        published_at    DATETIMEOFFSET NOT NULL,
        detected_at     DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        content_hash    NVARCHAR(64) NOT NULL,
        CONSTRAINT uq_competitor_posts_source_hash UNIQUE (source_id, content_hash)
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_competitor_posts_tenant_detected' AND object_id = OBJECT_ID(N'dbo.competitor_posts'))
    CREATE INDEX ix_competitor_posts_tenant_detected ON dbo.competitor_posts (tenant_id, detected_at DESC);
