param(
    [string]$Path = "deploy/seed/kb-authoring.json",
    [string]$OutputPath = "deploy/seed/kb-modules.sql",
    [string]$RequiredManifest = "deploy/seed/kb-authoring.required.json",
    [switch]$SmokeTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Escape-Sql {
    param([object]$Value)

    if ($null -eq $Value) {
        return "NULL"
    }

    $text = [string]$Value
    return "N'$($text.Replace("'", "''"))'"
}

function Read-Property {
    param(
        [object]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function As-Array {
    param([object]$Value)

    $items = [System.Collections.ArrayList]::new()
    if ($null -ne $Value) {
        foreach ($item in @($Value)) {
            [void]$items.Add($item)
        }
    }

    return ,$items
}

function Var-Name {
    param([Parameter(Mandatory = $true)][string]$Code)

    return "@module_$($Code.Replace("-", "_"))"
}

function New-SmokeAuthoringFile {
    param(
        [Parameter(Mandatory = $true)][string]$RequiredManifest,
        [Parameter(Mandatory = $true)][string]$Directory
    )

    $required = Get-Content -LiteralPath $RequiredManifest -Raw | ConvertFrom-Json
    $modules = [System.Collections.ArrayList]::new()
    foreach ($requiredModule in (As-Array (Read-Property $required "requiredModules"))) {
        $code = [string](Read-Property $requiredModule "code")
        $testCases = [System.Collections.ArrayList]::new()
        for ($i = 1; $i -le 20; $i++) {
            [void]$testCases.Add([ordered]@{
                question = "Smoke question $code $i"
                expectedAnswer = "Smoke answer $code $i"
            })
        }

        [void]$modules.Add([ordered]@{
            code = $code
            name = [string](Read-Property $requiredModule "name")
            description = "Smoke validation description for $code."
            ownerRole = [string](Read-Property $requiredModule "ownerRole")
            contentMd = "Smoke validation content for $code. This file is generated only by -SmokeTest."
            testCases = @($testCases)
        })
    }

    $path = Join-Path $Directory "kb-authoring.smoke.json"
    [ordered]@{
        tenantSlug = "smoke-tenant"
        modules = @($modules)
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$smokeDir = $null
if ($SmokeTest) {
    $smokeDir = Join-Path ([System.IO.Path]::GetTempPath()) "clawbot-kb-seed-$([Guid]::NewGuid().ToString("N"))"
    New-Item -ItemType Directory -Path $smokeDir | Out-Null
    $Path = New-SmokeAuthoringFile -RequiredManifest $RequiredManifest -Directory $smokeDir
    $OutputPath = Join-Path $smokeDir "kb-modules.sql"
}

$validator = Join-Path $scriptDir "validate-kb-authoring.ps1"
& $validator -Path $Path -RequiredManifest $RequiredManifest
if (-not $?) {
    exit 1
}

$source = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
$tenantSlug = [string](Read-Property $source "tenantSlug")
$modules = As-Array (Read-Property $source "modules")

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("-- ClawBot KB seed generated from deploy/seed/kb-authoring.json")
$lines.Add("-- Do not edit generated SQL directly; update the authoring JSON and rerun generate-kb-seed.ps1.")
$lines.Add("SET XACT_ABORT ON;")
$lines.Add("BEGIN TRANSACTION;")
$lines.Add("")
$lines.Add("DECLARE @tenant_slug NVARCHAR(128) = $(Escape-Sql $tenantSlug);")
$lines.Add("DECLARE @tenant_id UNIQUEIDENTIFIER = (SELECT TOP 1 id FROM tenants WHERE slug = @tenant_slug);")
$lines.Add("IF @tenant_id IS NULL THROW 51000, 'Tenant slug not found for KB seed.', 1;")
$lines.Add("")

foreach ($module in $modules) {
    $code = [string](Read-Property $module "code")
    $name = Read-Property $module "name"
    $description = Read-Property $module "description"
    $ownerRole = Read-Property $module "ownerRole"
    $contentMd = Read-Property $module "contentMd"
    $testCases = As-Array (Read-Property $module "testCases")
    $moduleVar = Var-Name $code

    $lines.Add("-- $code")
    $lines.Add("DECLARE $moduleVar UNIQUEIDENTIFIER;")
    $lines.Add("MERGE INTO kb_modules AS target")
    $lines.Add("USING (SELECT @tenant_id AS tenant_id, $(Escape-Sql $code) AS code, $(Escape-Sql $name) AS name, $(Escape-Sql $description) AS description, $(Escape-Sql $ownerRole) AS owner_role) AS source")
    $lines.Add("ON target.tenant_id = source.tenant_id AND target.code = source.code")
    $lines.Add("WHEN MATCHED THEN UPDATE SET name = source.name, description = source.description, owner_role = source.owner_role, status = N'active', updated_at = SYSDATETIMEOFFSET(), deleted_at = NULL")
    $lines.Add("WHEN NOT MATCHED THEN INSERT (tenant_id, code, name, description, owner_role, status) VALUES (source.tenant_id, source.code, source.name, source.description, source.owner_role, N'active');")
    $lines.Add("SET $moduleVar = (SELECT id FROM kb_modules WHERE tenant_id = @tenant_id AND code = $(Escape-Sql $code));")
    $lines.Add("")
    $lines.Add("MERGE INTO kb_versions AS target")
    $lines.Add("USING (SELECT $moduleVar AS kb_module_id, 1 AS version, $(Escape-Sql $contentMd) AS content_md) AS source")
    $lines.Add("ON target.kb_module_id = source.kb_module_id AND target.version = source.version")
    $lines.Add("WHEN MATCHED THEN UPDATE SET content_md = source.content_md, status = N'deployed', deployed_at = COALESCE(target.deployed_at, SYSDATETIMEOFFSET())")
    $lines.Add("WHEN NOT MATCHED THEN INSERT (kb_module_id, version, content_md, status, deployed_at) VALUES (source.kb_module_id, source.version, source.content_md, N'deployed', SYSDATETIMEOFFSET());")
    $lines.Add("")
    $lines.Add("DELETE FROM kb_test_cases WHERE kb_module_id = $moduleVar;")

    foreach ($testCase in $testCases) {
        $question = Read-Property $testCase "question"
        $expectedAnswer = Read-Property $testCase "expectedAnswer"
        $lines.Add("INSERT INTO kb_test_cases (kb_module_id, question, expected_answer, is_active) VALUES ($moduleVar, $(Escape-Sql $question), $(Escape-Sql $expectedAnswer), 1);")
    }

    $lines.Add("")
}

$lines.Add("DECLARE @expected_modules INT = $($modules.Count);")
$lines.Add("DECLARE @seeded_modules INT = (SELECT COUNT(*) FROM kb_modules WHERE tenant_id = @tenant_id AND code IN ($(($modules | ForEach-Object { Escape-Sql (Read-Property $_ "code") }) -join ', ')));")
$lines.Add("IF @seeded_modules <> @expected_modules THROW 51001, 'KB seed module count mismatch.', 1;")
$lines.Add("IF EXISTS (")
$lines.Add("    SELECT 1")
$lines.Add("    FROM kb_modules m")
$lines.Add("    WHERE m.tenant_id = @tenant_id")
$lines.Add("      AND m.code IN ($(($modules | ForEach-Object { Escape-Sql (Read-Property $_ "code") }) -join ', '))")
$lines.Add("      AND (SELECT COUNT(*) FROM kb_test_cases tc WHERE tc.kb_module_id = m.id AND tc.is_active = 1) < 20")
$lines.Add(") THROW 51002, 'KB seed requires at least 20 active test cases per module.', 1;")
$lines.Add("")
$lines.Add("COMMIT TRANSACTION;")

$outputDir = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDir) -and -not (Test-Path -LiteralPath $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

Set-Content -LiteralPath $OutputPath -Value $lines -Encoding UTF8
Write-Host "Generated KB seed SQL: $OutputPath"

if ($SmokeTest) {
    $generated = Get-Content -LiteralPath $OutputPath -Raw
    $expectedInserts = 120
    $actualInserts = [regex]::Matches($generated, "INSERT INTO kb_test_cases").Count
    if ($actualInserts -ne $expectedInserts) {
        throw "SmokeTest expected $expectedInserts kb_test_cases inserts, found $actualInserts."
    }

    foreach ($code in "KB-01", "KB-02", "KB-03", "KB-04", "KB-05", "KB-06") {
        if (-not $generated.Contains($code)) {
            throw "SmokeTest generated SQL is missing module $code."
        }
    }

    Write-Host "KB seed generator SmokeTest passed: $expectedInserts kb_test_cases inserts across 6 modules."
}
