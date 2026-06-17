<#
.SYNOPSIS
Replays a captured Pancake webhook payload into a Clawbot tenant callback.

.DESCRIPTION
Use this after capturing one real Pancake webhook body. The script signs the
exact payload bytes with the configured webhook secret and posts them to
POST /webhooks/pancake/{tenantSlug}. PANCAKE_WEBHOOK_PAYLOAD can be either a
path to a JSON file or an inline JSON string.

Required environment variables:
  CLAWBOT_PUBLIC_BASE_URL   Public API origin, no trailing slash
  PANCAKE_TENANT_SLUG       Clawbot tenant slug
  PANCAKE_WEBHOOK_SECRET    Secret configured in Clawbot and Pancake
  PANCAKE_WEBHOOK_PAYLOAD   File path or inline JSON webhook body

Optional environment variables:
  PANCAKE_SIGNATURE_HEADER     Default: x-pancake-signature
  PANCAKE_SIGNATURE_ENCODING   hex or base64. Default: hex
  PANCAKE_SIGNATURE_PREFIX     Default: sha256= for hex, empty for base64

.EXAMPLE
  ./deploy/pancake-webhook-replay.ps1 -DryRun
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

function Resolve-Payload {
    param([Parameter(Mandatory = $true)][string]$Value)

    if (Test-Path -LiteralPath $Value -PathType Leaf) {
        return Get-Content -LiteralPath $Value -Raw
    }

    return $Value
}

function ConvertTo-Hex {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)

    return (($Bytes | ForEach-Object { $_.ToString("x2") }) -join "")
}

function New-HmacSignature {
    param(
        [Parameter(Mandatory = $true)][string]$Body,
        [Parameter(Mandatory = $true)][string]$Secret,
        [Parameter(Mandatory = $true)][string]$Encoding,
        [Parameter(Mandatory = $true)][string]$Prefix
    )

    $hmac = [System.Security.Cryptography.HMACSHA256]::new([System.Text.Encoding]::UTF8.GetBytes($Secret))
    try {
        $hash = $hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Body))
        switch ($Encoding.ToLowerInvariant()) {
            "hex" {
                return "$Prefix$(ConvertTo-Hex $hash)"
            }
            "base64" {
                # Equivalent .NET API: Convert.ToBase64String
                return [Convert]::ToBase64String($hash)
            }
            default {
                throw "Unsupported PANCAKE_SIGNATURE_ENCODING '$Encoding'. Use 'hex' or 'base64'."
            }
        }
    }
    finally {
        $hmac.Dispose()
    }
}

function Mask-Signature {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    $separatorIndex = $Value.IndexOf("=")
    if ($separatorIndex -ge 0) {
        return "$($Value.Substring(0, $separatorIndex + 1))****"
    }

    if ($Value.Length -le 12) {
        return "****"
    }

    return "$($Value.Substring(0, 6))****$($Value.Substring($Value.Length - 4))"
}

$publicBaseUrl = (Require-Env "CLAWBOT_PUBLIC_BASE_URL").TrimEnd("/")
$tenantSlug = Require-Env "PANCAKE_TENANT_SLUG"
$webhookSecret = Require-Env "PANCAKE_WEBHOOK_SECRET"
$payloadInput = Require-Env "PANCAKE_WEBHOOK_PAYLOAD"
$signatureHeader = Read-EnvOrDefault "PANCAKE_SIGNATURE_HEADER" "x-pancake-signature"
$signatureEncoding = Read-EnvOrDefault "PANCAKE_SIGNATURE_ENCODING" "hex"
$defaultPrefix = if ($signatureEncoding.ToLowerInvariant() -eq "hex") { "sha256=" } else { "" }
$signaturePrefix = Read-EnvOrDefault "PANCAKE_SIGNATURE_PREFIX" $defaultPrefix

$body = Resolve-Payload $payloadInput
$callbackUrl = "$publicBaseUrl/webhooks/pancake/$tenantSlug"
$signature = New-HmacSignature $body $webhookSecret $signatureEncoding $signaturePrefix
$headers = @{
    $signatureHeader = $signature
}

if ($DryRun) {
    $safeHeaders = @{}
    $safeHeaders[$signatureHeader] = Mask-Signature $signature

    Write-Host "Dry run: POST $callbackUrl"
    Write-Host "Headers:"
    $safeHeaders | ConvertTo-Json -Depth 4
    Write-Host "Payload bytes: $([System.Text.Encoding]::UTF8.GetByteCount($body))"
    exit 0
}

Invoke-RestMethod -Method Post -Uri $callbackUrl -Headers $headers -ContentType "application/json" -Body $body
