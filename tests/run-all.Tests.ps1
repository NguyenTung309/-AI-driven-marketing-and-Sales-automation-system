$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$script = Get-Content (Join-Path $root 'run-all.bat') -Raw

foreach ($needle in @(
    'require_orchestration_approval',
    'require_content_review',
    'require_chat_reply_approval',
    'require_kb_human_review',
    'monthly_cost_cap_usd',
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

$existingSchemaBlockStart = $script.IndexOf('if "%HAS_SCHEMA%"=="1" (')
$existingSchemaBlockEnd = $script.IndexOf('goto replay_migrations', $existingSchemaBlockStart)
if ($existingSchemaBlockStart -lt 0 -or $existingSchemaBlockEnd -lt 0) {
    throw 'run-all.bat must keep the existing-schema repair branch'
}

$existingSchemaBlock = $script.Substring(
    $existingSchemaBlockStart,
    $existingSchemaBlockEnd - $existingSchemaBlockStart)
$repairIndex = $existingSchemaBlock.IndexOf('call :repair_runtime_columns')
$baselineIndex = $existingSchemaBlock.IndexOf('call :baseline_existing_migrations')
if ($repairIndex -lt 0 -or $baselineIndex -lt 0 -or $repairIndex -gt $baselineIndex) {
    throw 'run-all.bat must repair the existing schema before baselining migration history'
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

foreach ($needle in @(
    ':repair_tenant_runtime_columns',
    ':verify_tenant_runtime_columns',
    'deploy\repair_tenant_runtime_columns.sql',
    'call :repair_tenant_runtime_columns',
    'call :verify_tenant_runtime_columns'
)) {
    if (-not $script.Contains($needle)) {
        throw "run-all.bat tenant runtime gate missing $needle"
    }
}

$repairCallIndex = $script.IndexOf('call :apply_migrations_if_needed')
$tenantRepairIndex = $script.IndexOf('call :repair_tenant_runtime_columns', $repairCallIndex)
$tenantVerifyIndex = $script.IndexOf('call :verify_tenant_runtime_columns', $tenantRepairIndex)
$serviceStartIndex = $script.IndexOf('Opening service windows...')
if ($tenantRepairIndex -lt 0 -or $tenantVerifyIndex -lt 0 -or $serviceStartIndex -lt 0 -or $tenantVerifyIndex -gt $serviceStartIndex) {
    throw 'run-all.bat must repair and verify tenant runtime columns before starting services'
}

$tenantRepairSql = Get-Content (Join-Path $root 'deploy\repair_tenant_runtime_columns.sql') -Raw
foreach ($needle in @(
    'monthly_cost_cap_usd',
    'require_content_review',
    'require_chat_reply_approval',
    'require_kb_human_review'
)) {
    if (-not $tenantRepairSql.Contains($needle)) {
        throw "deploy/repair_tenant_runtime_columns.sql missing $needle"
    }
}

$repairStart = $script.IndexOf(':repair_runtime_columns')
$repairEnd = $script.IndexOf(':incomplete_schema')
$repairBlock = $script.Substring($repairStart, $repairEnd - $repairStart)
foreach ($needle in @(
    'SET QUOTED_IDENTIFIER ON;',
    'SET ARITHABORT ON;',
    'call :repair_tenant_runtime_columns',
    'monthly_cost_cap_usd',
    'require_content_review',
    'require_chat_reply_approval',
    'require_kb_human_review'
)) {
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

$globalSeedFiles = @(
    '01_cleanup_duplicate_inbox_members.sql',
    '05_permission_admin_inboxes.sql'
)
foreach ($seed in Get-ChildItem (Join-Path $root 'deploy\seed') -Filter '*.sql') {
    if ($seed.Name -in $globalSeedFiles) {
        continue
    }

    $seedText = Get-Content $seed.FullName -Raw
    if (-not $seedText.Contains('N''$(TenantSlug)''')) {
        throw "$($seed.Name) must use SQLCMD TenantSlug"
    }
}

$dryRun = & cmd /c "`"$(Join-Path $root 'run-all.bat')`" --dry-run --seed --tenant demo" 2>&1 | Out-String
if (-not $dryRun.Contains('tenant demo')) {
    throw "run-all.bat --tenant demo dry-run did not select tenant demo: $dryRun"
}
