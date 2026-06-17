param(
    [switch]$RunIntegrationTests,
    [string]$Project = "tests/Clawbot.Integration.Tests/Clawbot.Integration.Tests.csproj",
    [string]$Configuration = "Release",
    [string]$ResultsDirectory = "./TestResults"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Fail {
    param([Parameter(Mandatory = $true)][string]$Message)

    Write-Error $Message
    exit 1
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Fail "Docker CLI not found. Install Docker Desktop or Docker Engine before running Testcontainers-backed integration tests."
}

Write-Host "Checking Docker client/server..."
& docker version
if ($LASTEXITCODE -ne 0) {
    Fail "Docker CLI is installed, but Docker server is not reachable. Start Docker before running Testcontainers-backed integration tests."
}

Write-Host "Checking Docker daemon details..."
& docker info
if ($LASTEXITCODE -ne 0) {
    Fail "Docker daemon is not ready for Testcontainers."
}

Write-Host "Docker/Testcontainers preflight passed."

if ($RunIntegrationTests) {
    Write-Host "Running integration tests: $Project"
    & dotnet test $Project `
        --no-build `
        --configuration $Configuration `
        --logger "trx;LogFileName=integration-results.trx" `
        --collect:"XPlat Code Coverage" `
        --results-directory $ResultsDirectory
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
