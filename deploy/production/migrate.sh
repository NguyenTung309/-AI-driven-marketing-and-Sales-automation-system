#!/usr/bin/env sh
set -eu

: "${MSSQL_SA_PASSWORD:?MSSQL_SA_PASSWORD is required}"
: "${SQLSERVER_CONTAINER:?SQLSERVER_CONTAINER is required}"
: "${MIGRATION_DATABASE:=clawbot}"
: "${MIGRATIONS_DIR:=/opt/clawbot/migrations}"
: "${REPAIRS_DIR:=/opt/clawbot}"
: "${APP_SQL_USER:=clawbot_app}"
: "${APP_SQL_PASSWORD:?APP_SQL_PASSWORD is required}"

case "$MIGRATION_DATABASE" in
  ''|*[!A-Za-z0-9_]*|[0-9]*)
    printf '%s\n' 'MIGRATION_DATABASE must start with a letter or underscore and contain only letters, digits, and underscores.' >&2
    exit 1
    ;;
esac
case "$APP_SQL_USER" in
  ''|*[!A-Za-z0-9_]*|[0-9]*)
    printf '%s\n' 'APP_SQL_USER must start with a letter or underscore and contain only letters, digits, and underscores.' >&2
    exit 1
    ;;
esac

container_migrations=/tmp/clawbot-migrations

escape_sql_literal() {
  printf '%s' "$1" | sed "s/'/''/g"
}

run_sqlcmd_from_stdin() {
  database=$1
  user=$2
  password=$3
  shift 3

  {
    printf '%s\n' "$password"
    cat
  } | docker exec -i "$SQLSERVER_CONTAINER" sh -c '
    IFS= read -r SQLCMDPASSWORD
    export SQLCMDPASSWORD
    database=$1
    user=$2
    shift 2
    exec /opt/mssql-tools18/bin/sqlcmd -S localhost,1433 -U "$user" -C -d "$database" -b "$@" -i /dev/stdin
  ' sh "$database" "$user" "$@"
}

run_query_in_database() {
  database=$1
  query=$2
  printf '%s\n' "$query" | run_sqlcmd_from_stdin "$database" sa "$MSSQL_SA_PASSWORD" -h -1 -W
}

run_query() {
  run_query_in_database "$MIGRATION_DATABASE" "$1"
}

run_query_from_stdin_in_database() {
  database=$1
  query=$2
  printf '%s\n' "$query" | run_sqlcmd_from_stdin "$database" sa "$MSSQL_SA_PASSWORD" -h -1 -W
}

for attempt in $(seq 1 60); do
  if printf '%s\n' 'SELECT 1' | run_sqlcmd_from_stdin master sa "$MSSQL_SA_PASSWORD" >/dev/null 2>&1; then
    break
  fi
  if [ "$attempt" -eq 60 ]; then
    printf '%s\n' 'SQL Server did not become available' >&2
    exit 1
  fi
  sleep 5
done

if [ "${MIGRATION_PREFLIGHT_ONLY:-false}" = "true" ]; then
  printf '%s\n' 'SELECT 1' | run_sqlcmd_from_stdin "$MIGRATION_DATABASE" "$APP_SQL_USER" "$APP_SQL_PASSWORD" >/dev/null
  history_state=$(run_query "SET NOCOUNT ON; IF OBJECT_ID(N'dbo.schema_migrations', N'U') IS NULL BEGIN IF EXISTS (SELECT 1 FROM sys.tables WHERE is_ms_shipped = 0) SELECT 2; ELSE SELECT 0; END ELSE IF EXISTS (SELECT 1 FROM dbo.schema_migrations) SELECT 1; ELSE IF EXISTS (SELECT 1 FROM sys.tables WHERE is_ms_shipped = 0 AND name <> N'schema_migrations') SELECT 3; ELSE SELECT 4;")
  case "$(printf '%s' "$history_state" | tr -d '[:space:]')" in
    0|1|4) exit 0 ;;
    2|3)
      printf '%s\n' 'Existing database has no migration history; run the reviewed legacy-baseline procedure before production deployment.' >&2
      exit 1
      ;;
    *)
      printf '%s\n' 'Migration history probe returned an invalid result.' >&2
      exit 1
      ;;
  esac
fi

: "${SCHEMA_MUTATION_RUN_ID:?SCHEMA_MUTATION_RUN_ID is required}"
case "$SCHEMA_MUTATION_RUN_ID" in
  *[!A-Za-z0-9._-]*|'')
    printf '%s\n' 'SCHEMA_MUTATION_RUN_ID contains unsupported characters.' >&2
    exit 1
    ;;
esac

database_literal=$(escape_sql_literal "$MIGRATION_DATABASE")
mutation_run_literal=$(escape_sql_literal "$SCHEMA_MUTATION_RUN_ID")
app_user_literal=$(escape_sql_literal "$APP_SQL_USER")
app_password_literal=$(escape_sql_literal "$APP_SQL_PASSWORD")

# Bootstrap the database and the runtime principal only for a full migration.
run_query_from_stdin_in_database master "IF DB_ID(N'$database_literal') IS NULL CREATE DATABASE [$MIGRATION_DATABASE]; IF SUSER_ID(N'$app_user_literal') IS NULL CREATE LOGIN [$APP_SQL_USER] WITH PASSWORD = N'$app_password_literal';"
run_query "IF USER_ID(N'$app_user_literal') IS NULL CREATE USER [$APP_SQL_USER] FOR LOGIN [$APP_SQL_USER]; ELSE ALTER USER [$APP_SQL_USER] WITH LOGIN = [$APP_SQL_USER]; IF NOT EXISTS (SELECT 1 FROM sys.database_role_members drm JOIN sys.database_principals r ON r.principal_id = drm.role_principal_id JOIN sys.database_principals m ON m.principal_id = drm.member_principal_id WHERE r.name = N'db_datareader' AND m.name = N'$app_user_literal') ALTER ROLE [db_datareader] ADD MEMBER [$APP_SQL_USER]; IF NOT EXISTS (SELECT 1 FROM sys.database_role_members drm JOIN sys.database_principals r ON r.principal_id = drm.role_principal_id JOIN sys.database_principals m ON m.principal_id = drm.member_principal_id WHERE r.name = N'db_datawriter' AND m.name = N'$app_user_literal') ALTER ROLE [db_datawriter] ADD MEMBER [$APP_SQL_USER]; IF SCHEMA_ID(N'HangFire') IS NULL EXEC(N'CREATE SCHEMA [HangFire] AUTHORIZATION [dbo]'); GRANT CREATE TABLE TO [$APP_SQL_USER]; GRANT ALTER ON SCHEMA::[HangFire] TO [$APP_SQL_USER]; GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[HangFire] TO [$APP_SQL_USER];"
printf '%s\n' 'SELECT 1' | run_sqlcmd_from_stdin "$MIGRATION_DATABASE" "$APP_SQL_USER" "$APP_SQL_PASSWORD" >/dev/null

history_state=$(run_query "SET NOCOUNT ON; IF OBJECT_ID(N'dbo.schema_migrations', N'U') IS NULL BEGIN IF EXISTS (SELECT 1 FROM sys.tables WHERE is_ms_shipped = 0) SELECT 2; ELSE SELECT 0; END ELSE IF EXISTS (SELECT 1 FROM dbo.schema_migrations) SELECT 1; ELSE IF EXISTS (SELECT 1 FROM sys.tables WHERE is_ms_shipped = 0 AND name <> N'schema_migrations') SELECT 3; ELSE SELECT 4;")
migration_history_bootstrap_pending=false
case "$(printf '%s' "$history_state" | tr -d '[:space:]')" in
  0|4)
    migration_history_bootstrap_pending=true
    ;;
  1) ;;
  2|3)
    printf '%s\n' 'Existing database has no migration history; run the reviewed legacy-baseline procedure before production deployment.' >&2
    exit 1
    ;;
  *)
    printf '%s\n' 'Migration history probe returned an invalid result.' >&2
    exit 1
    ;;
esac

if [ "$migration_history_bootstrap_pending" = true ]; then
  highest_baseline_number=0
else
  highest_baseline_number=$(run_query "SET NOCOUNT ON; SELECT COALESCE(MAX(TRY_CONVERT(INT, SUBSTRING(filename, 12, 4))), 0) FROM dbo.schema_migrations WHERE LEN(filename) = 17 AND filename COLLATE Latin1_General_100_BIN2 LIKE N'[_][_]baseline[_][0-9][0-9][0-9][0-9][_][_]' COLLATE Latin1_General_100_BIN2;")
  highest_baseline_number=$(printf '%s' "$highest_baseline_number" | tr -d '[:space:]')
fi
case "$highest_baseline_number" in
  ''|*[!0-9]*)
    printf '%s\n' 'Migration history contains an invalid baseline marker.' >&2
    exit 1
    ;;
esac

docker exec "$SQLSERVER_CONTAINER" mkdir -p "$container_migrations"
docker cp "$MIGRATIONS_DIR/." "$SQLSERVER_CONTAINER:$container_migrations/"

apply_file() {
  file=$1
  base=$(basename "$file")
  escaped=$(printf '%s' "$base" | sed "s/'/''/g")
  case "$base" in
    [0-9][0-9][0-9][0-9]_[A-Za-z0-9_.-]*.sql) ;;
    *) printf 'Unsafe migration filename: %s\n' "$base" >&2; exit 1 ;;
  esac

  migration_number=$(printf '%s' "${base%%_*}" | sed 's/^0*//; s/^$/0/')
  if [ "$highest_baseline_number" -gt 0 ] && [ "$migration_number" -le "$highest_baseline_number" ]; then
    printf '[SKIP] %s (baseline %04d)\n' "$base" "$highest_baseline_number"
    return
  fi

  if [ "$migration_history_bootstrap_pending" = false ]; then
    applied=$(run_query "SET NOCOUNT ON; SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.schema_migrations WHERE filename = N'$escaped') THEN 1 ELSE 0 END;")
    applied=$(printf '%s' "$applied" | tr -d '[:space:]')
    if [ "$applied" = "1" ]; then
      printf '[SKIP] %s\n' "$base"
      return
    fi
  fi

  wrapper=$(mktemp)
  cat > "$wrapper" <<EOF
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ARITHABORT ON;
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.schema_migrations', N'U') IS NULL
        CREATE TABLE dbo.schema_migrations (filename NVARCHAR(260) NOT NULL CONSTRAINT PK_schema_migrations PRIMARY KEY, applied_at DATETIMEOFFSET NOT NULL);
    IF OBJECT_ID(N'dbo.schema_mutation_runs', N'U') IS NULL
        CREATE TABLE dbo.schema_mutation_runs (run_id NVARCHAR(128) NOT NULL CONSTRAINT PK_schema_mutation_runs PRIMARY KEY, committed_at DATETIMEOFFSET NOT NULL);
    DECLARE @lockResult INT;
    EXEC @lockResult = sp_getapplock @Resource = N'clawbot-schema-migrations', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;
    IF @lockResult < 0 THROW 51000, 'Could not acquire schema migration lock.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.schema_migrations WHERE filename = N'$escaped')
    BEGIN
:r $container_migrations/$base
        INSERT INTO dbo.schema_migrations (filename, applied_at) VALUES (N'$escaped', SYSDATETIMEOFFSET());
    END;
    IF NOT EXISTS (SELECT 1 FROM dbo.schema_mutation_runs WHERE run_id = N'$mutation_run_literal')
        INSERT INTO dbo.schema_mutation_runs (run_id, committed_at) VALUES (N'$mutation_run_literal', SYSDATETIMEOFFSET());
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
EOF
  container_wrapper="$container_migrations/.wrapper.sql"
  docker cp "$wrapper" "$SQLSERVER_CONTAINER:$container_wrapper"
  printf '[SQL] %s\n' "$base"
  cat "$wrapper" | run_sqlcmd_from_stdin "$MIGRATION_DATABASE" sa "$MSSQL_SA_PASSWORD" -I
  migration_history_bootstrap_pending=false
  docker exec -u root "$SQLSERVER_CONTAINER" rm -f "$container_wrapper" "$container_migrations/$base" >/dev/null 2>&1 || true
  rm -f "$wrapper"
}

apply_sql_contract() {
  file=$1
  [ -f "$file" ] || {
    printf 'Required SQL contract is missing: %s\n' "$file" >&2
    exit 1
  }

  base=$(basename "$file")
  case "$base" in
    repair_[A-Za-z0-9_.-]*.sql) ;;
    *) printf 'Unsafe SQL repair filename: %s\n' "$base" >&2; exit 1 ;;
  esac

  container_repair="$container_migrations/$base"
  container_wrapper="$container_migrations/.repair-wrapper.sql"
  wrapper=$(mktemp)
  docker cp "$file" "$SQLSERVER_CONTAINER:$container_repair"
  cat > "$wrapper" <<EOF
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ARITHABORT ON;
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.schema_mutation_runs', N'U') IS NULL
        CREATE TABLE dbo.schema_mutation_runs (run_id NVARCHAR(128) NOT NULL CONSTRAINT PK_schema_mutation_runs PRIMARY KEY, committed_at DATETIMEOFFSET NOT NULL);
    DECLARE @lockResult INT;
    EXEC @lockResult = sp_getapplock @Resource = N'clawbot-schema-migrations', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;
    IF @lockResult < 0 THROW 51000, 'Could not acquire schema migration lock.', 1;
:r $container_repair
    IF NOT EXISTS (SELECT 1 FROM dbo.schema_mutation_runs WHERE run_id = N'$mutation_run_literal')
        INSERT INTO dbo.schema_mutation_runs (run_id, committed_at) VALUES (N'$mutation_run_literal', SYSDATETIMEOFFSET());
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
EOF
  docker cp "$wrapper" "$SQLSERVER_CONTAINER:$container_wrapper"
  printf '[SQL] %s\n' "$base"
  cat "$wrapper" | run_sqlcmd_from_stdin "$MIGRATION_DATABASE" sa "$MSSQL_SA_PASSWORD" -I
  docker exec -u root "$SQLSERVER_CONTAINER" rm -f "$container_wrapper" "$container_repair" >/dev/null 2>&1 || true
  rm -f "$wrapper"
}

verify_sql_contract() {
  file=$1
  expected=$2
  [ -f "$file" ] || {
    printf 'Required SQL verification is missing: %s\n' "$file" >&2
    exit 1
  }

  output=$(cat "$file" | run_sqlcmd_from_stdin "$MIGRATION_DATABASE" sa "$MSSQL_SA_PASSWORD" -I -h -1 -W)
  result=$(printf '%s\n' "$output" | tr -d '\r' | grep -E "^$expected$" | tail -n 1 || true)
  [ -n "$result" ] || {
    printf 'SQL verification failed for %s.\n' "$(basename "$file")" >&2
    return 1
  }
  printf '[VERIFY] %s\n' "$(basename "$file")"
}

verify_database_consolidation() {
  file="$REPAIRS_DIR/verify_database_table_consolidation.sql"
  [ -f "$file" ] || {
    printf 'Required SQL verification is missing: %s\n' "$file" >&2
    exit 1
  }

  output=$(cat "$file" | run_sqlcmd_from_stdin "$MIGRATION_DATABASE" sa "$MSSQL_SA_PASSWORD" -I -h -1 -W)
  result=$(printf '%s\n' "$output" | tr -d '\r' | grep -E '^[01]{15}\|[0-9]+\|[0-9]+$' | tail -n 1 || true)
  flags=${result%%|*}
  [ "$flags" = "111111111111111" ] || {
    printf '%s\n' 'Database consolidation verification returned an invalid result.' >&2
    return 1
  }
  printf '[VERIFY] %s\n' "$(basename "$file")"
}

for file in "$MIGRATIONS_DIR"/*.sql; do
  [ -f "$file" ] || continue
  apply_file "$file"
done

# Explicitly curated legacy repairs; new repair files require a conscious deployment review.
apply_sql_contract "$REPAIRS_DIR/repair_tenant_runtime_columns.sql"
apply_sql_contract "$REPAIRS_DIR/repair_inbox_runtime_columns.sql"
apply_sql_contract "$REPAIRS_DIR/repair_agent_runtime_columns.sql"
apply_sql_contract "$REPAIRS_DIR/repair_inbox_collaboration_tables.sql"
apply_sql_contract "$REPAIRS_DIR/repair_agent_allowed_tools.sql"
verify_sql_contract "$REPAIRS_DIR/verify_content_render_tasks.sql" '1111111111111'
verify_database_consolidation

printf '%s\n' 'SQL migrations and production schema contracts completed'
