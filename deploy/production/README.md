# Production deployment contract

The production stack is intentionally separate from `deploy/docker-compose.yml`, which remains the local-development entrypoint.

## Files on the host

- `/etc/clawbot/releases/<sha>-<run-id>-<attempt>/`: immutable candidate bundle, mode `0700`; contains the Compose file, its resolved environment, runtime/provider configuration, SearXNG settings, scripts, migrations, and SQL contracts.
- `/etc/clawbot/releases/current` and `/etc/clawbot/releases/previous`: atomically promoted symlinks to the current and immediately previous successful release. A failed candidate never changes `current`; if updating `previous` fails after `current` is promoted, deployment stops for an operator to complete the preserved pointer move.
- `/etc/clawbot/releases/rollback-<timestamp>-<pid>/`: immutable hybrid release state created by an application-only rollback. It preserves the current infrastructure configuration while pinning the restored application images and runtime configuration, so `current` always describes the containers actually running.
- Each release runtime environment is mode `0600` and is used only by API and AgentService. On a first deployment it must include `Bootstrap__InitialAdminEmail` and `Bootstrap__InitialAdminPassword`; the API refuses to start without them while the Identity store is empty.
- `/var/backups/clawbot`: verified SQL backups, owned by SQL Server container UID `10001` and group `0`, mode `0770`; the deploy account does not receive broad write access.

## Required GitHub Environment secrets

- `PRODUCTION_GHCR_USERNAME` and `PRODUCTION_GHCR_TOKEN`: read-only GHCR credentials used by the production host.
- `PRODUCTION_SEARXNG_SETTINGS`: complete production SearXNG YAML, written with mode `0600`.
- `PRODUCTION_RUNTIME_ENV`: provider credentials plus the initial administrator variables described above.

`PRODUCTION_COMPOSE_ENV` must not define `CLAWBOT_API_IMAGE`, `CLAWBOT_GATEWAY_IMAGE`, `CLAWBOT_AGENT_IMAGE`, or `CLAWBOT_WEB_IMAGE`; the immutable release manifest is the sole source for those values. It also cannot choose the runtime or SearXNG configuration paths: the installer synthesizes those paths inside the immutable release bundle. The workflow rejects duplicate environment keys before touching Docker, uploads through a mode-`0700` per-run staging directory, and promotes `current` only after candidate smoke checks pass.

The first deployment creates the MinIO application user from `MINIO_APP_ACCESS_KEY` and `MINIO_APP_SECRET_KEY` before starting API and AgentService. Normal application releases preserve that user and require its credentials to match the active release; the `MINIO_MC_IMAGE` reference therefore remains digest-pinned.

Shared infrastructure credentials are fail-closed: a normal application release requires SQL Server, Redis, RabbitMQ, MinIO, and runtime SQL credentials to match the active release. Rotate them only through a controlled maintenance procedure, then establish a new known-good release. `APP_SQL_PASSWORD` is preflighted before the current application is stopped; a mismatch leaves the current release running and applies no migration.

The current Compose contract uses `TrustServerCertificate=True` for the internal SQL Server connection because the stock SQL Server container does not ship a trusted certificate. This is acceptable only for the temporary IP-only staging deployment; configure a mounted SQL Server certificate and trusted CA, then change it to `False`, before secure production.

## Release order

1. Install a versioned candidate bundle without changing `current` or `previous`.
2. Parse the protected environment with Docker Compose itself, require digest-pinned images, require exact `Standard` or `Enterprise` SQL Server PID, validate port `58080`, and reject shared-infrastructure credential or Compose/SearXNG changes against an active release.
3. For an existing database, preflight runtime SQL credentials and require non-empty migration history before any service is stopped. A legacy database must first undergo the reviewed baseline procedure.
4. Pull immutable images while the current application remains available, then stop only the services that were running and verify they are stopped.
5. Take and verify a full `clawbot` backup only after application quiescence and before any Compose operation can reconcile SQL Server. This captures every committed production write before the migration window.
6. Reconcile unchanged infrastructure, apply forward-compatible migrations, and execute each curated repair inside a runner-owned SQL transaction before verification and application startup. The migration runner records a deployment-specific marker in the same SQL transaction as an applied numbered migration, so recovery never restarts an old binary after a committed schema change. Idempotent runtime repairs are deliberately excluded from this marker because they are forward-compatible with the prior binary.
7. Start candidate services in dependency order, then run `smoke.sh`. A failed candidate is stopped before release-state handling. A failure before a committed migration/repair restores the prior application without recreating infrastructure. Once schema mutation commits, automatic application recovery is blocked because the prior binary may be incompatible; the deploy output gives the exact command for `restore-verified-backup.sh`, which verifies the retained backup, restores it, and restarts the current known-compatible release (or restores without app restart when none exists).
8. Only after smoke succeeds, promote the candidate to `current`, then advance `previous`. Application-only rollback creates an immutable hybrid rollback release with current infrastructure and restored application images/runtime, then atomically makes that hybrid release `current`.

The initial IP-only endpoint is HTTP staging. Do not treat it as secure production until a domain and TLS certificate are configured.
