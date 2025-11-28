# IfcTester ArchiCAD Add-On Copy Script
# Copies the built add-on to the ArchiCAD Add-Ons folder

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
Write-Host " IfcTester ArchiCAD Copy Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Build output location (CMake)
$BuildOutputPath = Join-Path $ArchiCADDir "cmake-build\$Configuration\IfcTesterArchiCAD.apx"
$BuildWebAppPath = Join-Path $ArchiCADDir "cmake-build\$Configuration\WebApp"

# ArchiCAD Add-Ons folder
$AddOnsFolder = "$env:APPDATA\Graphisoft\ArchiCAD $ArchiCADVersion\Add-Ons"
$TargetFolder = Join-Path $AddOnsFolder "IfcTesterArchiCAD"
$TargetPath = Join-Path $TargetFolder "IfcTesterArchiCAD.apx"
$TargetWebAppPath = Join-Path $TargetFolder "WebApp"

# Check if build exists
if (-not (Test-Path $BuildOutputPath)) {
    Write-Host "ERROR: Build output not found at:" -ForegroundColor Red
    Write-Host "  $BuildOutputPath" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Please build the project first." -ForegroundColor Yellow
    exit 1
}

# Check if ArchiCAD is running
$archicadProcesses = Get-Process -Name "Archicad" -ErrorAction SilentlyContinue
if ($archicadProcesses) {
    Write-Host "WARNING: ArchiCAD is currently running!" -ForegroundColor Yellow
    Write-Host "The add-on file may be locked. Please close ArchiCAD first." -ForegroundColor Yellow
    Write-Host ""
    $response = Read-Host "Continue anyway? (y/N)"
    if ($response -ne "y" -and $response -ne "Y") {
        exit 0
    }
}

# Create target folder
Write-Host "Creating target folder..." -ForegroundColor Yellow
if (-not (Test-Path $TargetFolder)) {
    New-Item -ItemType Directory -Path $TargetFolder -Force | Out-Null
    Write-Host "✓ Created: $TargetFolder" -ForegroundColor Green
} else {
    Write-Host "✓ Folder exists: $TargetFolder" -ForegroundColor Green
}

# Copy .apx file
Write-Host ""
Write-Host "Copying add-on file..." -ForegroundColor Yellow
try {
    Copy-Item $BuildOutputPath $TargetPath -Force
    $fileInfo = Get-Item $TargetPath
    Write-Host "✓ Copied: $TargetPath" -ForegroundColor Green
    Write-Host "  Size: $([math]::Round($fileInfo.Length / 1KB, 2)) KB" -ForegroundColor Gray
    Write-Host "  Modified: $($fileInfo.LastWriteTime)" -ForegroundColor Gray
} catch {
    Write-Host "ERROR: Failed to copy add-on file!" -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Make sure ArchiCAD is closed and try again." -ForegroundColor Yellow
    exit 1
}

# Copy WebApp folder
Write-Host ""
Write-Host "Copying WebApp folder..." -ForegroundColor Yellow
if (Test-Path $BuildWebAppPath) {
    try {
        if (Test-Path $TargetWebAppPath) {
            Remove-Item $TargetWebAppPath -Recurse -Force
        }
        Copy-Item $BuildWebAppPath $TargetWebAppPath -Recurse -Force
        Write-Host "✓ Copied: $TargetWebAppPath" -ForegroundColor Green
    } catch {
        Write-Host "ERROR: Failed to copy WebApp folder!" -ForegroundColor Red
        Write-Host "  $($_.Exception.Message)" -ForegroundColor Gray
        exit 1
    }
} else {
    Write-Host "⚠ WARNING: WebApp folder not found at build location!" -ForegroundColor Yellow
    Write-Host "  Expected: $BuildWebAppPath" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Make sure the web app is built first:" -ForegroundColor Yellow
    Write-Host "  cd web" -ForegroundColor Gray
    Write-Host "  npm run build" -ForegroundColor Gray
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " Copy Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Restart ArchiCAD (if it was running)" -ForegroundColor Gray
Write-Host "  2. Open Window → Palettes → Report to verify add-on loaded" -ForegroundColor Gray
Write-Host "  3. Look for: 'IfcTester ArchiCAD Add-On v1.0.0'" -ForegroundColor Gray
Write-Host "  4. Look for: 'IfcTester: API server started on http://127.0.0.1:48882'" -ForegroundColor Gray
Write-Host ""
Write-Host "To verify the build was copied correctly, run:" -ForegroundColor Cyan
Write-Host "  .\scripts\verify-build.ps1 -Configuration $Configuration -ArchiCADVersion $ArchiCADVersion" -ForegroundColor Gray

