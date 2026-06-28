# Security — Rotate Leaked Secrets (2026-06-28)

A secret audit (SPEC-16) found several secrets hardcoded in committed `appsettings.json` / `deploy/.env.example`.
The **live Pancake JWT** has been removed (see below). The remaining secrets are local-dev values but are in
**git history** → they must be rotated on the backing systems, not just removed from code.

## ✅ Done: Pancake access token (live external secret)

- Removed the live Pancake JWT from **both** `src/agents/Clawbot.AgentService/appsettings.json` and `src/api/Clawbot.Api/appsettings.json` (AccessToken → `""`).
- Added `PancakeBootstrapSeeder` (AgentService startup): reads `PANCAKE_PAGE_ACCESS_TOKEN` (or `PANCAKE_USER_ACCESS_TOKEN`) + `PANCAKE_PAGE_ID` env vars and stores the page token **encrypted** in `pancake_pages` for the `default` tenant — so prod never keeps the token in appsettings or env long-term (page tokens never expire; drop the env var after boot).
- `PancakePageTokenService.StorePageTokenDirectAsync` stores an already-minted page token encrypted without calling the mint gateway.
- `deploy/.env.example` documents `PANCAKE_USER_ACCESS_TOKEN` + the bootstrap behavior.
- Tests: `PancakeBootstrapSeederTests` (5) + `StorePageTokenDirectAsync` tests (2).

**Action still required by user:** the old JWT is in git history. **Rotate the Pancake page/user token on Pancake's side** (generate a new one) and use that new value in the env var. Code-side removal is complete.

## ⚠️ Remaining: rotate these (same class — committed in appsettings / .env.example)

### 1. `Encryption:Base64Key` (AES-256 key) — HIGHEST RISK
This key protects **every** encrypted DB token: `pancake_pages.page_access_token_encrypted`, `social_credentials.credentials_encrypted`, `inboxes.encrypted_access_token`, `users.pancake_access_token_encrypted`. With the key committed, anyone with DB access decrypts all of them.

Rotation procedure (run with the app stopped, or a maintenance window):
1. Generate a new AES-256 base64 key: e.g. `openssl rand -base64 32`.
2. Set **both** keys temporarily: `Encryption__Base64Key` (new) + `Encryption__LegacyBase64Key` (old) env vars.
3. Run a one-time re-encryption job over every encrypted column:
   - decrypt each row with the **legacy** key → re-encrypt with the **new** key → update the row.
   - tables/columns: `pancake_pages.page_access_token_encrypted`, `social_credentials.credentials_encrypted`, `inboxes.encrypted_access_token`, `users.pancake_access_token_encrypted`.
4. Remove `Encryption__LegacyBase64Key`; keep only `Encryption__Base64Key` (new) in env.
5. (Optional) purge git history (`git filter-repo --replace-text` targeting the old key) — old key remains in history otherwise.

> The re-encryption job is not pre-built here because it must run against the user's live DB with both keys configured. A `LegacyKeyEncryptor` + migration worker can be added on request.

### 2. `ConnectionStrings:SqlServer` SA password (`Clawbot!2026`)
- Rotate the SA password on SQL Server; update the docker-compose / SQL container.
- In prod, set `ConnectionStrings__SqlServer` env var (the .NET host reads env vars over appsettings). Documented in `deploy/.env.example`.

### 3. `ConnectionStrings:RabbitMq` (`guest:guest`)
- Replace guest:guest with real creds; rotate.
- In prod, set `ConnectionStrings__RabbitMq` env var. Documented in `deploy/.env.example`.

### 4. `deploy/.env.example` dev defaults
`MSSQL_SA_PASSWORD=Clawbot!2026`, `MINIO_PASSWORD=minio12345`, `METABASE_PASSWORD=metabase12345`, `RABBITMQ_PASSWORD=guest` — these repeat the committed appsettings values. Treat as leaked (in git history) → rotate each on its backing system. The `.env.example` is a template; replace with placeholders if not already.

## Net

- **Code-side: Pancake JWT removed, encrypted-DB bootstrap path in place.** No live external secret remains in committed source.
- **User-side required: rotate the Pancake token, the AES key (with re-encryption), the SA password, the RabbitMQ creds, and the .env.example dev defaults.** All are in git history; rotation on the backing system is the only way to invalidate them. Optional: `git filter-repo` history purge.
