# 빌드 시 기본 CMS 설정을 포함하는 빌드 스크립트
# 사용법: .\build-with-config.ps1 -CmsAddress "http://localhost:8000/xmds.php" -CmsKey "yourserverkey"

param(
    [string]$Configuration = "Release",
    [string]$Platform = "Any CPU",
    [Parameter(Mandatory=$false)]
    [string]$CmsAddress = "",
    [Parameter(Mandatory=$false)]
    [string]$CmsKey = ""
)

Write-Host "=== NOA Player 빌드 (설정 포함) ===" -ForegroundColor Cyan
Write-Host ""

# 기본 빌드 스크립트 실행
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
& "$scriptPath\build.ps1" -Configuration $Configuration -Platform $Platform -DefaultCmsAddress $CmsAddress -DefaultCmsKey $CmsKey

if ($LASTEXITCODE -ne 0) {
    Write-Host "빌드 실패" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== 완료 ===" -ForegroundColor Green
Write-Host ""
if (-not [string]::IsNullOrEmpty($CmsAddress)) {
    Write-Host "기본 CMS 주소가 설정된 플레이어가 빌드되었습니다." -ForegroundColor Cyan
    Write-Host "  CMS Address: $CmsAddress" -ForegroundColor White
}

