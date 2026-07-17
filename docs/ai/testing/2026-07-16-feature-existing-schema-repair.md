---
phase: testing
title: Existing Schema Repair Testing Strategy
description: Verify idempotent repair of tenant runtime columns before API startup
---

# Existing Schema Repair Testing Strategy

## Test Coverage Goals

- Cover every tenant column required by the current EF Core model but added after the original local schema baseline.
- Verify the existing-schema branch repairs runtime columns before baselining migration history.
- Exercise API startup against an upgraded local SQL Server database.
- Keep the repair idempotent so rerunning `run-all.bat` is safe.

## Unit Tests

### `tests/run-all.Tests.ps1`

- [x] Verify `run-all.bat` contains repairs for `monthly_cost_cap_usd`.
- [x] Verify `run-all.bat` contains repairs for `require_content_review`.
- [x] Verify `run-all.bat` contains repairs for `require_chat_reply_approval`.
- [x] Verify `run-all.bat` contains repairs for `require_kb_human_review`.
- [x] Verify the existing-schema branch calls `:repair_runtime_columns` before `:baseline_existing_migrations`.
- [x] Keep tenant-scoping assertions for tenant-specific seed files while explicitly allowing global cleanup and permission seeds.

## Integration Tests

- [x] Query the running `clawbot` SQL Server database and confirm all four tenant columns exist with the expected nullability/defaults.
- [x] Execute the four guarded `ALTER TABLE` statements again to verify idempotency.
- [x] Run `RbacSeeder` coverage through `DevDataSeederTests`.
- [x] Start `Clawbot.Api` against the repaired database and observe `/health/live` returning HTTP 200.

## End-to-End Tests

- [x] Local startup regression: the API passes `RbacSeeder.EnsureDefaultTenantAsync` without `Invalid column name` errors.
- [ ] Full `run-all.bat` launch remains a manual smoke test because it opens persistent service windows and the frontend dev server.

## Test Data

- Existing local SQL Server container: `clawbot-sqlserver`.
- Existing `clawbot` database containing migration history and tenant data.
- Repair statements use `COL_LENGTH` guards and preserve existing tenant values.

## Test Reporting & Coverage

Commands:

```powershell
& .\tests\run-all.Tests.ps1
dotnet test tests/Clawbot.Infrastructure.Tests/Clawbot.Infrastructure.Tests.csproj --filter "FullyQualifiedName~DevDataSeederTests" --no-restore
```

Results on 2026-07-16:

- `tests/run-all.Tests.ps1`: passed.
- `DevDataSeederTests`: 2 passed, 0 failed.
- API startup smoke test: `/health/live` returned `{"status":"live"}`.

This change affects a batch orchestration script rather than instrumented .NET production code, so line coverage is not applicable. Behavioral coverage is provided by script assertions and the real SQL Server startup smoke test.

## Manual Testing

- Run `run-all.bat` on an existing local database.
- Confirm the log prints `Repairing runtime columns on existing schema...` before service windows open.
- Confirm API, AgentService, Gateway, and frontend start normally.

## Performance Testing

- Not required. Each repair uses metadata checks and executes once during local startup.

## Bug Tracking

- Regression symptom: API startup fails in `RbacSeeder.EnsureDefaultTenantAsync` with SQL Server error 207 for tenant columns present in the EF model but absent from an older database.
- Common local pitfall: two SQL Servers on the machine.
  - Docker ClawBot DB: `localhost,11433` (sa / deploy password) — this is the DB `run-all.bat` repairs.
  - Host SQL Express/default: `localhost` / `localhost,1433` with `Trusted_Connection=True` from base `appsettings.json` — often an older `clawbot` schema without the four tenant columns.
- Regression prevention:
  - every post-baseline runtime column must be added both to its numbered migration and to `deploy/repair_tenant_runtime_columns.sql`
  - `run-all.bat` must call `:repair_tenant_runtime_columns` + `:verify_tenant_runtime_columns` before opening service windows
  - Development config / `run-all.bat` service env must force `Server=localhost,11433`
