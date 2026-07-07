# migrate-local.ps1
# Script to create clawbot database and apply migrations to local SQL Server instance using Windows Authentication.

$ErrorActionPreference = "Stop"

$server = "localhost"
$dbName = "clawbot"

Write-Host "[INFO] Connecting to SQL Server: $server" -ForegroundColor Cyan

# 1. Create database if not exists
$createDbQuery = "IF DB_ID(N'$dbName') IS NULL CREATE DATABASE $dbName;"
& sqlcmd -S $server -E -C -Q $createDbQuery -b
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to create/verify database '$dbName'."
    exit $LASTEXITCODE
}
Write-Host "[OK] Database '$dbName' is ready." -ForegroundColor Green

# 2. Apply migrations in order
$migrationsDir = Join-Path $PSScriptRoot "deploy\migrations"
Write-Host "[INFO] Applying migrations from $migrationsDir..." -ForegroundColor Cyan

$sqlFiles = Get-ChildItem -Path $migrationsDir -Filter *.sql | Sort-Object Name

foreach ($file in $sqlFiles) {
    Write-Host "[SQL] Applying $($file.Name)..." -ForegroundColor Yellow
    # Prepend SET QUOTED_IDENTIFIER ON and SET ARITHABORT ON to avoid common compliance issues
    $tempFile = [System.IO.Path]::GetTempFileName()
    "SET QUOTED_IDENTIFIER ON;" | Out-File -FilePath $tempFile -Encoding utf8
    "SET ARITHABORT ON;" | Out-File -FilePath $tempFile -Append -Encoding utf8
    Get-Content -Path $file.FullName | Out-File -FilePath $tempFile -Append -Encoding utf8

    & sqlcmd -S $server -E -C -d $dbName -i $tempFile -b
    $exitCode = $LASTEXITCODE
    Remove-Item -Path $tempFile -Force

    if ($exitCode -ne 0) {
        Write-Error "Failed to apply migration file $($file.Name)."
        exit $exitCode
    }
}

Write-Host "[OK] All migrations applied successfully to local database '$dbName'." -ForegroundColor Green
