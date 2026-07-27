IF OBJECT_ID(N'dbo.request_stats_hourly', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.request_stats_hourly (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_request_stats_hourly PRIMARY KEY,
        bucket_hour DATETIMEOFFSET NOT NULL,
        tenant_id UNIQUEIDENTIFIER NOT NULL,
        status_class NVARCHAR(8) NOT NULL,
        count BIGINT NOT NULL CONSTRAINT df_request_stats_hourly_count DEFAULT 0
    );

    CREATE UNIQUE INDEX ux_request_stats_hourly_bucket_tenant_class
        ON dbo.request_stats_hourly(bucket_hour, tenant_id, status_class);

    CREATE INDEX ix_request_stats_hourly_tenant_bucket
        ON dbo.request_stats_hourly(tenant_id, bucket_hour DESC);
END
