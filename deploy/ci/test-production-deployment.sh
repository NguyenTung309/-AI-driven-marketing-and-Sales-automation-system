#!/usr/bin/env sh
set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT INT TERM

fail() {
  printf 'Production deployment test failed: %s\n' "$1" >&2
  exit 1
}

assert_file_contains() {
  file=$1
  expected=$2
  grep -qxF "$expected" "$file" || fail "missing expected value '$expected' in $file"
}

assert_no_duplicate_keys() {
  file=$1
  awk -F= '
    /^[[:space:]]*($|#)/ { next }
    { if (seen[$1]++) exit 1 }
  ' "$file" || fail "duplicate environment keys in $file"
}

digest() {
  character=$1
  printf '%064d' 0 | tr '0' "$character"
}

fake_bin="$temp_dir/bin"
mkdir -p "$fake_bin"
cat > "$fake_bin/docker" <<'DOCKER'
#!/usr/bin/env sh
set -eu
previous=
environment_file=
for argument in "$@"; do
  if [ "$previous" = "--env-file" ]; then
    environment_file=$argument
  fi
  previous=$argument
done
if [ -n "$environment_file" ]; then
  cp "$environment_file" "$TEST_CAPTURED_ENV"
fi
# The startup mode is part of the traced command: a candidate must be started passively.
printf '%s%s\n' "${CLAWBOT_STARTUP_MODE:+CLAWBOT_STARTUP_MODE=$CLAWBOT_STARTUP_MODE }" "$*" >> "$TEST_DOCKER_TRACE"
case " $* " in
  *" config --environment "*)
    [ -n "${TEST_RESOLVED_ENV:-}" ] && cat "$TEST_RESOLVED_ENV"
    ;;
  *" ps -aq "*)
    if [ -n "${TEST_UNMANAGED_RUNNING_SERVICE:-}" ]; then
      case " $* " in
        *"label=com.docker.compose.service=${TEST_UNMANAGED_RUNNING_SERVICE}"*)
          printf '%s\n' 'unmanaged-clawbot-container'
          ;;
      esac
    fi
    ;;
  *" inspect -f {{.State.Running}} "*)
    printf '%s\n' true
    ;;
esac
DOCKER
chmod 755 "$fake_bin/docker"
cat > "$fake_bin/curl" <<'CURL'
#!/usr/bin/env sh
set -eu
# Traced alongside Docker so the tests can assert what ran before and after a smoke check.
[ -z "${TEST_DOCKER_TRACE:-}" ] || printf 'curl %s\n' "$*" >> "$TEST_DOCKER_TRACE"
exit 0
CURL
chmod 755 "$fake_bin/curl"
cat > "$fake_bin/install" <<'INSTALL'
#!/usr/bin/env sh
set -eu
if [ "$1" = "-d" ]; then
  shift
  [ "$1" != "-m" ] || shift 2
  mkdir -p "$1"
  exit 0
fi
[ "$1" != "-m" ] || shift 2
destination=
for argument in "$@"; do
  destination=$argument
done
if [ -d "$destination" ]; then
  for source in "$@"; do
    [ "$source" = "$destination" ] || cp "$source" "$destination"
  done
else
  cp "$1" "$destination"
fi
INSTALL
chmod 755 "$fake_bin/install"
cat > "$fake_bin/mv" <<'MOVE'
#!/usr/bin/env sh
set -eu
source=
for argument in "$@"; do
  case "$argument" in -*) ;; *) source=$argument; break ;; esac
done
destination=
for argument in "$@"; do
  destination=$argument
done
case "$source" in
  *.new)
    target=$(readlink "$source")
    rm -rf "$destination"
    ln -s "$target" "$destination"
    rm -f "$source"
    ;;
  *) /bin/mv "$@" ;;
esac
MOVE
chmod 755 "$fake_bin/mv"

# Duplicate keys must fail before deploy.sh invokes Docker.
mkdir -p "$temp_dir/release"
cp "$root_dir/deploy/production/common.sh" "$temp_dir/release/common.sh"
cp "$root_dir/deploy/production/deploy.sh" "$temp_dir/release/deploy.sh"
cp "$root_dir/deploy/production/backup.sh" "$temp_dir/release/backup.sh"
cp "$root_dir/deploy/production/migrate.sh" "$temp_dir/release/migrate.sh"
cp "$root_dir/deploy/production/smoke.sh" "$temp_dir/release/smoke.sh"
chmod 755 "$temp_dir/release"/*.sh
cat > "$temp_dir/duplicate.env" <<'ENV'
REDIS_PASSWORD=first
REDIS_PASSWORD=second
ENV
: > "$temp_dir/docker-trace"
if PATH="$fake_bin:$PATH" TEST_CAPTURED_ENV="$temp_dir/captured.env" TEST_DOCKER_TRACE="$temp_dir/docker-trace" \
  COMPOSE_FILE="$temp_dir/unused.yml" COMPOSE_ENV_FILE="$temp_dir/duplicate.env" "$temp_dir/release/deploy.sh" >"$temp_dir/deploy.out" 2>"$temp_dir/deploy.err"; then
  fail "duplicate environment keys were accepted"
fi
grep -q 'REDIS_PASSWORD' "$temp_dir/deploy.err" || fail "duplicate key was not identified"
[ ! -s "$temp_dir/docker-trace" ] || fail "duplicate environment keys reached Docker"

# Deployment reads values resolved by Docker Compose, and rejects unsupported SQL Server editions before mutation.
invalid_pid_digest=$(digest 5)
cat > "$temp_dir/invalid-pid.env" <<ENV
SQLSERVER_IMAGE=sqlserver@sha256:$invalid_pid_digest
REDIS_IMAGE=redis@sha256:$invalid_pid_digest
RABBITMQ_IMAGE=rabbitmq@sha256:$invalid_pid_digest
QDRANT_IMAGE=qdrant@sha256:$invalid_pid_digest
SEARXNG_IMAGE=searxng@sha256:$invalid_pid_digest
MINIO_IMAGE=minio@sha256:$invalid_pid_digest
MINIO_MC_IMAGE=minio-mc@sha256:$invalid_pid_digest
CLAWBOT_API_IMAGE=api@sha256:$invalid_pid_digest
CLAWBOT_GATEWAY_IMAGE=gateway@sha256:$invalid_pid_digest
CLAWBOT_AGENT_IMAGE=agent@sha256:$invalid_pid_digest
CLAWBOT_WEB_IMAGE=web@sha256:$invalid_pid_digest
MSSQL_PID=Developer
MSSQL_SA_PASSWORD=validation-only
APP_SQL_USER=clawbot_app
APP_SQL_PASSWORD='quoted value # kept literal'
RABBITMQ_USER=clawbot
RABBITMQ_PASSWORD=validation-only
CLAWBOT_HTTP_PORT=58080
ENV
cat > "$temp_dir/resolved-invalid-pid.env" <<ENV
SQLSERVER_IMAGE=sqlserver@sha256:$invalid_pid_digest
REDIS_IMAGE=redis@sha256:$invalid_pid_digest
RABBITMQ_IMAGE=rabbitmq@sha256:$invalid_pid_digest
QDRANT_IMAGE=qdrant@sha256:$invalid_pid_digest
SEARXNG_IMAGE=searxng@sha256:$invalid_pid_digest
MINIO_IMAGE=minio@sha256:$invalid_pid_digest
MINIO_MC_IMAGE=minio-mc@sha256:$invalid_pid_digest
CLAWBOT_API_IMAGE=api@sha256:$invalid_pid_digest
CLAWBOT_GATEWAY_IMAGE=gateway@sha256:$invalid_pid_digest
CLAWBOT_AGENT_IMAGE=agent@sha256:$invalid_pid_digest
CLAWBOT_WEB_IMAGE=web@sha256:$invalid_pid_digest
MSSQL_PID=Developer
MSSQL_SA_PASSWORD=validation-only
APP_SQL_USER=clawbot_app
APP_SQL_PASSWORD=quoted value # kept literal
RABBITMQ_USER=clawbot
RABBITMQ_PASSWORD=validation-only
CLAWBOT_HTTP_PORT=58080
ENV
: > "$temp_dir/docker-trace"
if PATH="$fake_bin:$PATH" TEST_CAPTURED_ENV="$temp_dir/captured.env" TEST_DOCKER_TRACE="$temp_dir/docker-trace" \
  TEST_RESOLVED_ENV="$temp_dir/resolved-invalid-pid.env" \
  COMPOSE_FILE="$temp_dir/unused.yml" COMPOSE_ENV_FILE="$temp_dir/invalid-pid.env" \
  "$temp_dir/release/deploy.sh" >"$temp_dir/deploy.out" 2>"$temp_dir/deploy.err"; then
  fail "unsupported SQL Server edition was accepted"
fi
grep -q 'Standard or Enterprise' "$temp_dir/deploy.err" || fail "unsupported SQL Server edition was not identified"
if grep -Eq ' (pull|up|stop|exec) ' "$temp_dir/docker-trace"; then
  fail "unsupported SQL Server edition reached a mutating Docker command"
fi
if grep -Fq "'quoted value # kept literal'" "$temp_dir/deploy.out" "$temp_dir/deploy.err" "$temp_dir/docker-trace"; then
  fail "raw Compose secret representation leaked during preflight"
fi

# A running Clawbot stack without a current-release pointer cannot safely be migrated or adopted implicitly.
sed 's/^MSSQL_PID=Developer$/MSSQL_PID=Standard/' "$temp_dir/invalid-pid.env" > "$temp_dir/unmanaged-stack.env"
sed 's/^MSSQL_PID=Developer$/MSSQL_PID=Standard/' "$temp_dir/resolved-invalid-pid.env" > "$temp_dir/resolved-unmanaged-stack.env"
printf '%s\n' 'services: {}' > "$temp_dir/unmanaged-stack.yml"
: > "$temp_dir/unmanaged-stack.trace"
if PATH="$fake_bin:$PATH" TEST_CAPTURED_ENV="$temp_dir/unmanaged-stack.captured.env" \
  TEST_DOCKER_TRACE="$temp_dir/unmanaged-stack.trace" \
  TEST_RESOLVED_ENV="$temp_dir/resolved-unmanaged-stack.env" \
  TEST_UNMANAGED_RUNNING_SERVICE=api \
  COMPOSE_FILE="$temp_dir/unmanaged-stack.yml" COMPOSE_ENV_FILE="$temp_dir/unmanaged-stack.env" \
  SKIP_BACKUP=true SKIP_MIGRATIONS=true \
  "$temp_dir/release/deploy.sh" >"$temp_dir/unmanaged-stack.out" 2>"$temp_dir/unmanaged-stack.err"; then
  fail "running unmanaged Clawbot stack was accepted"
fi
grep -Fq 'running Clawbot api container exists without a current release pointer' "$temp_dir/unmanaged-stack.err" || fail "unmanaged stack error was not identified"
if grep -Eq ' (pull|up|stop|exec|rm) ' "$temp_dir/unmanaged-stack.trace"; then
  fail "unmanaged stack preflight reached a mutating Docker command"
fi

# A failed candidate release must not move the current or previous release pointers.
create_install_stage() {
  stage_dir=$1
  mkdir -p "$stage_dir/migrations"
  cp "$root_dir/deploy/production/common.sh" "$stage_dir/common.sh"
  cp "$root_dir/deploy/production/install-release.sh" "$stage_dir/install-release.sh"
  cat > "$stage_dir/deploy.sh" <<'DEPLOY'
#!/usr/bin/env sh
set -eu
[ "${TEST_INSTALL_FAIL:-false}" != true ] || exit 1
if [ "${TEST_INSTALL_SCHEMA_MUTATION:-false}" = true ]; then
  printf '%s\n' "${TEST_SCHEMA_BACKUP_ID:-}" > "${CLAWBOT_RELEASE_ROOT:?}/.schema-recovery-required"
  exit 1
fi
printf '%s\n' "$COMPOSE_ENV_FILE" > "$TEST_DEPLOY_MARKER"
DEPLOY
  for script in backup.sh migrate.sh rollback.sh restore-verified-backup.sh smoke.sh; do
    printf '#!/usr/bin/env sh\nexit 0\n' > "$stage_dir/$script"
  done
  chmod 755 "$stage_dir"/*.sh
  for contract in \
    repair_tenant_runtime_columns.sql \
    repair_inbox_runtime_columns.sql \
    repair_agent_runtime_columns.sql \
    repair_inbox_collaboration_tables.sql \
    repair_agent_allowed_tools.sql \
    verify_content_render_tasks.sql \
    verify_database_table_consolidation.sql; do
    printf 'SELECT 1;\n' > "$stage_dir/$contract"
  done
  printf 'SELECT 1;\n' > "$stage_dir/migrations/0001_init.sql"
  printf 'name: clawbot-production\n' > "$stage_dir/docker-compose.production.yml"
  printf 'Bootstrap__InitialAdminEmail=release@example.test\n' > "$stage_dir/runtime.env"
  printf 'use_default_settings: true\n' > "$stage_dir/settings.yml"
  cat > "$stage_dir/production.env" <<'ENV'
SQLSERVER_IMAGE=sqlserver@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
CLAWBOT_RUNTIME_ENV_FILE=/etc/clawbot/runtime.env
SEARXNG_SETTINGS_FILE=/etc/clawbot/searxng/settings.yml
ENV
  printf 'CLAWBOT_API_IMAGE=api@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n' > "$stage_dir/images.env"
}

# A failed candidate that committed a schema mutation must block later release changes while allowing a verified restore.
schema_recovery_root="$temp_dir/schema-recovery-releases"
schema_failure_stage="$temp_dir/schema-failure-stage"
create_install_stage "$schema_failure_stage"
schema_failure_id="$(printf '%040d' 0 | tr '0' 'c')-1-1"
if PATH="$fake_bin:$PATH" CLAWBOT_RELEASE_ROOT="$schema_recovery_root" TEST_DEPLOY_MARKER="$temp_dir/deploy-marker" \
  TEST_INSTALL_SCHEMA_MUTATION=true TEST_SCHEMA_BACKUP_ID=20260809010101 \
  sh "$root_dir/deploy/production/install-release.sh" "$schema_failure_stage" "$schema_failure_id" >/dev/null 2>&1; then
  fail "schema-mutated candidate failure was accepted"
fi
[ -f "$schema_recovery_root/.schema-recovery-required" ] || fail "schema-mutated candidate failure did not record a schema recovery requirement"
[ ! -e "$schema_recovery_root/.promotion-pending" ] || fail "schema recovery requirement blocked verified database restoration"

retry_stage="$temp_dir/schema-recovery-retry-stage"
create_install_stage "$retry_stage"
retry_release_id="$(printf '%040d' 0 | tr '0' 'd')-1-1"
if PATH="$fake_bin:$PATH" CLAWBOT_RELEASE_ROOT="$schema_recovery_root" TEST_DEPLOY_MARKER="$temp_dir/deploy-marker" \
  sh "$root_dir/deploy/production/install-release.sh" "$retry_stage" "$retry_release_id" >/dev/null 2>&1; then
  fail "schema recovery requirement did not block a later release installation"
fi

release_root="$temp_dir/releases"
old_release="$release_root/old-release"
mkdir -p "$old_release"
printf 'name: clawbot-production\n' > "$old_release/docker-compose.production.yml"
printf 'use_default_settings: true\n' > "$old_release/settings.yml"
printf 'CLAWBOT_RUNTIME_ENV_FILE=%s\n' "$old_release/runtime.env" > "$old_release/effective.env"
printf 'Bootstrap__InitialAdminEmail=old@example.test\n' > "$old_release/runtime.env"
if ln -s "$old_release" "$release_root/current" 2>/dev/null && [ -L "$release_root/current" ]; then

success_stage="$temp_dir/success-stage"
create_install_stage "$success_stage"
success_release_id="$(printf '%040d' 0 | tr '0' 'a')-1-1"
if ! PATH="$fake_bin:$PATH" CLAWBOT_RELEASE_ROOT="$release_root" TEST_DEPLOY_MARKER="$temp_dir/deploy-marker" \
  sh "$root_dir/deploy/production/install-release.sh" "$success_stage" "$success_release_id" >/dev/null; then
  fail "successful candidate release installation failed"
fi
[ "$(readlink -f "$release_root/current")" = "$release_root/$success_release_id" ] || fail "successful candidate was not promoted to current"
[ "$(readlink -f "$release_root/previous")" = "$old_release" ] || fail "successful candidate did not preserve previous release"
[ ! -e "$release_root/.promotion-pending" ] || fail "successful candidate retained the promotion marker"

failed_stage="$temp_dir/failed-stage"
create_install_stage "$failed_stage"
failed_release_id="$(printf '%040d' 0 | tr '0' 'b')-2-1"
if PATH="$fake_bin:$PATH" CLAWBOT_RELEASE_ROOT="$release_root" TEST_DEPLOY_MARKER="$temp_dir/deploy-marker" TEST_INSTALL_FAIL=true \
  sh "$root_dir/deploy/production/install-release.sh" "$failed_stage" "$failed_release_id" >/dev/null 2>&1; then
  fail "failed candidate release was accepted"
fi
[ "$(readlink -f "$release_root/current")" = "$release_root/$success_release_id" ] || fail "failed candidate changed current release"
[ "$(readlink -f "$release_root/previous")" = "$old_release" ] || fail "failed candidate changed previous release"
else
  printf '%s\n' 'Candidate pointer behavioral test skipped because this host cannot create symbolic links.'
fi

# Application-only rollback must retain current infrastructure and use prior application digests.
current_infrastructure_digest=$(digest 1)
previous_infrastructure_digest=$(digest 2)
current_application_digest=$(digest 3)
previous_application_digest=$(digest 4)
cat > "$temp_dir/current.env" <<ENV
SQLSERVER_IMAGE=sqlserver@sha256:$current_infrastructure_digest
REDIS_IMAGE=redis@sha256:$current_infrastructure_digest
RABBITMQ_IMAGE=rabbitmq@sha256:$current_infrastructure_digest
QDRANT_IMAGE=qdrant@sha256:$current_infrastructure_digest
SEARXNG_IMAGE=searxng@sha256:$current_infrastructure_digest
MINIO_IMAGE=minio@sha256:$current_infrastructure_digest
MINIO_MC_IMAGE=minio-mc@sha256:$current_infrastructure_digest
CLAWBOT_API_IMAGE=api@sha256:$current_application_digest
CLAWBOT_GATEWAY_IMAGE=gateway@sha256:$current_application_digest
CLAWBOT_AGENT_IMAGE=agent@sha256:$current_application_digest
CLAWBOT_WEB_IMAGE=web@sha256:$current_application_digest
CLAWBOT_HTTP_PORT=58080
ENV
cat > "$temp_dir/previous.env" <<ENV
SQLSERVER_IMAGE=sqlserver@sha256:$previous_infrastructure_digest
REDIS_IMAGE=redis@sha256:$previous_infrastructure_digest
RABBITMQ_IMAGE=rabbitmq@sha256:$previous_infrastructure_digest
QDRANT_IMAGE=qdrant@sha256:$previous_infrastructure_digest
SEARXNG_IMAGE=searxng@sha256:$previous_infrastructure_digest
MINIO_IMAGE=minio@sha256:$previous_infrastructure_digest
MINIO_MC_IMAGE=minio-mc@sha256:$previous_infrastructure_digest
CLAWBOT_API_IMAGE=api@sha256:$previous_application_digest
CLAWBOT_GATEWAY_IMAGE=gateway@sha256:$previous_application_digest
CLAWBOT_AGENT_IMAGE=agent@sha256:$previous_application_digest
CLAWBOT_WEB_IMAGE=web@sha256:$previous_application_digest
ENV
printf '%s\n' 'Bootstrap__InitialAdminEmail=rollback@example.test' > "$temp_dir/previous-runtime.env"
schema_rollback_root="$temp_dir/schema-rollback-releases"
mkdir -p "$schema_rollback_root"
printf '%s\n' 20260809010101 > "$schema_rollback_root/.schema-recovery-required"
: > "$temp_dir/docker-trace"
if PATH="$fake_bin:$PATH" CLAWBOT_RELEASE_ROOT="$schema_rollback_root" TEST_RESOLVED_ENV="$temp_dir/current.env" TEST_CAPTURED_ENV="$temp_dir/captured.env" TEST_DOCKER_TRACE="$temp_dir/docker-trace" \
  COMPOSE_FILE="$temp_dir/unused.yml" COMPOSE_ENV_FILE="$temp_dir/current.env" \
  CURRENT_RELEASE_ENV_FILE="$temp_dir/current.env" PREVIOUS_RELEASE_ENV_FILE="$temp_dir/previous.env" \
  PREVIOUS_RUNTIME_ENV_FILE="$temp_dir/previous-runtime.env" \
  CONFIRM_ROLLBACK=ROLLBACK_APPLICATION_ONLY "$root_dir/deploy/production/rollback.sh" >/dev/null 2>&1; then
  fail "schema recovery requirement did not block application rollback"
fi
[ ! -s "$temp_dir/docker-trace" ] || fail "schema recovery requirement reached Docker during rollback"

: > "$temp_dir/docker-trace"
PATH="$fake_bin:$PATH" CLAWBOT_RELEASE_ROOT="$temp_dir/direct-rollback-releases" TEST_RESOLVED_ENV="$temp_dir/current.env" TEST_CAPTURED_ENV="$temp_dir/captured.env" TEST_DOCKER_TRACE="$temp_dir/docker-trace" \
  COMPOSE_FILE="$temp_dir/unused.yml" COMPOSE_ENV_FILE="$temp_dir/current.env" \
  CURRENT_RELEASE_ENV_FILE="$temp_dir/current.env" PREVIOUS_RELEASE_ENV_FILE="$temp_dir/previous.env" \
  PREVIOUS_RUNTIME_ENV_FILE="$temp_dir/previous-runtime.env" \
  CONFIRM_ROLLBACK=ROLLBACK_APPLICATION_ONLY "$root_dir/deploy/production/rollback.sh" >/dev/null

assert_file_contains "$temp_dir/captured.env" "SQLSERVER_IMAGE=sqlserver@sha256:$current_infrastructure_digest"
assert_file_contains "$temp_dir/captured.env" "REDIS_IMAGE=redis@sha256:$current_infrastructure_digest"
assert_file_contains "$temp_dir/captured.env" "CLAWBOT_API_IMAGE=api@sha256:$previous_application_digest"
assert_file_contains "$temp_dir/captured.env" "CLAWBOT_WEB_IMAGE=web@sha256:$previous_application_digest"
assert_file_contains "$temp_dir/captured.env" "CLAWBOT_RUNTIME_ENV_FILE=$temp_dir/previous-runtime.env"
assert_no_duplicate_keys "$temp_dir/captured.env"
grep -q 'up -d --wait --no-deps agentservice' "$temp_dir/docker-trace" || fail "rollback recreated infrastructure dependencies"
if grep -Eq '(sqlserver|redis|rabbitmq|qdrant|minio).*up -d' "$temp_dir/docker-trace"; then
  fail "rollback attempted to recreate infrastructure"
fi
# The restored application runs against infrastructure this host has not served it with before, so
# it must pass its smoke checks passively before anything lets it consume queues or run schedules.
awk '
  /^CLAWBOT_STARTUP_MODE=passive .* --no-deps (api|agentservice)$/ { if (smoked) exit 1; passive++ }
  /^curl / { if (!passive) exit 2; smoked++ }
  /^compose .* --no-deps (api|agentservice)$/ { if (!smoked) exit 3; activated++ }
  END { if (passive != 2 || !smoked || activated != 2) exit 4 }
' "$temp_dir/docker-trace" || fail "rollback did not prove the restored release passively before activating background processing"

# A successful application rollback must make the restored release authoritative for future recovery.
if [ -L "$release_root/current" ]; then
  cp "$temp_dir/current.env" "$release_root/$success_release_id/effective.env"
  cp "$temp_dir/previous.env" "$old_release/effective.env"
  cp "$temp_dir/previous-runtime.env" "$old_release/runtime.env"
  : > "$temp_dir/docker-trace"
  PATH="$fake_bin:$PATH" CLAWBOT_RELEASE_ROOT="$release_root" TEST_RESOLVED_ENV="$temp_dir/current.env" TEST_CAPTURED_ENV="$temp_dir/captured.env" TEST_DOCKER_TRACE="$temp_dir/docker-trace" \
    COMPOSE_FILE="$release_root/$success_release_id/docker-compose.production.yml" COMPOSE_ENV_FILE="$release_root/$success_release_id/effective.env" \
    CURRENT_RELEASE_ENV_FILE="$release_root/$success_release_id/effective.env" \
    PREVIOUS_RELEASE_ENV_FILE="$old_release/effective.env" PREVIOUS_RUNTIME_ENV_FILE="$old_release/runtime.env" \
    CONFIRM_ROLLBACK=ROLLBACK_APPLICATION_ONLY "$root_dir/deploy/production/rollback.sh" >/dev/null
  rollback_release=$(readlink -f "$release_root/current")
  case "$rollback_release" in
    "$release_root"/rollback-*) ;;
    *) fail "rollback did not create a durable hybrid release state" ;;
  esac
  assert_file_contains "$rollback_release/effective.env" "SQLSERVER_IMAGE=sqlserver@sha256:$current_infrastructure_digest"
  assert_file_contains "$rollback_release/effective.env" "CLAWBOT_API_IMAGE=api@sha256:$previous_application_digest"
  assert_file_contains "$rollback_release/effective.env" "CLAWBOT_RUNTIME_ENV_FILE=$rollback_release/runtime.env"
fi

printf '%s\n' 'Production deployment behavioral tests passed.'
