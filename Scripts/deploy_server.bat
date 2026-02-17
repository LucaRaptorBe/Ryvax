@echo off
REM deploy_server.bat - Script de déploiement Windows pour serveur Ryvax
REM Nécessite: WSL (Windows Subsystem for Linux) ou Git Bash

setlocal enabledelayedexpansion

REM Configuration
set SERVER_IP=%1
set SERVER_USER=%2
set BUILD_PATH=%3
set SERVER_PORT=%4

if "%SERVER_IP%"=="" (
    echo [ERROR] Usage: deploy_server.bat SERVER_IP [SERVER_USER] [BUILD_PATH] [SERVER_PORT]
    echo Example: deploy_server.bat 192.168.1.100 ryvaxserver Build\LinuxServer 7777
    exit /b 1
)

if "%SERVER_USER%"=="" set SERVER_USER=ryvaxserver
if "%BUILD_PATH%"=="" set BUILD_PATH=Build\LinuxServer
if "%SERVER_PORT%"=="" set SERVER_PORT=7777

echo [INFO] Deploiement du serveur Ryvax
echo [INFO] Serveur: %SERVER_USER%@%SERVER_IP%
echo [INFO] Build: %BUILD_PATH%
echo [INFO] Port: %SERVER_PORT%
echo.

REM Vérifier que le build existe
if not exist "%BUILD_PATH%" (
    echo [ERROR] Le dossier de build n'existe pas: %BUILD_PATH%
    echo [ERROR] Creez d'abord un build Linux depuis Unity
    exit /b 1
)

REM Vérifier si WSL ou Git Bash est disponible
where bash >nul 2>nul
if %ERRORLEVEL% EQU 0 (
    echo [INFO] Git Bash detecte, utilisation du script bash...
    bash Scripts/deploy_server.sh %SERVER_IP% %SERVER_USER% %BUILD_PATH% %SERVER_PORT%
) else (
    where wsl >nul 2>nul
    if %ERRORLEVEL% EQU 0 (
        echo [INFO] WSL detecte, utilisation du script bash...
        wsl bash Scripts/deploy_server.sh %SERVER_IP% %SERVER_USER% %BUILD_PATH% %SERVER_PORT%
    ) else (
        echo [ERROR] Git Bash ou WSL requis pour le deploiement
        echo [ERROR] Installez Git for Windows ou WSL:
        echo   - Git for Windows: https://git-scm.com/download/win
        echo   - WSL: wsl --install
        exit /b 1
    )
)

endlocal
