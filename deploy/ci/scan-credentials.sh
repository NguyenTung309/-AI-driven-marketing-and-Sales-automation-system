#!/bin/sh
# Chan credential bi commit vao repo.
#
# Pattern nam o file rieng thay vi viet thang trong workflow: de inline thi chinh
# dong lenh git grep se tu khop pattern cua no, buoc phai loai tru ca file workflow
# khoi pham vi quet. Tach ra day chi phai loai tru dung mot file - chinh no.
set -eu

pattern='aigw_[A-Za-z0-9]{20,}|sk-[A-Za-z0-9]{20,}|dev-only-jwt-signing-key|Password=[^;[:space:]]{8,}|Base64Key": "[A-Za-z0-9+/=]{40,}"'

# Compose interpolation (${VAR}) va cho giu cho REPLACE_WITH_* khong mang gia tri that:
# gia tri thuc chi duoc nap luc deploy tu file environment duoc bao ve. Chi trung hoa
# dung hai token do roi quet lai, phan con lai cua dong van bi soi binh thuong.
sanitize='s/Password=\$\{[^}]*\}/Password=/g; s/Password=REPLACE_WITH_[A-Z_]+/Password=/g'

scan() {
    git grep -I "$@" -- \
        . \
        ':(exclude)deploy/.env.example' \
        ':(exclude)deploy/.env.production.example' \
        ':(exclude)deploy/docker-compose.yml' \
        ':(exclude)deploy/ci/scan-credentials.sh' \
        ':(exclude)run-all.bat' \
        ':(exclude)docs/**'
}

set +e
scan -qE "$pattern"
status=$?
set -e

if [ "$status" -gt 1 ]; then
    printf '%s\n' 'Credential scan failed unexpectedly.' >&2
    exit "$status"
fi

if [ "$status" -eq 1 ]; then
    printf '%s\n' 'Credential scan passed: no tracked credential material.'
    exit 0
fi

# Chi file:line ra khoi pipeline; noi dung dong khop khong bao gio roi vao log CI.
residual=$(scan -nE "$pattern" | sed -E "$sanitize" | grep -E "$pattern" | cut -d: -f1,2)

if [ -n "$residual" ]; then
    printf '%s\n' 'Tracked credential-looking material must be removed before production publishing.' >&2
    printf '%s\n' "$residual" >&2
    exit 1
fi

printf '%s\n' 'Credential scan passed: only environment references and placeholders matched.'
