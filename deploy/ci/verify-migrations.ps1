param(
    [string]$MigrationRoot = "deploy/migrations"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Fail {
    param([Parameter(Mandatory = $true)][string]$Message)

    Write-Error $Message
    exit 1
}

function Normalize-SqlIdentifier {
    param([Parameter(Mandatory = $true)][string]$Value)

    $identifier = $Value.Trim().Trim("[", "]")
    if ($identifier.StartsWith("dbo.", [StringComparison]::OrdinalIgnoreCase)) {
        $identifier = $identifier.Substring(4).Trim().Trim("[", "]")
    }

    return $identifier.ToLowerInvariant()
}

function Extract-AlterAddedColumns {
    param([Parameter(Mandatory = $true)][string]$Content)

    $result = @{}
    $matches = [regex]::Matches(
        $Content,
        '(?is)\bALTER\s+TABLE\s+(?<table>(?:dbo\.)?\[?[A-Za-z_][A-Za-z0-9_]*\]?)\s+ADD\s+(?<columns>.*?);')

    foreach ($match in $matches) {
        $table = Normalize-SqlIdentifier $match.Groups["table"].Value
        if (-not $result.ContainsKey($table)) {
            $result[$table] = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        }

        $columnMatches = [regex]::Matches($match.Groups["columns"].Value, '(?im)(?:^|,)\s*\[?(?<column>[A-Za-z_][A-Za-z0-9_]*)\]?\s+')
        foreach ($columnMatch in $columnMatches) {
            [void]$result[$table].Add((Normalize-SqlIdentifier $columnMatch.Groups["column"].Value))
        }
    }

    return $result
}

function Remove-SqlStringLiterals {
    param([Parameter(Mandatory = $true)][string]$Content)

    return [regex]::Replace($Content, "(?is)N?'(?:''|[^'])*'", "''")
}

function Assert-NoSameBatchIndexOnAlterAddedColumn {
    param(
        [Parameter(Mandatory = $true)][string]$FileName,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $addedColumnsByTable = Extract-AlterAddedColumns $Content
    if ($addedColumnsByTable.Count -eq 0) {
        return
    }

    $contentWithoutStringLiterals = Remove-SqlStringLiterals $Content
    $indexMatches = [regex]::Matches(
        $contentWithoutStringLiterals,
        '(?is)\bCREATE\s+(?:UNIQUE\s+)?INDEX\s+\S+\s+ON\s+(?<table>(?:dbo\.)?\[?[A-Za-z_][A-Za-z0-9_]*\]?)\s*\((?<columns>[^)]*)\)')

    foreach ($indexMatch in $indexMatches) {
        $table = Normalize-SqlIdentifier $indexMatch.Groups["table"].Value
        if (-not $addedColumnsByTable.ContainsKey($table)) {
            continue
        }

        $indexColumns = $indexMatch.Groups["columns"].Value.Split(",") |
            ForEach-Object { Normalize-SqlIdentifier (($_.Trim() -split '\s+')[0]) }

        foreach ($indexColumn in $indexColumns) {
            if ($addedColumnsByTable[$table].Contains($indexColumn)) {
                Fail "Migration '$FileName' creates an index on ALTER-added column '$indexColumn' in the same file. Move CREATE INDEX to a later migration because SqlServerFixture and CI execute each .sql file as one batch."
            }
        }
    }
}

if (-not (Test-Path -LiteralPath $MigrationRoot -PathType Container)) {
    Fail "Migration root not found: $MigrationRoot"
}

$files = Get-ChildItem -LiteralPath $MigrationRoot -Filter "*.sql" -File | Sort-Object Name
if ($files.Count -eq 0) {
    Fail "No .sql migration files found under $MigrationRoot."
}

$seenPrefixes = [System.Collections.Generic.HashSet[string]]::new()
foreach ($file in $files) {
    if ($file.Name -notmatch '^\d{4}_.+\.sql$') {
        Fail "Migration file '$($file.Name)' must start with a four-digit prefix, e.g. 0001_init.sql."
    }

    $prefix = $file.Name.Substring(0, 4)
    if (-not $seenPrefixes.Add($prefix)) {
        Fail "Duplicate migration prefix '$prefix' found at '$($file.Name)'."
    }

    $content = Get-Content -LiteralPath $file.FullName -Raw
    if ($content -match '(?im)^\s*GO\s*$') {
        Fail "Migration '$($file.Name)' must not contain GO batch separators; SqlServerFixture and CI execute each .sql file as one batch."
    }

    Assert-NoSameBatchIndexOnAlterAddedColumn -FileName $file.Name -Content $content
}

Write-Host "Migration static guard passed: $($files.Count) .sql files checked under $MigrationRoot."
