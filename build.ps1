# Xibo Client Build Script
# Visual Studio 없이 MSBuild를 사용하여 빌드하는 스크립트

param(
    [string]$Configuration = "Release",
    [string]$Platform = "Any CPU",
    [string]$DefaultCmsAddress = "",
    [string]$DefaultCmsKey = ""
)

Write-Host "=== Xibo Client Build Script ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host "Platform: $Platform" -ForegroundColor Yellow
Write-Host ""

# MSBuild 경로 찾기
$msbuildPaths = @(
    "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
)

$msbuild = $null
foreach ($path in $msbuildPaths) {
    if (Test-Path $path) {
        $msbuild = $path
        Write-Host "MSBuild found: $path" -ForegroundColor Green
        break
    }
}

if ($null -eq $msbuild) {
    Write-Host "ERROR: MSBuild를 찾을 수 없습니다." -ForegroundColor Red
    Write-Host ""
    Write-Host "Visual Studio Build Tools를 설치하거나 다음 중 하나를 설치하세요:" -ForegroundColor Yellow
    Write-Host "1. Visual Studio Build Tools 2022: https://visualstudio.microsoft.com/downloads/" -ForegroundColor Yellow
    Write-Host "2. Visual Studio Community 2022 (무료)" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "설치 시 '.NET desktop build tools' 워크로드를 선택하세요." -ForegroundColor Yellow
    exit 1
}

# 프로젝트 디렉토리로 이동
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptPath

Write-Host "현재 디렉토리: $(Get-Location)" -ForegroundColor Cyan
Write-Host ""

# NuGet 패키지 복원
Write-Host "NuGet 패키지 복원 중..." -ForegroundColor Cyan
& $msbuild XiboClient.sln /t:Restore /p:Configuration=$Configuration /p:Platform="$Platform"
if ($LASTEXITCODE -ne 0) {
    Write-Host "NuGet 패키지 복원 실패" -ForegroundColor Red
    exit 1
}

Write-Host ""

# 프로젝트 빌드
Write-Host "프로젝트 빌드 중..." -ForegroundColor Cyan
& $msbuild XiboClient.sln /t:Build /p:Configuration=$Configuration /p:Platform="$Platform" /m
if ($LASTEXITCODE -ne 0) {
    Write-Host "빌드 실패" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== 빌드 완료 ===" -ForegroundColor Green

# 출력 디렉토리 확인
$outputPath = "bin\$Configuration"
if ($Platform -eq "x86") {
    $outputPath = "bin\x86\$Configuration"
}

if (Test-Path $outputPath) {
    Write-Host "출력 디렉토리: $outputPath" -ForegroundColor Green
    Get-ChildItem $outputPath -Filter "*.exe" | ForEach-Object {
        Write-Host "  - $($_.Name)" -ForegroundColor Cyan
    }
}

# 기본 설정 파일 수정 (옵션)
if (-not [string]::IsNullOrEmpty($DefaultCmsAddress) -or -not [string]::IsNullOrEmpty($DefaultCmsKey)) {
    Write-Host ""
    Write-Host "기본 설정 파일 수정 중..." -ForegroundColor Cyan
    
    $defaultConfigPath = Join-Path $scriptPath "default.config.xml"
    
    if (Test-Path $defaultConfigPath) {
        try {
            [xml]$xml = Get-Content $defaultConfigPath
            
            if (-not [string]::IsNullOrEmpty($DefaultCmsAddress)) {
                $xml.ApplicationSettings.ServerUri = $DefaultCmsAddress
                Write-Host "  CMS Address 설정: $DefaultCmsAddress" -ForegroundColor Green
            }
            
            if (-not [string]::IsNullOrEmpty($DefaultCmsKey)) {
                $xml.ApplicationSettings.ServerKey = $DefaultCmsKey
                Write-Host "  CMS Key 설정됨" -ForegroundColor Green
            }
            
            $xml.Save($defaultConfigPath)
            Write-Host "[성공] 기본 설정 파일 수정 완료" -ForegroundColor Green
        }
        catch {
            Write-Host "[경고] 기본 설정 파일 수정 실패: $_" -ForegroundColor Yellow
        }
    }
    else {
        Write-Host "[경고] default.config.xml 파일을 찾을 수 없습니다." -ForegroundColor Yellow
    }
}

