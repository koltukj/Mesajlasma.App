@echo off
chcp 65001 >nul
set /p ONAY="Tum yerel veriler (veritabani ve yuklenen dosyalar) silinecek. Emin misiniz? (E/H): "
if /i not "%ONAY%"=="E" exit /b
if exist "%~dp0MesajlasmaApp\App_Data" (
    rmdir /s /q "%~dp0MesajlasmaApp\App_Data"
    echo Veriler sifirlandi.
) else (
    echo Silinecek veri bulunamadi.
)
pause

