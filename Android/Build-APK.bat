@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
set "LOG=%~dp0apk-build-log.txt"
echo PC Panel APK build %date% %time% > "%LOG%"

echo.
echo   ===============================
echo        PC Panel - Android APK
echo   ===============================
echo.

rem ---- Java 17/21 (Gradle 8.11 doesn't run on newer Java). Android Studio keeps downloaded JDKs in %USERPROFILE%\.jdks
set "JAVA_HOME="
for /d %%J in ("%USERPROFILE%\.jdks\*21*" "%USERPROFILE%\.jdks\*17*") do (
    if exist "%%~J\bin\java.exe" set "JAVA_HOME=%%~J"
)
if not defined JAVA_HOME (
    for %%P in ("D:\Program Files\Android\AndroidStudio" "D:\Program Files\Android\Android Studio" "C:\Program Files\Android\Android Studio" "C:\Program Files\Android\AndroidStudio" "%LOCALAPPDATA%\Programs\Android Studio") do (
        if exist "%%~P\jbr\bin\java.exe" set "JAVA_HOME=%%~P\jbr"
    )
)
if not defined JAVA_HOME (
    echo   Android Studio's Java was not found. Open the Android folder in Android Studio instead.
    echo ERROR: JAVA_HOME not found >> "%LOG%"
    pause
    exit /b 1
)
echo JAVA_HOME=%JAVA_HOME% >> "%LOG%"

rem ---- Android SDK location
if not exist local.properties (
    set "SDK=%LOCALAPPDATA%\Android\Sdk"
    call echo sdk.dir=%%SDK:\=/%%> local.properties
)

echo   Building... (the first build downloads Gradle and takes a few minutes)
call gradlew.bat assembleRelease --console=plain >> "%LOG%" 2>&1
if errorlevel 1 (
    echo.
    echo   BUILD FAILED - details are in apk-build-log.txt
    echo BUILD FAILED >> "%LOG%"
    pause
    exit /b 1
)
copy /y "app\build\outputs\apk\release\app-release.apk" "%~dp0..\PC-Panel.apk" >nul
echo BUILD OK >> "%LOG%"
echo.
echo   Done! The APK is next to this folder: PC-Panel.apk
timeout /t 6 >nul
