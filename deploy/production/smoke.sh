#!/usr/bin/env sh
set -eu

base_url=${CLAWBOT_PUBLIC_BASE_URL:?CLAWBOT_PUBLIC_BASE_URL is required}
curl --fail --silent --show-error "$base_url/health/live" >/dev/null
curl --fail --silent --show-error "$base_url/health/ready" >/dev/null
curl --fail --silent --show-error "$base_url/login" >/dev/null
printf '%s\n' 'production smoke checks passed'
