@echo off
title NexusWpp Controller
chcp 65001 > nul
cls

set SCRIPT_DIR=%~dp0
set APP_PATH=C:\nexuswpp\nexuswpp.exe

:menu
echo ====================================================================
echo NexusWpp - Native Dynamic Desktop Wallpaper
echo ====================================================================
echo.
echo [1] Demarrer le fond d'ecran maintenant
echo [2] Compiler l'hote natif
echo [3] Deployer et configurer le demarrage Windows
echo [4] Arreter le fond d'ecran
echo [5] Quitter
echo.
set /p USER_CHOICE="Choisissez une option (1-5) : "

if "%USER_CHOICE%"=="1" goto start_native
if "%USER_CHOICE%"=="2" goto compile_native
if "%USER_CHOICE%"=="3" goto deploy_native
if "%USER_CHOICE%"=="4" goto stop_native
if "%USER_CHOICE%"=="5" goto end
goto menu

:start_native
echo.
if exist "%APP_PATH%" (
    start "" "%APP_PATH%"
) else (
    start "" "%SCRIPT_DIR%bin\nexuswpp.exe" "%SCRIPT_DIR%index.html"
)
echo [OK] Lancement demande.
echo.
pause
goto menu

:compile_native
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%compile.ps1"
echo.
pause
goto menu

:deploy_native
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%deploy_local.ps1"
echo.
pause
goto menu

:stop_native
echo.
taskkill /f /im nexuswpp.exe > nul 2>&1
echo [OK] Arret demande.
echo.
pause
goto menu

:end
exit
