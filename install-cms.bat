@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

:: ============================================
:: NOA CMS 설치 스크립트 (고객 PC용)
:: ============================================
title NOA CMS 설치

echo.
echo ============================================
echo   NOA CMS 설치
echo ============================================
echo.

:: 현재 디렉토리 확인
set "INSTALL_DIR=%~dp0"
cd /d "%INSTALL_DIR%"

:: 1. Docker 확인
echo [1/4] Docker 설치 확인 중...
docker --version >nul 2>&1
if errorlevel 1 (
    echo [오류] Docker Desktop이 설치되어 있지 않습니다.
    echo.
    echo Docker Desktop을 설치해주세요:
    echo https://www.docker.com/products/docker-desktop
    echo.
    pause
    exit /b 1
)
echo [성공] Docker가 설치되어 있습니다.
docker --version

:: Docker 서비스 실행 확인
docker ps >nul 2>&1
if errorlevel 1 (
    echo [경고] Docker 서비스가 실행되지 않았습니다.
    echo Docker Desktop을 실행해주세요.
    echo.
    pause
    exit /b 1
)
echo [성공] Docker 서비스가 실행 중입니다.
echo.

:: 2. 데이터 디렉토리 생성
echo [2/4] 데이터 디렉토리 생성 중...
if not exist "data\db" mkdir "data\db"
if not exist "data\library" mkdir "data\library"
if not exist "data\cache" mkdir "data\cache"
echo [성공] 데이터 디렉토리 생성 완료.
echo.

:: 3. Docker 이미지 확인/로드
echo [3/4] Docker 이미지 확인 중...
if exist "noa-cms-latest.tar" (
    echo Docker 이미지 파일을 발견했습니다. 로드 중...
    docker load -i "noa-cms-latest.tar"
    if errorlevel 1 (
        echo [경고] 이미지 로드 실패. 계속 진행합니다...
    ) else (
        echo [성공] 이미지 로드 완료.
    )
) else (
    echo [정보] 이미지 파일이 없습니다. docker-compose.yml에서 빌드하거나 이미지를 다운로드하세요.
)
echo.

:: 4. 서비스 시작
echo [4/4] 서비스 시작 중...
docker-compose -f docker-compose.prod.yml up -d
if errorlevel 1 (
    echo [오류] 서비스 시작에 실패했습니다.
    pause
    exit /b 1
)

echo.
echo [성공] 서비스가 시작되었습니다!
echo.

:: 서비스 상태 확인
echo 서비스 상태 확인 중...
timeout /t 5 /nobreak >nul
docker-compose -f docker-compose.prod.yml ps
echo.

:: 완료 메시지
echo ============================================
echo   설치 완료!
echo ============================================
echo.
echo CMS 접속 주소:
echo   http://localhost:8000
echo.
echo 기본 로그인 정보:
echo   - 사용자명: xibo_admin
echo   - 비밀번호: password
echo.
echo 서비스 관리:
echo   - 시작: docker-compose -f docker-compose.prod.yml up -d
echo   - 중지: docker-compose -f docker-compose.prod.yml down
echo   - 재시작: docker-compose -f docker-compose.prod.yml restart
echo   - 로그: docker-compose -f docker-compose.prod.yml logs -f
echo.
echo 플레이어 설정:
echo   CMS Address: http://localhost:8000/xmds.php
echo   CMS Key: CMS 관리자 페이지에서 확인
echo.

pause

