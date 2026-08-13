#!/usr/bin/env sh
set -eu

# Prepares a production host for Clawbot deployments. Safe to re-run: every step
# reports the state it found and only changes what is missing or wrong.
#
# Run as the deploy account, not as root. Steps that need root are performed
# through sudo; when sudo is unavailable non-interactively the script prints the
# exact command an operator must run and stops without partial changes.

deploy_user=${CLAWBOT_DEPLOY_USER:-$(id -un)}
config_root=${CLAWBOT_CONFIG_ROOT:-/etc/clawbot}
backup_dir=${CLAWBOT_BACKUP_DIR:-/var/backups/clawbot}
tls_dir=${CLAWBOT_TLS_DIR:-}
tls_hostname=${CLAWBOT_TLS_HOSTNAME:-agentservice}
tls_days=${CLAWBOT_TLS_DAYS:-825}
http_port=${CLAWBOT_HTTP_PORT:-58080}
sqlserver_uid=${CLAWBOT_SQLSERVER_UID:-10001}
install_packages=false
check_only=false

while [ "$#" -gt 0 ]; do
  case "$1" in
    --deploy-user) deploy_user=${2:?--deploy-user requires a value}; shift 2 ;;
    --config-root) config_root=${2:?--config-root requires a value}; shift 2 ;;
    --backup-dir) backup_dir=${2:?--backup-dir requires a value}; shift 2 ;;
    --tls-dir) tls_dir=${2:?--tls-dir requires a value}; shift 2 ;;
    --tls-hostname) tls_hostname=${2:?--tls-hostname requires a value}; shift 2 ;;
    --http-port) http_port=${2:?--http-port requires a value}; shift 2 ;;
    --install-packages) install_packages=true; shift ;;
    --check-only) check_only=true; shift ;;
    *) printf 'Unknown argument: %s\n' "$1" >&2; exit 2 ;;
  esac
done

[ -n "$tls_dir" ] || tls_dir="$config_root/tls"

case "$tls_hostname" in
  ''|*[!A-Za-z0-9.-]*)
    printf '%s\n' 'TLS hostname must contain only letters, digits, dots, and hyphens.' >&2
    exit 2
    ;;
esac
case "$http_port" in
  ''|*[!0-9]*) printf '%s\n' 'HTTP port must be numeric.' >&2; exit 2 ;;
esac
case "$sqlserver_uid$tls_days" in
  *[!0-9]*) printf '%s\n' 'Numeric bootstrap parameters must contain only digits.' >&2; exit 2 ;;
esac

[ "$(id -u)" -ne 0 ] || {
  printf '%s\n' 'Run this script as the deploy account; it escalates through sudo only where required.' >&2
  exit 2
}
id "$deploy_user" >/dev/null 2>&1 || {
  printf 'Deploy account does not exist: %s\n' "$deploy_user" >&2
  exit 1
}

failures=0
pending_root_actions=$(mktemp)
trap 'rm -f "$pending_root_actions"' EXIT

report() {
  printf '[%s] %s\n' "$1" "$2"
}

fail() {
  report FAIL "$1"
  failures=$((failures + 1))
}

# Runs a privileged command, or records it for an operator when sudo cannot run
# unattended. Never prompts, so the script stays usable from CI and from ssh.
run_privileged() {
  description=$1
  shift
  if [ "$check_only" = true ]; then
    report SKIP "$description (check-only)"
    return 1
  fi
  if sudo -n true 2>/dev/null; then
    sudo -n "$@" || {
      fail "$description"
      return 1
    }
    report DONE "$description"
    return 0
  fi
  printf 'sudo %s\n' "$*" >> "$pending_root_actions"
  fail "$description requires root; sudo is not available without a password"
  return 1
}

require_directory() {
  path=$1
  mode=$2
  owner=$3
  description=$4

  if [ -d "$path" ]; then
    actual_mode=$(stat -c '%a' "$path")
    actual_owner=$(stat -c '%u:%g' "$path")
    numeric_owner=$(resolve_owner "$owner")
    if [ "$actual_mode" = "$mode" ] && [ "$actual_owner" = "$numeric_owner" ]; then
      report OK "$description already present at $path ($mode $owner)"
      return 0
    fi
    run_privileged "correct ownership and mode of $path" \
      sh -c "chown '$owner' '$path' && chmod '$mode' '$path'"
    return $?
  fi

  if [ -e "$path" ]; then
    fail "$path exists but is not a directory"
    return 1
  fi

  run_privileged "create $description at $path" \
    sh -c "install -d -m '$mode' -o '${owner%%:*}' -g '${owner##*:}' '$path'"
}

resolve_owner() {
  owner=$1
  owner_user=${owner%%:*}
  owner_group=${owner##*:}
  case "$owner_user" in
    *[!0-9]*) owner_user=$(id -u "$owner_user") ;;
  esac
  case "$owner_group" in
    *[!0-9]*) owner_group=$(getent group "$owner_group" | cut -d: -f3) ;;
  esac
  printf '%s:%s' "$owner_user" "$owner_group"
}

printf '%s\n' "Clawbot production host bootstrap"
printf '  deploy account : %s\n' "$deploy_user"
printf '  config root    : %s\n' "$config_root"
printf '  backup dir     : %s\n' "$backup_dir"
printf '  tls dir        : %s\n' "$tls_dir"
printf '\n'

# --- Container runtime -------------------------------------------------------

if command -v docker >/dev/null 2>&1; then
  report OK "docker present ($(docker --version))"
  if docker compose version >/dev/null 2>&1; then
    report OK "docker compose v2 present ($(docker compose version --short))"
  else
    fail 'docker compose v2 plugin is missing; install docker-compose-plugin'
  fi
  if docker info >/dev/null 2>&1; then
    report OK "$deploy_user can reach the Docker daemon"
  else
    fail "$deploy_user cannot reach the Docker daemon; add the account to the docker group and re-login"
  fi
else
  fail 'docker is not installed'
fi

# --- PowerShell (required by the go-live readiness gate) ---------------------

if command -v pwsh >/dev/null 2>&1; then
  report OK "pwsh present ($(pwsh -NoLogo -NoProfile -NonInteractive -Command '$PSVersionTable.PSVersion.ToString()' 2>/dev/null))"
elif [ "$install_packages" = true ]; then
  # PowerShell is not carried by the Ubuntu archive, so the snap is the
  # supported route; apt is only attempted where a vendor repository is present.
  if command -v snap >/dev/null 2>&1; then
    run_privileged 'install PowerShell from the snap store' snap install powershell --classic || :
  elif command -v apt-get >/dev/null 2>&1; then
    run_privileged 'install PowerShell from a configured apt repository' \
      sh -c 'export DEBIAN_FRONTEND=noninteractive; apt-get update -qq && apt-get install -y --no-install-recommends powershell' || :
  else
    fail 'pwsh is missing and automatic installation supports snap or apt hosts only'
  fi
  command -v pwsh >/dev/null 2>&1 || fail 'pwsh is still unavailable after the installation attempt'
else
  fail 'pwsh is missing; re-run with --install-packages or install PowerShell manually'
fi

# --- Directories -------------------------------------------------------------

require_directory "$config_root" 700 "$deploy_user:$deploy_user" 'configuration root' || :

# The deploy account deliberately gets no write access to backups: only the SQL
# Server container writes there, and restores are an explicit operator action.
require_directory "$backup_dir" 770 "$sqlserver_uid:0" 'SQL Server backup directory' || :

if [ -d "$config_root" ] && [ -w "$config_root" ]; then
  if [ ! -d "$tls_dir" ]; then
    if [ "$check_only" = true ]; then
      report SKIP "create TLS material directory at $tls_dir (check-only)"
    else
      install -d -m 0700 "$tls_dir"
      report DONE "created TLS material directory at $tls_dir"
    fi
  else
    report OK "TLS material directory already present at $tls_dir"
  fi
fi

# --- AgentService gRPC TLS material ------------------------------------------

ca_certificate="$tls_dir/agentservice-ca.pem"
server_pfx="$tls_dir/agentservice-grpc.pfx"
pfx_password_file="$tls_dir/agentservice-grpc.pfx.password"

certificate_material_exists() {
  [ -f "$ca_certificate" ] || [ -f "$server_pfx" ]
}

if ! command -v openssl >/dev/null 2>&1; then
  fail 'openssl is required to provision AgentService gRPC TLS material'
elif certificate_material_exists; then
  # Existing key material is never regenerated: the PFX password lives in a
  # protected secret, so silently replacing the pair would break every release
  # that already references it. Report what can be checked without the password.
  if [ -f "$ca_certificate" ] && [ -f "$server_pfx" ]; then
    if openssl x509 -in "$ca_certificate" -noout -checkend 0 >/dev/null 2>&1; then
      report OK "AgentService gRPC TLS material already present in $tls_dir"
    else
      fail "the AgentService gRPC CA in $tls_dir has expired; rotate it through the controlled maintenance procedure"
    fi
  else
    fail "AgentService gRPC TLS material in $tls_dir is incomplete; remove the partial files and re-run"
  fi
elif [ "$check_only" = true ]; then
  report SKIP "provision AgentService gRPC TLS material in $tls_dir (check-only)"
elif [ ! -d "$tls_dir" ]; then
  fail "cannot provision TLS material because $tls_dir is missing"
else
  umask 077
  work_dir=$(mktemp -d)
  # The PFX password must match AGENT_SERVICE_TLS_CERTIFICATE_PASSWORD in the
  # protected Compose environment. When the caller does not supply one, a random
  # password is written to a mode-0600 file for the operator to copy into the
  # GitHub environment secret; it is never printed to stdout.
  if [ -n "${AGENT_SERVICE_TLS_CERTIFICATE_PASSWORD:-}" ]; then
    pfx_password=$AGENT_SERVICE_TLS_CERTIFICATE_PASSWORD
    password_source='supplied through AGENT_SERVICE_TLS_CERTIFICATE_PASSWORD'
  else
    pfx_password=$(openssl rand -base64 32 | tr -d '\n')
    printf '%s' "$pfx_password" > "$pfx_password_file"
    chmod 0600 "$pfx_password_file"
    password_source="generated and stored at $pfx_password_file"
  fi

  openssl req -x509 -newkey rsa:4096 -sha256 -days 3650 -nodes \
    -keyout "$work_dir/ca.key" -out "$work_dir/ca.pem" \
    -subj "/CN=Clawbot AgentService Internal CA" \
    -addext 'basicConstraints=critical,CA:TRUE,pathlen:0' \
    -addext 'keyUsage=critical,keyCertSign,cRLSign' >/dev/null 2>&1

  openssl req -newkey rsa:2048 -sha256 -nodes \
    -keyout "$work_dir/server.key" -out "$work_dir/server.csr" \
    -subj "/CN=$tls_hostname" >/dev/null 2>&1

  # Server-auth EKU and a DNS SAN are both required: the readiness gate matches
  # the DNS name against the Compose service name and validates the chain with a
  # server-auth application policy.
  printf 'subjectAltName=DNS:%s\nextendedKeyUsage=serverAuth\nkeyUsage=critical,digitalSignature,keyEncipherment\nbasicConstraints=critical,CA:FALSE\n' \
    "$tls_hostname" > "$work_dir/server.ext"

  openssl x509 -req -in "$work_dir/server.csr" -CA "$work_dir/ca.pem" -CAkey "$work_dir/ca.key" \
    -CAcreateserial -days "$tls_days" -sha256 -extfile "$work_dir/server.ext" \
    -out "$work_dir/server.pem" >/dev/null 2>&1

  openssl pkcs12 -export -out "$work_dir/server.pfx" \
    -inkey "$work_dir/server.key" -in "$work_dir/server.pem" \
    -passout "pass:$pfx_password" >/dev/null 2>&1

  # Both files are world-readable inside the mode-0700 TLS directory: the
  # AgentService and API containers run as an unprivileged user whose UID does
  # not exist on the host, while other host accounts cannot traverse the
  # directory to reach them.
  install -m 0644 "$work_dir/ca.pem" "$ca_certificate"
  install -m 0644 "$work_dir/server.pfx" "$server_pfx"
  rm -rf "$work_dir"
  umask 022
  report DONE "provisioned AgentService gRPC TLS material in $tls_dir (password $password_source)"
fi

# --- Public ingress port -----------------------------------------------------

if command -v ss >/dev/null 2>&1; then
  if ss -ltn "sport = :$http_port" 2>/dev/null | grep -q ":$http_port"; then
    fail "port $http_port is already bound by another workload"
  else
    report OK "port $http_port is free for the Clawbot ingress"
  fi
else
  report SKIP "cannot verify port $http_port because ss is unavailable"
fi

# --- Result ------------------------------------------------------------------

printf '\n'
if [ -s "$pending_root_actions" ]; then
  printf '%s\n' 'Run the following as an operator with sudo access, then re-run this script:' >&2
  cat "$pending_root_actions" >&2
  printf '\n' >&2
fi

if [ "$failures" -gt 0 ]; then
  printf 'Host bootstrap incomplete: %d check(s) failed.\n' "$failures" >&2
  exit 1
fi

printf '%s\n' 'Host bootstrap complete. Record the TLS paths in the protected Compose environment:'
printf '  AGENT_SERVICE_TLS_CERTIFICATE_PATH=%s\n' "$server_pfx"
printf '  AGENT_SERVICE_TLS_CA_CERTIFICATE_PATH=%s\n' "$ca_certificate"
