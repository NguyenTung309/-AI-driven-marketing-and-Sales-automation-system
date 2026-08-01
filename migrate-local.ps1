# migrate-local.ps1
# Create the local Docker-backed clawbot database and apply pending migrations transactionally.

[CmdletBinding()]
param(
    # 127.0.0.1 chu khong phai localhost: docker chi publish IPv4 nen localhost (::1) se treo den het timeout.
    [string]$Server = "127.0.0.1,11433",
    [string]$Database = "clawbot",
    [string]$Username = "sa",
    [string]$Password = $env:MSSQL_SA_PASSWORD
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Password)) {
    $envPath = Join-Path $PSScriptRoot "deploy\.env"
    if (Test-Path $envPath -PathType Leaf) {
        $passwordLine = [IO.File]::ReadAllLines($envPath) |
            Where-Object { $_ -match '^MSSQL_SA_PASSWORD=' } |
            Select-Object -First 1
        if ($passwordLine) {
            $Password = $passwordLine.Substring("MSSQL_SA_PASSWORD=".Length).Trim()
        }
    }
}
if ([string]::IsNullOrWhiteSpace($Password)) {
    throw "Set MSSQL_SA_PASSWORD, create deploy/.env, or pass -Password."
}

$connectionArgs = @("-S", $Server, "-U", $Username, "-P", $Password, "-C")
$migrationsDirectory = Join-Path $PSScriptRoot "deploy\migrations"

function Invoke-SqlQuery {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Query
    )

    & sqlcmd @connectionArgs -d $Database -Q $Query -b
    if ($LASTEXITCODE -ne 0) {
        throw "SQL query failed for database '$database'."
    }
}

function Invoke-Migration {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo]$File
    )

    if ($File.Name -notmatch '^\d{4}_[A-Za-z0-9_.-]+\.sql$') {
        throw "Unsafe migration filename: $($File.Name)"
    }

    $escapedName = $File.Name.Replace("'", "''")
    $migrationNumber = [int]$File.Name.Substring(0, 4)
    $isAppliedQuery = @"
SET NOCOUNT ON;
SELECT CASE
    WHEN EXISTS (SELECT 1 FROM dbo.schema_migrations WHERE filename = N'$escapedName') THEN 1
    WHEN EXISTS (
        SELECT 1
        FROM dbo.schema_migrations
        WHERE filename COLLATE Latin1_General_100_BIN2
            LIKE N'[_][_]baseline[_][0-9][0-9][0-9][0-9][_][_]' COLLATE Latin1_General_100_BIN2
          AND TRY_CONVERT(INT, SUBSTRING(filename, 12, 4)) >= $migrationNumber
    ) THEN 1
    ELSE 0
END;
"@
    $isAppliedOutput = & sqlcmd @connectionArgs -d $Database -Q $isAppliedQuery -b -h -1 -W
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read migration state for $($File.Name)."
    }

    $isApplied = ($isAppliedOutput |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Last 1).Trim()
    if ($isApplied -eq "1") {
        Write-Host "[SKIP] $($File.Name)" -ForegroundColor DarkGray
        return
    }

    $newLine = [Environment]::NewLine
    $migrationSql = @"
SET QUOTED_IDENTIFIER ON;
SET ARITHABORT ON;
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
"@ + $newLine + [IO.File]::ReadAllText($File.FullName) + $newLine + @"

    IF NOT EXISTS (SELECT 1 FROM dbo.schema_migrations WHERE filename = N'$escapedName')
        INSERT INTO dbo.schema_migrations (filename, applied_at)
        VALUES (N'$escapedName', SYSDATETIMEOFFSET());
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
"@

    $temporaryFile = [System.IO.Path]::GetTempFileName()
    try {
        [IO.File]::WriteAllText(
            $temporaryFile,
            $migrationSql,
            (New-Object Text.UTF8Encoding($false)))

        Write-Host "[SQL] Applying $($File.Name)..." -ForegroundColor Yellow
        & sqlcmd @connectionArgs -d $Database -i $temporaryFile -b
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to apply migration file $($File.Name)."
        }
    }
    finally {
        if (Test-Path $temporaryFile) {
            Remove-Item -Path $temporaryFile -Force -Confirm:$false
        }
    }
}

Write-Host "[INFO] Connecting to SQL Server: $server" -ForegroundColor Cyan

$createDatabaseQuery = "IF DB_ID(N'$database') IS NULL CREATE DATABASE [$database];"
& sqlcmd @connectionArgs -Q $createDatabaseQuery -b
if ($LASTEXITCODE -ne 0) {
    throw "Failed to create or verify database '$database'."
}
Write-Host "[OK] Database '$database' is ready." -ForegroundColor Green

if (-not (Test-Path $migrationsDirectory -PathType Container)) {
    throw "Migrations directory not found: $migrationsDirectory"
}

Invoke-SqlQuery -Query @"
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.schema_migrations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.schema_migrations (
        filename NVARCHAR(260) NOT NULL CONSTRAINT PK_schema_migrations PRIMARY KEY,
        applied_at DATETIMEOFFSET NOT NULL
    );
END;
"@

# Migration 0094 compiles against the canonical token column. Repair drift before replay;
# this is a no-op on a fresh database until migration 0026 creates dbo.inboxes.
Invoke-SqlQuery -Query @"
SET QUOTED_IDENTIFIER ON;
SET ARITHABORT ON;
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL
   AND EXISTS (
       SELECT 1
       FROM dbo.schema_migrations
       WHERE filename = N'0030_add_inbox_encrypted_token.sql'
          OR (
              -- Cùng cách nhận diện marker như Invoke-Migration và apply-migrations.ps1: so BIN2 để
              -- collation CI không nuốt marker viết hoa sai (__BASELINE_0094__) thành marker hợp lệ.
              LEN(filename) = 17
              AND filename COLLATE Latin1_General_100_BIN2
                  LIKE N'[_][_]baseline[_][0-9][0-9][0-9][0-9][_][_]' COLLATE Latin1_General_100_BIN2
              AND TRY_CONVERT(INT, SUBSTRING(filename, 12, 4)) >= 30)
   )
BEGIN
    IF COL_LENGTH(N'dbo.inboxes', N'encrypted_access_token') IS NULL
        ALTER TABLE dbo.inboxes ADD encrypted_access_token NVARCHAR(MAX) NULL;
    IF EXISTS (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.inboxes')
          AND name = N'encrypted_access_token'
          AND (max_length <> -1 OR is_nullable = 0)
    )
        ALTER TABLE dbo.inboxes ALTER COLUMN encrypted_access_token NVARCHAR(MAX) NULL;
END;
"@

Write-Host "[INFO] Applying migrations from $migrationsDirectory..." -ForegroundColor Cyan
$migrationFiles = Get-ChildItem -LiteralPath $migrationsDirectory -Filter "*.sql" -File |
    Sort-Object Name

foreach ($migrationFile in $migrationFiles) {
    Invoke-Migration -File $migrationFile
}

$runtimeColumnRepairPath = Join-Path $PSScriptRoot "deploy\repair_inbox_runtime_columns.sql"
$collaborationRepairPath = Join-Path $PSScriptRoot "deploy\repair_inbox_collaboration_tables.sql"
$consolidationVerificationPath = Join-Path $PSScriptRoot "deploy\verify_database_table_consolidation.sql"

# Cùng bước run-all.bat chạy sau khi apply migration. Bắt buộc phải có: ledger đánh dấu 0094 đã chạy
# thì mọi cột 0094 thêm sẽ không bao giờ được replay, nên schema lệch chỉ sửa được bằng repair.
# Thiếu bước này, verification bên dưới ném lỗi mà không có đường tự chữa.
Write-Host "[REPAIR] Restoring inbox runtime columns..." -ForegroundColor Yellow
& sqlcmd @connectionArgs -d $Database -I -i $runtimeColumnRepairPath -b
if ($LASTEXITCODE -ne 0) {
    throw "Failed to repair inbox runtime columns."
}

Write-Host "[REPAIR] Restoring inbox collaboration tables..." -ForegroundColor Yellow
& sqlcmd @connectionArgs -d $Database -I -i $collaborationRepairPath -b
if ($LASTEXITCODE -ne 0) {
    throw "Failed to repair inbox collaboration tables."
}

$verificationOutput = & sqlcmd @connectionArgs -d $Database -I -i $consolidationVerificationPath -b -h -1 -W
if ($LASTEXITCODE -ne 0) {
    throw "Database consolidation verification failed."
}

$verificationResult = $verificationOutput |
    Where-Object { $_ -match '^\d{15}\|\d+\|\d+$' } |
    Select-Object -Last 1
if ($verificationResult -notmatch '^111111111111111\|') {
    throw "Database consolidation verification returned an invalid result."
}

Write-Host "[OK] Database consolidation verified: $verificationResult" -ForegroundColor Green
Write-Host "[OK] All pending migrations applied successfully to local database '$database'." -ForegroundColor Green
