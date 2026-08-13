#!/usr/bin/env sh
set -eu

# Generates the protected material for the GitHub `production` environment and
# stores it on the host under mode-0600 files for an operator to copy into
# GitHub. Secrets are never written to stdout.
#
# Run on the production host, as the deploy account, after bootstrap-host.sh.
#
# Re-running never rotates existing material: shared infrastructure credentials
# are fail-closed against the active release, so silent rotation would break the
# next deployment. Use --force only inside a controlled maintenance procedure.

config_root=${CLAWBOT_CONFIG_ROOT:-/etc/clawbot}
tls_dir=${CLAWBOT_TLS_DIR:-}
secrets_dir=${CLAWBOT_SECRETS_DIR:-}
public_host=${CLAWBOT_PUBLIC_HOST:-}
ssh_port=${CLAWBOT_SSH_PORT:-22}
force=false

while [ "$#" -gt 0 ]; do
  case "$1" in
    --config-root) config_root=${2:?--config-root requires a value}; shift 2 ;;
    --tls-dir) tls_dir=${2:?--tls-dir requires a value}; shift 2 ;;
    --secrets-dir) secrets_dir=${2:?--secrets-dir requires a value}; shift 2 ;;
    --public-host) public_host=${2:?--public-host requires a value}; shift 2 ;;
    --ssh-port) ssh_port=${2:?--ssh-port requires a value}; shift 2 ;;
    --force) force=true; shift ;;
    *) printf 'Unknown argument: %s\n' "$1" >&2; exit 2 ;;
  esac
done

[ -n "$tls_dir" ] || tls_dir="$config_root/tls"
[ -n "$secrets_dir" ] || secrets_dir="$config_root/github-secrets"

[ -n "$public_host" ] || { printf '%s\n' '--public-host is required.' >&2; exit 2; }
case "$ssh_port" in ''|*[!0-9]*) printf '%s\n' '--ssh-port must be numeric.' >&2; exit 2 ;; esac
[ "$(id -u)" -ne 0 ] || { printf '%s\n' 'Run as the deploy account, not root.' >&2; exit 2; }

pfx_password_file="$tls_dir/agentservice-grpc.pfx.password"
[ -f "$pfx_password_file" ] || {
  printf 'AgentService PFX password is unavailable at %s; run bootstrap-host.sh first.\n' "$pfx_password_file" >&2
  exit 1
}

umask 077
install -d -m 0700 "$secrets_dir"

compose_env_file="$secrets_dir/PRODUCTION_COMPOSE_ENV"
runtime_env_file="$secrets_dir/PRODUCTION_RUNTIME_ENV"
known_hosts_file="$secrets_dir/PRODUCTION_KNOWN_HOSTS"
private_key_file="$secrets_dir/PRODUCTION_SSH_PRIVATE_KEY"

if [ "$force" != true ]; then
  for existing in "$compose_env_file" "$runtime_env_file" "$private_key_file"; do
    [ ! -e "$existing" ] || {
      printf 'Refusing to rotate existing material: %s\nUse --force only inside a controlled maintenance procedure.\n' "$existing" >&2
      exit 1
    }
  done
fi

# Alphanumeric only: these values are interpolated into Compose connection
# strings and AMQP URLs, where ; : @ / and $ change the meaning of the string.
random_alnum() {
  LC_ALL=C tr -dc 'A-Za-z0-9' < /dev/urandom | head -c "$1"
}

random_sql_password() {
  while :; do
    candidate=$(random_alnum 32)
    printf '%s' "$candidate" | grep -q '[A-Z]' || continue
    printf '%s' "$candidate" | grep -q '[a-z]' || continue
    printf '%s' "$candidate" | grep -q '[0-9]' || continue
    printf '%s' "$candidate"
    return 0
  done
}

resolve_digest() {
  reference=$1
  digest=$(docker buildx imagetools inspect "$reference" 2>/dev/null |
    awk '$1 == "Digest:" { print $2; exit }')
  case "$digest" in
    sha256:????????????????????????????????????????????????????????????????)
      printf '%s@%s' "$reference" "$digest"
      ;;
    *)
      printf 'Could not resolve an immutable digest for %s\n' "$reference" >&2
      return 1
      ;;
  esac
}

# Tags are only a readable label; the resolved digest is what the deployment
# pins. Override them when moving to a newer reviewed infrastructure version.
printf 'Resolving immutable infrastructure image digests...\n' >&2
sqlserver_image=$(resolve_digest "${SQLSERVER_IMAGE_REF:-mcr.microsoft.com/mssql/server:2022-latest}")
redis_image=$(resolve_digest "${REDIS_IMAGE_REF:-redis:7-alpine}")
rabbitmq_image=$(resolve_digest "${RABBITMQ_IMAGE_REF:-rabbitmq:3.13-management}")
qdrant_image=$(resolve_digest "${QDRANT_IMAGE_REF:-qdrant/qdrant:v1.14.1}")
searxng_image=$(resolve_digest "${SEARXNG_IMAGE_REF:-searxng/searxng:2026.8.10-0a118066d}")
minio_image=$(resolve_digest "${MINIO_IMAGE_REF:-minio/minio:RELEASE.2025-02-28T09-55-16Z}")
minio_mc_image=$(resolve_digest "${MINIO_MC_IMAGE_REF:-minio/mc:latest}")

cat > "$compose_env_file" <<ENVEOF
SQLSERVER_IMAGE=$sqlserver_image
REDIS_IMAGE=$redis_image
RABBITMQ_IMAGE=$rabbitmq_image
QDRANT_IMAGE=$qdrant_image
SEARXNG_IMAGE=$searxng_image
MINIO_IMAGE=$minio_image
MINIO_MC_IMAGE=$minio_mc_image
CLAWBOT_HTTP_PORT=58080
MSSQL_PID=Standard
MSSQL_SA_PASSWORD=$(random_sql_password)
APP_SQL_USER=clawbot_app
APP_SQL_PASSWORD=$(random_sql_password)
SQLSERVER_BACKUP_DIR=/var/backups/clawbot
REDIS_PASSWORD=$(random_alnum 32)
RABBITMQ_USER=clawbot
RABBITMQ_PASSWORD=$(random_alnum 32)
MINIO_ROOT_USER=clawbot-root
MINIO_ROOT_PASSWORD=$(random_alnum 32)
MINIO_APP_ACCESS_KEY=$(random_alnum 20)
MINIO_APP_SECRET_KEY=$(random_alnum 40)
JWT_SIGNING_KEY=$(openssl rand -base64 48 | tr -d '\n')
AGENT_SERVICE_AUTH_SIGNING_KEY=$(openssl rand -base64 48 | tr -d '\n')
AGENT_SERVICE_TLS_CERTIFICATE_PATH=$tls_dir/agentservice-grpc.pfx
AGENT_SERVICE_TLS_CERTIFICATE_PASSWORD=$(cat "$pfx_password_file")
AGENT_SERVICE_TLS_CA_CERTIFICATE_PATH=$tls_dir/agentservice-ca.pem
ENCRYPTION_BASE64_KEY=$(openssl rand -base64 32 | tr -d '\n')
PANCAKE_WEBHOOK_SECRET=$(random_alnum 40)
CLAWBOT_RUNTIME_ENV_FILE=$config_root/runtime.env
SEARXNG_SETTINGS_FILE=$config_root/searxng/settings.yml
ENVEOF
chmod 0600 "$compose_env_file"

# The runtime file carries provider credentials and the first-run administrator.
# Placeholders are deliberate: the API refuses to start while they are unset.
if [ ! -e "$runtime_env_file" ] || [ "$force" = true ]; then
  cat > "$runtime_env_file" <<'RUNTIMEEOF'
Bootstrap__InitialAdminEmail=REPLACE_WITH_ADMIN_EMAIL
Bootstrap__InitialAdminPassword=REPLACE_WITH_STRONG_ADMIN_PASSWORD
RUNTIMEEOF
  chmod 0600 "$runtime_env_file"
fi

if [ ! -e "$private_key_file" ] || [ "$force" = true ]; then
  rm -f "$private_key_file" "$private_key_file.pub"
  ssh-keygen -t ed25519 -N '' -C 'clawbot-github-actions' -f "$private_key_file" >/dev/null
  chmod 0600 "$private_key_file"
fi

authorized_keys="$HOME/.ssh/authorized_keys"
install -d -m 0700 "$HOME/.ssh"
[ -f "$authorized_keys" ] || { : > "$authorized_keys"; chmod 0600 "$authorized_keys"; }
deploy_public_key=$(cut -d' ' -f1-2 < "$private_key_file.pub")
if grep -qF "$deploy_public_key" "$authorized_keys"; then
  printf 'Deploy public key is already authorized.\n' >&2
else
  cat "$private_key_file.pub" >> "$authorized_keys"
  printf 'Authorized the GitHub Actions deploy public key.\n' >&2
fi

: > "$known_hosts_file"
chmod 0600 "$known_hosts_file"
for host_key in /etc/ssh/ssh_host_ed25519_key.pub /etc/ssh/ssh_host_rsa_key.pub; do
  [ -f "$host_key" ] || continue
  key_type=$(cut -d' ' -f1 < "$host_key")
  key_material=$(cut -d' ' -f2 < "$host_key")
  if [ "$ssh_port" = "22" ]; then
    printf '%s %s %s\n' "$public_host" "$key_type" "$key_material" >> "$known_hosts_file"
  else
    printf '[%s]:%s %s %s\n' "$public_host" "$ssh_port" "$key_type" "$key_material" >> "$known_hosts_file"
  fi
done

printf '\n'
printf 'Protected material is ready under %s (mode 0600).\n' "$secrets_dir"
printf 'Copy each file into the GitHub environment named production:\n'
printf '  PRODUCTION_COMPOSE_ENV        <- %s\n' "$compose_env_file"
printf '  PRODUCTION_RUNTIME_ENV        <- %s (fill the placeholders first)\n' "$runtime_env_file"
printf '  PRODUCTION_KNOWN_HOSTS        <- %s\n' "$known_hosts_file"
printf '  PRODUCTION_SSH_PRIVATE_KEY    <- %s\n' "$private_key_file"
printf '  PRODUCTION_SEARXNG_SETTINGS   <- the reviewed production SearXNG YAML\n'
printf '  PRODUCTION_HOST               <- %s\n' "$public_host"
printf '  PRODUCTION_SSH_PORT           <- %s\n' "$ssh_port"
printf '  PRODUCTION_USER               <- %s\n' "$(id -un)"
printf '  PRODUCTION_GHCR_USERNAME      <- a GitHub account with read:packages\n'
printf '  PRODUCTION_GHCR_TOKEN         <- that account read-only GHCR token\n'
printf '\nDelete %s once every value is stored in GitHub.\n' "$secrets_dir"
