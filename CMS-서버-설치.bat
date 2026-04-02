@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

:: NOA CMS 서버 설치 프로그램 (배치 파일 래퍼)
title NOA CMS 서버 설치

:: PowerShell 스크립트 실행
set "SCRIPT_DIR=%~dp0"
powershell.exe -ExecutionPolicy Bypass -File "%SCRIPT_DIR%CMS-서버-설치-프로그램.ps1" %*

if errorlevel 1 (
    echo.
    echo [오류] 설치 중 오류가 발생했습니다.
    pause
    exit /b 1
)

pause

