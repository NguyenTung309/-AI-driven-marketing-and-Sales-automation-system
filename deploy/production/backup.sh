#!/usr/bin/env sh
set -eu

: "${MSSQL_SA_PASSWORD:?MSSQL_SA_PASSWORD is required}"
: "${SQLSERVER_CONTAINER:?SQLSERVER_CONTAINER is required}"
: "${MIGRATION_DATABASE:=clawbot}"

backup_id=${BACKUP_ID:-$(date -u +%Y%m%d%H%M%S)}
case "$MIGRATION_DATABASE" in
  ''|*[!A-Za-z0-9_]*|[0-9]*)
    printf '%s\n' 'MIGRATION_DATABASE must start with a letter or underscore and contain only letters, digits, and underscores.' >&2
    exit 1
    ;;
esac
case "$backup_id" in
  ''|*[!0-9]*)
    printf '%s\n' 'BACKUP_ID must contain only digits.' >&2
    exit 1
    ;;
esac
if [ "${#backup_id}" -ne 14 ]; then
  printf '%s\n' 'BACKUP_ID must use the UTC yyyyMMddHHmmss format.' >&2
  exit 1
fi
backup_path="/var/opt/mssql/backup/${MIGRATION_DATABASE}-${backup_id}.bak"

run_sqlcmd() {
  {
    printf '%s\n' "$MSSQL_SA_PASSWORD"
    cat
  } | docker exec -i "$SQLSERVER_CONTAINER" sh -c '
    IFS= read -r SQLCMDPASSWORD
    export SQLCMDPASSWORD
    exec /opt/mssql-tools18/bin/sqlcmd -S localhost,1433 -U sa -C -b -i /dev/stdin
  '
}

printf '%s\n' "BACKUP DATABASE [${MIGRATION_DATABASE}] TO DISK = N'${backup_path}' WITH CHECKSUM, INIT, COMPRESSION" | run_sqlcmd >/dev/null
printf '%s\n' "RESTORE VERIFYONLY FROM DISK = N'${backup_path}' WITH CHECKSUM" | run_sqlcmd >/dev/null

printf '%s\n' "$backup_id"
