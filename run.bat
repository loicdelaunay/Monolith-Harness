@echo off
cd /d "%~dp0"
dotnet run --project src\MonolithHarness.App -f net10.0-desktop -c Release
pause
