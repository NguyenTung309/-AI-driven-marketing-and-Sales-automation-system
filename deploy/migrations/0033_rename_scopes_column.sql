-- Migration: rename api_keys.scopes to scopes_json
-- Executed by deploy/test runner as one SqlCommand, so keep it GO-free.
SET QUOTED_IDENTIFIER ON;
SET ARITHABORT ON;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.api_keys') AND name = N'scopes_json')
BEGIN
    IF COL_LENGTH(N'dbo.api_keys', N'scopes') IS NOT NULL
        EXEC sp_rename N'dbo.api_keys.scopes', N'scopes_json', N'COLUMN';
    ELSE
        ALTER TABLE dbo.api_keys ADD scopes_json NVARCHAR(MAX) NOT NULL DEFAULT N'[]';
END;

EXEC(N'UPDATE dbo.api_keys SET scopes_json = N''[]'' WHERE scopes_json IS NULL;');

