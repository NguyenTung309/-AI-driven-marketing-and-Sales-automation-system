IF OBJECT_ID(N'dbo.system_logs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.system_logs (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_system_logs PRIMARY KEY,
        occurred_at DATETIMEOFFSET NOT NULL,
        level NVARCHAR(16) NOT NULL,
        source NVARCHAR(32) NOT NULL,
        category NVARCHAR(256) NULL,
        message NVARCHAR(2048) NOT NULL,
        exception NVARCHAR(MAX) NULL,
        status_code INT NULL,
        method NVARCHAR(10) NULL,
        path NVARCHAR(512) NULL,
        elapsed_ms FLOAT NULL,
        trace_id NVARCHAR(64) NULL,
        tenant_id UNIQUEIDENTIFIER NULL,
        user_id UNIQUEIDENTIFIER NULL,
        properties NVARCHAR(MAX) NULL
    );

    CREATE INDEX ix_system_logs_occurred
        ON dbo.system_logs(occurred_at DESC)
        INCLUDE (level, tenant_id);

    CREATE INDEX ix_system_logs_tenant
        ON dbo.system_logs(tenant_id, occurred_at DESC);

    CREATE INDEX ix_system_logs_trace
        ON dbo.system_logs(trace_id)
        WHERE trace_id IS NOT NULL;
END
