-- 0055: Meta Facebook Login for Business connections and per-Page assets.

IF OBJECT_ID(N'dbo.meta_connections', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.meta_connections (
        id                       UNIQUEIDENTIFIER NOT NULL,
        tenant_id                UNIQUEIDENTIFIER NOT NULL,
        client_business_id       NVARCHAR(128) NOT NULL,
        system_user_id           NVARCHAR(128) NOT NULL,
        token_type               NVARCHAR(64) NOT NULL,
        access_token_encrypted   NVARCHAR(MAX) NOT NULL,
        granted_scopes_json      NVARCHAR(MAX) NOT NULL,
        expires_at               DATETIMEOFFSET NULL,
        data_access_expires_at   DATETIMEOFFSET NULL,
        last_validated_at        DATETIMEOFFSET NULL,
        status                   NVARCHAR(32) NOT NULL,
        last_error               NVARCHAR(1024) NULL,
        created_at               DATETIMEOFFSET NOT NULL,
        updated_at               DATETIMEOFFSET NOT NULL,
        CONSTRAINT PK_meta_connections PRIMARY KEY (id),
        CONSTRAINT FK_meta_connections_tenants FOREIGN KEY (tenant_id) REFERENCES dbo.tenants(id),
        CONSTRAINT UQ_meta_connections_tenant UNIQUE (tenant_id)
    );
END;

IF OBJECT_ID(N'dbo.meta_assets', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.meta_assets (
        id                       UNIQUEIDENTIFIER NOT NULL,
        tenant_id                UNIQUEIDENTIFIER NOT NULL,
        connection_id            UNIQUEIDENTIFIER NOT NULL,
        asset_type               NVARCHAR(32) NOT NULL,
        external_id              NVARCHAR(128) NOT NULL,
        name                     NVARCHAR(256) NOT NULL,
        tasks_json               NVARCHAR(MAX) NOT NULL,
        access_token_encrypted   NVARCHAR(MAX) NOT NULL,
        is_default               BIT NOT NULL CONSTRAINT DF_meta_assets_is_default DEFAULT 0,
        is_active                BIT NOT NULL CONSTRAINT DF_meta_assets_is_active DEFAULT 1,
        last_synced_at           DATETIMEOFFSET NOT NULL,
        created_at               DATETIMEOFFSET NOT NULL,
        updated_at               DATETIMEOFFSET NOT NULL,
        CONSTRAINT PK_meta_assets PRIMARY KEY (id),
        CONSTRAINT FK_meta_assets_tenants FOREIGN KEY (tenant_id) REFERENCES dbo.tenants(id),
        CONSTRAINT FK_meta_assets_connections FOREIGN KEY (connection_id) REFERENCES dbo.meta_connections(id) ON DELETE CASCADE,
        CONSTRAINT UQ_meta_assets_tenant_type_external UNIQUE (tenant_id, asset_type, external_id)
    );

    CREATE INDEX IX_meta_assets_connection_id ON dbo.meta_assets(connection_id);
END;

IF OBJECT_ID(N'dbo.meta_oauth_states', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.meta_oauth_states (
        id            UNIQUEIDENTIFIER NOT NULL,
        tenant_id     UNIQUEIDENTIFIER NOT NULL,
        user_id       UNIQUEIDENTIFIER NOT NULL,
        state_hash    NVARCHAR(64) NOT NULL,
        expires_at    DATETIMEOFFSET NOT NULL,
        consumed_at   DATETIMEOFFSET NULL,
        created_at    DATETIMEOFFSET NOT NULL,
        CONSTRAINT PK_meta_oauth_states PRIMARY KEY (id),
        CONSTRAINT FK_meta_oauth_states_tenants FOREIGN KEY (tenant_id) REFERENCES dbo.tenants(id),
        CONSTRAINT FK_meta_oauth_states_users FOREIGN KEY (user_id) REFERENCES dbo.users(id),
        CONSTRAINT UQ_meta_oauth_states_hash UNIQUE (state_hash)
    );

    CREATE INDEX IX_meta_oauth_states_expires_at ON dbo.meta_oauth_states(expires_at);
END;

IF OBJECT_ID(N'dbo.content_schedule', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.content_schedule', N'meta_asset_id') IS NULL
BEGIN
    ALTER TABLE dbo.content_schedule ADD meta_asset_id UNIQUEIDENTIFIER NULL;
END;

IF OBJECT_ID(N'dbo.content_schedule', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.meta_assets', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.content_schedule', N'meta_asset_id') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.foreign_keys
       WHERE name = N'FK_content_schedule_meta_assets'
         AND parent_object_id = OBJECT_ID(N'dbo.content_schedule'))
BEGIN
    ALTER TABLE dbo.content_schedule ADD CONSTRAINT FK_content_schedule_meta_assets
        FOREIGN KEY (meta_asset_id) REFERENCES dbo.meta_assets(id) ON DELETE SET NULL;
END;

IF OBJECT_ID(N'dbo.content_schedule', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.content_schedule', N'meta_asset_id') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE name = N'IX_content_schedule_meta_asset_id'
         AND object_id = OBJECT_ID(N'dbo.content_schedule'))
BEGIN
    CREATE INDEX IX_content_schedule_meta_asset_id ON dbo.content_schedule(meta_asset_id);
END;
