@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo Docker ile uygulama derleniyor ve baslatiliyor...
docker compose up --build -d
if %ERRORLEVEL% EQU 0 (
    echo Uygulama hazir: http://localhost:5144
    start "" "http://localhost:5144"
) else (
    echo Docker baslatilamadi. Docker Desktop acik mi kontrol edin.
)
pause

