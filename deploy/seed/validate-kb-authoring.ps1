param(
    [string]$Path = "deploy/seed/kb-authoring.json",
    [string]$RequiredManifest = "deploy/seed/kb-authoring.required.json"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Read-JsonFile {
    param([Parameter(Mandatory = $true)][string]$FilePath)

    if (-not (Test-Path -LiteralPath $FilePath -PathType Leaf)) {
        throw "File not found: $FilePath"
    }

    return Get-Content -LiteralPath $FilePath -Raw | ConvertFrom-Json
}

function Is-Blank {
    param([object]$Value)

    return $null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)
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

function Has-Placeholder {
    param([object]$Value)

    if ($null -eq $Value) {
        return $false
    }

    $text = [string]$Value
    return $text -match "<[^>]+>|TODO|TBD|PLACEHOLDER|fill approved|paste approved"
}

function Validate-RequiredText {
    param(
        [System.Collections.Generic.List[string]]$Errors,
        [Parameter(Mandatory = $true)][string]$Scope,
        [Parameter(Mandatory = $true)][string]$Field,
        [object]$Value
    )

    if (Is-Blank $Value) {
        $Errors.Add("$Scope field '$Field' is required.")
        return
    }

    if (Has-Placeholder $Value) {
        $Errors.Add("$Scope field '$Field' cannot be a placeholder.")
    }
}

$errors = New-Object System.Collections.Generic.List[string]

try {
    $required = Read-JsonFile $RequiredManifest
    $source = Read-JsonFile $Path
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}

$minTestCasesPerModule = [int](Read-Property $required "minTestCasesPerModule")
if ($minTestCasesPerModule -lt 20) {
    $errors.Add("minTestCasesPerModule must be at least 20.")
}

$tenantSlug = Read-Property $source "tenantSlug"
Validate-RequiredText $errors "Root" "tenantSlug" $tenantSlug

$modulesValue = Read-Property $source "modules"
$modules = As-Array $modulesValue
$requiredModulesValue = Read-Property $required "requiredModules"
$requiredModules = As-Array $requiredModulesValue
if ($modules.Count -ne $requiredModules.Count) {
    $errors.Add("Expected $($requiredModules.Count) KB modules, found $($modules.Count).")
}

foreach ($requiredModule in $requiredModules) {
    $code = [string](Read-Property $requiredModule "code")
    $module = $modules | Where-Object { (Read-Property $_ "code") -eq $code } | Select-Object -First 1
    if ($null -eq $module) {
        $errors.Add("Missing required module $code.")
        continue
    }

    foreach ($field in @("name", "description", "ownerRole", "contentMd")) {
        $value = Read-Property $module $field
        Validate-RequiredText $errors "Module $code" $field $value
    }

    $testCasesValue = Read-Property $module "testCases"
    $testCases = As-Array $testCasesValue
    if ($testCases.Count -lt $minTestCasesPerModule) {
        $errors.Add("Module $code must contain at least $minTestCasesPerModule testCases; found $($testCases.Count).")
    }

    for ($i = 0; $i -lt $testCases.Count; $i++) {
        $caseNumber = $i + 1
        $testCase = $testCases[$i]
        foreach ($field in @("question", "expectedAnswer")) {
            $value = Read-Property $testCase $field
            Validate-RequiredText $errors "Module $code test case $caseNumber" $field $value
        }
    }
}

if ($errors.Count -gt 0) {
    Write-Error ("KB authoring validation failed:`n - " + ($errors -join "`n - "))
    exit 1
}

Write-Host "KB authoring validation passed: $($modules.Count) modules, >=$minTestCasesPerModule test cases per module."
