[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ContainerName,

    [Parameter(Mandatory = $true)]
    [string]$Database,

    [Parameter(Mandatory = $true)]
    [string]$SaPassword,

    [Parameter(Mandatory = $true)]
    [string]$MigrationsDir,

    [Parameter(Mandatory = $true)]
    [string]$SqlCmdPath,

    [ValidateRange(0, 9999)]
    [int]$BaselineNumber = 0,

    [switch]$BaselineExisting,

    [string]$RepairFilesCsv = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($BaselineExisting -and $BaselineNumber -lt 1) {
    throw "BaselineNumber must be between 1 and 9999 when BaselineExisting is set."
}

function Invoke-SqlQuery {
    param([Parameter(Mandatory = $true)][string]$Query)

    $output = & docker exec $ContainerName $SqlCmdPath `
        -S localhost -U sa -P $SaPassword -C -d $Database -b -h -1 -W -Q $Query
    if ($LASTEXITCODE -ne 0) {
        throw "SQL query failed for database $Database."
    }

    return ($output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 1).Trim()
}

function Invoke-SqlFile {
    param(
        [Parameter(Mandatory = $true)][string]$Sql,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $id = [Guid]::NewGuid().ToString("N")
    $hostPath = Join-Path ([IO.Path]::GetTempPath()) "clawbot_migration_$id.sql"
    $containerPath = "/tmp/clawbot_migration_$id.sql"

    try {
        [IO.File]::WriteAllText($hostPath, $Sql, (New-Object Text.UTF8Encoding($false)))
        & docker cp $hostPath "${ContainerName}:${containerPath}" | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Could not copy migration $Label into $ContainerName."
        }

        & docker exec $ContainerName $SqlCmdPath `
            -S localhost -U sa -P $SaPassword -C -d $Database -b -i $containerPath
        if ($LASTEXITCODE -ne 0) {
            throw "Migration failed: $Label"
        }
    }
    finally {
        if (Test-Path $hostPath) {
            Remove-Item $hostPath -Force -Confirm:$false
        }
        # docker cp để file thuộc root, còn container mssql chạy user 'mssql' và /tmp có sticky bit
        # → rm phải chạy -u root, không thì "Operation not permitted". EAP hạ tạm xuống Continue vì
        # PS 5.1 wrap stderr của native command thành NativeCommandError khi đang Stop.
        $previousEap = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        & docker exec -u root $ContainerName rm -f $containerPath 2>&1 | Out-Null
        $ErrorActionPreference = $previousEap
    }
}

if (-not (Test-Path $MigrationsDir -PathType Container)) {
    throw "Migrations directory not found: $MigrationsDir"
}

Invoke-SqlQuery @"
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.schema_migrations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.schema_migrations (
        filename NVARCHAR(260) NOT NULL CONSTRAINT PK_schema_migrations PRIMARY KEY,
        applied_at DATETIMEOFFSET NOT NULL
    );
END;
SELECT COUNT(1) FROM dbo.schema_migrations;
"@ | Out-Null

$repairFiles = $RepairFilesCsv.Split(',', [StringSplitOptions]::RemoveEmptyEntries)
foreach ($repairFile in $repairFiles) {
    if ($repairFile -notmatch '^\d{4}_[A-Za-z0-9_.-]+\.sql$') {
        throw "Unsafe repair migration filename: $repairFile"
    }

    $repairPath = Join-Path $MigrationsDir $repairFile
    if (-not (Test-Path $repairPath -PathType Leaf)) {
        throw "Repair migration not found: $repairPath"
    }

    $newLine = [Environment]::NewLine
    $repairSql = @"
SET QUOTED_IDENTIFIER ON;
SET ARITHABORT ON;
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
"@ + $newLine + [IO.File]::ReadAllText($repairPath) + $newLine + @"
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
"@
    Write-Output "[REPAIR] $repairFile"
    Invoke-SqlFile -Sql $repairSql -Label $repairFile
}

$baselineMarker = "__baseline_$($BaselineNumber.ToString("D4"))__"
$validHistoryCount = [int](Invoke-SqlQuery @"
SET NOCOUNT ON;
SELECT COUNT(1)
FROM dbo.schema_migrations
WHERE (
        LEN(filename) = 17
        AND filename COLLATE Latin1_General_100_BIN2
            LIKE N'[_][_]baseline[_][0-9][0-9][0-9][0-9][_][_]' COLLATE Latin1_General_100_BIN2
    )
    OR (
        LEN(filename) >= 10
        AND TRY_CONVERT(INT, LEFT(filename, 4)) IS NOT NULL
        AND SUBSTRING(filename, 5, 1) = N'_'
        AND RIGHT(filename, 4) COLLATE Latin1_General_100_BIN2
            = N'.sql' COLLATE Latin1_General_100_BIN2
    );
"@)
if ($BaselineExisting -and $validHistoryCount -eq 0) {
    Invoke-SqlQuery @"
SET NOCOUNT ON;
INSERT INTO dbo.schema_migrations (filename, applied_at)
VALUES (N'$baselineMarker', SYSDATETIMEOFFSET());
SELECT 1;
"@ | Out-Null
    Write-Output "[BASELINE] Existing repaired schema through migration $($BaselineNumber.ToString("D4"))."
}

$highestBaselineNumber = [int](Invoke-SqlQuery @"
SET NOCOUNT ON;
SELECT COALESCE(MAX(TRY_CONVERT(INT, SUBSTRING(filename, 12, 4))), 0)
FROM dbo.schema_migrations
WHERE LEN(filename) = 17
  AND filename COLLATE Latin1_General_100_BIN2
      LIKE N'[_][_]baseline[_][0-9][0-9][0-9][0-9][_][_]' COLLATE Latin1_General_100_BIN2;
"@)
$migrationFiles = Get-ChildItem -LiteralPath $MigrationsDir -Filter "*.sql" -File | Sort-Object Name

foreach ($file in $migrationFiles) {
    if ($file.Name -notmatch '^\d{4}_[A-Za-z0-9_.-]+\.sql$') {
        throw "Unsafe migration filename: $($file.Name)"
    }

    $migrationNumber = [int]$file.Name.Substring(0, 4)
    if ($highestBaselineNumber -gt 0 -and $migrationNumber -le $highestBaselineNumber) {
        Write-Output "[SKIP] $($file.Name) (baseline $($highestBaselineNumber.ToString("D4")))"
        continue
    }

    $escapedName = $file.Name.Replace("'", "''")
    $isApplied = (Invoke-SqlQuery "SET NOCOUNT ON; SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.schema_migrations WHERE filename = N'$escapedName') THEN 1 ELSE 0 END;") -eq "1"
    if ($isApplied) {
        Write-Output "[SKIP] $($file.Name)"
        continue
    }

    $newLine = [Environment]::NewLine
    $prefix = @"
SET QUOTED_IDENTIFIER ON;
SET ARITHABORT ON;
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
"@
    $suffix = @"

    IF NOT EXISTS (SELECT 1 FROM dbo.schema_migrations WHERE filename = N'$escapedName')
        INSERT INTO dbo.schema_migrations (filename, applied_at) VALUES (N'$escapedName', SYSDATETIMEOFFSET());
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
"@
    $sql = $prefix + $newLine + [IO.File]::ReadAllText($file.FullName) + $newLine + $suffix

    Write-Output "[SQL] $($file.Name)"
    Invoke-SqlFile -Sql $sql -Label $file.Name
}
