#!/usr/bin/env sh
set -eu

: "${COMPOSE_FILE:?COMPOSE_FILE is required}"
: "${COMPOSE_ENV_FILE:?COMPOSE_ENV_FILE is required}"
: "${CURRENT_RELEASE_ENV_FILE:?CURRENT_RELEASE_ENV_FILE is required}"
: "${PREVIOUS_RELEASE_ENV_FILE:?PREVIOUS_RELEASE_ENV_FILE is required}"
: "${PREVIOUS_RUNTIME_ENV_FILE:?PREVIOUS_RUNTIME_ENV_FILE is required}"

if [ "${CONFIRM_ROLLBACK:-}" != "ROLLBACK_APPLICATION_ONLY" ]; then
  printf '%s\n' 'Set CONFIRM_ROLLBACK=ROLLBACK_APPLICATION_ONLY to continue.' >&2
  exit 2
fi

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
. "$script_dir/common.sh"

validate_environment_file "$CURRENT_RELEASE_ENV_FILE"
validate_environment_file "$PREVIOUS_RELEASE_ENV_FILE"
[ -f "$PREVIOUS_RUNTIME_ENV_FILE" ] || {
  printf 'Previous runtime environment does not exist: %s\n' "$PREVIOUS_RUNTIME_ENV_FILE" >&2
  exit 1
}

release_root=${CLAWBOT_RELEASE_ROOT:-/etc/clawbot/releases}
acquire_release_lifecycle_lock "$release_root"
trap 'release_lifecycle_lock_cleanup' EXIT
trap 'exit 1' HUP INT TERM

if [ -e "$release_root/.schema-recovery-required" ] || [ -L "$release_root/.schema-recovery-required" ]; then
  printf '%s\n' 'A failed candidate changed the database schema. Restore its verified pre-migration backup or obtain explicit operator compatibility approval before rolling back application images.' >&2
  exit 1
fi

if [ -e "$release_root/.promotion-pending" ] \
  || [ -e "$release_root/current.new" ] || [ -L "$release_root/current.new" ] \
  || [ -e "$release_root/previous.new" ] || [ -L "$release_root/previous.new" ]; then
  printf '%s\n' 'A release-pointer promotion is incomplete. Resolve .promotion-pending, current.new, and previous.new before rolling back.' >&2
  exit 1
fi

update_release_pointer=false
current_release_link=
previous_release_link=
if [ -L "$release_root/current" ] || [ -L "$release_root/previous" ]; then
  current_release_link="$release_root/current"
  previous_release_link="$release_root/previous"
  [ -L "$current_release_link" ] && [ -L "$previous_release_link" ] || {
    printf '%s\n' 'Production release pointers are incomplete; refusing rollback without durable release state.' >&2
    exit 1
  }
  current_release_dir=$(readlink -f "$current_release_link")
  previous_release_dir=$(readlink -f "$previous_release_link")
  [ "$(readlink -f "$CURRENT_RELEASE_ENV_FILE")" = "$current_release_dir/effective.env" ] || {
    printf '%s\n' 'CURRENT_RELEASE_ENV_FILE does not match the current production release pointer.' >&2
    exit 1
  }
  [ "$(readlink -f "$PREVIOUS_RELEASE_ENV_FILE")" = "$previous_release_dir/effective.env" ] || {
    printf '%s\n' 'PREVIOUS_RELEASE_ENV_FILE does not match the previous production release pointer.' >&2
    exit 1
  }
  [ "$(readlink -f "$PREVIOUS_RUNTIME_ENV_FILE")" = "$previous_release_dir/runtime.env" ] || {
    printf '%s\n' 'PREVIOUS_RUNTIME_ENV_FILE does not match the previous production release pointer.' >&2
    exit 1
  }
  update_release_pointer=true
fi

rollback_environment=$(mktemp)
rollback_compose_environment=$rollback_environment
rollback_release_dir=
rollback_recovery_armed=false

rollback_pointer_is_promoted() {
  [ -n "$rollback_release_dir" ] || return 1
  [ -L "$current_release_link" ] || return 1
  [ "$(readlink -f "$current_release_link")" = "$rollback_release_dir" ]
}

recover_current_application() {
  status=$?
  trap - EXIT HUP INT TERM

  if [ "$status" -ne 0 ] && [ "$rollback_recovery_armed" = true ] && ! rollback_pointer_is_promoted; then
    printf '%s\n' 'Application rollback failed; restoring the current application release.' >&2
    set +e
    recovery_status=0
    for service in agentservice api gateway web; do
      docker compose --env-file "$CURRENT_RELEASE_ENV_FILE" -f "$COMPOSE_FILE" up -d --wait --no-deps "$service" || recovery_status=1
    done
    if [ "$recovery_status" -ne 0 ]; then
      printf '%s\n' 'Current application recovery also failed; inspect the current release before retrying rollback.' >&2
    fi
    set -e
  fi

  rm -f "$rollback_environment"
  if ! rollback_pointer_is_promoted; then
    [ -z "$rollback_release_dir" ] || rm -rf "$rollback_release_dir"
  fi
  release_lifecycle_lock_cleanup
  exit "$status"
}

# Retain current credentials and infrastructure digests, then replace only application images.
awk -F= '!/^CLAWBOT_(API|GATEWAY|AGENT|WEB)_IMAGE=/ && !/^CLAWBOT_RUNTIME_ENV_FILE=/' "$CURRENT_RELEASE_ENV_FILE" > "$rollback_environment"
printf 'CLAWBOT_RUNTIME_ENV_FILE=%s\n' "$PREVIOUS_RUNTIME_ENV_FILE" >> "$rollback_environment"
for image_variable in CLAWBOT_API_IMAGE CLAWBOT_GATEWAY_IMAGE CLAWBOT_AGENT_IMAGE CLAWBOT_WEB_IMAGE; do
  image=$(read_environment_value "$image_variable" "$PREVIOUS_RELEASE_ENV_FILE")
  require_immutable_image "$image_variable" "$image"
  printf '%s=%s\n' "$image_variable" "$image" >> "$rollback_environment"
done
validate_environment_file "$rollback_environment"

docker compose --env-file "$rollback_environment" -f "$COMPOSE_FILE" config --quiet
docker compose --env-file "$rollback_environment" -f "$COMPOSE_FILE" pull agentservice api gateway web
trap 'recover_current_application' EXIT
trap 'exit 1' HUP INT TERM

if [ "$update_release_pointer" = true ]; then
  [ "$(readlink -f "$COMPOSE_FILE")" = "$current_release_dir/docker-compose.production.yml" ] || {
    printf '%s\n' 'COMPOSE_FILE does not match the current production release pointer.' >&2
    exit 1
  }
  [ -f "$current_release_dir/settings.yml" ] || {
    printf '%s\n' 'Current production release is missing SearXNG settings.' >&2
    exit 1
  }

  rollback_release_id="rollback-$(date -u +%Y%m%d%H%M%S)-$$"
  rollback_release_dir="$release_root/$rollback_release_id"
  [ ! -e "$rollback_release_dir" ] || {
    printf '%s\n' 'Rollback release identifier already exists; retry the rollback.' >&2
    exit 1
  }
  install -d -m 0700 "$rollback_release_dir"
  install -m 0600 "$COMPOSE_FILE" "$rollback_release_dir/docker-compose.production.yml"
  install -m 0600 "$current_release_dir/settings.yml" "$rollback_release_dir/settings.yml"
  install -m 0600 "$PREVIOUS_RUNTIME_ENV_FILE" "$rollback_release_dir/runtime.env"
  awk -v runtime_file="$rollback_release_dir/runtime.env" '
    /^CLAWBOT_RUNTIME_ENV_FILE=/ { print "CLAWBOT_RUNTIME_ENV_FILE=" runtime_file; next }
    { print }
  ' "$rollback_environment" > "$rollback_release_dir/effective.env"
  chmod 0600 "$rollback_release_dir/effective.env"
  validate_environment_file "$rollback_release_dir/effective.env"
  rollback_compose_environment="$rollback_release_dir/effective.env"
  rm -f "$release_root/current.new"
fi

# The restored application images run against the current infrastructure, which is a combination
# this host has not served before, so it is proven passively first exactly like a candidate.
rollback_recovery_armed=true
CLAWBOT_STARTUP_MODE=passive docker compose --env-file "$rollback_compose_environment" -f "$COMPOSE_FILE" up -d --wait --no-deps agentservice
CLAWBOT_STARTUP_MODE=passive docker compose --env-file "$rollback_compose_environment" -f "$COMPOSE_FILE" up -d --wait --no-deps api
docker compose --env-file "$rollback_compose_environment" -f "$COMPOSE_FILE" up -d --wait --no-deps gateway
docker compose --env-file "$rollback_compose_environment" -f "$COMPOSE_FILE" up -d --wait --no-deps web

resolved_environment=$(resolve_compose_environment "$rollback_compose_environment" "$COMPOSE_FILE")
http_port=$(read_resolved_environment_value CLAWBOT_HTTP_PORT "$resolved_environment")
[ -n "$http_port" ] || {
  printf '%s\n' 'CLAWBOT_HTTP_PORT is missing from the rollback environment.' >&2
  exit 1
}
CLAWBOT_PUBLIC_BASE_URL=${CLAWBOT_PUBLIC_BASE_URL:-http://127.0.0.1:$http_port} "$script_dir/smoke.sh"

printf '%s\n' 'Smoke checks passed; activating background processing on the restored release.'
docker compose --env-file "$rollback_compose_environment" -f "$COMPOSE_FILE" up -d --wait --no-deps agentservice
docker compose --env-file "$rollback_compose_environment" -f "$COMPOSE_FILE" up -d --wait --no-deps api

CLAWBOT_PUBLIC_BASE_URL=${CLAWBOT_PUBLIC_BASE_URL:-http://127.0.0.1:$http_port} "$script_dir/smoke.sh"

if [ "$update_release_pointer" = true ]; then
  ln -s "$rollback_release_dir" "$release_root/current.new"
  mv -Tf "$release_root/current.new" "$current_release_link"
fi

rollback_recovery_armed=false
release_lifecycle_lock_cleanup
trap - EXIT HUP INT TERM
rm -f "$rollback_environment"
printf '%s\n' 'application image rollback completed; infrastructure images and database schema were not changed'
