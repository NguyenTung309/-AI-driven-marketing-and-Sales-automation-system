-- 0037: pancake_pages — per-page Pancake credential map (SPEC-16 §5.1).
-- A tenant connects one Pancake user access token (users.pancake_access_token_encrypted, 0036) which mints a
-- page access token per page. Page ops run under pages.fm/api/public_api/v1 with the page_access_token.
-- Page tokens never expire; the user token (<=90d) is re-minted by the admin connect flow (Module M-4).

IF OBJECT_ID(N'dbo.pancake_pages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.pancake_pages (
        id                      UNIQUEIDENTIFIER NOT NULL,
        tenant_id               UNIQUEIDENTIFIER NOT NULL,
        page_id                 NVARCHAR(128)    NOT NULL,
        name                    NVARCHAR(256)    NOT NULL,
        platform                NVARCHAR(64)     NOT NULL,
        page_access_token_encrypted NVARCHAR(2048) NOT NULL,
        page_token_minted_at    DATETIMEOFFSET   NULL,
        is_active               BIT              NOT NULL CONSTRAINT DF_pancake_pages_is_active DEFAULT 1,
        created_at              DATETIMEOFFSET   NOT NULL,
        updated_at              DATETIMEOFFSET   NOT NULL,
        deleted_at              DATETIMEOFFSET   NULL,
        CONSTRAINT PK_pancake_pages PRIMARY KEY (id),
        CONSTRAINT UQ_pancake_pages_tenant_page UNIQUE (tenant_id, page_id)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_pancake_pages_tenant_active')
    CREATE INDEX IX_pancake_pages_tenant_active ON dbo.pancake_pages (tenant_id, is_active) INCLUDE (page_id);
