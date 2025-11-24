# MSI Installer Build Script
# WiX Toolset must be installed

param(
    [string]$Configuration = "Release"
)

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
& ".\build.ps1" -Configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Host "Client build failed" -ForegroundColor Red
    exit 1
}

Write-Host ""

# WiX Environment Variable
$env:WIX = $wixPath

# WiX Build
Write-Host "Building MSI package..." -ForegroundColor Cyan

$binPath = "bin\$Configuration"
$solutionDir = (Get-Location).Path

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
& $candle "installer.wxs" -out $wixObjFile -dSourceDir="$solutionDir\$binPath"
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
    & $candle $heatFragment -out $heatObjFile -dSourceDir="$solutionDir\$binPath"
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
