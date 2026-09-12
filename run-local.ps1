# Falaq Food Call Center — PowerShell Launcher
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "   🍽️  FALAQ FOOD CALL CENTER — LOCAL RUNNER" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "[1/3] Ensuring LocalDB instance is started..." -ForegroundColor Yellow
try {
    sqllocaldb start MSSQLLocalDB | Out-Null
} catch {
    Write-Warning "Could not start MSSQLLocalDB automatically. Ensure SQL Server/LocalDB is running."
}

Write-Host "[2/3] Launching Backend API (.NET 8) in a new window..." -ForegroundColor Yellow
$backendPath = Join-Path $PSScriptRoot "backend\src\CallCenter.Api"
Start-Process powershell -ArgumentList "-NoExit", "-Command", "Set-Location '$backendPath'; Write-Host 'Starting Backend API on http://localhost:5289...' -ForegroundColor Green; dotnet run"

Write-Host "[3/3] Launching Frontend (Angular) in a new window..." -ForegroundColor Yellow
$frontendPath = Join-Path $PSScriptRoot "frontend"
Start-Process powershell -ArgumentList "-NoExit", "-Command", "Set-Location '$frontendPath'; Write-Host 'Starting Angular Frontend on http://localhost:4200...' -ForegroundColor Green; npm start"

Write-Host ""
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host " 🚀 Applications are starting up in separate windows!" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host " Backend API:    http://localhost:5289" -ForegroundColor White
Write-Host " Swagger UI:     http://localhost:5289/swagger" -ForegroundColor White
Write-Host " Frontend UI:    http://localhost:4200" -ForegroundColor White
Write-Host ""
Write-Host " Default Login Credentials:" -ForegroundColor Yellow
Write-Host "   Username:     admin" -ForegroundColor White
Write-Host "   Password:     ChangeMe123!" -ForegroundColor White
Write-Host "========================================================" -ForegroundColor Cyan
