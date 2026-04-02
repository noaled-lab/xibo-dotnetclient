# MSI Installer Build Script
# WiX Toolset must be installed

param(
    [string]$Configuration = "Release"
)

function Get-MsBuildPath {
    $candidates = @(
        "msbuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    )

    foreach ($candidate in $candidates) {
        if ($candidate -eq "msbuild.exe") {
            $cmd = Get-Command $candidate -ErrorAction SilentlyContinue
            if ($cmd) { return $cmd.Source }
        } elseif (Test-Path $candidate) {
            return $candidate
        }
    }

    return $null
}

Write-Host "=== NOA Player MSI Installer Build ===" -ForegroundColor Cyan
Write-Host ""

# WiX Installation Check
$wixPath = $null
$wixPaths = @(
    "C:\Program Files (x86)\WiX Toolset v3.11\bin\candle.exe",
    "C:\Program Files (x86)\WiX Toolset v3.10\bin\candle.exe",
    "${env:ProgramFiles(x86)}\WiX Toolset v3.11\bin\candle.exe",
    "${env:ProgramFiles(x86)}\WiX Toolset v3.10\bin\candle.exe",
    "${env:ProgramFiles(x86)}\WiX Toolset v3.14\bin\candle.exe"
)

foreach ($path in $wixPaths) {
    if (Test-Path $path) {
        $wixPath = Split-Path -Parent $path
        Write-Host "WiX Toolset found: $wixPath" -ForegroundColor Green
        break
    }
}

if ($null -eq $wixPath) {
    Write-Host "ERROR: WiX Toolset not found." -ForegroundColor Red
    Write-Host ""
    Write-Host "Please install WiX Toolset:" -ForegroundColor Yellow
    Write-Host "1. Visit https://wixtoolset.org/releases/" -ForegroundColor Yellow
    Write-Host "2. Download and install WiX Toolset v3.11 or later" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Or install using winget:" -ForegroundColor Yellow
    Write-Host "winget install WiXToolset.WiXToolset" -ForegroundColor Cyan
    exit 1
}

$candle = Join-Path $wixPath "candle.exe"
$light = Join-Path $wixPath "light.exe"
$heat = Join-Path $wixPath "heat.exe"

# Change to project directory
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptPath

Write-Host "Current directory: $(Get-Location)" -ForegroundColor Cyan
Write-Host ""

# Build client first
Write-Host "Building client..." -ForegroundColor Cyan
$buildSucceeded = $false
if (Test-Path ".\build.ps1") {
    & ".\build.ps1" -Configuration $Configuration
    if ($LASTEXITCODE -eq 0) {
        $buildSucceeded = $true
    } else {
        Write-Host "build.ps1 failed. Falling back to direct msbuild..." -ForegroundColor Yellow
    }
}

if (-not $buildSucceeded) {
    $msbuild = Get-MsBuildPath
    if (-not $msbuild) {
        Write-Host "Client build failed (msbuild not found)" -ForegroundColor Red
        exit 1
    }

    & $msbuild "XiboClient.sln" "/t:Build" "/p:Configuration=$Configuration" "/p:Platform=x86" "/m"
    if ($LASTEXITCODE -eq 0) {
        $buildSucceeded = $true
    }
}

if (-not $buildSucceeded) {
    Write-Host "Client build failed" -ForegroundColor Red
    exit 1
}

Write-Host ""

# WiX Environment Variable
$env:WIX = $wixPath

# WiX Build
Write-Host "Building MSI package..." -ForegroundColor Cyan

$binPath = "bin\x86\$Configuration"
$solutionDir = (Get-Location).Path
$outputDir = Join-Path $solutionDir $binPath
$productVersion = "4.40.53"

# Ensure libmpv-2.dll is present in output so MSI always includes it.
$mpvDllTarget = Join-Path $outputDir "libmpv-2.dll"
if (-not (Test-Path $mpvDllTarget)) {
    $dllCandidates = @(
        (Join-Path $solutionDir "libmpv-2.dll"),
        "C:\workbench\mpv-dll\mpv-dev-i686-20260331-git-9465b30\libmpv-2.dll",
        "C:\workbench\mpv\mpv-dev-i686-20260331-git-9465b30\libmpv-2.dll"
    )

    $mpvDllSource = $dllCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

    if (-not $mpvDllSource) {
        $mpvDllSource = Get-ChildItem "C:\workbench\mpv-dll" -Filter "libmpv-2.dll" -File -Recurse -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty FullName -First 1
    }

    if ($mpvDllSource) {
        Copy-Item -Path $mpvDllSource -Destination $mpvDllTarget -Force
        Write-Host "libmpv-2.dll copied to output: $mpvDllTarget" -ForegroundColor Green
    } else {
        Write-Host "WARNING: libmpv-2.dll source not found. MSI may be built without mpv DLL." -ForegroundColor Yellow
    }
} else {
    Write-Host "libmpv-2.dll already exists in output: $mpvDllTarget" -ForegroundColor Green
}

# Use Heat to automatically include all files (DLLs, etc.)
Write-Host "Scanning dependency files..." -ForegroundColor Cyan
$heatFragment = "heat-fragment.wxs"
& $heat dir $binPath -cg Dependencies -gg -srd -sfrag -dr PlayerFolder -var var.SourceDir -out $heatFragment
if ($LASTEXITCODE -ne 0) {
    Write-Host "Heat execution failed - proceeding manually" -ForegroundColor Yellow
    $heatFragment = $null
} else {
    Write-Host "Dependency file list generated" -ForegroundColor Green
}

# Compile .wixobj with candle
$wixObjFile = "installer.wixobj"
& $candle "installer.wxs" -out $wixObjFile -dSourceDir="$solutionDir\$binPath" -dProductVersion="$productVersion"
if ($LASTEXITCODE -ne 0) {
    Write-Host "WiX compilation failed" -ForegroundColor Red
    if ($heatFragment -and (Test-Path $heatFragment)) {
        Remove-Item $heatFragment -ErrorAction SilentlyContinue
    }
    exit 1
}

# Compile Heat fragment separately if exists
$heatObjFile = $null
if ($heatFragment -and (Test-Path $heatFragment)) {
    Write-Host "Compiling dependency fragment..." -ForegroundColor Cyan
    $heatObjFile = "heat-fragment.wixobj"
    & $candle $heatFragment -out $heatObjFile -dSourceDir="$solutionDir\$binPath" -dProductVersion="$productVersion"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Heat Fragment compilation failed" -ForegroundColor Red
        Remove-Item $heatFragment -ErrorAction SilentlyContinue
        exit 1
    }
}

# Generate MSI with light
$msiFile = "NOA-Player-v4.40.53.msi"
$lightArgs = @($wixObjFile, "-out", $msiFile, "-ext", "WixUIExtension")
if ($heatObjFile) {
    $lightArgs += $heatObjFile
}
& $light $lightArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "MSI package generation failed" -ForegroundColor Red
    if ($heatFragment -and (Test-Path $heatFragment)) {
        Remove-Item $heatFragment -ErrorAction SilentlyContinue
    }
    exit 1
}

# Cleanup temporary files
if ($heatFragment -and (Test-Path $heatFragment)) {
    Remove-Item $heatFragment -ErrorAction SilentlyContinue
}
if ($heatObjFile -and (Test-Path $heatObjFile)) {
    Remove-Item $heatObjFile -ErrorAction SilentlyContinue
}
if (Test-Path $wixObjFile) {
    Remove-Item $wixObjFile -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "=== MSI Package Build Complete ===" -ForegroundColor Green
Write-Host "Output file: $msiFile" -ForegroundColor Cyan
Write-Host "Location: $(Get-Location)\$msiFile" -ForegroundColor Cyan
