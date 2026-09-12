@echo off
title Falaq Food Call Center Launcher
color 0A

echo ========================================================
echo    🍽️  FALAQ FOOD CALL CENTER — LOCAL RUNNER
echo ========================================================
echo.

echo [1/3] Ensuring LocalDB instance is started...
sqllocaldb start MSSQLLocalDB >nul 2>&1

echo [2/3] Launching Backend API (.NET 8) in a new window...
start "Falaq Food API (Backend)" cmd /k "cd /d %~dp0backend\src\CallCenter.Api && dotnet run"

echo [3/3] Launching Frontend (Angular) in a new window...
start "Falaq Food Web (Frontend)" cmd /k "cd /d %~dp0frontend && npm start"

echo.
echo ========================================================
echo  🚀 Applications are starting up!
echo ========================================================
echo  Backend API:    http://localhost:5289
echo  Swagger UI:     http://localhost:5289/swagger
echo  Frontend UI:    http://localhost:4200
echo.
echo  Default Login:
echo    Username:     admin
echo    Password:     ChangeMe123!
echo ========================================================
echo.
echo Leave this window or close it when done.
pause
