param(
    [switch]$Strict,
    [switch]$ReportOnly,
    [switch]$SkipDockerProbe,
    [string]$KbAuthoringPath = "deploy/seed/kb-authoring.json",
    [string]$RequiredManifest = "deploy/seed/kb-authoring.required.json"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($Strict -and $ReportOnly) {
    Write-Error "Use either -Strict or -ReportOnly, not both."
    exit 1
}

$reportOnlyMode = $ReportOnly -or -not $Strict
$checks = New-Object System.Collections.Generic.List[object]

function Add-Check {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Status,
        [Parameter(Mandatory = $true)][string]$Detail
    )

    $checks.Add([pscustomobject]@{
        Name = $Name
        Status = $Status
        Detail = $Detail
    })
}

function Get-EnvValue {
    param([Parameter(Mandatory = $true)][string]$Name)

    return [Environment]::GetEnvironmentVariable($Name, "Process")
}

function Get-EnvMatch {
    param([Parameter(Mandatory = $true)][string[]]$Names)

    foreach ($candidate in $Names) {
        $value = Get-EnvValue $candidate
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return [pscustomobject]@{
                Name = $candidate
                Value = $value
            }
        }
    }

    return $null
}

function Test-RequiredEnv {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Purpose,
        [string[]]$Aliases = @()
    )

    $names = @($Name) + $Aliases
    $match = Get-EnvMatch -Names $names
    if (-not $match) {
        $aliasText = if ($Aliases.Count -gt 0) { " Accepted aliases: $($Aliases -join ', ')." } else { "" }
        Add-Check $Name "MISSING" "$Purpose is not configured.$aliasText"
        return
    }

    Add-Check $Name "PASS" "$Purpose is configured via $($match.Name)."
}

function Get-PowerShellExecutable {
    $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($pwsh) {
        return $pwsh.Source
    }

    $powershell = Get-Command powershell -ErrorAction SilentlyContinue
    if ($powershell) {
        return $powershell.Source
    }

    return $null
}

function Test-DockerReadiness {
    if ($SkipDockerProbe) {
        Add-Check "Docker/Testcontainers" "SKIP" "Docker probe skipped by -SkipDockerProbe. Full check mirrors deploy/ci/verify-testcontainers.ps1."
        return
    }

    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if (-not $docker) {
        Add-Check "Docker/Testcontainers" "MISSING" "Docker CLI not found. Install Docker Desktop or Docker Engine; full integration gate is deploy/ci/verify-testcontainers.ps1."
        return
    }

    & docker version *> $null
    if ($LASTEXITCODE -ne 0) {
        Add-Check "Docker/Testcontainers" "FAIL" "Docker CLI exists but server is not reachable. Start Docker before running Testcontainers."
        return
    }

    & docker info *> $null
    if ($LASTEXITCODE -ne 0) {
        Add-Check "Docker/Testcontainers" "FAIL" "Docker daemon is not ready for Testcontainers."
        return
    }

    Add-Check "Docker/Testcontainers" "PASS" "Docker is ready for Testcontainers."
}

function Test-KbAuthoringReadiness {
    if (-not (Test-Path -LiteralPath $KbAuthoringPath -PathType Leaf)) {
        Add-Check "KB authoring" "MISSING" "Approved KB authoring file '$KbAuthoringPath' is missing. Start from deploy/seed/kb-authoring.template.json."
        return
    }

    $shell = Get-PowerShellExecutable
    if (-not $shell) {
        Add-Check "KB authoring" "FAIL" "PowerShell runtime not found for deploy/seed/validate-kb-authoring.ps1."
        return
    }

    $validateScript = Join-Path "deploy/seed" "validate-kb-authoring.ps1"
    $output = & $shell -NoProfile -ExecutionPolicy Bypass -File $validateScript -Path $KbAuthoringPath -RequiredManifest $RequiredManifest 2>&1
    if ($LASTEXITCODE -ne 0) {
        Add-Check "KB authoring" "FAIL" ("validate-kb-authoring.ps1 failed: " + (($output | Out-String).Trim()))
        return
    }

    Add-Check "KB authoring" "PASS" "Approved KB content and >=20 Q/A per required module passed validation."
}

function Test-PancakeReadiness {
    Test-RequiredEnv -Name "PANCAKE_BASE_URL" -Purpose "Pancake API base URL" -Aliases @("Channels__Pancake__BaseUrl")
    Test-RequiredEnv -Name "PANCAKE_ACCESS_TOKEN" -Purpose "Pancake access token" -Aliases @("Channels__Pancake__AccessToken")
    Test-RequiredEnv -Name "PANCAKE_PAGE_ID" -Purpose "Pancake page/account id"
    Test-RequiredEnv -Name "PANCAKE_TENANT_SLUG" -Purpose "tenant slug for live webhook URL"
    Test-RequiredEnv -Name "CLAWBOT_PUBLIC_BASE_URL" -Purpose "public ClawBot API base URL"
    Test-RequiredEnv -Name "PANCAKE_WEBHOOK_SECRET" -Purpose "Pancake webhook secret" -Aliases @("Channels__Pancake__WebhookSecret", "Gateway__Pancake__WebhookSecret")
    Test-RequiredEnv -Name "PANCAKE_WEBHOOK_PAYLOAD" -Purpose "captured live Pancake comment webhook payload"
}

function Test-LlmReadiness {
    Test-RequiredEnv -Name "ANTHROPIC_API_KEY" -Purpose "Anthropic API key for chat, RAG evaluation, and accuracy runs" -Aliases @("Anthropic__ApiKey")
    Test-RequiredEnv -Name "EMBEDDING_API_KEY" -Purpose "real embedding provider key for KB deploy/search accuracy; maps to Embedding:ApiKey" -Aliases @("Embedding__ApiKey")
    Test-RequiredEnv -Name "CONTENT_LLM_API_KEY" -Purpose "OpenAI-compatible content LLM API key; maps to Content:Llm:ApiKey" -Aliases @("Content__Llm__ApiKey")
}

function Test-VendorReadiness {
    Test-RequiredEnv -Name "META_ACCESS_TOKEN" -Purpose "Meta Graph/Marketing API access token" -Aliases @("Ads__Meta__AccessToken")
    Test-RequiredEnv -Name "META_PAGE_ID" -Purpose "Meta page id for native publishing"
    Test-RequiredEnv -Name "TIKTOK_ACCESS_TOKEN" -Purpose "TikTok Business API access token" -Aliases @("Ads__TikTok__AccessToken")
    Test-RequiredEnv -Name "TIKTOK_ADVERTISER_ID" -Purpose "TikTok advertiser id for ads/lookalike verification" -Aliases @("Ads__TikTok__AdvertiserId")
    Test-RequiredEnv -Name "CONTENT_PUBLISHER_BASE_URL" -Purpose "native or brokered content publisher API base URL" -Aliases @("Content__Publisher__Endpoint")
    Test-RequiredEnv -Name "CONTENT_PUBLISHER_API_KEY" -Purpose "content publisher API key" -Aliases @("Content__Publisher__Token")
}

Test-DockerReadiness
Test-KbAuthoringReadiness
Test-PancakeReadiness
Test-LlmReadiness
Test-VendorReadiness

$checks | Sort-Object Status, Name | Format-Table -AutoSize

$blocking = @($checks | Where-Object { $_.Status -in @("MISSING", "FAIL") })
if ($blocking.Count -gt 0) {
    Write-Host "GO-LIVE READINESS FAILED: $($blocking.Count) blocking check(s) are not ready."
    if ($reportOnlyMode) {
        Write-Host "ReportOnly mode: exiting 0. Re-run with -Strict to fail on blockers."
        exit 0
    }

    exit 1
}

Write-Host "GO-LIVE READINESS PASSED: all external readiness checks are satisfied."
