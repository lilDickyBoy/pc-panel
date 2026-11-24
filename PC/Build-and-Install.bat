@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

rem ---- ask for admin once (needed to replace the running app and for the sensor driver)
net session >nul 2>&1
if errorlevel 1 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

set "LOG=%~dp0build-log.txt"
set "DEST=%LOCALAPPDATA%\Programs\PC Panel"
echo PC Panel build %date% %time% > "%LOG%"

echo.
echo   ===============================
echo          PC Panel installer
echo   ===============================
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo   .NET 8 SDK was not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0
    echo ERROR: dotnet not found >> "%LOG%"
    pause
    exit /b 1
)

echo   [1/4] Closing running versions...
taskkill /F /IM PCPanel.exe >nul 2>&1
taskkill /F /IM MobiLink.exe >nul 2>&1
schtasks /Delete /TN "MobiLink" /F >nul 2>&1

echo   [2/4] Sensor driver (PawnIO)...
if not exist "%ProgramFiles%\PawnIO" (
    winget install --id namazso.PawnIO -e --accept-source-agreements --accept-package-agreements >> "%LOG%" 2>&1
)

echo   [3/4] Building (first time takes a minute)...
dotnet publish "PcPanel\PcPanel.csproj" -c Release -r win-x64 --self-contained false -o "%DEST%" >> "%LOG%" 2>&1
if errorlevel 1 (
    echo.
    echo   BUILD FAILED - details are in build-log.txt
    echo BUILD FAILED >> "%LOG%"
    pause
    exit /b 1
)
echo BUILD OK >> "%LOG%"

echo   [4/4] Removing the old MobiLink version...
if exist "%LOCALAPPDATA%\Programs\MobiLink" rmdir /s /q "%LOCALAPPDATA%\Programs\MobiLink" >nul 2>&1

start "" "%DEST%\PCPanel.exe"
echo.
echo   Done! PC Panel is running (look at the system tray).
timeout /t 5 >nul
