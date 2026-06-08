-- SPEC-11 §6 — refresh tokens (httpOnly + SHA-256 hash-at-rest + rotate + reuse-detection).
-- ADR-009: this DDL is the source of truth for the schema; EF maps to it.
-- Dev builds the schema via EnsureCreated from the EF model, which mirrors this file.
--
-- NOTE: user_id references the ASP.NET Identity user (AppUser, stored in AspNetUsers) — NOT
-- the domain `users` table. There is intentionally NO FK here: the EF model carries no
-- navigation/FK to AspNetUsers (RefreshTokenConfiguration), and cleanup is application-managed
-- (RefreshTokenService.RevokeAllForUserAsync + RefreshTokenCleanupJob). A FK to `users` would
-- be the wrong table and fail on insert; a FK to AspNetUsers would require the Identity schema
-- to exist first (no SQL DDL for it yet — see prod Identity-DDL open item). Keep it FK-less so
-- dev (EnsureCreated) and prod (this DDL) produce an identical table.

CREATE TABLE refresh_tokens (
    id          UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    user_id     UNIQUEIDENTIFIER NOT NULL,         -- AppUser.Id (AspNetUsers) — no FK, see note above
    family_id   UNIQUEIDENTIFIER NOT NULL,         -- session/family — revoke whole family with one UPDATE
    token_hash  NVARCHAR(128) NOT NULL,            -- SHA-256, raw is never stored
    expires_at  DATETIMEOFFSET NOT NULL,
    revoked_at  DATETIMEOFFSET,
    replaced_by UNIQUEIDENTIFIER,                  -- successor token id (audit; no self-FK, avoids multi-cascade-path)
    created_at  DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    created_ip  NVARCHAR(64)
);
CREATE INDEX ix_refresh_tokens_user   ON refresh_tokens (user_id, expires_at DESC);
CREATE INDEX ix_refresh_tokens_hash   ON refresh_tokens (token_hash);
CREATE INDEX ix_refresh_tokens_family ON refresh_tokens (family_id);
