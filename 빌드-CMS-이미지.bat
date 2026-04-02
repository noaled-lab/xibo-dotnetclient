@echo off
chcp 65001 >nul
title CMS Docker 이미지 빌드

:: PowerShell 스크립트 실행
set "SCRIPT_DIR=%~dp0"
cd /d "%SCRIPT_DIR%\.."

powershell.exe -ExecutionPolicy Bypass -File "%SCRIPT_DIR%빌드-CMS-이미지.ps1" %*

if errorlevel 1 (
    echo.
    echo [오류] 빌드 중 오류가 발생했습니다.
    pause
    exit /b 1
)

pause

