$ErrorActionPreference = "Stop"
$testProjects = Get-ChildItem "D:\LockscreenGif-master\Tests" -Recurse -Filter *.csproj

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " RUNNING ALL 11 REGRESSION TEST SUITES                    " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$passed = 0
$total = $testProjects.Count

foreach ($proj in $testProjects) {
    Write-Host "`n---> Running: $($proj.Name)" -ForegroundColor Yellow
    dotnet run --project $proj.FullName -c Release
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: $($proj.Name) exited with code $LASTEXITCODE" -ForegroundColor Red
        exit 1
    }
    $passed++
    Write-Host "PASS: $($proj.Name)" -ForegroundColor Green
}

Write-Host "`n---> Running: XAML Resource Regression Guard (scripts/test-xaml-resources.ps1)" -ForegroundColor Yellow
& powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "test-xaml-resources.ps1")
if ($LASTEXITCODE -ne 0) {
    Write-Host "FAILED: XAML resource regression guard failed!" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: XAML Resource Regression Guard" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ALL $passed / $total REGRESSION TEST SUITES PASSED 100%! " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
