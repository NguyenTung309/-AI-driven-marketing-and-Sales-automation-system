# initialize-local-env.ps1
# Dien gia tri ngau nhien cho nhung secret con trong trong deploy/.env.
# Script khong bao gio in gia tri secret, chi in ten khoa da sinh.

param(
    [Parameter(Mandatory = $true)]
    [string]$Path,

    # appsettings.json cua cac service, dung de lay lai dung Encryption:Base64Key dang chay.
    [string[]]$AppSettingsFile = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function New-RandomBytes([int]$Count) {
    $bytes = New-Object byte[] $Count
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
    }
    finally {
        $rng.Dispose()
    }

    return $bytes
}

function New-Base64Secret([int]$ByteCount) {
    return [Convert]::ToBase64String((New-RandomBytes $ByteCount))
}

# Alphabet chi gom ky tu "unreserved" cua RFC 3986. Base64 sinh ra '/', '+', '=' se pha vo
# amqp://user:pass@host trong docker-compose va chuoi ket noi SQL Server, nen mat khau nao duoc
# nhung vao URI/connection string deu phai dung bo ky tu nay.
$script:SafeAlphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~'

function New-SafeSecret([int]$Length) {
    $alphabet = $script:SafeAlphabet
    $builder = New-Object Text.StringBuilder
    # Loai bo modulo bias: bo qua byte roi vao phan du khong chia het cho do dai alphabet.
    $limit = 256 - (256 % $alphabet.Length)
    while ($builder.Length -lt $Length) {
        foreach ($byte in (New-RandomBytes ($Length * 2))) {
            if ($byte -ge $limit) { continue }
            [void]$builder.Append($alphabet[$byte % $alphabet.Length])
            if ($builder.Length -ge $Length) { break }
        }
    }

    return $builder.ToString()
}

# Service chay ngoai run-all.bat fallback ve appsettings. Neu .env mang khoa ma hoa khac
# appsettings thi du lieu ghi bang khoa nay khong doc duoc bang khoa kia (llm/embedding api key,
# inbox/pancake token) va loi decrypt xay ra am tham — nen uu tien dung lai dung khoa da cau hinh.
function Get-ConfiguredEncryptionKey([string[]]$Paths) {
    foreach ($settingsPath in $Paths) {
        if ([string]::IsNullOrWhiteSpace($settingsPath)) { continue }
        if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) { continue }

        try {
            $settings = [IO.File]::ReadAllText($settingsPath) | ConvertFrom-Json
        }
        catch {
            continue
        }

        if ($null -eq $settings) { continue }
        $encryption = $settings.PSObject.Properties['Encryption']
        if ($null -eq $encryption -or $null -eq $encryption.Value) { continue }
        $base64Key = $encryption.Value.PSObject.Properties['Base64Key']
        if ($null -eq $base64Key) { continue }
        if (-not [string]::IsNullOrWhiteSpace($base64Key.Value)) {
            return [string]$base64Key.Value
        }
    }

    return $null
}

$generators = [ordered]@{
    # Tien to 'Cb1-' bao dam du 4 nhom do phuc tap ma SQL Server yeu cau (hoa, thuong, so, ky tu dac biet).
    'MSSQL_SA_PASSWORD'     = { 'Cb1-' + (New-SafeSecret 36) }
    'JWT_SIGNING_KEY'       = { New-Base64Secret 48 }
    'AGENT_SERVICE_AUTH_SIGNING_KEY' = { New-Base64Secret 48 }
    'ENCRYPTION_BASE64_KEY' = {
        $configured = Get-ConfiguredEncryptionKey $AppSettingsFile
        if ([string]::IsNullOrWhiteSpace($configured)) { New-Base64Secret 32 } else { $configured }
    }
    'RABBITMQ_PASSWORD'     = { New-SafeSecret 40 }
    'MINIO_PASSWORD'        = { New-SafeSecret 40 }
    'METABASE_PASSWORD'     = { New-SafeSecret 40 }
}

$resolvedPath = [IO.Path]::GetFullPath($Path)
$lines = [Collections.Generic.List[string]]::new()
foreach ($line in [IO.File]::ReadAllLines($resolvedPath)) {
    [void]$lines.Add($line)
}

$generated = [Collections.Generic.List[string]]::new()
foreach ($entry in $generators.GetEnumerator()) {
    $prefix = $entry.Key + '='
    $index = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i].StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            $index = $i
            break
        }
    }

    if ($index -ge 0) {
        $current = $lines[$index].Substring($prefix.Length).Trim()
        if ([string]::IsNullOrWhiteSpace($current)) {
            $lines[$index] = $prefix + (& $entry.Value)
            [void]$generated.Add($entry.Key)
        }
    }
    else {
        $lines.Add($prefix + (& $entry.Value))
        [void]$generated.Add($entry.Key)
    }
}

$utf8WithoutBom = New-Object Text.UTF8Encoding $false
[IO.File]::WriteAllLines($resolvedPath, $lines, $utf8WithoutBom)

if ($generated.Count -gt 0) {
    Write-Output ('[ENV] Generated local values for: ' + ($generated -join ', '))
    Write-Output '[ENV] Values live in deploy/.env only. Use a secret manager for shared environments.'
}

# Fail closed: khong de service khoi dong voi secret rong.
$missing = [Collections.Generic.List[string]]::new()
foreach ($entry in $generators.GetEnumerator()) {
    $prefix = $entry.Key + '='
    $value = ''
    foreach ($line in $lines) {
        if ($line.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            $value = $line.Substring($prefix.Length).Trim()
            break
        }
    }
    if ([string]::IsNullOrWhiteSpace($value)) {
        [void]$missing.Add($entry.Key)
    }
}
if ($missing.Count -gt 0) {
    throw "deploy/.env is still missing values for: $($missing -join ', ')"
}
