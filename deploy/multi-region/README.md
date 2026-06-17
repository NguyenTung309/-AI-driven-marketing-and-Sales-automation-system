# Multi-region replication runbook

This runbook documents the Clawbot application contract for multi-region deployment.
It does not create SQL Server Always On, managed read replicas, DNS failover, or object
storage replication by itself. Those are infrastructure concerns. The application exposes
configuration, write-guard metadata, and readiness checks so ops can promote or demote
regions without guessing which node is allowed to accept writes.

## Runtime contract

- Health endpoint: `GET /health/replication`
- Config section: `Deployment:Replication`
- Primary region: the only region that may accept write traffic.
- Secondary region: may serve read-heavy traffic and background warm-up, but must not
  receive writes while it is not primary.
- Replica lag: checked on secondary regions through `Deployment__Replication__LagProbeSql`.

Example response fields:

```json
{
  "status": "ok",
  "currentRegion": "sea",
  "primaryRegion": "sea",
  "currentRole": "primary",
  "writesAllowed": true,
  "activeRegions": 2,
  "replicaLagSeconds": null
}
```

## Required environment variables

Use double underscores for hierarchical config in container platforms:

```powershell
Deployment__Replication__Enabled=true
Deployment__Replication__CurrentRegion=sea
Deployment__Replication__PrimaryRegion=sea
Deployment__Replication__AllowWrites=true
Deployment__Replication__MaxReplicaLagSeconds=30
Deployment__Replication__LagProbeTimeoutSeconds=5
Deployment__Replication__LagProbeSql=SELECT ISNULL(MAX(DATEDIFF(SECOND,last_commit_time,SYSUTCDATETIME())),0) FROM sys.dm_hadr_database_replica_states WHERE is_local=1 AND database_id=DB_ID()

Deployment__Replication__Regions__0__Name=sea
Deployment__Replication__Regions__0__Role=primary
Deployment__Replication__Regions__0__Priority=1
Deployment__Replication__Regions__0__AppBaseUrl=https://sea.clawbot.example

Deployment__Replication__Regions__1__Name=hkg
Deployment__Replication__Regions__1__Role=secondary
Deployment__Replication__Regions__1__Priority=2
Deployment__Replication__Regions__1__AppBaseUrl=https://hkg.clawbot.example
```

Set `CurrentRegion` differently in each deployment. Keep `PrimaryRegion` identical across
regions until an intentional failover.

## SQL Server lag probe

For SQL Server Always On, use a scalar query that returns lag in seconds. The default
application config leaves `LagProbeSql` empty; when replication is enabled on a secondary,
an empty lag probe degrades `/health/replication`.

Recommended starting point:

```sql
SELECT ISNULL(MAX(DATEDIFF(SECOND, last_commit_time, SYSUTCDATETIME())), 0)
FROM sys.dm_hadr_database_replica_states
WHERE is_local = 1
  AND database_id = DB_ID();
```

Managed cloud replicas may need provider-specific SQL. Keep the return shape identical:
one scalar integer or decimal value representing seconds.

## Deployment checks

Run these checks after every regional deploy:

1. `GET /health/live` returns `200`.
2. `GET /health/ready` returns `200`.
3. `GET /health/replication` returns:
   - primary: `status=ok`, `currentRole=primary`, `writesAllowed=true`.
   - secondary: `status=ok`, `currentRole=secondary`, `writesAllowed=false`,
     `replicaLagSeconds <= MaxReplicaLagSeconds`.
4. `GET /health/channels/pancake` returns `ok` or the expected credential-related
   degraded state for that environment.

## Planned failover

1. Freeze scheduled write-heavy jobs in the old primary region.
2. Set `Deployment__Replication__AllowWrites=false` in all regions and deploy/restart.
3. Confirm every region reports `writesAllowed=false` from `/health/replication`.
4. Promote the target database replica using the SQL Server or cloud-provider runbook.
5. Update application config in every region:
   - `Deployment__Replication__PrimaryRegion=<new-primary>`
   - region role entries so the new primary has `Role=primary`
   - old primary has `Role=secondary`
6. Update DNS, gateway routing, or load-balancer write routing to the new primary app.
7. Set `Deployment__Replication__AllowWrites=true` only after the new primary is healthy.
8. Confirm `/health/replication`:
   - new primary: `status=ok`, `writesAllowed=true`
   - old primary/new secondary: `status=ok`, `writesAllowed=false`
9. Re-enable scheduled jobs in the new primary.

## Emergency failover

1. Stop traffic to the unhealthy primary at the gateway/load balancer.
2. Promote the healthiest secondary database replica.
3. Apply the same config changes from planned failover.
4. Keep secondary app regions in read-only mode until `/health/replication` is green.
5. Review audit logs and `agent_sessions` for duplicate work after recovery.

## Rollback

Rollback is a new failover in reverse. Do not point writes at both regions. Keep
`Deployment__Replication__AllowWrites=false` until the database authority is clear and
`/health/replication` reports one primary region.
