@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

:: NOA Player 설정 자동화 스크립트 (배치 버전)
:: 플레이어 설치 후 CMS 주소 및 기타 설정을 자동으로 구성합니다.

echo.
echo ============================================
echo   NOA Player 설정 자동화
echo ============================================
echo.

:: PowerShell 스크립트 실행
set "SCRIPT_DIR=%~dp0"
powershell.exe -ExecutionPolicy Bypass -File "%SCRIPT_DIR%configure-player-settings.ps1"

if errorlevel 1 (
    echo.
    echo [오류] 설정 중 오류가 발생했습니다.
    pause
    exit /b 1
)

pause

