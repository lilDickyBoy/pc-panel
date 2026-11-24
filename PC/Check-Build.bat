@echo off
cd /d "%~dp0"
echo Checking PC Panel build...
dotnet build "PcPanel\PcPanel.csproj" -c Release -o "%~dp0_check" > "%~dp0check-log.txt" 2>&1
echo EXIT %errorlevel% >> "%~dp0check-log.txt"
