# IfcTester ArchiCAD Add-On Build Verification Script
# Verifies that the correct build version was copied to the ArchiCAD Add-Ons folder

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [ValidateSet("25", "26", "27", "29")]
    [string]$ArchiCADVersion = "27"
)

$ErrorActionPreference = "Stop"

# Script location
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir
$ArchiCADDir = Join-Path $ProjectRoot "archicad"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " IfcTester ArchiCAD Build Verification" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Expected build output location (CMake)
$BuildOutputPath = Join-Path $ArchiCADDir "cmake-build\$Configuration\IfcTesterArchiCAD.apx"

# ArchiCAD Add-Ons folder
$AddOnsFolder = "$env:APPDATA\Graphisoft\ArchiCAD $ArchiCADVersion\Add-Ons"
$InstalledPath = Join-Path $AddOnsFolder "IfcTesterArchiCAD\IfcTesterArchiCAD.apx"

Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "ArchiCAD Version: $ArchiCADVersion" -ForegroundColor Cyan
Write-Host ""

# Check if build output exists
Write-Host "Checking build output..." -ForegroundColor Yellow
if (-not (Test-Path $BuildOutputPath)) {
    Write-Host "ERROR: Build output not found at:" -ForegroundColor Red
    Write-Host "  $BuildOutputPath" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Please build the project first using:" -ForegroundColor Yellow
    Write-Host "  cd archicad\cmake-build" -ForegroundColor Gray
    Write-Host "  cmake --build . --config $Configuration" -ForegroundColor Gray
    exit 1
}

$BuildFile = Get-Item $BuildOutputPath
Write-Host "✓ Build output found" -ForegroundColor Green
Write-Host "  Path: $BuildOutputPath" -ForegroundColor Gray
Write-Host "  Size: $([math]::Round($BuildFile.Length / 1KB, 2)) KB" -ForegroundColor Gray
Write-Host "  Modified: $($BuildFile.LastWriteTime)" -ForegroundColor Gray
Write-Host ""

# Check if installed version exists
Write-Host "Checking installed version..." -ForegroundColor Yellow
if (-not (Test-Path $InstalledPath)) {
    Write-Host "WARNING: Installed version not found at:" -ForegroundColor Yellow
    Write-Host "  $InstalledPath" -ForegroundColor Gray
    Write-Host ""
    Write-Host "The add-on has not been copied to ArchiCAD Add-Ons folder yet." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "To install:" -ForegroundColor Cyan
    Write-Host "  1. Create folder: $AddOnsFolder\IfcTesterArchiCAD\" -ForegroundColor Gray
    Write-Host "  2. Copy: $BuildOutputPath" -ForegroundColor Gray
    Write-Host "     To:   $InstalledPath" -ForegroundColor Gray
    Write-Host "  3. Copy WebApp folder: archicad\cmake-build\$Configuration\WebApp" -ForegroundColor Gray
    Write-Host "     To:   $AddOnsFolder\IfcTesterArchiCAD\WebApp" -ForegroundColor Gray
    exit 1
}

$InstalledFile = Get-Item $InstalledPath
Write-Host "✓ Installed version found" -ForegroundColor Green
Write-Host "  Path: $InstalledPath" -ForegroundColor Gray
Write-Host "  Size: $([math]::Round($InstalledFile.Length / 1KB, 2)) KB" -ForegroundColor Gray
Write-Host "  Modified: $($InstalledFile.LastWriteTime)" -ForegroundColor Gray
Write-Host ""

# Compare versions
Write-Host "Comparing versions..." -ForegroundColor Yellow
$BuildTime = $BuildFile.LastWriteTime
$InstalledTime = $InstalledFile.LastWriteTime

if ($BuildTime -gt $InstalledTime) {
    Write-Host "⚠ WARNING: Build is NEWER than installed version!" -ForegroundColor Yellow
    Write-Host "  Build time:    $BuildTime" -ForegroundColor Gray
    Write-Host "  Installed time: $InstalledTime" -ForegroundColor Gray
    Write-Host ""
    Write-Host "The installed version is outdated. Please copy the new build:" -ForegroundColor Yellow
    Write-Host "  Copy-Item '$BuildOutputPath' '$InstalledPath' -Force" -ForegroundColor Gray
    exit 1
} elseif ($BuildTime -lt $InstalledTime) {
    Write-Host "⚠ WARNING: Installed version is NEWER than build!" -ForegroundColor Yellow
    Write-Host "  Build time:    $BuildTime" -ForegroundColor Gray
    Write-Host "  Installed time: $InstalledTime" -ForegroundColor Gray
    Write-Host ""
    Write-Host "This might indicate the build is outdated or was modified after installation." -ForegroundColor Yellow
} else {
    Write-Host "✓ Versions match (same modification time)" -ForegroundColor Green
}

# Check file sizes
if ($BuildFile.Length -ne $InstalledFile.Length) {
    Write-Host "⚠ WARNING: File sizes differ!" -ForegroundColor Yellow
    Write-Host "  Build size:    $($BuildFile.Length) bytes" -ForegroundColor Gray
    Write-Host "  Installed size: $($InstalledFile.Length) bytes" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Files may be different versions. Consider copying the build again." -ForegroundColor Yellow
} else {
    Write-Host "✓ File sizes match" -ForegroundColor Green
}

# Check WebApp folder
$BuildWebAppPath = Join-Path $ArchiCADDir "cmake-build\$Configuration\WebApp"
$InstalledWebAppPath = Join-Path $AddOnsFolder "IfcTesterArchiCAD\WebApp"

Write-Host ""
Write-Host "Checking WebApp folder..." -ForegroundColor Yellow
if (Test-Path $InstalledWebAppPath) {
    Write-Host "✓ WebApp folder found" -ForegroundColor Green
} else {
    Write-Host "⚠ WARNING: WebApp folder not found!" -ForegroundColor Yellow
    Write-Host "  Expected: $InstalledWebAppPath" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Copy the WebApp folder:" -ForegroundColor Yellow
    Write-Host "  Copy-Item '$BuildWebAppPath' '$InstalledWebAppPath' -Recurse -Force" -ForegroundColor Gray
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " Verification Complete" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "To check if the API server is running:" -ForegroundColor Cyan
Write-Host "  1. Open ArchiCAD" -ForegroundColor Gray
Write-Host "  2. Open Window → Palettes → Report" -ForegroundColor Gray
Write-Host "  3. Look for messages starting with 'IfcTester:'" -ForegroundColor Gray
Write-Host "  4. You should see: 'IfcTester ArchiCAD Add-On v1.0.0'" -ForegroundColor Gray
Write-Host "  5. And: 'IfcTester: API server started on http://127.0.0.1:48882'" -ForegroundColor Gray
Write-Host ""
Write-Host "If the server didn't start, check the Report window for error messages." -ForegroundColor Yellow

