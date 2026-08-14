#!/usr/bin/env sh
set -eu

stage=${1:?release staging directory is required}
release_id=${2:?release identifier is required}
release_root=${CLAWBOT_RELEASE_ROOT:-/etc/clawbot/releases}

printf '%s' "$release_id" | grep -Eq '^[a-f0-9]{40}-[0-9]+-[0-9]+$' || {
  printf '%s\n' 'Release identifier is invalid.' >&2
  exit 1
}

[ -d "$stage" ] || {
  printf '%s\n' 'Release staging directory is missing.' >&2
  exit 1
}

. "$stage/common.sh"

umask 077
if [ ! -e "$release_root" ]; then
  install -d -m 0700 "$release_root"
fi
[ -d "$release_root" ] || {
  printf '%s\n' 'Release storage path is not a directory.' >&2
  exit 1
}
acquire_release_lifecycle_lock "$release_root"
lock_dir=$lifecycle_lock_dir
trap 'release_lifecycle_lock_cleanup' EXIT
trap 'exit 1' HUP INT TERM

release_dir="$release_root/$release_id"
[ ! -e "$release_dir" ] || {
  printf 'Release directory already exists: %s\n' "$release_dir" >&2
  exit 1
}
install -d -m 0700 "$release_dir"

validate_environment_file "$stage/production.env"
validate_environment_file "$stage/runtime.env"
validate_environment_file "$stage/images.env"
if ! awk -F= '$1 ~ /^CLAWBOT_(API|GATEWAY|AGENT|WEB)_IMAGE$/ { exit 1 }' "$stage/production.env"; then
  printf '%s\n' 'Candidate Compose environment must not define application image variables.' >&2
  exit 1
fi

install -m 0644 "$stage/docker-compose.production.yml" "$release_dir/docker-compose.production.yml"
install -m 0600 "$stage/production.env" "$release_dir/production.env"
install -m 0600 "$stage/runtime.env" "$release_dir/runtime.env"
install -m 0600 "$stage/settings.yml" "$release_dir/settings.yml"
install -m 0600 "$stage/images.env" "$release_dir/images.env"
install -m 0755 \
  "$stage/backup.sh" \
  "$stage/common.sh" \
  "$stage/deploy.sh" \
  "$stage/install-release.sh" \
  "$stage/migrate.sh" \
  "$stage/rollback.sh" \
  "$stage/restore-verified-backup.sh" \
  "$stage/smoke.sh" \
  "$release_dir/"
install -m 0644 \
  "$stage/repair_tenant_runtime_columns.sql" \
  "$stage/repair_inbox_runtime_columns.sql" \
  "$stage/repair_agent_runtime_columns.sql" \
  "$stage/repair_inbox_collaboration_tables.sql" \
  "$stage/repair_agent_allowed_tools.sql" \
  "$stage/verify_content_render_tasks.sql" \
  "$stage/verify_database_table_consolidation.sql" \
  "$release_dir/"
install -d -m 0755 "$release_dir/migrations"
for migration in "$stage/migrations"/*.sql; do
  [ -f "$migration" ] || continue
  migration_name=$(basename "$migration")
  case "$migration_name" in
    [0-9][0-9][0-9][0-9]_[A-Za-z0-9_.-]*.sql) ;;
    *)
      printf 'Unsafe migration filename: %s\n' "$migration_name" >&2
      exit 1
      ;;
  esac
  install -m 0644 "$migration" "$release_dir/migrations/$migration_name"
done

awk -v runtime_file="$release_dir/runtime.env" -v settings_file="$release_dir/settings.yml" '
  /^CLAWBOT_RUNTIME_ENV_FILE=/ { print "CLAWBOT_RUNTIME_ENV_FILE=" runtime_file; runtime_count++; next }
  /^SEARXNG_SETTINGS_FILE=/ { print "SEARXNG_SETTINGS_FILE=" settings_file; settings_count++; next }
  { print }
  END { if (runtime_count != 1 || settings_count != 1) exit 1 }
' "$release_dir/production.env" > "$release_dir/candidate-production.env"
validate_environment_file "$release_dir/candidate-production.env"
cat "$release_dir/candidate-production.env" "$release_dir/images.env" > "$release_dir/effective.env"
chmod 0600 "$release_dir/effective.env"
validate_environment_file "$release_dir/effective.env"

current_link="$release_root/current"
previous_link="$release_root/previous"
current_release_env=
current_compose_file=
if [ -L "$current_link" ]; then
  current_dir=$(readlink -f "$current_link")
  [ -f "$current_dir/effective.env" ] && [ -f "$current_dir/docker-compose.production.yml" ] || {
    printf '%s\n' 'Current release pointer is incomplete.' >&2
    exit 1
  }
  cmp -s "$release_dir/docker-compose.production.yml" "$current_dir/docker-compose.production.yml" || {
    printf '%s\n' 'Compose changes require a controlled maintenance procedure and cannot be bundled with an application release.' >&2
    exit 1
  }
  cmp -s "$release_dir/settings.yml" "$current_dir/settings.yml" || {
    printf '%s\n' 'SearXNG settings changes require a controlled maintenance procedure and cannot be bundled with an application release.' >&2
    exit 1
  }
  current_release_env="$current_dir/effective.env"
  current_compose_file="$current_dir/docker-compose.production.yml"
fi

schema_recovery_marker="$release_root/.schema-recovery-required"
if [ -e "$schema_recovery_marker" ] || [ -L "$schema_recovery_marker" ]; then
  printf '%s\n' 'A prior candidate changed the database schema. Restore its verified pre-migration backup or clear the recovery requirement after an operator confirms compatibility.' >&2
  exit 1
fi

promotion_marker="$release_root/.promotion-pending"
if [ -e "$promotion_marker" ] \
  || [ -e "$release_root/previous.new" ] || [ -L "$release_root/previous.new" ] \
  || [ -e "$release_root/current.new" ] || [ -L "$release_root/current.new" ]; then
  printf '%s\n' 'A prior release-pointer promotion is incomplete. Resolve .promotion-pending, current.new, and previous.new before starting another lifecycle operation.' >&2
  exit 1
fi

printf '%s\n' "$release_id" > "$promotion_marker"

if ! COMPOSE_FILE="$release_dir/docker-compose.production.yml" \
  COMPOSE_ENV_FILE="$release_dir/effective.env" \
  CURRENT_RELEASE_ENV_FILE="$current_release_env" \
  CURRENT_COMPOSE_FILE="$current_compose_file" \
  "$release_dir/deploy.sh"; then
  if [ -e "$schema_recovery_marker" ] || [ -L "$schema_recovery_marker" ]; then
    rm -f "$promotion_marker" || {
      printf 'Candidate deployment changed the database schema, but %s could not be cleared. Do not start another lifecycle operation until an operator verifies the running containers, database, and release markers.\n' "$promotion_marker" >&2
      exit 1
    }
    printf '%s\n' 'Candidate deployment changed the database schema and failed. Application rollback is blocked until its verified pre-migration backup is restored or an operator confirms compatibility.' >&2
    exit 1
  fi
  if [ -e "$release_dir/.schema-recovery-required" ] || [ -L "$release_dir/.schema-recovery-required" ]; then
    printf '%s\n' 'Candidate deployment changed the database schema, but its durable root recovery requirement could not be recorded. The promotion marker remains so no lifecycle operation can proceed without operator recovery.' >&2
    exit 1
  fi

  rm -f "$promotion_marker" || {
    printf 'Deployment failed and %s could not be cleared. Do not start another lifecycle operation until an operator verifies the running containers and release pointers.\n' "$promotion_marker" >&2
  }
  exit 1
fi

previous_target=
if [ -L "$current_link" ]; then
  previous_target=$(readlink -f "$current_link")
  if ! ln -s "$previous_target" "$release_root/previous.new"; then
    printf '%s\n' 'Candidate is healthy and remains running, but the previous release pointer could not be staged. Do not start another lifecycle operation; repair the release pointers before proceeding.' >&2
    exit 1
  fi
fi

if ! ln -s "$release_dir" "$release_root/current.new"; then
  printf '%s\n' 'Candidate is healthy and remains running, but the current release pointer could not be staged. The preserved previous.new marks this incomplete promotion; do not start another lifecycle operation until an operator repairs the release pointers.' >&2
  exit 1
fi

if ! mv -Tf "$release_root/current.new" "$current_link"; then
  printf '%s\n' 'Candidate is healthy and remains running, but the current release pointer could not be promoted. The preserved current.new and previous.new mark this incomplete promotion; do not start another lifecycle operation until an operator repairs the release pointers.' >&2
  exit 1
fi

if [ -n "$previous_target" ] && ! mv -Tf "$release_root/previous.new" "$previous_link"; then
  printf 'Current release %s was promoted, but the previous pointer was not updated. Complete the preserved move %s -> %s before the next deployment.\n' "$release_id" "$release_root/previous.new" "$previous_link" >&2
  exit 1
fi

if ! rm -f "$schema_recovery_marker"; then
  printf 'Release %s is running and its pointers are promoted, but %s could not be cleared. Do not start another lifecycle operation until an operator verifies and clears the marker.\n' \
    "$release_id" "$schema_recovery_marker" >&2
  exit 1
fi

if ! rm -f "$promotion_marker"; then
  printf 'Release %s is running and its pointers are promoted, but %s could not be cleared. Do not start another lifecycle operation until an operator verifies and clears the marker.\n' \
    "$release_id" "$promotion_marker" >&2
  exit 1
fi

printf 'Promoted candidate release %s after successful smoke checks.\n' "$release_id"
