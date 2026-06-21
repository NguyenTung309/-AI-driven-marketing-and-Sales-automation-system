-- SQL Server migration script to update pancake_configs table to match C# models
DECLARE @ConstraintName nvarchar(200);
SELECT @ConstraintName = name FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID('pancake_configs') AND type = 'UQ';
IF @ConstraintName IS NOT NULL
    EXEC('ALTER TABLE pancake_configs DROP CONSTRAINT ' + @ConstraintName);

IF COL_LENGTH('pancake_configs', 'channel') IS NOT NULL
    ALTER TABLE pancake_configs DROP COLUMN channel;

IF COL_LENGTH('pancake_configs', 'base_url') IS NULL
    ALTER TABLE pancake_configs ADD base_url NVARCHAR(256) NOT NULL DEFAULT 'https://pancake.vn/api/v1';

IF COL_LENGTH('pancake_configs', 'signature_header') IS NULL
    ALTER TABLE pancake_configs ADD signature_header NVARCHAR(64) NOT NULL DEFAULT 'x-pancake-signature';

IF COL_LENGTH('pancake_configs', 'signature_algo') IS NULL
    ALTER TABLE pancake_configs ADD signature_algo NVARCHAR(32) NOT NULL DEFAULT 'hmac-sha256';

IF COL_LENGTH('pancake_configs', 'signature_encoding') IS NULL
    ALTER TABLE pancake_configs ADD signature_encoding NVARCHAR(16) NOT NULL DEFAULT 'hex';

IF COL_LENGTH('pancake_configs', 'send_path_template') IS NULL
    ALTER TABLE pancake_configs ADD send_path_template NVARCHAR(512) NOT NULL DEFAULT '/pages/{page_id}/conversations/{thread_id}/messages';

IF COL_LENGTH('pancake_configs', 'auth_mode') IS NULL
    ALTER TABLE pancake_configs ADD auth_mode NVARCHAR(16) NOT NULL DEFAULT 'query';

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID('pancake_configs') AND type = 'UQ')
    ALTER TABLE pancake_configs ADD CONSTRAINT UQ_pancake_configs_tenant_id UNIQUE (tenant_id);
