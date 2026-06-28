IF COL_LENGTH(N'dbo.users', N'pancake_access_token_encrypted') IS NULL
BEGIN
    ALTER TABLE dbo.users ADD pancake_access_token_encrypted NVARCHAR(2048) NULL;
END;

IF COL_LENGTH(N'dbo.users', N'pancake_access_token_updated_at') IS NULL
BEGIN
    ALTER TABLE dbo.users ADD pancake_access_token_updated_at DATETIMEOFFSET NULL;
END;
