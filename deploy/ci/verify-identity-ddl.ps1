param(
    [string]$MigrationRoot = "deploy/migrations",
    [string]$SourceRoot = "src/shared/Clawbot.Infrastructure"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

function Read-RequiredFile([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        Fail "Required file not found: $Path"
    }

    return Get-Content -LiteralPath $Path -Raw
}

function Assert-Contains([string]$Content, [string]$Needle, [string]$Label) {
    if ($Content -notlike "*$Needle*") {
        Fail "$Label missing required token: $Needle"
    }
}

function Assert-NoGoSeparator([string]$Content, [string]$Label) {
    if ($Content -match '(?im)^\s*GO\s*$') {
        Fail "$Label must not contain GO batch separators; the migration runner executes each .sql file as one batch."
    }
}

$initialSchema = Join-Path $MigrationRoot "0001_init.sql"
$identityReconcile = Join-Path $MigrationRoot "0013_identity_reconcile.sql"
$identityIndexes = Join-Path $MigrationRoot "0014_identity_user_indexes.sql"
$identityMapping = Join-Path $SourceRoot "Persistence/Configurations/IdentityUserConfiguration.cs"

$initial = Read-RequiredFile $initialSchema
$reconcile = Read-RequiredFile $identityReconcile
$indexes = Read-RequiredFile $identityIndexes
$mapping = Read-RequiredFile $identityMapping

Assert-NoGoSeparator $initial "0001_init.sql"
Assert-NoGoSeparator $reconcile "0013_identity_reconcile.sql"
Assert-NoGoSeparator $indexes "0014_identity_user_indexes.sql"

Assert-Contains $mapping 'builder.ToTable("users")' "IdentityUserConfiguration"

Assert-Contains $initial "CREATE TABLE users" "0001_init.sql"
Assert-Contains $reconcile "ALTER TABLE users ADD" "0013_identity_reconcile.sql"
Assert-Contains $reconcile "user_name" "0013_identity_reconcile.sql"
Assert-Contains $reconcile "normalized_user_name" "0013_identity_reconcile.sql"
Assert-Contains $reconcile "normalized_email" "0013_identity_reconcile.sql"
Assert-Contains $reconcile "email_confirmed" "0013_identity_reconcile.sql"
Assert-Contains $reconcile "concurrency_stamp" "0013_identity_reconcile.sql"
Assert-Contains $reconcile "phone_number" "0013_identity_reconcile.sql"
Assert-Contains $reconcile "phone_number_confirmed" "0013_identity_reconcile.sql"
Assert-Contains $reconcile "two_factor_enabled" "0013_identity_reconcile.sql"
Assert-Contains $reconcile "lockout_enabled" "0013_identity_reconcile.sql"
Assert-Contains $reconcile "date_of_birth" "0013_identity_reconcile.sql"
Assert-Contains $reconcile "avatar_url" "0013_identity_reconcile.sql"

Assert-Contains $reconcile "CREATE TABLE AspNetRoles" "0013_identity_reconcile.sql"
Assert-Contains $reconcile "CREATE TABLE AspNetUserRoles" "0013_identity_reconcile.sql"
Assert-Contains $reconcile "CREATE TABLE AspNetUserClaims" "0013_identity_reconcile.sql"
Assert-Contains $reconcile "CREATE TABLE AspNetUserLogins" "0013_identity_reconcile.sql"
Assert-Contains $reconcile "CREATE TABLE AspNetUserTokens" "0013_identity_reconcile.sql"
Assert-Contains $reconcile "CREATE TABLE AspNetRoleClaims" "0013_identity_reconcile.sql"

Assert-Contains $indexes "ix_users_normalized_email" "0014_identity_user_indexes.sql"
Assert-Contains $indexes "ix_users_normalized_user_name" "0014_identity_user_indexes.sql"

Write-Host "Identity DDL preflight passed."
