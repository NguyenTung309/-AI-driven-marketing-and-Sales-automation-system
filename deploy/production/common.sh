#!/usr/bin/env sh

validate_environment_file() {
  environment_file=$1
  [ -f "$environment_file" ] || {
    printf 'Environment file does not exist: %s\n' "$environment_file" >&2
    return 1
  }

  validation_error=$(awk '
    /^[[:space:]]*($|#)/ { next }
    !/^[A-Za-z_][A-Za-z0-9_]*=/ {
      print "invalid-format"
      exit 1
    }
    {
      separator = index($0, "=")
      key = substr($0, 1, separator - 1)
      if (seen[key]++) {
        print key
        exit 1
      }
    }
  ' "$environment_file") || {
    printf 'Environment file contains an invalid or duplicate key: %s\n' "${validation_error:-unknown}" >&2
    return 1
  }
}

read_environment_value() {
  key=$1
  environment_file=$2
  awk -F= -v expected_key="$key" '$1 == expected_key { sub(/^[^=]*=/, ""); print; exit }' "$environment_file"
}

resolve_compose_environment() {
  compose_environment_file=$1
  compose_definition_file=$2
  docker compose --env-file "$compose_environment_file" -f "$compose_definition_file" config --environment
}

read_resolved_environment_value() {
  key=$1
  resolved_environment=$2
  printf '%s\n' "$resolved_environment" |
    awk -F= -v expected_key="$key" '$1 == expected_key { sub(/^[^=]*=/, ""); print; exit }'
}

require_immutable_image() {
  key=$1
  image=$2
  if ! printf '%s' "$image" | grep -Eq '^.+@sha256:[a-f0-9]{64}$'; then
    printf 'Image variable %s must be an immutable sha256 reference.\n' "$key" >&2
    return 1
  fi
}

acquire_release_lifecycle_lock() {
  release_root=$1
  install -d -m 0700 "$release_root"
  lifecycle_lock_dir="$release_root/.install-lock"
  if ! mkdir "$lifecycle_lock_dir" 2>/dev/null; then
    printf '%s\n' 'Another Clawbot deployment, rollback, or database restore is already running.' >&2
    return 1
  fi
}

release_lifecycle_lock_cleanup() {
  [ -n "${lifecycle_lock_dir:-}" ] || return 0
  rmdir "$lifecycle_lock_dir" 2>/dev/null || true
}
