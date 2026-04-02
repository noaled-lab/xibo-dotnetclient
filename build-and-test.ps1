# XiboClient 빌드 + 자동 테스트 스크립트
# 1. 기존 프로세스 종료
# 2. 빌드 (x86 Release)
# 3. exe를 설치 경로에 복사
# 4. 설치 경로에서 실행 (CefSharp이 상대경로로 서브프로세스를 찾으므로 working dir 중요)
# 5. 지정 시간 후 종료 → 로그 출력

param(
    [int]$WaitSeconds = 30
)

$msbuild    = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
$sln        = "$PSScriptRoot\XiboClient.sln"
$buildExe   = "$PSScriptRoot\bin\x86\Release\XiboClient.exe"
$installDir = "C:\Program Files (x86)\NOA Player\Player"
$installExe = "$installDir\XiboClient.exe"
$libraryDir = "C:\xibo2"

# ---------------------------------------------------------------
# 1. 기존 프로세스 종료
# ---------------------------------------------------------------
Write-Host "`n[1/4] 기존 XiboClient 프로세스 종료..." -ForegroundColor Cyan
$procs = Get-Process -Name "XiboClient" -ErrorAction SilentlyContinue
if ($procs) {
    $procs | Stop-Process -Force
    Start-Sleep -Seconds 2
    Write-Host "  -> 종료 완료" -ForegroundColor Green
} else {
    Write-Host "  -> 실행 중인 프로세스 없음" -ForegroundColor Yellow
}

# ---------------------------------------------------------------
# 2. 빌드
# ---------------------------------------------------------------
Write-Host "`n[2/4] 빌드 (x86 Release)..." -ForegroundColor Cyan
$result = & $msbuild $sln /p:Configuration=Release /p:Platform=x86 /m /v:minimal /p:GenerateSerializationAssemblies=Off 2>&1
$errors = $result | Where-Object { $_ -match ': error ' -and $_ -notmatch 'CS0649' }
if ($errors) {
    Write-Host "`n  [ERROR] 빌드 실패:" -ForegroundColor Red
    $errors | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host "  -> 빌드 성공" -ForegroundColor Green

# ---------------------------------------------------------------
# 3. 빌드된 exe를 설치 경로의 working dir로 실행
#    (CefSharp이 상대경로로 BrowserSubprocess를 찾으므로 working dir = 설치경로 필수)
#    exe 복사 권한이 없어도 동작함
# ---------------------------------------------------------------
Write-Host "`n[3/4] 빌드된 exe를 설치 경로 working dir로 실행..." -ForegroundColor Cyan

# ---------------------------------------------------------------
# 4. 실행 → 대기 → 로그 출력
# ---------------------------------------------------------------
Write-Host "`n[4/4] XiboClient 실행 (exe=$buildExe, workdir=$installDir)..." -ForegroundColor Cyan
$proc = Start-Process -FilePath $buildExe -WorkingDirectory $installDir -PassThru
Write-Host "  -> PID: $($proc.Id). $WaitSeconds 초 대기..." -ForegroundColor Green

Start-Sleep -Seconds $WaitSeconds

# 죽었는지 확인
if ($proc.HasExited) {
    Write-Host "`n  [ERROR] 프로세스가 종료되었습니다. ExitCode: $($proc.ExitCode)" -ForegroundColor Red
} else {
    Write-Host "`n  -> 프로세스 정상 실행 중. 종료합니다..." -ForegroundColor Green
    $proc | Stop-Process -Force
    Start-Sleep -Seconds 1
}

# 최신 로그 출력
$logFiles = Get-ChildItem -Path $libraryDir -Filter "log.xml_*" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending
if ($logFiles) {
    $latestLog = $logFiles[0].FullName
    Write-Host "`n  최신 로그: $latestLog" -ForegroundColor Yellow
    Write-Host "  ---- 오류/MPV 관련 로그 ----" -ForegroundColor Yellow
    Select-String -Path $latestLog -Pattern "(error|Error|VideoMpv|MpvHost|VIDEO_RECONFIG|Stopped|RenderMedia)" |
        Select-Object -Last 40 |
        ForEach-Object { Write-Host $_.Line }
}

Write-Host "`n================================================" -ForegroundColor Magenta
Write-Host "  결과를 알려주세요!" -ForegroundColor Magenta
Write-Host "================================================`n" -ForegroundColor Magenta
