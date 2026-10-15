@echo off
chcp 65001 >nul
cd /d "%~dp0"
dotnet run --project KrsImporter.csproj -- --probe 2026-01-02
pause
