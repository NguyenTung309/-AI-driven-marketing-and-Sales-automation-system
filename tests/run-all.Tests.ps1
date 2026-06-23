$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$script = Get-Content (Join-Path $root 'run-all.bat') -Raw

foreach ($needle in @(
    'require_orchestration_approval',
    'requires_approval',
    'llm_config_id',
    'IX_agent_sessions_tenant_status_started_at'
)) {
    if (-not $script.Contains($needle)) {
        throw "run-all.bat schema gate missing $needle"
    }
}

foreach ($needle in @('--seed', ':apply_seeds_if_requested', 'deploy\seed')) {
    if (-not $script.Contains($needle)) {
        throw "run-all.bat seed option missing $needle"
    }
}
