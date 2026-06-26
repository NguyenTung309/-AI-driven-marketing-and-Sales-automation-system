$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$script = Get-Content (Join-Path $root 'run-all.bat') -Raw

foreach ($needle in @(
    'require_orchestration_approval',
    'requires_approval',
    'llm_config_id',
    'IX_agent_sessions_tenant_status_started_at',
    'HAS_PANCAKE_CONFIG_RUNTIME_COLUMNS',
    'auth_mode',
    'send_path_template'
)) {
    if (-not $script.Contains($needle)) {
        throw "run-all.bat schema gate missing $needle"
    }
}

foreach ($needle in @('--seed', '--tenant', 'SEED_TENANT_SLUG', ':ensure_seed_tenant', ':apply_seeds_if_requested', 'deploy\seed')) {
    if (-not $script.Contains($needle)) {
        throw "run-all.bat seed option missing $needle"
    }
}

foreach ($needle in @('ENCRYPTION_BASE64_KEY', 'Encryption__Base64Key')) {
    if (-not $script.Contains($needle)) {
        throw "run-all.bat local app encryption config missing $needle"
    }
}

$repairIndex = $script.IndexOf('call :repair_runtime_columns')
$skipIndex = $script.IndexOf('Existing schema detected; skipping SQL migration replay.')
if ($repairIndex -lt 0 -or $repairIndex -gt $skipIndex) {
    throw 'run-all.bat must repair latest schema before skipping migration replay'
}

if ($script.Contains('NEEDS_RUNTIME_REPAIR')) {
    throw 'run-all.bat must not depend on parse-time NEEDS_RUNTIME_REPAIR inside batch blocks'
}

$migration0027 = Get-Content (Join-Path $root 'deploy\migrations\0027_agents_llm_config_fk.sql') -Raw
if ($migration0027.Contains('ON DELETE SET NULL')) {
    throw '0027 FK must use NO ACTION to avoid SQL Server multiple cascade paths'
}
if (-not $migration0027.Contains('ON DELETE NO ACTION')) {
    throw '0027 FK missing explicit ON DELETE NO ACTION'
}

$migration0034 = Get-Content (Join-Path $root 'deploy\migrations\0034_pancake_config_runtime_columns.sql') -Raw
foreach ($needle in @('base_url', 'auth_mode', 'send_path_template', 'signature_algo', 'signature_encoding', 'signature_header')) {
    if (-not $migration0034.Contains($needle)) {
        throw "0034 missing pancake config column $needle"
    }
}

$initSql = Get-Content (Join-Path $root 'deploy\migrations\0001_init.sql') -Raw
foreach ($needle in @('base_url', 'auth_mode', 'send_path_template', 'signature_algo', 'signature_encoding', 'signature_header')) {
    if (-not $initSql.Contains($needle)) {
        throw "0001 missing pancake config column $needle"
    }
}

$repairStart = $script.IndexOf(':repair_runtime_columns')
$repairEnd = $script.IndexOf(':incomplete_schema')
$repairBlock = $script.Substring($repairStart, $repairEnd - $repairStart)
foreach ($needle in @('SET QUOTED_IDENTIFIER ON;', 'SET ARITHABORT ON;')) {
    if (-not $repairBlock.Contains($needle)) {
        throw "run-all.bat repair SQL missing $needle"
    }
}

$seedBlock = $script.Substring($script.IndexOf(':apply_seeds_if_requested'))
foreach ($needle in @('-v TenantSlug="%SEED_TENANT_SLUG%"', '$(TenantSlug)')) {
    if (-not $seedBlock.Contains($needle)) {
        throw "run-all.bat seed SQL missing $needle"
    }
}

foreach ($seed in Get-ChildItem (Join-Path $root 'deploy\seed') -Filter '*.sql') {
    $seedText = Get-Content $seed.FullName -Raw
    if (-not $seedText.Contains('N''$(TenantSlug)''')) {
        throw "$($seed.Name) must use SQLCMD TenantSlug"
    }
}

$dryRun = & cmd /c "`"$(Join-Path $root 'run-all.bat')`" --dry-run --seed --tenant demo" 2>&1 | Out-String
if (-not $dryRun.Contains('tenant demo')) {
    throw "run-all.bat --tenant demo dry-run did not select tenant demo: $dryRun"
}
