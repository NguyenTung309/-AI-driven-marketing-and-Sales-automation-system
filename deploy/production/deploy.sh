#!/usr/bin/env sh
set -eu

compose_file=${COMPOSE_FILE:?COMPOSE_FILE is required}
compose_env=${COMPOSE_ENV_FILE:?COMPOSE_ENV_FILE is required}
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
. "$script_dir/common.sh"

release_root=${CLAWBOT_RELEASE_ROOT:-/etc/clawbot/releases}
schema_recovery_marker="$release_root/.schema-recovery-required"
schema_recovery_signal="$script_dir/.schema-recovery-required"

schema_recovery_required() {
  [ -e "$schema_recovery_marker" ] || [ -L "$schema_recovery_marker" ]
}

record_schema_recovery_requirement() {
  if ! (umask 077; printf '%s\n' "$verified_backup_id" > "$schema_recovery_signal"); then
    printf 'Could not record the candidate schema recovery signal at %s.\n' "$schema_recovery_signal" >&2
    return 1
  fi

  if ! install -d -m 0700 "$release_root"; then
    printf 'Could not create the release storage path for schema recovery at %s.\n' "$release_root" >&2
    return 1
  fi

  marker_temp=$(mktemp "$release_root/.schema-recovery-required.XXXXXX") || return 1
  if ! (umask 077; printf '%s\n' "$verified_backup_id" > "$marker_temp"); then
    rm -f "$marker_temp"
    return 1
  fi
  if ! mv -f "$marker_temp" "$schema_recovery_marker"; then
    rm -f "$marker_temp"
    return 1
  fi
}

clear_schema_recovery_requirement() {
  rm -f "$schema_recovery_signal" "$schema_recovery_marker"
}

if schema_recovery_required; then
  printf '%s\n' 'A previous candidate changed the database schema and requires verified recovery before another deployment.' >&2
  exit 1
fi

validate_environment_file "$compose_env"
resolved_environment=$(resolve_compose_environment "$compose_env" "$compose_file")

read_env_value() {
  read_resolved_environment_value "$1" "$resolved_environment"
}

require_value() {
  key=$1
  value=$2
  [ -n "$value" ] || {
    printf '%s is missing from the protected Compose environment.\n' "$key" >&2
    exit 1
  }
}

for image_variable in SQLSERVER_IMAGE REDIS_IMAGE RABBITMQ_IMAGE QDRANT_IMAGE SEARXNG_IMAGE MINIO_IMAGE MINIO_MC_IMAGE CLAWBOT_API_IMAGE CLAWBOT_GATEWAY_IMAGE CLAWBOT_AGENT_IMAGE CLAWBOT_WEB_IMAGE; do
  image=$(read_env_value "$image_variable")
  require_immutable_image "$image_variable" "$image"
done

mssql_pid=$(read_env_value MSSQL_PID)
mssql_password=$(read_env_value MSSQL_SA_PASSWORD)
app_sql_user=$(read_env_value APP_SQL_USER)
app_sql_password=$(read_env_value APP_SQL_PASSWORD)
rabbitmq_user=$(read_env_value RABBITMQ_USER)
rabbitmq_password=$(read_env_value RABBITMQ_PASSWORD)
http_port=$(read_env_value CLAWBOT_HTTP_PORT)

for required_key in MSSQL_PID MSSQL_SA_PASSWORD APP_SQL_USER APP_SQL_PASSWORD RABBITMQ_USER RABBITMQ_PASSWORD CLAWBOT_HTTP_PORT; do
  require_value "$required_key" "$(read_env_value "$required_key")"
done

case "$mssql_pid" in
  Standard|Enterprise) ;;
  *)
    printf '%s\n' 'MSSQL_PID must be exactly Standard or Enterprise.' >&2
    exit 1
    ;;
esac

[ "$http_port" = "58080" ] || {
  printf '%s\n' 'CLAWBOT_HTTP_PORT must be 58080 for the temporary IP staging ingress.' >&2
  exit 1
}

run_sa_query() {
  sqlserver_container=$1
  query=$2
  database=${3:-master}
  {
    printf '%s\n' "$mssql_password"
    printf '%s\n' "$query"
  } | docker exec -i "$sqlserver_container" sh -c '
    IFS= read -r SQLCMDPASSWORD
    export SQLCMDPASSWORD
    database=$1
    exec /opt/mssql-tools18/bin/sqlcmd -S localhost,1433 -U sa -C -d "$database" -b -h -1 -W -i /dev/stdin
  ' sh "$database"
}

database_exists() {
  sqlserver_container=$1
  result=$(run_sa_query "$sqlserver_container" "SET NOCOUNT ON; SELECT CASE WHEN DB_ID(N'clawbot') IS NULL THEN 0 ELSE 1 END;")
  result=$(printf '%s' "$result" | tr -d '[:space:]')
  case "$result" in
    0|1) printf '%s\n' "$result" ;;
    *)
      printf '%s\n' 'Database existence probe returned an invalid result.' >&2
      return 1
      ;;
  esac
}

current_service_container() {
  service=$1
  containers=$(docker ps -aq \
    --filter label=com.docker.compose.project=clawbot-production \
    --filter "label=com.docker.compose.service=$service")
  case "$containers" in
    '') return 0 ;;
    *'
'*)
      printf 'Multiple existing Clawbot %s containers were found.\n' "$service" >&2
      return 1
      ;;
    *) printf '%s\n' "$containers" ;;
  esac
}

require_running_container() {
  container=$1
  service=$2
  is_running=$(docker inspect -f '{{.State.Running}}' "$container")
  [ "$is_running" = true ] || {
    printf 'Existing Clawbot %s container is stopped; recover it before deploying.\n' "$service" >&2
    exit 1
  }
}

preflight_http_port() {
  current_web_container=$(docker compose --env-file "$compose_env" -f "$compose_file" ps -q web 2>/dev/null || true)
  port_containers=$(docker ps -q --filter "publish=$http_port")

  if [ -n "$port_containers" ]; then
    while IFS= read -r port_container; do
      [ "$port_container" = "$current_web_container" ] || {
        printf 'Port %s is already published by a container outside the current Clawbot release.\n' "$http_port" >&2
        return 1
      }
    done <<EOF
$port_containers
EOF
    return 0
  fi

  if ss -lnt "sport = :$http_port" | grep -q LISTEN; then
    printf 'Port %s is already in use by a non-Docker process.\n' "$http_port" >&2
    return 1
  fi
}

preflight_agentservice_tls_mount() {
  agentservice_image=$(read_env_value CLAWBOT_AGENT_IMAGE)
  certificate_path=$(read_env_value AGENT_SERVICE_TLS_CERTIFICATE_PATH)
  [ -f "$certificate_path" ] || {
    printf '%s\n' 'AgentService TLS certificate file is missing before container access validation.' >&2
    return 1
  }

  docker run --rm \
    --read-only \
    --user app \
    --entrypoint /bin/sh \
    --mount "type=bind,src=$certificate_path,dst=/run/secrets/agentservice-grpc.pfx,readonly" \
    "$agentservice_image" \
    -c 'test -r /run/secrets/agentservice-grpc.pfx'
}

preflight_api_tls_ca_mount() {
  api_image=$(read_env_value CLAWBOT_API_IMAGE)
  certificate_authority_path=$(read_env_value AGENT_SERVICE_TLS_CA_CERTIFICATE_PATH)
  [ -f "$certificate_authority_path" ] || {
    printf '%s\n' 'AgentService TLS CA certificate file is missing before API container access validation.' >&2
    return 1
  }

  docker run --rm \
    --read-only \
    --user app \
    --entrypoint /bin/sh \
    --mount "type=bind,src=$certificate_authority_path,dst=/run/secrets/agentservice-ca.pem,readonly" \
    "$api_image" \
    -c 'test -r /run/secrets/agentservice-ca.pem'
}

authenticate_rabbitmq_user() {
  rabbitmq_container=$1
  username=$2
  password=$3
  printf '%s\n' "$password" | docker exec -i "$rabbitmq_container" sh -c '
    IFS= read -r password
    exec rabbitmqctl authenticate_user "$1" "$password"
  ' sh "$username" >/dev/null
}

add_rabbitmq_user() {
  rabbitmq_container=$1
  username=$2
  password=$3
  printf '%s\n' "$password" | docker exec -i "$rabbitmq_container" sh -c '
    IFS= read -r password
    exec rabbitmqctl add_user "$1" "$password"
  ' sh "$username"
}

current_release_env=${CURRENT_RELEASE_ENV_FILE:-}
current_compose_file=${CURRENT_COMPOSE_FILE:-}
has_current_release=false
current_resolved_environment=

validate_current_release() {
  [ -n "$current_release_env" ] || return 0
  [ -n "$current_compose_file" ] || {
    printf '%s\n' 'CURRENT_COMPOSE_FILE is required when CURRENT_RELEASE_ENV_FILE is set.' >&2
    exit 1
  }
  validate_environment_file "$current_release_env"
  current_resolved_environment=$(resolve_compose_environment "$current_release_env" "$current_compose_file")

  for key in \
    SQLSERVER_IMAGE REDIS_IMAGE RABBITMQ_IMAGE QDRANT_IMAGE SEARXNG_IMAGE MINIO_IMAGE MINIO_MC_IMAGE \
    MSSQL_PID MSSQL_SA_PASSWORD APP_SQL_USER APP_SQL_PASSWORD SQLSERVER_BACKUP_DIR \
    REDIS_PASSWORD RABBITMQ_USER RABBITMQ_PASSWORD \
    MINIO_ROOT_USER MINIO_ROOT_PASSWORD MINIO_APP_ACCESS_KEY MINIO_APP_SECRET_KEY; do
    candidate_value=$(read_resolved_environment_value "$key" "$resolved_environment")
    current_value=$(read_resolved_environment_value "$key" "$current_resolved_environment")
    [ "$candidate_value" = "$current_value" ] || {
      printf 'Shared infrastructure setting %s changed; use a controlled maintenance procedure instead of an application release.\n' "$key" >&2
      exit 1
    }
  done

  has_current_release=true
}

existing_sqlserver=
existing_database=0
previously_running_services=
verified_backup_id=
schema_mutation_run_id="deploy-$(date -u +%Y%m%d%H%M%S)-$$"

capture_current_application_state() {
  if [ "$has_current_release" = false ]; then
    for service in agentservice api gateway web; do
      unmanaged_container=$(current_service_container "$service")
      [ -z "$unmanaged_container" ] && continue
      unmanaged_running=$(docker inspect -f '{{.State.Running}}' "$unmanaged_container")
      [ "$unmanaged_running" != true ] && continue
      printf 'A running Clawbot %s container exists without a current release pointer. Recover or adopt that deployment through a controlled procedure before migration.\n' "$service" >&2
      exit 1
    done
    return 0
  fi

  for service in agentservice api gateway web; do
    running_container=$(docker compose --env-file "$current_release_env" -f "$current_compose_file" ps --status running -q "$service")
    [ -z "$running_container" ] || previously_running_services="$previously_running_services $service"
  done
}

preflight_existing_services() {
  existing_sqlserver=$(current_service_container sqlserver)
  [ -n "$existing_sqlserver" ] || return 0

  require_running_container "$existing_sqlserver" sqlserver
  existing_database=$(database_exists "$existing_sqlserver")
  if [ "$existing_database" = "1" ]; then
    MSSQL_SA_PASSWORD="$mssql_password" SQLSERVER_CONTAINER="$existing_sqlserver" \
      APP_SQL_USER="$app_sql_user" APP_SQL_PASSWORD="$app_sql_password" \
      MIGRATIONS_DIR="$script_dir/migrations" MIGRATION_PREFLIGHT_ONLY=true "$script_dir/migrate.sh"
  fi

  [ "$has_current_release" = true ] || return 0
  existing_rabbitmq=$(current_service_container rabbitmq)
  [ -n "$existing_rabbitmq" ] || {
    printf '%s\n' 'Current release has no RabbitMQ container; recover it before deploying.' >&2
    exit 1
  }
  require_running_container "$existing_rabbitmq" rabbitmq
  authenticate_rabbitmq_user "$existing_rabbitmq" "$rabbitmq_user" "$rabbitmq_password"
}

backup_existing_database() {
  [ -n "$existing_sqlserver" ] || {
    printf '%s\n' 'No existing Clawbot SQL Server container found; skipping pre-recreation backup.'
    return 0
  }
  [ "$existing_database" = "1" ] || {
    printf '%s\n' 'Existing Clawbot SQL Server has no clawbot database; skipping pre-recreation backup.'
    return 0
  }

  docker exec -u root "$existing_sqlserver" sh -c 'chown 10001:0 /var/opt/mssql/backup && chmod 0770 /var/opt/mssql/backup'
  verified_backup_id=$(MSSQL_SA_PASSWORD="$mssql_password" SQLSERVER_CONTAINER="$existing_sqlserver" \
    "$script_dir/backup.sh")
  case "$verified_backup_id" in
    [0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]) ;;
    *) printf '%s\n' 'Database backup returned an invalid identifier.' >&2; return 1 ;;
  esac
  printf 'Verified database backup: %s\n' "$verified_backup_id"
}

verify_application_stopped() {
  for service in agentservice api gateway web; do
    running_container=$(docker compose --env-file "$compose_env" -f "$compose_file" ps --status running -q "$service")
    [ -z "$running_container" ] || {
      printf 'Application service %s is still running after quiesce.\n' "$service" >&2
      return 1
    }
  done
}

application_recovery_armed=false
candidate_application_started=false
migration_invoked=false
schema_mutation_started=false

schema_mutation_committed() {
  [ -n "${sqlserver_container:-}" ] || return 2
  if ! result=$(run_sa_query "$sqlserver_container" "SET NOCOUNT ON; IF OBJECT_ID(N'dbo.schema_mutation_runs', N'U') IS NULL SELECT 0; ELSE SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.schema_mutation_runs WHERE run_id = N'$schema_mutation_run_id') THEN 1 ELSE 0 END;" clawbot); then
    return 2
  fi
  case "$(printf '%s' "$result" | tr -d '[:space:]')" in
    1) return 0 ;;
    0) return 1 ;;
    *) return 2 ;;
  esac
}

# A failed candidate's container logs only live on the host and are overwritten by the next
# deployment, so the pipeline sees one "is unhealthy" line and every hypothesis costs another
# deploy cycle. Password-shaped values are redacted because startup logs echo connection strings.
print_candidate_diagnostics() {
  set +e
  for service in agentservice api gateway web; do
    diagnostic_container=$(docker compose --env-file "$compose_env" -f "$compose_file" ps -aq "$service" 2>/dev/null)
    [ -n "$diagnostic_container" ] || continue
    diagnostic_state=$(docker inspect \
      -f 'status={{.State.Status}} exit={{.State.ExitCode}} oom={{.State.OOMKilled}} restarts={{.RestartCount}} health={{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' \
      "$diagnostic_container" 2>/dev/null)
    [ -n "$diagnostic_state" ] || continue
    case "$diagnostic_state" in
      status=running*health=healthy) continue ;;
      status=running*health=none) continue ;;
    esac

    printf 'Candidate %s: %s\n' "$service" "$diagnostic_state" >&2
    docker logs --tail 120 "$diagnostic_container" 2>&1 \
      | sed -E 's/([Pp]assword=)[^;"[:space:]]*/\1<redacted>/g; s#(://[^:@[:space:]]+:)[^@[:space:]]*@#\1<redacted>@#g' >&2
    docker inspect \
      -f '{{if .State.Health}}{{range .State.Health.Log}}health probe exit={{.ExitCode}}: {{.Output}}{{end}}{{end}}' \
      "$diagnostic_container" 2>/dev/null | tail -n 5 >&2
  done
  set -e
}

print_restore_instruction() {
  if [ -z "$verified_backup_id" ]; then
    printf '%s\n' 'No pre-migration database backup is available; do not start the prior application until an operator has assessed the schema state.' >&2
  elif [ -n "$current_release_env" ] && [ -n "$current_compose_file" ]; then
    printf 'Restore the verified backup with: COMPOSE_FILE=%s COMPOSE_ENV_FILE=%s BACKUP_ID=%s CONFIRM_DATABASE_RESTORE=RESTORE_VERIFIED_BACKUP %s/restore-verified-backup.sh\n' "$current_compose_file" "$current_release_env" "$verified_backup_id" "$script_dir" >&2
  else
    printf 'Restore the verified backup without restarting applications: COMPOSE_FILE=%s COMPOSE_ENV_FILE=%s BACKUP_ID=%s RESTART_APPLICATION=false CONFIRM_DATABASE_RESTORE=RESTORE_VERIFIED_BACKUP %s/restore-verified-backup.sh\n' "$compose_file" "$compose_env" "$verified_backup_id" "$script_dir" >&2
  fi
}

recover_previous_application() {
  status=$?
  trap - EXIT HUP INT TERM
  if [ "$migration_invoked" = true ]; then
    if schema_mutation_committed; then
      schema_mutation_started=true
    else
      mutation_probe_status=$?
      if [ "$mutation_probe_status" -eq 1 ]; then
        if ! clear_schema_recovery_requirement; then
          schema_mutation_started=true
          printf '%s\n' 'The migration transaction did not commit, but the schema recovery marker could not be cleared; automatic application recovery is blocked.' >&2
        fi
      else
        schema_mutation_started=true
        printf '%s\n' 'Could not determine whether the schema mutation committed; automatic application recovery is blocked.' >&2
      fi
    fi
  fi

  if [ "$status" -ne 0 ]; then
    if [ "$schema_mutation_started" = true ] && ! record_schema_recovery_requirement; then
      printf '%s\n' 'Could not persist the schema recovery requirement; do not start another lifecycle operation until an operator verifies the database and release state.' >&2
    fi

    if [ "$candidate_application_started" = true ]; then
      print_candidate_diagnostics
      printf '%s\n' 'Stopping failed candidate application services.' >&2
      set +e
      docker compose --env-file "$compose_env" -f "$compose_file" stop agentservice api gateway web
      candidate_stop_status=$?
      set -e
      if [ "$candidate_stop_status" -ne 0 ]; then
        printf '%s\n' 'Candidate application cleanup also failed; verify no candidate service remains running before database recovery.' >&2
      fi
    fi

    if [ "$application_recovery_armed" = true ] && [ -n "$previously_running_services" ]; then
      if [ "$schema_mutation_started" = true ]; then
        printf '%s\n' 'Candidate deployment failed after schema mutation; automatic application recovery is blocked to prevent running the prior binary against an incompatible schema.' >&2
        print_restore_instruction
      else
        printf '%s\n' 'Candidate deployment failed before schema mutation; restoring the prior application release.' >&2
        set +e
        recovery_status=0
        for service in $previously_running_services; do
          docker compose --env-file "$current_release_env" -f "$current_compose_file" up -d --wait --no-deps "$service" || recovery_status=1
        done
        if [ "$recovery_status" -ne 0 ]; then
          printf '%s\n' 'Prior application recovery also failed; inspect the preserved release and verified database backup.' >&2
        fi
        set -e
      fi
    elif [ "$schema_mutation_started" = true ]; then
      printf '%s\n' 'Candidate deployment failed after schema mutation; automatic application recovery is blocked because no prior application release was running.' >&2
      print_restore_instruction
    fi
  fi

  exit "$status"
}

# Phase A: complete all non-mutating validation before backup, pulls, credentials, or service changes.
validate_current_release
docker compose --env-file "$compose_env" -f "$compose_file" config --quiet
preflight_http_port
capture_current_application_state
preflight_existing_services

# Phase B: obtain immutable candidate images while the current application remains available.
docker compose --env-file "$compose_env" -f "$compose_file" pull
preflight_agentservice_tls_mount
preflight_api_tls_ca_mount
trap 'recover_previous_application' EXIT
trap 'exit 1' HUP INT TERM

# Phase C: quiesce only existing application services, then recover the prior release on later failure.
if [ -n "$previously_running_services" ]; then
  application_recovery_armed=true
  docker compose --env-file "$compose_env" -f "$compose_file" stop $previously_running_services
  verify_application_stopped
fi

# Capture all committed writes after quiescence and before any service can reconcile SQL Server.
backup_existing_database

# Phase D: reconcile unchanged infrastructure and apply forward-only schema changes.
docker compose --env-file "$compose_env" -f "$compose_file" up -d sqlserver redis rabbitmq qdrant searxng minio --wait
sqlserver_container=$(docker compose --env-file "$compose_env" -f "$compose_file" ps -q sqlserver)
[ -n "$sqlserver_container" ] || { printf '%s\n' 'SQL Server container was not created.' >&2; exit 1; }
docker exec -u root "$sqlserver_container" sh -c 'chown 10001:0 /var/opt/mssql/backup && chmod 0770 /var/opt/mssql/backup'

rabbitmq_container=$(docker compose --env-file "$compose_env" -f "$compose_file" ps -q rabbitmq)
[ -n "$rabbitmq_container" ] || { printf '%s\n' 'RabbitMQ container was not created.' >&2; exit 1; }
if [ "$has_current_release" = true ]; then
  authenticate_rabbitmq_user "$rabbitmq_container" "$rabbitmq_user" "$rabbitmq_password"
else
  rabbitmq_users=$(docker exec "$rabbitmq_container" rabbitmqctl list_users -q)
  if printf '%s\n' "$rabbitmq_users" | awk -v expected="$rabbitmq_user" '$1 == expected { found = 1 } END { exit found ? 0 : 1 }'; then
    authenticate_rabbitmq_user "$rabbitmq_container" "$rabbitmq_user" "$rabbitmq_password"
  else
    add_rabbitmq_user "$rabbitmq_container" "$rabbitmq_user" "$rabbitmq_password"
  fi
  docker exec "$rabbitmq_container" rabbitmqctl set_permissions -p / "$rabbitmq_user" '.*' '.*' '.*'
fi

if [ "$has_current_release" = true ]; then
  case "$verified_backup_id" in
    [0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]) ;;
    *) printf '%s\n' 'A verified pre-migration database backup is required before changing the schema.' >&2; exit 1 ;;
  esac
fi
if ! record_schema_recovery_requirement; then
  printf '%s\n' 'Could not persist the schema recovery requirement; refusing to invoke migrations.' >&2
  exit 1
fi
migration_invoked=true
MSSQL_SA_PASSWORD="$mssql_password" SQLSERVER_CONTAINER="$sqlserver_container" \
  APP_SQL_USER="$app_sql_user" APP_SQL_PASSWORD="$app_sql_password" \
  MIGRATIONS_DIR="$script_dir/migrations" REPAIRS_DIR="$script_dir" \
  SCHEMA_MUTATION_RUN_ID="$schema_mutation_run_id" "$script_dir/migrate.sh"
if schema_mutation_committed; then
  schema_mutation_started=true
else
  mutation_probe_status=$?
  [ "$mutation_probe_status" -ne 2 ] || {
    printf '%s\n' 'Could not determine whether the schema mutation committed; refusing to continue application deployment.' >&2
    exit 1
  }

  if ! clear_schema_recovery_requirement; then
    printf '%s\n' 'The migration transaction did not commit, but the schema recovery marker could not be cleared.' >&2
    exit 1
  fi
fi

if [ "$schema_mutation_started" = true ] && ! record_schema_recovery_requirement; then
  printf '%s\n' 'Could not persist the schema recovery requirement; refusing to start the candidate application.' >&2
  exit 1
fi

if [ "$has_current_release" = false ]; then
  docker compose --env-file "$compose_env" -f "$compose_file" rm -sf minio-init
  docker compose --env-file "$compose_env" -f "$compose_file" up -d --wait minio-init
fi

run_smoke() {
  CLAWBOT_PUBLIC_BASE_URL=${CLAWBOT_PUBLIC_BASE_URL:-http://127.0.0.1:$http_port} \
    "$script_dir/smoke.sh"
}

# Phase E: start the candidate passively and require a public smoke check. A passive candidate
# serves HTTP but consumes no queue message, runs no schedule, and processes no background job, so
# a candidate that fails here leaves no background side effect behind for the rollback to undo.
candidate_application_started=true
CLAWBOT_STARTUP_MODE=passive docker compose --env-file "$compose_env" -f "$compose_file" up -d --wait --no-deps agentservice
CLAWBOT_STARTUP_MODE=passive docker compose --env-file "$compose_env" -f "$compose_file" up -d --wait --no-deps api
docker compose --env-file "$compose_env" -f "$compose_file" up -d --wait --no-deps gateway
docker compose --env-file "$compose_env" -f "$compose_file" up -d --wait --no-deps web

run_smoke

# Phase F: activate the proven candidate. Only api and agentservice read the startup mode, so
# gateway and web keep serving while those two are recreated with background processing enabled,
# and the smoke check is repeated before the release pointer is allowed to move.
printf '%s\n' 'Smoke checks passed; activating background processing on the candidate.'
docker compose --env-file "$compose_env" -f "$compose_file" up -d --wait --no-deps agentservice
docker compose --env-file "$compose_env" -f "$compose_file" up -d --wait --no-deps api

run_smoke

application_recovery_armed=false
trap - EXIT HUP INT TERM
printf '%s\n' 'Candidate application deployment completed.'
