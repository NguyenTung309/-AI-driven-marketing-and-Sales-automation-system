-- Rename scopes to scopes_json for consistency with EF model
IF COL_LENGTH('api_keys', 'scopes') IS NOT NULL
BEGIN
    EXEC sp_rename 'api_keys.scopes', 'scopes_json', 'COLUMN';
END
