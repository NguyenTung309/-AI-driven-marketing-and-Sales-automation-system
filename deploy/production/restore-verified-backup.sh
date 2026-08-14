#!/usr/bin/env sh
set -eu

: "${COMPOSE_FILE:?COMPOSE_FILE is required}"
: "${COMPOSE_ENV_FILE:?COMPOSE_ENV_FILE is required}"
: "${BACKUP_ID:?BACKUP_ID is required}"

if [ "${CONFIRM_DATABASE_RESTORE:-}" != "RESTORE_VERIFIED_BACKUP" ]; then
  printf '%s\n' 'Set CONFIRM_DATABASE_RESTORE=RESTORE_VERIFIED_BACKUP to continue.' >&2
  exit 2
fi

case "$BACKUP_ID" in
  ''|*[!0-9]*)
    printf '%s\n' 'BACKUP_ID must use the UTC yyyyMMddHHmmss format.' >&2
    exit 1
    ;;
esac
if [ "${#BACKUP_ID}" -ne 14 ]; then
  printf '%s\n' 'BACKUP_ID must use the UTC yyyyMMddHHmmss format.' >&2
  exit 1
fi

restart_application=${RESTART_APPLICATION:-true}
case "$restart_application" in
  true|false) ;;
  *) printf '%s\n' 'RESTART_APPLICATION must be true or false.' >&2; exit 1 ;;
esac

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
. "$script_dir/common.sh"

release_root=${CLAWBOT_RELEASE_ROOT:-/etc/clawbot/releases}
schema_recovery_marker="$release_root/.schema-recovery-required"
acquire_release_lifecycle_lock "$release_root"
trap 'release_lifecycle_lock_cleanup' EXIT
trap 'exit 1' HUP INT TERM

if [ -e "$release_root/.promotion-pending" ] \
  || [ -e "$release_root/current.new" ] || [ -L "$release_root/current.new" ] \
  || [ -e "$release_root/previous.new" ] || [ -L "$release_root/previous.new" ]; then
  printf '%s\n' 'A release-pointer promotion is incomplete. Resolve .promotion-pending, current.new, and previous.new before restoring a database backup.' >&2
  exit 1
fi

schema_recovery_active=false
if [ -e "$schema_recovery_marker" ] || [ -L "$schema_recovery_marker" ]; then
  [ ! -L "$schema_recovery_marker" ] && [ -f "$schema_recovery_marker" ] || {
    printf '%s\n' 'The schema recovery requirement is invalid; application services will not be changed.' >&2
    exit 1
  }
  required_backup_id=$(tr -d '\r\n[:space:]' < "$schema_recovery_marker")
  printf '%s' "$required_backup_id" | grep -Eq '^[0-9]{14}$' && [ "$BACKUP_ID" = "$required_backup_id" ] || {
    printf '%s\n' 'BACKUP_ID does not match the required verified pre-migration backup; application services will not be changed.' >&2
    exit 1
  }
  schema_recovery_active=true
fi

if [ -L "$release_root/current" ]; then
  current_release_dir=$(readlink -f "$release_root/current")
  [ "$(readlink -f "$COMPOSE_FILE")" = "$current_release_dir/docker-compose.production.yml" ] || {
    printf '%s\n' 'COMPOSE_FILE does not match the current production release pointer.' >&2
    exit 1
  }
  [ "$(readlink -f "$COMPOSE_ENV_FILE")" = "$current_release_dir/effective.env" ] || {
    printf '%s\n' 'COMPOSE_ENV_FILE does not match the current production release pointer.' >&2
    exit 1
  }
elif [ "$restart_application" = true ]; then
  printf '%s\n' 'No current release pointer exists; restore without application restart until a compatible release is deployed.' >&2
  exit 1
fi

validate_environment_file "$COMPOSE_ENV_FILE"
resolved_environment=$(resolve_compose_environment "$COMPOSE_ENV_FILE" "$COMPOSE_FILE")
mssql_password=$(read_resolved_environment_value MSSQL_SA_PASSWORD "$resolved_environment")
http_port=$(read_resolved_environment_value CLAWBOT_HTTP_PORT "$resolved_environment")
[ -n "$mssql_password" ] || {
  printf '%s\n' 'MSSQL_SA_PASSWORD is missing from the production environment.' >&2
  exit 1
}
[ -n "$http_port" ] || {
  printf '%s\n' 'CLAWBOT_HTTP_PORT is missing from the production environment.' >&2
  exit 1
}

backup_path="/var/opt/mssql/backup/clawbot-${BACKUP_ID}.bak"
restore_started=false
database_restore_started=false
restore_completed=false
application_recovery_armed=false
previously_running_services=
sqlserver_container=

run_sa_query() {
  container=$1
  query=$2
  {
    printf '%s\n' "$mssql_password"
    printf '%s\n' "$query"
  } | docker exec -i "$container" sh -c '
    IFS= read -r SQLCMDPASSWORD
    export SQLCMDPASSWORD
    exec /opt/mssql-tools18/bin/sqlcmd -S localhost,1433 -U sa -C -d master -b -i /dev/stdin
  '
}

resolve_schema_recovery_backup_id() {
  [ ! -L "$schema_recovery_marker" ] && [ -f "$schema_recovery_marker" ] || return 1
  required_backup_id=$(tr -d '\r\n[:space:]' < "$schema_recovery_marker")
  printf '%s' "$required_backup_id" | grep -Eq '^[0-9]{14}$' || return 1
  printf '%s\n' "$required_backup_id"
}

capture_running_services() {
  for service in agentservice api gateway web; do
    if container=$(docker compose --env-file "$COMPOSE_ENV_FILE" -f "$COMPOSE_FILE" ps -q --status running "$service") && [ -n "$container" ]; then
      previously_running_services="${previously_running_services}${previously_running_services:+ }$service"
    fi
  done
}

restore_cleanup() {
  status=$?
  trap - EXIT HUP INT TERM

  if [ "$restore_started" = true ] && [ -n "$sqlserver_container" ]; then
    set +e
    run_sa_query "$sqlserver_container" "IF DB_ID(N'clawbot') IS NOT NULL ALTER DATABASE [clawbot] SET MULTI_USER;" >/dev/null
    set -e
  fi

  if [ "$status" -ne 0 ] \
    && [ "$application_recovery_armed" = true ] \
    && [ "$restart_application" = true ] \
    && { [ "$database_restore_started" = false ] || [ "$restore_completed" = true ]; } \
    && [ -n "$previously_running_services" ]; then
    printf '%s\n' 'Restore failed before database recovery completed; restarting the previously running application services.' >&2
    set +e
    recovery_status=0
    for service in $previously_running_services; do
      docker compose --env-file "$COMPOSE_ENV_FILE" -f "$COMPOSE_FILE" up -d --wait --no-deps "$service" || recovery_status=1
    done
    if [ "$recovery_status" -ne 0 ]; then
      printf '%s\n' 'Application recovery also failed; inspect the current release before retrying the restore.' >&2
    fi
    set -e
  fi

  release_lifecycle_lock_cleanup
  exit "$status"
}

trap 'restore_cleanup' EXIT
trap 'exit 1' HUP INT TERM

# Reject an invalid or corrupt backup before quiescing a healthy application.
sqlserver_container=$(docker compose --env-file "$COMPOSE_ENV_FILE" -f "$COMPOSE_FILE" ps -q sqlserver)
[ -n "$sqlserver_container" ] || {
  printf '%s\n' 'SQL Server container is not available for restore.' >&2
  exit 1
}
docker exec "$sqlserver_container" test -f "$backup_path"
run_sa_query "$sqlserver_container" "RESTORE VERIFYONLY FROM DISK = N'${backup_path}' WITH CHECKSUM" >/dev/null

capture_running_services
application_recovery_armed=true
docker compose --env-file "$COMPOSE_ENV_FILE" -f "$COMPOSE_FILE" stop agentservice api gateway web
restore_started=true
run_sa_query "$sqlserver_container" "ALTER DATABASE [clawbot] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;" >/dev/null
database_restore_started=true
run_sa_query "$sqlserver_container" "RESTORE DATABASE [clawbot] FROM DISK = N'${backup_path}' WITH REPLACE, CHECKSUM;" >/dev/null
run_sa_query "$sqlserver_container" "ALTER DATABASE [clawbot] SET MULTI_USER;" >/dev/null
restore_started=false
restore_completed=true

if [ "$schema_recovery_active" = true ] && ! rm -f "$schema_recovery_marker"; then
  application_recovery_armed=false
  printf '%s\n' 'The verified backup was restored, but the schema recovery requirement could not be cleared. Application services remain stopped.' >&2
  exit 1
fi

if [ "$restart_application" = false ]; then
  application_recovery_armed=false
  release_lifecycle_lock_cleanup
  trap - EXIT HUP INT TERM
  printf 'Restored verified backup %s without restarting application services.\n' "$BACKUP_ID"
  exit 0
fi

docker compose --env-file "$COMPOSE_ENV_FILE" -f "$COMPOSE_FILE" up -d --wait --no-deps agentservice
docker compose --env-file "$COMPOSE_ENV_FILE" -f "$COMPOSE_FILE" up -d --wait --no-deps api
docker compose --env-file "$COMPOSE_ENV_FILE" -f "$COMPOSE_FILE" up -d --wait --no-deps gateway
docker compose --env-file "$COMPOSE_ENV_FILE" -f "$COMPOSE_FILE" up -d --wait --no-deps web
CLAWBOT_PUBLIC_BASE_URL=${CLAWBOT_PUBLIC_BASE_URL:-http://127.0.0.1:$http_port} "$script_dir/smoke.sh"

application_recovery_armed=false
release_lifecycle_lock_cleanup
trap - EXIT HUP INT TERM
printf 'Restored verified backup %s and restarted the current application release.\n' "$BACKUP_ID"
