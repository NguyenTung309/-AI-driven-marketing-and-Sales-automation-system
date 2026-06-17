<#
.SYNOPSIS
Registers the Clawbot tenant webhook callback with Pancake.

.DESCRIPTION
The script is intentionally environment-driven because Pancake account
subscription endpoints can differ by tenant/account. Override
PANCAKE_SUBSCRIBE_PATH when the live account exposes a different path.

Required environment variables:
  PANCAKE_BASE_URL          Example: https://pancake.vn/api/v1
  PANCAKE_ACCESS_TOKEN      Pancake page/account access token
  PANCAKE_PAGE_ID           Pancake page identifier
  PANCAKE_TENANT_SLUG       Clawbot tenant slug
  CLAWBOT_PUBLIC_BASE_URL   Public API origin, no trailing slash
  PANCAKE_WEBHOOK_SECRET    Secret configured in Clawbot and Pancake

Optional environment variables:
  PANCAKE_SUBSCRIBE_PATH       Default: /pages/{page_id}/webhooks
  PANCAKE_AUTH_MODE            query or bearer. Default: query
  PANCAKE_WEBHOOK_EVENTS       Comma list. Default: MESSAGE,COMMENT,DM
  PANCAKE_SIGNATURE_HEADER     Default: x-pancake-signature
  PANCAKE_SIGNATURE_ALGO       Default: hmac-sha256
  PANCAKE_SIGNATURE_ENCODING   Default: hex

Callback pattern:
  https://api.example.com/webhooks/pancake/{tenantSlug}

.EXAMPLE
  ./deploy/pancake-webhook-subscribe.ps1 -DryRun
#>

param(
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Require-Env {
    param([Parameter(Mandatory = $true)][string]$Name)

    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Missing required environment variable: $Name"
    }

    return $value
}

function Read-EnvOrDefault {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Default
    )

    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $Default
    }

    return $value
}

function Mask-Secret {
    param([string]$Value)

    if ([string]::IsNullOrEmpty($Value)) {
        return ""
    }

    if ($Value.Length -le 8) {
        return "****"
    }

    return "$($Value.Substring(0, 4))****$($Value.Substring($Value.Length - 4))"
}

function Add-QueryParameter {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Value
    )

    $separator = if ($Uri.Contains("?")) { "&" } else { "?" }
    return "$Uri$separator$Name=$([uri]::EscapeDataString($Value))"
}

$baseUrl = (Require-Env "PANCAKE_BASE_URL").TrimEnd("/")
$accessToken = Require-Env "PANCAKE_ACCESS_TOKEN"
$pageId = Require-Env "PANCAKE_PAGE_ID"
$tenantSlug = Require-Env "PANCAKE_TENANT_SLUG"
$publicBaseUrl = (Require-Env "CLAWBOT_PUBLIC_BASE_URL").TrimEnd("/")
$webhookSecret = Require-Env "PANCAKE_WEBHOOK_SECRET"

$subscribePath = Read-EnvOrDefault "PANCAKE_SUBSCRIBE_PATH" "/pages/{page_id}/webhooks"
$authMode = (Read-EnvOrDefault "PANCAKE_AUTH_MODE" "query").ToLowerInvariant()
$signatureHeader = Read-EnvOrDefault "PANCAKE_SIGNATURE_HEADER" "x-pancake-signature"
$signatureAlgo = Read-EnvOrDefault "PANCAKE_SIGNATURE_ALGO" "hmac-sha256"
$signatureEncoding = Read-EnvOrDefault "PANCAKE_SIGNATURE_ENCODING" "hex"
$events = (Read-EnvOrDefault "PANCAKE_WEBHOOK_EVENTS" "MESSAGE,COMMENT,DM").Split(",") |
    ForEach-Object { $_.Trim() } |
    Where-Object { $_ }

$callbackUrl = "$publicBaseUrl/webhooks/pancake/$tenantSlug"
$resolvedPath = $subscribePath.Replace("{page_id}", [uri]::EscapeDataString($pageId))
$subscribeUrl = "$baseUrl$resolvedPath"
$headers = @{}

switch ($authMode) {
    "query" {
        $subscribeUrl = Add-QueryParameter $subscribeUrl "access_token" $accessToken
    }
    "bearer" {
        $headers["Authorization"] = "Bearer $accessToken"
    }
    default {
        throw "Unsupported PANCAKE_AUTH_MODE '$authMode'. Use 'query' or 'bearer'."
    }
}

$body = [ordered]@{
    page_id = $pageId
    callback_url = $callbackUrl
    webhook_url = $callbackUrl
    secret = $webhookSecret
    events = @($events)
    signature_header = $signatureHeader
    signature_algo = $signatureAlgo
    signature_encoding = $signatureEncoding
}

if ($DryRun) {
    $safeUrl = $subscribeUrl.Replace([uri]::EscapeDataString($accessToken), (Mask-Secret $accessToken))
    $safeHeaders = @{}
    foreach ($key in $headers.Keys) {
        $safeHeaders[$key] = if ($key -eq "Authorization") { "Bearer $(Mask-Secret $accessToken)" } else { $headers[$key] }
    }

    $safeBody = [ordered]@{}
    foreach ($key in $body.Keys) {
        $safeBody[$key] = if ($key -eq "secret") { Mask-Secret $webhookSecret } else { $body[$key] }
    }

    Write-Host "Dry run: POST $safeUrl"
    Write-Host "Headers:"
    $safeHeaders | ConvertTo-Json -Depth 4
    Write-Host "Body:"
    $safeBody | ConvertTo-Json -Depth 6
    exit 0
}

$json = $body | ConvertTo-Json -Depth 6
Invoke-RestMethod -Method Post -Uri $subscribeUrl -Headers $headers -ContentType "application/json" -Body $json
