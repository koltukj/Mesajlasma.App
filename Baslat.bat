@echo off
chcp 65001 >nul
cd /d "%~dp0MesajlasmaApp"
echo Uygulama baslatiliyor: http://localhost:5144
start "" "http://localhost:5144"
dotnet run --urls "http://localhost:5144"
pause

