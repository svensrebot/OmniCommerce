<#
.SYNOPSIS
    Full verification: Docker Compose startup + ALL tests against the Docker SQL Server.

.DESCRIPTION
    1. docker compose up --build -d --wait   (API + SQL Server, migrations and sample data applied by the API)
    2. One real HTTP request to the running API container (POST /api/carts must return 201 with an ETag).
    3. The complete test suite against the Docker SQL Server with OMNICOMMERCE_REQUIRE_SQLSERVER_TESTS=1,
       so unconfigured/unreachable SQL tests FAIL instead of skipping.
    4. Parses the TRX result and fails unless every test passed, none was skipped or not executed, and
       both SQL Server-backed test classes were discovered and ran.
    Tests create and drop their own throwaway databases (OmniCommerce_Test_<guid>); the compose
    database "OmniCommerce" is never touched by them.

    Exit code 0 means fully verified. Any other exit code means NOT verified.
    Local development only. If your execution policy blocks local scripts, run:
        powershell -ExecutionPolicy Bypass -File scripts\verify-docker.ps1

.PARAMETER Down
    Stop and remove the containers afterwards (the SQL Server data volume is kept).

.PARAMETER ResultsDirectory
    Keep the TRX test results in this directory (used by CI to upload them). By default they go to a
    temporary directory that is deleted when the script ends.

    Works on Windows PowerShell 5.1 and PowerShell 7 (Linux/macOS); CI runs it with pwsh.
#>
[CmdletBinding()]
param([switch]$Down, [string]$ResultsDirectory)

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

function Fail([string]$message) {
    Write-Host "VERIFICATION FAILED: $message" -ForegroundColor Red
    exit 1
}

$password = if ($env:MSSQL_SA_PASSWORD) { $env:MSSQL_SA_PASSWORD } else { 'Dev_Only_Passw0rd!' }
$keepResults = [bool]$ResultsDirectory
$resultsDir = if ($keepResults) { [IO.Path]::GetFullPath($ResultsDirectory) }
              else { Join-Path ([IO.Path]::GetTempPath()) ("omnicommerce-verify-" + [guid]::NewGuid().ToString('N')) }

try {
    Write-Host '== 1/4 docker compose up --build -d --wait' -ForegroundColor Cyan
    docker compose up --build -d --wait
    if ($LASTEXITCODE -ne 0) { Fail "docker compose up exited with code $LASTEXITCODE" }

    Write-Host '== 2/4 HTTP smoke request against the API container' -ForegroundColor Cyan
    $created = $null
    for ($attempt = 1; $attempt -le 30 -and -not $created; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri 'http://127.0.0.1:8080/api/carts' -Method POST -UseBasicParsing
            if ([int]$response.StatusCode -eq 201 -and $response.Headers['ETag']) { $created = $response }
        } catch {
            Start-Sleep -Seconds 2
        }
    }
    if (-not $created) { Fail 'POST http://127.0.0.1:8080/api/carts did not return 201 with an ETag.' }
    Write-Host ("   201 Created, ETag {0}" -f ($created.Headers['ETag'] -join ','))

    Write-Host '== 3/4 dotnet test (all tests, SQL Server required)' -ForegroundColor Cyan
    $env:OMNICOMMERCE_TEST_SQLSERVER = "Server=127.0.0.1,1433;User Id=sa;Password=$password;TrustServerCertificate=True"
    $env:OMNICOMMERCE_REQUIRE_SQLSERVER_TESTS = '1'
    dotnet test OmniCommerce.sln --nologo --results-directory $resultsDir --logger 'trx;LogFileName=verify.trx'
    $testExit = $LASTEXITCODE

    Write-Host '== 4/4 checking results' -ForegroundColor Cyan
    $trx = Join-Path $resultsDir 'verify.trx'
    if (-not (Test-Path $trx)) { Fail 'No TRX result file was produced (tests were not run).' }

    $results = @(([xml](Get-Content -Raw $trx)).TestRun.Results.UnitTestResult)
    $passed = @($results | Where-Object { $_.outcome -eq 'Passed' }).Count
    $failed = @($results | Where-Object { $_.outcome -eq 'Failed' }).Count
    $skipped = $results.Count - $passed - $failed
    $api = @($results | Where-Object { $_.outcome -eq 'Passed' -and $_.testName -match '\.CartApiTests\.' }).Count
    $concurrency = @($results | Where-Object { $_.outcome -eq 'Passed' -and $_.testName -match '\.CartConcurrencyTests\.' }).Count

    Write-Host ("   total={0} passed={1} failed={2} skipped/not-executed={3}" -f $results.Count, $passed, $failed, $skipped)
    Write-Host ("   SQL Server-backed passed: CartApiTests={0} CartConcurrencyTests={1}" -f $api, $concurrency)

    if ($testExit -ne 0) { Fail "dotnet test exited with code $testExit" }
    if ($results.Count -eq 0) { Fail 'No tests were discovered.' }
    if ($failed -gt 0) { Fail "$failed test(s) failed." }
    if ($skipped -gt 0) { Fail "$skipped test(s) were skipped or not executed; database-backed verification is incomplete." }
    if ($api -eq 0 -or $concurrency -eq 0) { Fail 'SQL Server-backed test classes were not discovered or did not run.' }

    Write-Host 'VERIFICATION PASSED: all tests ran against the Docker SQL Server; none skipped.' -ForegroundColor Green
}
finally {
    if (-not $keepResults -and (Test-Path $resultsDir)) { Remove-Item $resultsDir -Recurse -Force }
    if ($Down) { docker compose down }
}
