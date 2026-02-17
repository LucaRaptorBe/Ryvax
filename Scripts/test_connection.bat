@echo off
REM test_connection.bat - Test la connexion au serveur Ryvax (Windows)

setlocal

set SERVER_IP=%1
set SERVER_PORT=%2

if "%SERVER_IP%"=="" (
    echo Usage: test_connection.bat SERVER_IP [SERVER_PORT]
    echo Example: test_connection.bat 192.168.1.100 7777
    exit /b 1
)

if "%SERVER_PORT%"=="" set SERVER_PORT=7777

echo Testing connection to %SERVER_IP%:%SERVER_PORT%...
echo.

REM Test 1: Ping
echo 1. Ping test:
ping -n 1 -w 2000 %SERVER_IP% >nul 2>nul
if %ERRORLEVEL% EQU 0 (
    echo    [OK] Success - Server is reachable
) else (
    echo    [FAIL] Server unreachable
)

REM Test 2: Port (nécessite telnet ou Test-NetConnection sur PowerShell)
echo 2. Port test:
powershell -Command "Test-NetConnection -ComputerName %SERVER_IP% -Port %SERVER_PORT% -InformationLevel Quiet" >nul 2>nul
if %ERRORLEVEL% EQU 0 (
    echo    [OK] Port %SERVER_PORT% is open
) else (
    echo    [WARN] Cannot verify port - PowerShell required
)

echo.
echo Connection test complete.
echo If all tests pass, try connecting from your Unity client with:
echo   Server Address: %SERVER_IP%
echo   Server Port: %SERVER_PORT%

endlocal
