IF OBJECT_ID(N'dbo.embedding_configs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.embedding_configs (
        id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        tenant_id UNIQUEIDENTIFIER NOT NULL,
        provider NVARCHAR(32) NOT NULL,
        model_id NVARCHAR(128) NOT NULL,
        display_name NVARCHAR(128) NULL,
        api_key_encrypted NVARCHAR(MAX) NOT NULL,
        base_url NVARCHAR(512) NULL,
        dimension INT NOT NULL CONSTRAINT df_embedding_configs_dimension DEFAULT 1536,
        is_active BIT NOT NULL CONSTRAINT df_embedding_configs_is_active DEFAULT 1,
        created_at DATETIMEOFFSET NOT NULL,
        updated_at DATETIMEOFFSET NOT NULL,
        CONSTRAINT fk_embedding_configs_tenants FOREIGN KEY (tenant_id) REFERENCES dbo.tenants(id) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_embedding_configs_tenant_id_is_active' AND object_id = OBJECT_ID(N'dbo.embedding_configs'))
BEGIN
    CREATE INDEX IX_embedding_configs_tenant_id_is_active ON dbo.embedding_configs (tenant_id, is_active);
END;
