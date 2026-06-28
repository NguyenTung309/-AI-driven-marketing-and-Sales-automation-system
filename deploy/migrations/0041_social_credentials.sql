-- 0041: social_credentials — encrypted storage for FB/Zalo channel credentials (SPEC-16 Module M-1).
-- One row per (tenant, provider, page_id). The credential payload is a single encrypted JSON blob
-- (IEncryptor); plaintext never persists. Replaces options-based GraphPublisherOptions creds for prod.

IF OBJECT_ID(N'dbo.social_credentials', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.social_credentials (
        id                    UNIQUEIDENTIFIER NOT NULL,
        tenant_id             UNIQUEIDENTIFIER NOT NULL,
        provider              NVARCHAR(32)     NOT NULL,
        page_id               NVARCHAR(128)    NULL,
        credentials_encrypted NVARCHAR(MAX)    NOT NULL,
        is_active             BIT              NOT NULL CONSTRAINT DF_social_credentials_is_active DEFAULT 1,
        created_at            DATETIMEOFFSET   NOT NULL,
        updated_at            DATETIMEOFFSET   NOT NULL,
        deleted_at            DATETIMEOFFSET   NULL,
        CONSTRAINT PK_social_credentials PRIMARY KEY (id),
        CONSTRAINT UQ_social_credentials_tenant_provider_page UNIQUE (tenant_id, provider, page_id)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_social_credentials_tenant_provider_active')
    CREATE INDEX IX_social_credentials_tenant_provider_active ON dbo.social_credentials (tenant_id, provider, is_active);
