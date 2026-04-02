# NOA Player 설정 자동화 스크립트
# 플레이어 설치 후 CMS 주소 및 기타 설정을 자동으로 구성합니다.

param(
    [string]$CmsAddress = "",
    [string]$CmsKey = "",
    [string]$LibraryPath = "",
    [string]$AppName = "XiboClient"
)

Write-Host "=== NOA Player 설정 자동화 ===" -ForegroundColor Cyan
Write-Host ""

# CMS 주소 입력 (없는 경우)
if ([string]::IsNullOrEmpty($CmsAddress)) {
    Write-Host "CMS 주소를 입력하세요." -ForegroundColor Yellow
    Write-Host "예: http://localhost:8000/xmds.php" -ForegroundColor Gray
    Write-Host "예: http://192.168.1.100:8000/xmds.php" -ForegroundColor Gray
    Write-Host "예: http://cms.example.com/xmds.php" -ForegroundColor Gray
    $CmsAddress = Read-Host "CMS Address"
}

# CMS Key 입력 (없는 경우)
if ([string]::IsNullOrEmpty($CmsKey)) {
    Write-Host ""
    Write-Host "CMS Key를 입력하세요." -ForegroundColor Yellow
    Write-Host "(CMS 관리자 페이지 > Settings > Display Settings에서 확인)" -ForegroundColor Gray
    $CmsKey = Read-Host "CMS Key"
}

# 라이브러리 경로 입력 (없는 경우)
if ([string]::IsNullOrEmpty($LibraryPath)) {
    Write-Host ""
    Write-Host "라이브러리 경로를 입력하세요." -ForegroundColor Yellow
    Write-Host "예: C:\XiboLibrary" -ForegroundColor Gray
    $LibraryPath = Read-Host "Library Path"
    
    # 기본값 사용
    if ([string]::IsNullOrEmpty($LibraryPath)) {
        $LibraryPath = "$env:USERPROFILE\Documents\$AppName Library"
        Write-Host "기본 경로 사용: $LibraryPath" -ForegroundColor Gray
    }
}

# 경로 정리
$CmsAddress = $CmsAddress.Trim()
$CmsKey = $CmsKey.Trim()
$LibraryPath = $LibraryPath.TrimEnd('\', '/')

# APPDATA 경로
$AppDataPath = [Environment]::GetFolderPath("ApplicationData")
$ConfigFilePath = Join-Path $AppDataPath "$AppName.xml"

Write-Host ""
Write-Host "설정 정보:" -ForegroundColor Cyan
Write-Host "  CMS Address: $CmsAddress" -ForegroundColor White
Write-Host "  CMS Key: $($CmsKey.Substring(0, [Math]::Min(10, $CmsKey.Length)))..." -ForegroundColor White
Write-Host "  Library Path: $LibraryPath" -ForegroundColor White
Write-Host "  Config File: $ConfigFilePath" -ForegroundColor White
Write-Host ""

# 확인
$confirm = Read-Host "이 설정으로 진행하시겠습니까? (Y/N)"
if ($confirm -ne "Y" -and $confirm -ne "y") {
    Write-Host "취소되었습니다." -ForegroundColor Yellow
    exit 0
}

# 라이브러리 경로 생성
if (-not (Test-Path $LibraryPath)) {
    try {
        New-Item -ItemType Directory -Path $LibraryPath -Force | Out-Null
        Write-Host "[성공] 라이브러리 경로 생성: $LibraryPath" -ForegroundColor Green
    }
    catch {
        Write-Host "[오류] 라이브러리 경로 생성 실패: $_" -ForegroundColor Red
        exit 1
    }
}

# XML 설정 파일 생성
try {
    $xmlWriter = New-Object System.Xml.XmlTextWriter($ConfigFilePath, [System.Text.Encoding]::UTF8)
    $xmlWriter.Formatting = [System.Xml.Formatting]::Indented
    $xmlWriter.Indentation = 2
    
    $xmlWriter.WriteStartDocument()
    $xmlWriter.WriteStartElement("ApplicationSettings")
    
    $xmlWriter.WriteElementString("ServerUri", $CmsAddress)
    $xmlWriter.WriteElementString("ServerKey", $CmsKey)
    $xmlWriter.WriteElementString("LibraryPath", $LibraryPath)
    
    $xmlWriter.WriteEndElement()
    $xmlWriter.WriteEndDocument()
    $xmlWriter.Close()
    
    Write-Host "[성공] 설정 파일 생성 완료: $ConfigFilePath" -ForegroundColor Green
}
catch {
    Write-Host "[오류] 설정 파일 생성 실패: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== 설정 완료 ===" -ForegroundColor Green
Write-Host ""
Write-Host "다음 단계:" -ForegroundColor Cyan
Write-Host "1. NOA Player Options를 실행하여 연결을 확인하세요." -ForegroundColor White
Write-Host "2. 'Connect' 버튼을 클릭하여 CMS에 연결하세요." -ForegroundColor White
Write-Host ""

