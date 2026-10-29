# tests/run-all-tests.ps1
# Script tự động kiểm tra toàn bộ hệ thống sau khi build/thay đổi code.
param(
    [switch]$IncludeHttp
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Get-Item $PSScriptRoot).Parent.FullName

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [TEST SUITE] 1. Kiem tra Build Web (Bms.Web)..." -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
dotnet build "$repoRoot/src/Bms.Web/Bms.Web.csproj" -c Debug
if ($LASTEXITCODE -ne 0) { throw "Build Bms.Web that bai!" }

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [TEST SUITE] 2. Kiem tra Build Smoke Test Suite..." -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
dotnet build "$repoRoot/tests/Bms.Smoke/Bms.Smoke.csproj" -c Debug
if ($LASTEXITCODE -ne 0) { throw "Build Bms.Smoke that bai!" }

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [TEST SUITE] 3. Chay Integration & Controller Smoke Tests..." -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
dotnet run --project "$repoRoot/tests/Bms.Smoke/Bms.Smoke.csproj"
if ($LASTEXITCODE -ne 0) { throw "Smoke tests that bai!" }

if ($IncludeHttp) {
    Write-Host "`n==========================================================" -ForegroundColor Cyan
    Write-Host " [TEST SUITE] 4. Khoi dong Web va kiem tra HTTP Endpoints..." -ForegroundColor Cyan
    Write-Host "==========================================================" -ForegroundColor Cyan
    $webProc = Start-Process -FilePath "dotnet" -ArgumentList "run --project `"$repoRoot/src/Bms.Web/Bms.Web.csproj`" --urls http://localhost:5000" -PassThru
    try {
        Start-Sleep -Seconds 7
        & powershell -File "$PSScriptRoot/http-smoke.ps1" -BaseUrl "http://localhost:5000" -SqlServer "localhost\SQLEXPRESS"
        if ($LASTEXITCODE -ne 0) { throw "HTTP Smoke tests that bai!" }
    }
    finally {
        Stop-Process -Id $webProc.Id -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " [TEST SUITE] TOAN BO KIEM TRA DA THANH CONG! " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
