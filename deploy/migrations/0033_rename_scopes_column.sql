-- Migration: rename api_keys.scopes to scopes_json
-- Run as: type file.sql | docker exec -i clawbot-sqlserver sqlcmd ...
SET QUOTED_IDENTIFIER ON;
SET ARITHABORT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.api_keys') AND name = N'scopes_json')
BEGIN
    IF COL_LENGTH(N'dbo.api_keys', N'scopes') IS NOT NULL
        EXEC sp_rename N'dbo.api_keys.scopes', N'scopes_json', N'COLUMN';
    ELSE
        ALTER TABLE dbo.api_keys ADD scopes_json NVARCHAR(MAX) NOT NULL DEFAULT N'[]';
END;
GO

UPDATE dbo.api_keys SET scopes_json = N'[]' WHERE scopes_json IS NULL;
GO
