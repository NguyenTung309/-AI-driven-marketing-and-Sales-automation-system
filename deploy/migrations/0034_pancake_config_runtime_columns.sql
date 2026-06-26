-- 0034: Pancake channel runtime endpoint/signature columns.
-- Column-adds only; defaults backfill existing local configs safely.

IF COL_LENGTH(N'dbo.pancake_configs', N'base_url') IS NULL
    EXEC(N'ALTER TABLE pancake_configs ADD base_url NVARCHAR(256) NOT NULL CONSTRAINT DF_pancake_configs_base_url DEFAULT N''https://pancake.vn/api/v1'';');

IF COL_LENGTH(N'dbo.pancake_configs', N'signature_header') IS NULL
    EXEC(N'ALTER TABLE pancake_configs ADD signature_header NVARCHAR(64) NOT NULL CONSTRAINT DF_pancake_configs_signature_header DEFAULT N''x-pancake-signature'';');

IF COL_LENGTH(N'dbo.pancake_configs', N'signature_algo') IS NULL
    EXEC(N'ALTER TABLE pancake_configs ADD signature_algo NVARCHAR(32) NOT NULL CONSTRAINT DF_pancake_configs_signature_algo DEFAULT N''hmac-sha256'';');

IF COL_LENGTH(N'dbo.pancake_configs', N'signature_encoding') IS NULL
    EXEC(N'ALTER TABLE pancake_configs ADD signature_encoding NVARCHAR(16) NOT NULL CONSTRAINT DF_pancake_configs_signature_encoding DEFAULT N''hex'';');

IF COL_LENGTH(N'dbo.pancake_configs', N'send_path_template') IS NULL
    EXEC(N'ALTER TABLE pancake_configs ADD send_path_template NVARCHAR(512) NOT NULL CONSTRAINT DF_pancake_configs_send_path_template DEFAULT N''/pages/{page_id}/conversations/{thread_id}/messages'';');

IF COL_LENGTH(N'dbo.pancake_configs', N'auth_mode') IS NULL
    EXEC(N'ALTER TABLE pancake_configs ADD auth_mode NVARCHAR(16) NOT NULL CONSTRAINT DF_pancake_configs_auth_mode DEFAULT N''query'';');
