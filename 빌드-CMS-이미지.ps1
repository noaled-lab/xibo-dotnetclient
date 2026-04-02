# CMS Docker 이미지 빌드 스크립트
# 개발자 PC에서 실행하여 배포용 이미지를 생성합니다.

param(
    [string]$ImageName = "noa-cms",
    [string]$ImageTag = "latest",
    [string]$OutputFile = "noa-cms-latest.tar",
    [switch]$SkipBuild = $false
)

$Host.UI.RawUI.WindowTitle = "CMS Docker 이미지 빌드"

function Write-ColorOutput {
    param(
        [string]$Message,
        [string]$Color = "White"
    )
    Write-Host $Message -ForegroundColor $Color
}

Write-ColorOutput "============================================" "Cyan"
Write-ColorOutput "   CMS Docker 이미지 빌드" "Cyan"
Write-ColorOutput "============================================" "Cyan"
Write-Host ""

# 프로젝트 루트 디렉토리 확인
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir

Write-ColorOutput "프로젝트 루트: $projectRoot" "Gray"
Set-Location $projectRoot

# Docker 확인
Write-ColorOutput "[1/4] Docker 확인 중..." "Yellow"
try {
    docker --version | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-ColorOutput "[오류] Docker가 설치되어 있지 않습니다." "Red"
        exit 1
    }
    Write-ColorOutput "[성공] Docker 확인 완료" "Green"
}
catch {
    Write-ColorOutput "[오류] Docker를 찾을 수 없습니다." "Red"
    exit 1
}
Write-Host ""

# Docker 서비스 확인
Write-ColorOutput "[2/4] Docker 서비스 확인 중..." "Yellow"
try {
    docker ps | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-ColorOutput "[오류] Docker 서비스가 실행되지 않았습니다." "Red"
        Write-ColorOutput "Docker Desktop을 실행해주세요." "Yellow"
        exit 1
    }
    Write-ColorOutput "[성공] Docker 서비스 실행 중" "Green"
}
catch {
    Write-ColorOutput "[오류] Docker 서비스 확인 실패" "Red"
    exit 1
}
Write-Host ""

# 이미지 빌드
if (-not $SkipBuild) {
    Write-ColorOutput "[3/4] Docker 이미지 빌드 중..." "Yellow"
    Write-ColorOutput "이 작업은 시간이 걸릴 수 있습니다 (10-30분)..." "Gray"
    Write-Host ""
    
    $fullImageName = "$ImageName`:$ImageTag"
    
    try {
        docker build -t $fullImageName -f Dockerfile .
        if ($LASTEXITCODE -ne 0) {
            Write-ColorOutput "[오류] 이미지 빌드 실패" "Red"
            exit 1
        }
        Write-ColorOutput "[성공] 이미지 빌드 완료: $fullImageName" "Green"
    }
    catch {
        Write-ColorOutput "[오류] 빌드 중 오류 발생: $_" "Red"
        exit 1
    }
    Write-Host ""
}
else {
    Write-ColorOutput "[3/4] 이미지 빌드 건너뜀" "Yellow"
    Write-Host ""
}

# 이미지 저장
Write-ColorOutput "[4/4] 이미지 파일로 저장 중..." "Yellow"
$outputPath = Join-Path $scriptDir $OutputFile

Write-ColorOutput "출력 파일: $outputPath" "Gray"
Write-ColorOutput "이 작업은 시간이 걸릴 수 있습니다..." "Gray"

try {
    $fullImageName = "$ImageName`:$ImageTag"
    docker save $fullImageName -o $outputPath
    if ($LASTEXITCODE -ne 0) {
        Write-ColorOutput "[오류] 이미지 저장 실패" "Red"
        exit 1
    }
    
    $fileInfo = Get-Item $outputPath
    $fileSizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
    $fileSizeGB = [math]::Round($fileInfo.Length / 1GB, 2)
    
    Write-ColorOutput "[성공] 이미지 저장 완료" "Green"
    Write-ColorOutput "파일 크기: $fileSizeMB MB ($fileSizeGB GB)" "Green"
    Write-ColorOutput "파일 위치: $outputPath" "Green"
}
catch {
    Write-ColorOutput "[오류] 저장 중 오류 발생: $_" "Red"
    exit 1
}

Write-Host ""
Write-ColorOutput "============================================" "Green"
Write-ColorOutput "   빌드 완료!" "Green"
Write-ColorOutput "============================================" "Green"
Write-Host ""

Write-ColorOutput "다음 단계:" "Cyan"
Write-Host "1. 이미지 파일을 배포 패키지에 포함" -ForegroundColor White
Write-Host "2. CMS-서버-설치-프로그램.ps1와 함께 배포" -ForegroundColor White
Write-Host "3. 고객 서버 PC에서 설치 프로그램 실행" -ForegroundColor White
Write-Host ""

# 배포 패키지 생성 제안
$createPackage = Read-Host "배포 패키지 폴더를 생성하시겠습니까? (Y/N)"
if ($createPackage -eq "Y" -or $createPackage -eq "y") {
    $packageDir = Join-Path $scriptDir "CMS-서버-배포패키지"
    
    if (-not (Test-Path $packageDir)) {
        New-Item -ItemType Directory -Path $packageDir | Out-Null
    }
    
    # 파일 복사
    Copy-Item $outputPath -Destination $packageDir -Force
    Copy-Item (Join-Path $scriptDir "CMS-서버-설치-프로그램.ps1") -Destination $packageDir -Force
    Copy-Item (Join-Path $scriptDir "CMS-서버-설치.bat") -Destination $packageDir -Force
    Copy-Item (Join-Path $scriptDir "README-서버설치.txt") -Destination $packageDir -Force
    
    Write-ColorOutput "[성공] 배포 패키지 생성 완료: $packageDir" "Green"
    Write-Host ""
    Write-ColorOutput "배포 패키지 내용:" "Cyan"
    Get-ChildItem $packageDir | ForEach-Object {
        Write-Host "  - $($_.Name)" -ForegroundColor White
    }
}

Write-Host ""
Read-Host "계속하려면 Enter를 누르세요"

