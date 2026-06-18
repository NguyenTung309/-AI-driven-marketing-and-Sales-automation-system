-- 0013: Identity <-> DDL reconcile (M23, Option A)
-- EF maps AppUser -> users (see IdentityUserConfiguration) while the other
-- Identity entities stay on their default AspNet* table names. The original
-- 0001 users table was missing columns required by Identity's UserManager.
--
-- NOTE: the migration runner executes each .sql file as one batch and does
-- not understand GO separators. Keep this script single-batch friendly.

-- 1) Add the Identity columns AppUser/UserManager needs to the existing users table.
-- Dynamic SQL keeps these guarded DDL statements reliable through sqlcmd stdin replay.
IF COL_LENGTH(N'dbo.users', N'user_name') IS NULL EXEC(N'ALTER TABLE users ADD user_name NVARCHAR(256);');
IF COL_LENGTH(N'dbo.users', N'normalized_user_name') IS NULL EXEC(N'ALTER TABLE users ADD normalized_user_name NVARCHAR(256);');
IF COL_LENGTH(N'dbo.users', N'normalized_email') IS NULL EXEC(N'ALTER TABLE users ADD normalized_email NVARCHAR(256);');
IF COL_LENGTH(N'dbo.users', N'email_confirmed') IS NULL EXEC(N'ALTER TABLE users ADD email_confirmed BIT NOT NULL DEFAULT 0;');
IF COL_LENGTH(N'dbo.users', N'concurrency_stamp') IS NULL EXEC(N'ALTER TABLE users ADD concurrency_stamp NVARCHAR(MAX);');
IF COL_LENGTH(N'dbo.users', N'phone_number') IS NULL EXEC(N'ALTER TABLE users ADD phone_number NVARCHAR(MAX);');
IF COL_LENGTH(N'dbo.users', N'phone_number_confirmed') IS NULL EXEC(N'ALTER TABLE users ADD phone_number_confirmed BIT NOT NULL DEFAULT 0;');
IF COL_LENGTH(N'dbo.users', N'two_factor_enabled') IS NULL EXEC(N'ALTER TABLE users ADD two_factor_enabled BIT NOT NULL DEFAULT 0;');
IF COL_LENGTH(N'dbo.users', N'lockout_enabled') IS NULL EXEC(N'ALTER TABLE users ADD lockout_enabled BIT NOT NULL DEFAULT 1;');
IF COL_LENGTH(N'dbo.users', N'date_of_birth') IS NULL EXEC(N'ALTER TABLE users ADD date_of_birth DATE;');
IF COL_LENGTH(N'dbo.users', N'avatar_url') IS NULL EXEC(N'ALTER TABLE users ADD avatar_url NVARCHAR(512);');

-- 2) Identity sub-tables (kept on AspNet* names; snake_case columns to match EF).
IF OBJECT_ID(N'dbo.AspNetRoles', N'U') IS NULL EXEC(N'
CREATE TABLE AspNetRoles (
    id                UNIQUEIDENTIFIER PRIMARY KEY,
    name              NVARCHAR(256),
    normalized_name   NVARCHAR(256),
    concurrency_stamp NVARCHAR(MAX)
);');

IF OBJECT_ID(N'dbo.AspNetRoles', N'U') IS NOT NULL
    AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_asp_net_roles_normalized_name' AND object_id = OBJECT_ID(N'dbo.AspNetRoles'))
    EXEC(N'CREATE INDEX ix_asp_net_roles_normalized_name ON AspNetRoles (normalized_name);');

IF OBJECT_ID(N'dbo.AspNetUserRoles', N'U') IS NULL EXEC(N'
CREATE TABLE AspNetUserRoles (
    user_id UNIQUEIDENTIFIER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    role_id UNIQUEIDENTIFIER NOT NULL REFERENCES AspNetRoles(id) ON DELETE CASCADE,
    PRIMARY KEY (user_id, role_id)
);');

IF OBJECT_ID(N'dbo.AspNetUserClaims', N'U') IS NULL EXEC(N'
CREATE TABLE AspNetUserClaims (
    id          INT IDENTITY(1,1) PRIMARY KEY,
    user_id     UNIQUEIDENTIFIER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    claim_type  NVARCHAR(MAX),
    claim_value NVARCHAR(MAX)
);');

IF OBJECT_ID(N'dbo.AspNetUserLogins', N'U') IS NULL EXEC(N'
CREATE TABLE AspNetUserLogins (
    login_provider        NVARCHAR(128) NOT NULL,
    provider_key          NVARCHAR(128) NOT NULL,
    provider_display_name NVARCHAR(MAX),
    user_id               UNIQUEIDENTIFIER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    PRIMARY KEY (login_provider, provider_key)
);');

IF OBJECT_ID(N'dbo.AspNetUserTokens', N'U') IS NULL EXEC(N'
CREATE TABLE AspNetUserTokens (
    user_id        UNIQUEIDENTIFIER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    login_provider NVARCHAR(128) NOT NULL,
    name           NVARCHAR(128) NOT NULL,
    value          NVARCHAR(MAX),
    PRIMARY KEY (user_id, login_provider, name)
);');

IF OBJECT_ID(N'dbo.AspNetRoleClaims', N'U') IS NULL EXEC(N'
CREATE TABLE AspNetRoleClaims (
    id          INT IDENTITY(1,1) PRIMARY KEY,
    role_id     UNIQUEIDENTIFIER NOT NULL REFERENCES AspNetRoles(id) ON DELETE CASCADE,
    claim_type  NVARCHAR(MAX),
    claim_value NVARCHAR(MAX)
);');
