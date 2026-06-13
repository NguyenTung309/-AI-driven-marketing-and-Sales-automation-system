-- 0013: Identity ↔ DDL reconcile (M23, Option A)
-- EF (IdentityDbContext) maps AppUser -> `users` (see IdentityUserConfiguration) and keeps the
-- other Identity entities on their default "AspNet*" table names (SnakeCaseConventions skips the
-- AspNet prefix, but DOES snake_case columns). The original 0001 `users` table was missing the
-- columns Identity requires + the AspNet* sub-tables did not exist at all → auth only worked
-- against EF EnsureCreated, never against the DDL schema. This migration closes that gap.
--
-- NOTE: the migration runner (SqlServerFixture / deploy) executes each .sql file as ONE batch
-- via SqlCommand — it does NOT understand the `GO` separator. So: no `GO` here, and indexes that
-- reference the just-ALTERed `users` columns live in 0014 (a separate file = a fresh batch, run
-- after this ALTER has committed). CREATE INDEX on freshly-CREATEd tables is fine in-batch.
-- ⚠️ Verify against a real SQL Server (integration auth test) before prod.

-- 1) Add the Identity columns AppUser/UserManager needs to the existing `users` table.
ALTER TABLE users ADD
    user_name              NVARCHAR(256),
    normalized_user_name   NVARCHAR(256),
    normalized_email       NVARCHAR(256),
    email_confirmed        BIT NOT NULL DEFAULT 0,
    concurrency_stamp      NVARCHAR(MAX),
    phone_number_confirmed BIT NOT NULL DEFAULT 0,
    two_factor_enabled     BIT NOT NULL DEFAULT 0,
    lockout_enabled        BIT NOT NULL DEFAULT 1,
    date_of_birth          DATE,
    avatar_url             NVARCHAR(512);

-- 2) Identity sub-tables (kept on AspNet* names; snake_case columns to match EF).
CREATE TABLE AspNetRoles (
    id                UNIQUEIDENTIFIER PRIMARY KEY,
    name              NVARCHAR(256),
    normalized_name   NVARCHAR(256),
    concurrency_stamp NVARCHAR(MAX)
);
CREATE INDEX ix_asp_net_roles_normalized_name ON AspNetRoles (normalized_name);

CREATE TABLE AspNetUserRoles (
    user_id UNIQUEIDENTIFIER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    role_id UNIQUEIDENTIFIER NOT NULL REFERENCES AspNetRoles(id) ON DELETE CASCADE,
    PRIMARY KEY (user_id, role_id)
);

CREATE TABLE AspNetUserClaims (
    id          INT IDENTITY(1,1) PRIMARY KEY,
    user_id     UNIQUEIDENTIFIER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    claim_type  NVARCHAR(MAX),
    claim_value NVARCHAR(MAX)
);

CREATE TABLE AspNetUserLogins (
    login_provider        NVARCHAR(128) NOT NULL,
    provider_key          NVARCHAR(128) NOT NULL,
    provider_display_name NVARCHAR(MAX),
    user_id               UNIQUEIDENTIFIER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    PRIMARY KEY (login_provider, provider_key)
);

CREATE TABLE AspNetUserTokens (
    user_id        UNIQUEIDENTIFIER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    login_provider NVARCHAR(128) NOT NULL,
    name           NVARCHAR(128) NOT NULL,
    value          NVARCHAR(MAX),
    PRIMARY KEY (user_id, login_provider, name)
);

CREATE TABLE AspNetRoleClaims (
    id          INT IDENTITY(1,1) PRIMARY KEY,
    role_id     UNIQUEIDENTIFIER NOT NULL REFERENCES AspNetRoles(id) ON DELETE CASCADE,
    claim_type  NVARCHAR(MAX),
    claim_value NVARCHAR(MAX)
);
