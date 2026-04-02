# MSBuild 및 .NET Framework 개발자 팩 자동 설치 스크립트
# 관리자 권한으로 실행해야 할 수 있습니다

Write-Host "=== MSBuild 및 .NET Framework 개발자 팩 설치 ===" -ForegroundColor Cyan
Write-Host ""

# winget 사용 가능 여부 확인
$wingetAvailable = $false
try {
    $wingetVersion = winget --version 2>&1
    if ($LASTEXITCODE -eq 0) {
        $wingetAvailable = $true
        Write-Host "winget 사용 가능: $wingetVersion" -ForegroundColor Green
    }
} catch {
    Write-Host "winget을 사용할 수 없습니다." -ForegroundColor Yellow
}

if ($wingetAvailable) {
    Write-Host ""
    Write-Host "winget을 사용하여 Visual Studio Build Tools 설치를 시도합니다..." -ForegroundColor Cyan
    Write-Host "주의: 설치에는 시간이 걸릴 수 있습니다." -ForegroundColor Yellow
    Write-Host ""
    
    $response = Read-Host "계속하시겠습니까? (Y/N)"
    if ($response -eq "Y" -or $response -eq "y") {
        Write-Host "Visual Studio Build Tools 설치 중..." -ForegroundColor Cyan
        winget install --id Microsoft.VisualStudio.2022.BuildTools --silent --accept-package-agreements --accept-source-agreements
    } else {
        Write-Host "설치가 취소되었습니다." -ForegroundColor Yellow
        exit 0
    }
} else {
    Write-Host ""
    Write-Host "수동 설치가 필요합니다:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "1. Visual Studio Build Tools 2022 다운로드:" -ForegroundColor Cyan
    Write-Host "   https://visualstudio.microsoft.com/downloads/" -ForegroundColor White
    Write-Host ""
    Write-Host "2. 설치 시 다음 워크로드 선택:" -ForegroundColor Cyan
    Write-Host "   - .NET desktop build tools" -ForegroundColor White
    Write-Host "   - .NET Framework 4.8 targeting pack" -ForegroundColor White
    Write-Host ""
    Write-Host "3. 또는 .NET Framework 4.8 개발자 팩 직접 설치:" -ForegroundColor Cyan
    Write-Host "   https://dotnet.microsoft.com/download/dotnet-framework/net48" -ForegroundColor White
    Write-Host ""
}

Write-Host ""
Write-Host "설치 완료 후 build.ps1 스크립트를 실행하여 빌드할 수 있습니다." -ForegroundColor Green

