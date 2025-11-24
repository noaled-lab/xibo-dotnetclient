# NOA CMS 서버 설치 프로그램
# Docker 이미지로 빌드한 CMS를 서버 PC에 설치합니다.

param(
    [string]$ImageFile = "",
    [switch]$SkipImageLoad = $false,
    [switch]$Silent = $false
)

# UI 설정
$Host.UI.RawUI.WindowTitle = "NOA CMS 서버 설치 프로그램"

function Write-ColorOutput {
    param(
        [string]$Message,
        [string]$Color = "White"
    )
    Write-Host $Message -ForegroundColor $Color
}

function Show-Header {
    Clear-Host
    Write-ColorOutput "============================================" "Cyan"
    Write-ColorOutput "   NOA CMS 서버 설치 프로그램" "Cyan"
    Write-ColorOutput "============================================" "Cyan"
    Write-Host ""
}

function Test-DockerInstalled {
    Write-ColorOutput "[1/7] Docker 설치 확인 중..." "Yellow"
    
    try {
        $dockerVersion = docker --version 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-ColorOutput "[성공] Docker가 설치되어 있습니다: $dockerVersion" "Green"
            return $true
        }
    }
    catch {
        Write-ColorOutput "[오류] Docker가 설치되어 있지 않습니다." "Red"
        return $false
    }
    
    return $false
}

function Test-DockerRunning {
    Write-ColorOutput "[2/7] Docker 서비스 확인 중..." "Yellow"
    
    try {
        docker ps | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-ColorOutput "[성공] Docker 서비스가 실행 중입니다." "Green"
            return $true
        }
    }
    catch {
        Write-ColorOutput "[경고] Docker 서비스가 실행되지 않았습니다." "Yellow"
        Write-ColorOutput "Docker Desktop을 실행해주세요." "Yellow"
        return $false
    }
    
    return $false
}

function Load-DockerImage {
    param([string]$ImagePath)
    
    Write-ColorOutput "[3/7] Docker 이미지 로드 중..." "Yellow"
    
    if (-not (Test-Path $ImagePath)) {
        Write-ColorOutput "[오류] 이미지 파일을 찾을 수 없습니다: $ImagePath" "Red"
        return $false
    }
    
    Write-ColorOutput "이미지 파일: $ImagePath" "Gray"
    Write-ColorOutput "이 작업은 시간이 걸릴 수 있습니다..." "Gray"
    
    try {
        docker load -i $ImagePath
        if ($LASTEXITCODE -eq 0) {
            Write-ColorOutput "[성공] Docker 이미지 로드 완료" "Green"
            return $true
        }
        else {
            Write-ColorOutput "[오류] 이미지 로드 실패" "Red"
            return $false
        }
    }
    catch {
        Write-ColorOutput "[오류] 이미지 로드 중 오류 발생: $_" "Red"
        return $false
    }
}

function Create-DataDirectories {
    Write-ColorOutput "[4/7] 데이터 디렉토리 생성 중..." "Yellow"
    
    $directories = @(
        "data\db",
        "data\library",
        "data\cache"
    )
    
    foreach ($dir in $directories) {
        if (-not (Test-Path $dir)) {
            try {
                New-Item -ItemType Directory -Path $dir -Force | Out-Null
                Write-ColorOutput "  생성: $dir" "Gray"
            }
            catch {
                Write-ColorOutput "[오류] 디렉토리 생성 실패: $dir" "Red"
                return $false
            }
        }
        else {
            Write-ColorOutput "  존재: $dir" "Gray"
        }
    }
    
    Write-ColorOutput "[성공] 데이터 디렉토리 준비 완료" "Green"
    return $true
}

function Create-DockerComposeFile {
    Write-ColorOutput "[5/7] Docker Compose 설정 파일 생성 중..." "Yellow"
    
    $composeContent = @"
version: "3"

services:
  db:
    image: mysql:8.0
    volumes:
      - ./data/db:/var/lib/mysql
    environment:
      MYSQL_ROOT_PASSWORD: "root"
      MYSQL_DATABASE: "cms"
    restart: unless-stopped
    networks:
      - cms-network

  xmr:
    image: ghcr.io/xibosignage/xibo-xmr:develop
    ports:
      - "9506:9505"
    restart: unless-stopped
    networks:
      - cms-network

  web:
    image: noa-cms:latest
    ports:
      - "8000:80"
    volumes:
      - ./data/library:/var/www/cms/library
      - ./data/cache:/var/www/cms/cache
    environment:
      CMS_DEV_MODE: "false"
      INSTALL_TYPE: "docker"
      MYSQL_HOST: "db"
      MYSQL_DATABASE: "cms"
      MYSQL_USER: "root"
      MYSQL_PASSWORD: "root"
      MYSQL_PORT: "3306"
      XMR_HOST: "xmr"
      CMS_SERVER_NAME: "localhost"
    depends_on:
      - db
      - xmr
    restart: unless-stopped
    networks:
      - cms-network

networks:
  cms-network:
    driver: bridge
"@
    
    try {
        $composeContent | Out-File -FilePath "docker-compose.yml" -Encoding UTF8 -NoNewline
        Write-ColorOutput "[성공] docker-compose.yml 파일 생성 완료" "Green"
        return $true
    }
    catch {
        Write-ColorOutput "[오류] 파일 생성 실패: $_" "Red"
        return $false
    }
}

function Start-CMSServices {
    Write-ColorOutput "[6/7] CMS 서비스 시작 중..." "Yellow"
    
    try {
        # 기존 서비스 중지
        docker-compose down 2>&1 | Out-Null
        
        # 서비스 시작
        docker-compose up -d
        if ($LASTEXITCODE -eq 0) {
            Write-ColorOutput "[성공] CMS 서비스가 시작되었습니다" "Green"
            
            # 서비스 상태 확인 대기
            Write-ColorOutput "서비스 초기화 대기 중..." "Gray"
            Start-Sleep -Seconds 5
            
            return $true
        }
        else {
            Write-ColorOutput "[오류] 서비스 시작 실패" "Red"
            return $false
        }
    }
    catch {
        Write-ColorOutput "[오류] 서비스 시작 중 오류 발생: $_" "Red"
        return $false
    }
}

function Get-ServerIP {
    Write-ColorOutput "[7/7] 서버 IP 주소 확인 중..." "Yellow"
    
    try {
        $ipConfig = ipconfig
        $ipv4Address = $ipConfig | Select-String -Pattern "IPv4.*:\s*(\d+\.\d+\.\d+\.\d+)" | 
                      ForEach-Object { $_.Matches.Groups[1].Value } | 
                      Select-Object -First 1
        
        if ($ipv4Address) {
            Write-ColorOutput "[성공] 서버 IP 주소: $ipv4Address" "Green"
            return $ipv4Address
        }
        else {
            Write-ColorOutput "[경고] IP 주소를 자동으로 찾을 수 없습니다." "Yellow"
            Write-ColorOutput "수동으로 확인하세요: ipconfig" "Yellow"
            return $null
        }
    }
    catch {
        Write-ColorOutput "[경고] IP 주소 확인 실패" "Yellow"
        return $null
    }
}

function Show-InstallationComplete {
    param([string]$ServerIP)
    
    Write-Host ""
    Write-ColorOutput "============================================" "Green"
    Write-ColorOutput "   설치 완료!" "Green"
    Write-ColorOutput "============================================" "Green"
    Write-Host ""
    
    Write-ColorOutput "CMS 접속 정보:" "Cyan"
    Write-Host "  - 로컬 접속: http://localhost:8000" -ForegroundColor White
    if ($ServerIP) {
        Write-Host "  - 네트워크 접속: http://$ServerIP:8000" -ForegroundColor White
    }
    Write-Host ""
    
    Write-ColorOutput "기본 로그인 정보:" "Cyan"
    Write-Host "  - 사용자명: xibo_admin" -ForegroundColor White
    Write-Host "  - 비밀번호: password" -ForegroundColor White
    Write-Host ""
    
    Write-ColorOutput "플레이어 설정:" "Cyan"
    if ($ServerIP) {
        Write-Host "  - CMS Address: http://$ServerIP:8000/xmds.php" -ForegroundColor White
    }
    else {
        Write-Host "  - CMS Address: http://[서버IP]:8000/xmds.php" -ForegroundColor White
        Write-Host "  - 서버 IP 확인: ipconfig" -ForegroundColor Gray
    }
    Write-Host "  - CMS Key: CMS 관리자 페이지에서 확인" -ForegroundColor White
    Write-Host ""
    
    Write-ColorOutput "서비스 관리 명령어:" "Cyan"
    Write-Host "  - 시작: docker-compose up -d" -ForegroundColor White
    Write-Host "  - 중지: docker-compose down" -ForegroundColor White
    Write-Host "  - 재시작: docker-compose restart" -ForegroundColor White
    Write-Host "  - 로그: docker-compose logs -f" -ForegroundColor White
    Write-Host "  - 상태: docker-compose ps" -ForegroundColor White
    Write-Host ""
}

# 메인 설치 프로세스
function Start-Installation {
    Show-Header
    
    # 현재 디렉토리 확인
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    Set-Location $scriptDir
    
    Write-ColorOutput "설치 디렉토리: $scriptDir" "Gray"
    Write-Host ""
    
    # 1. Docker 설치 확인
    if (-not (Test-DockerInstalled)) {
        Write-Host ""
        Write-ColorOutput "Docker Desktop을 설치해주세요:" "Yellow"
        Write-ColorOutput "https://www.docker.com/products/docker-desktop" "Cyan"
        if (-not $Silent) {
            Read-Host "계속하려면 Enter를 누르세요"
        }
        exit 1
    }
    Write-Host ""
    
    # 2. Docker 서비스 확인
    if (-not (Test-DockerRunning)) {
        Write-Host ""
        Write-ColorOutput "Docker Desktop을 실행한 후 다시 시도해주세요." "Yellow"
        if (-not $Silent) {
            Read-Host "계속하려면 Enter를 누르세요"
        }
        exit 1
    }
    Write-Host ""
    
    # 3. Docker 이미지 로드
    if (-not $SkipImageLoad) {
        if ([string]::IsNullOrEmpty($ImageFile)) {
            # 이미지 파일 찾기
            $imageFiles = Get-ChildItem -Path $scriptDir -Filter "*.tar" | Sort-Object LastWriteTime -Descending
            if ($imageFiles.Count -eq 0) {
                Write-ColorOutput "[경고] Docker 이미지 파일(.tar)을 찾을 수 없습니다." "Yellow"
                Write-ColorOutput "이미지 파일 경로를 입력하거나, 이미 로드된 이미지를 사용합니다." "Yellow"
                $skipLoad = Read-Host "이미지 로드를 건너뛰시겠습니까? (Y/N)"
                if ($skipLoad -eq "Y" -or $skipLoad -eq "y") {
                    $SkipImageLoad = $true
                }
                else {
                    $ImageFile = Read-Host "이미지 파일 경로를 입력하세요"
                }
            }
            else {
                $ImageFile = $imageFiles[0].FullName
                Write-ColorOutput "발견된 이미지 파일: $ImageFile" "Gray"
            }
        }
        
        if (-not $SkipImageLoad -and -not [string]::IsNullOrEmpty($ImageFile)) {
            if (-not (Load-DockerImage -ImagePath $ImageFile)) {
                Write-Host ""
                Write-ColorOutput "[오류] 이미지 로드 실패. 설치를 중단합니다." "Red"
                if (-not $Silent) {
                    Read-Host "계속하려면 Enter를 누르세요"
                }
                exit 1
            }
        }
    }
    Write-Host ""
    
    # 4. 데이터 디렉토리 생성
    if (-not (Create-DataDirectories)) {
        Write-Host ""
        Write-ColorOutput "[오류] 디렉토리 생성 실패. 설치를 중단합니다." "Red"
        if (-not $Silent) {
            Read-Host "계속하려면 Enter를 누르세요"
        }
        exit 1
    }
    Write-Host ""
    
    # 5. Docker Compose 파일 생성
    if (-not (Create-DockerComposeFile)) {
        Write-Host ""
        Write-ColorOutput "[오류] 설정 파일 생성 실패. 설치를 중단합니다." "Red"
        if (-not $Silent) {
            Read-Host "계속하려면 Enter를 누르세요"
        }
        exit 1
    }
    Write-Host ""
    
    # 6. 서비스 시작
    if (-not (Start-CMSServices)) {
        Write-Host ""
        Write-ColorOutput "[오류] 서비스 시작 실패." "Red"
        Write-ColorOutput "로그를 확인하세요: docker-compose logs" "Yellow"
        if (-not $Silent) {
            Read-Host "계속하려면 Enter를 누르세요"
        }
        exit 1
    }
    Write-Host ""
    
    # 7. 서버 IP 확인
    $serverIP = Get-ServerIP
    Write-Host ""
    
    # 설치 완료 메시지
    Show-InstallationComplete -ServerIP $serverIP
    
    # 서비스 상태 표시
    Write-ColorOutput "서비스 상태:" "Cyan"
    docker-compose ps
    Write-Host ""
    
    if (-not $Silent) {
        $openBrowser = Read-Host "브라우저를 열까요? (Y/N)"
        if ($openBrowser -eq "Y" -or $openBrowser -eq "y") {
            Start-Process "http://localhost:8000"
        }
        
        Read-Host "계속하려면 Enter를 누르세요"
    }
}

# 설치 시작
try {
    Start-Installation
}
catch {
    Write-ColorOutput "[치명적 오류] 설치 중 오류가 발생했습니다: $_" "Red"
    if (-not $Silent) {
        Read-Host "계속하려면 Enter를 누르세요"
    }
    exit 1
}

