param(
    [switch]$Strict,
    [switch]$ReportOnly,
    [switch]$SkipDockerProbe,
    [switch]$AgentServiceAuthenticationOnly,
    [string[]]$EnvironmentFile = @(),
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

function Import-EnvironmentFiles {
    param([Parameter(Mandatory = $true)][string[]]$Paths)

    $importedNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($path in $Paths) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Environment file is missing."
        }

        foreach ($line in [System.IO.File]::ReadLines($path)) {
            if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith("#")) {
                continue
            }

            $separator = $line.IndexOf("=")
            if ($separator -le 0) {
                throw "Environment file contains an invalid entry."
            }

            $name = $line.Substring(0, $separator)
            if ($name -notmatch "^[A-Za-z_][A-Za-z0-9_]*$" -or -not $importedNames.Add($name)) {
                throw "Environment file contains an invalid or duplicate entry."
            }

            $value = $line.Substring($separator + 1)
            if ($value -match "^(?<quote>[`"'])(?<content>.*)\k<quote>(?:\s+#.*)?$") {
                $value = $Matches.content
            }
            elseif ($value -match "^(.*?)\s+#") {
                $value = $Matches[1].TrimEnd()
            }

            [Environment]::SetEnvironmentVariable($name, $value, "Process")
        }
    }
}

if ($EnvironmentFile.Count -gt 0) {
    Import-EnvironmentFiles -Paths $EnvironmentFile
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
        Add-Check "Docker" "SKIP" "Docker probe skipped by -SkipDockerProbe."
        return
    }

    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if (-not $docker) {
        Add-Check "Docker" "MISSING" "Docker CLI not found. Install Docker Desktop or Docker Engine to run the local stack."
        return
    }

    & docker version *> $null
    if ($LASTEXITCODE -ne 0) {
        Add-Check "Docker" "FAIL" "Docker CLI exists but server is not reachable. Start Docker before launching the local stack."
        return
    }

    & docker info *> $null
    if ($LASTEXITCODE -ne 0) {
        Add-Check "Docker" "FAIL" "Docker daemon is not ready."
        return
    }

    Add-Check "Docker" "PASS" "Docker is ready for the local stack."
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
    # Meta App credentials are tenant-managed in the encrypted admin UI; env values are optional bootstrap fallback.
    Test-RequiredEnv -Name "CONTENT_PUBLISHER_BASE_URL" -Purpose "native or brokered content publisher API base URL" -Aliases @("Content__Publisher__Endpoint")
    Test-RequiredEnv -Name "CONTENT_PUBLISHER_API_KEY" -Purpose "content publisher API key" -Aliases @("Content__Publisher__Token")
}

function Test-AgentServiceAuthenticationReadiness {
    if ($PSVersionTable.PSEdition -ne "Core" -or $PSVersionTable.PSVersion -lt [Version]"7.2") {
        Add-Check "PowerShell" "FAIL" "AgentService TLS readiness requires PowerShell 7.2 or later."
        return
    }

    Add-Check "PowerShell" "PASS" "PowerShell runtime supports the AgentService TLS readiness checks."
    Test-RequiredEnv -Name "JWT_SIGNING_KEY" -Purpose "public API JWT signing key"
    Test-RequiredEnv -Name "AGENT_SERVICE_AUTH_SIGNING_KEY" -Purpose "dedicated API-to-AgentService signing key"
    Test-RequiredEnv -Name "AGENT_SERVICE_TLS_CERTIFICATE_PATH" -Purpose "AgentService gRPC TLS certificate path"
    Test-RequiredEnv -Name "AGENT_SERVICE_TLS_CA_CERTIFICATE_PATH" -Purpose "trusted AgentService gRPC CA certificate path"
    Test-RequiredEnv -Name "AGENT_SERVICE_TLS_CERTIFICATE_PASSWORD" -Purpose "AgentService gRPC TLS certificate password"

    $agentServiceKey = Get-EnvValue "AGENT_SERVICE_AUTH_SIGNING_KEY"
    if ([string]::IsNullOrWhiteSpace($agentServiceKey)) {
        return
    }

    $agentServiceKeyBytes = $null
    try {
        $agentServiceKeyBytes = [Convert]::FromBase64String($agentServiceKey.Trim())
        if ($agentServiceKeyBytes.Length -lt 32) {
            Add-Check "AGENT_SERVICE_AUTH_SIGNING_KEY_FORMAT" "FAIL" "Dedicated AgentService signing key must decode from Base64 to at least 32 bytes."
        }
        else {
            Add-Check "AGENT_SERVICE_AUTH_SIGNING_KEY_FORMAT" "PASS" "Dedicated AgentService signing key has a valid Base64 length."
        }
    }
    catch {
        Add-Check "AGENT_SERVICE_AUTH_SIGNING_KEY_FORMAT" "FAIL" "Dedicated AgentService signing key must be Base64 encoded."
    }

    $publicJwtKey = Get-EnvValue "JWT_SIGNING_KEY"
    if (-not [string]::IsNullOrWhiteSpace($publicJwtKey)) {
        $publicJwtKeyBytes = [System.Text.Encoding]::UTF8.GetBytes($publicJwtKey)
        if ($publicJwtKeyBytes.Length -lt 32) {
            Add-Check "JWT_SIGNING_KEY_FORMAT" "FAIL" "Public API JWT signing key must contain at least 32 UTF-8 bytes."
        }
        else {
            Add-Check "JWT_SIGNING_KEY_FORMAT" "PASS" "Public API JWT signing key has a valid length."
        }

        if ($null -ne $agentServiceKeyBytes -and [System.Security.Cryptography.CryptographicOperations]::FixedTimeEquals($agentServiceKeyBytes, $publicJwtKeyBytes)) {
            Add-Check "AGENT_SERVICE_AUTH_SIGNING_KEY_DISTINCT" "FAIL" "Dedicated AgentService signing key must not reuse the public API JWT signing key material."
        }
        else {
            Add-Check "AGENT_SERVICE_AUTH_SIGNING_KEY_DISTINCT" "PASS" "Dedicated AgentService signing key is distinct from the public API JWT signing key material."
        }
    }

    $certificatePath = Get-EnvValue "AGENT_SERVICE_TLS_CERTIFICATE_PATH"
    $certificatePassword = Get-EnvValue "AGENT_SERVICE_TLS_CERTIFICATE_PASSWORD"
    $caCertificatePath = Get-EnvValue "AGENT_SERVICE_TLS_CA_CERTIFICATE_PATH"
    if ([string]::IsNullOrWhiteSpace($certificatePath) -or [string]::IsNullOrWhiteSpace($certificatePassword) -or [string]::IsNullOrWhiteSpace($caCertificatePath)) {
        return
    }

    if (-not (Test-Path -LiteralPath $certificatePath -PathType Leaf)) {
        Add-Check "AGENT_SERVICE_TLS_CERTIFICATE" "FAIL" "AgentService gRPC PFX certificate path is not a readable file."
        return
    }
    if (-not (Test-Path -LiteralPath $caCertificatePath -PathType Leaf)) {
        Add-Check "AGENT_SERVICE_TLS_CA_CERTIFICATE" "FAIL" "AgentService gRPC CA certificate path is not a readable file."
        return
    }

    $pfxCertificates = $null
    $serverCertificate = $null
    $caCertificate = $null
    try {
        $pfxCertificates = [System.Security.Cryptography.X509Certificates.X509Certificate2Collection]::new()
        $pfxCertificates.Import(
            $certificatePath,
            $certificatePassword,
            [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
        $serverCertificates = @($pfxCertificates | Where-Object HasPrivateKey)
        if ($serverCertificates.Count -ne 1) {
            Add-Check "AGENT_SERVICE_TLS_PRIVATE_KEY" "FAIL" "AgentService gRPC PFX must contain exactly one certificate with a private key."
            return
        }

        $serverCertificate = $serverCertificates[0]

        $privateKey = $null
        try {
            $privateKey = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($serverCertificate)
            if ($null -ne $privateKey) {
                [void]$privateKey.SignData(
                    [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32),
                    [System.Security.Cryptography.HashAlgorithmName]::SHA256,
                    [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
            }
            else {
                $privateKey = [System.Security.Cryptography.X509Certificates.ECDsaCertificateExtensions]::GetECDsaPrivateKey($serverCertificate)
                if ($null -eq $privateKey) {
                    Add-Check "AGENT_SERVICE_TLS_PRIVATE_KEY" "FAIL" "AgentService gRPC PFX private key is not RSA or ECDSA."
                    return
                }

                [void]$privateKey.SignData(
                    [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32),
                    [System.Security.Cryptography.HashAlgorithmName]::SHA256)
            }
        }
        finally {
            if ($null -ne $privateKey) { $privateKey.Dispose() }
        }

        Add-Check "AGENT_SERVICE_TLS_PRIVATE_KEY" "PASS" "AgentService gRPC PFX private key is available to the deploy identity."
        $caCertificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPem(
            [System.IO.File]::ReadAllText($caCertificatePath))
        $now = [DateTime]::UtcNow
        if ($serverCertificate.NotBefore.ToUniversalTime() -gt $now -or $serverCertificate.NotAfter.ToUniversalTime() -le $now) {
            Add-Check "AGENT_SERVICE_TLS_VALIDITY" "FAIL" "AgentService gRPC certificate is not currently valid."
            return
        }
        if ($serverCertificate.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::DnsName, $false) -ine "agentservice") {
            Add-Check "AGENT_SERVICE_TLS_HOSTNAME" "FAIL" "AgentService gRPC certificate must identify DNS name agentservice."
            return
        }

        $chain = [System.Security.Cryptography.X509Certificates.X509Chain]::new()
        try {
            $chain.ChainPolicy.TrustMode = [System.Security.Cryptography.X509Certificates.X509ChainTrustMode]::CustomRootTrust
            [void]$chain.ChainPolicy.CustomTrustStore.Add($caCertificate)
            [void]$chain.ChainPolicy.ApplicationPolicy.Add([System.Security.Cryptography.Oid]::new("1.3.6.1.5.5.7.3.1"))
            $chain.ChainPolicy.RevocationMode = [System.Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
            foreach ($certificate in $pfxCertificates) {
                if ($certificate.Thumbprint -ne $serverCertificate.Thumbprint -and $certificate.Thumbprint -ne $caCertificate.Thumbprint) {
                    [void]$chain.ChainPolicy.ExtraStore.Add($certificate)
                }
            }

            if (-not $chain.Build($serverCertificate)) {
                Add-Check "AGENT_SERVICE_TLS_CHAIN" "FAIL" "AgentService gRPC certificate does not validate to the configured CA for TLS server authentication."
                return
            }
        }
        finally {
            $chain.Dispose()
        }

        Add-Check "AGENT_SERVICE_TLS_CERTIFICATE" "PASS" "AgentService gRPC certificate, hostname, CA chain, validity, and server-auth usage are valid."
    }
    catch {
        Add-Check "AGENT_SERVICE_TLS_CERTIFICATE" "FAIL" "AgentService gRPC certificate or CA could not be loaded and validated."
    }
    finally {
        if ($null -ne $pfxCertificates) {
            foreach ($certificate in $pfxCertificates) { $certificate.Dispose() }
        }

        if ($null -ne $caCertificate) { $caCertificate.Dispose() }
    }
}

if ($AgentServiceAuthenticationOnly) {
    Test-AgentServiceAuthenticationReadiness
}
else {
    Test-DockerReadiness
    Test-KbAuthoringReadiness
    Test-PancakeReadiness
    Test-LlmReadiness
    Test-VendorReadiness
    Test-AgentServiceAuthenticationReadiness
}

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
