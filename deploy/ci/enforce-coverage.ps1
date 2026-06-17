param(
    [string]$CoverageRoot = "TestResults",
    [double]$MinimumLineCoverage = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Convert-ToDoubleInvariant {
    param([Parameter(Mandatory = $true)][object]$Value)

    return [double]::Parse(
        [string]$Value,
        [System.Globalization.NumberStyles]::Float,
        [System.Globalization.CultureInfo]::InvariantCulture)
}

if (-not (Test-Path -LiteralPath $CoverageRoot)) {
    Write-Error "Coverage root '$CoverageRoot' does not exist."
    exit 1
}

$reports = Get-ChildItem -LiteralPath $CoverageRoot -Recurse -Filter "coverage.cobertura.xml" -File
if (-not $reports) {
    Write-Error "No coverage.cobertura.xml files found under '$CoverageRoot'."
    exit 1
}

[hashtable]$lineHitsByKey = @{}
[double]$fallbackCovered = 0
[double]$fallbackValid = 0

foreach ($report in $reports) {
    [xml]$coverage = Get-Content -LiteralPath $report.FullName
    $coverageNode = $coverage.coverage

    $classNodes = $coverage.SelectNodes("//class[@filename]")
    [int]$reportLines = 0

    foreach ($classNode in $classNodes) {
        $filename = ([string]$classNode.filename).Replace("\", "/")
        if ([string]::IsNullOrWhiteSpace($filename)) {
            continue
        }

        $lineNodes = $classNode.SelectNodes(".//line[@number and @hits]")
        foreach ($lineNode in $lineNodes) {
            $number = [string]$lineNode.number
            if ([string]::IsNullOrWhiteSpace($number)) {
                continue
            }

            $key = "{0}:{1}" -f $filename, $number
            $hits = Convert-ToDoubleInvariant $lineNode.hits

            if (-not $lineHitsByKey.ContainsKey($key)) {
                $lineHitsByKey[$key] = $false
            }

            if ($hits -gt 0) {
                $lineHitsByKey[$key] = $true
            }

            $reportLines += 1
        }
    }

    if ($reportLines -gt 0) {
        continue
    }

    if ($coverageNode -and $coverageNode."lines-covered" -and $coverageNode."lines-valid") {
        $fallbackCovered += Convert-ToDoubleInvariant $coverageNode."lines-covered"
        $fallbackValid += Convert-ToDoubleInvariant $coverageNode."lines-valid"
        continue
    }

    if ($coverageNode -and $coverageNode."line-rate") {
        $fallbackValid += 1
        $fallbackCovered += Convert-ToDoubleInvariant $coverageNode."line-rate"
        continue
    }

    Write-Error "Coverage report '$($report.FullName)' does not contain line coverage attributes."
    exit 1
}

[double]$covered = $fallbackCovered
[double]$valid = $fallbackValid + $lineHitsByKey.Count

foreach ($line in $lineHitsByKey.GetEnumerator()) {
    if ($line.Value) {
        $covered += 1
    }
}

if ($valid -le 0) {
    Write-Error "Coverage reports do not contain any valid lines."
    exit 1
}

$lineCoverage = ($covered / $valid) * 100
Write-Host ("Line coverage: {0:N2}% ({1:N0}/{2:N0} lines)" -f $lineCoverage, $covered, $valid)

if ($lineCoverage -lt $MinimumLineCoverage) {
    Write-Error ("Line coverage {0:N2}% is below required {1:N2}%." -f $lineCoverage, $MinimumLineCoverage)
    exit 1
}

Write-Host ("Coverage gate passed: {0:N2}% >= {1:N2}%." -f $lineCoverage, $MinimumLineCoverage)
