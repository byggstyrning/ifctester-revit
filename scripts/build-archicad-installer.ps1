# IfcTester ArchiCAD Installer Build Script
# Builds the ArchiCAD add-on and creates an installer package
#
# Prerequisites:
# - Inno Setup 6+ (https://jrsoftware.org/isinfo.php)
# - ArchiCAD add-on already built (run build-archicad.ps1 first)
# - Web application already built (run 'npm run build' in web/ first)
#
# Usage:
#   .\build-archicad-installer.ps1 [-SkipBuild] [-SkipWebBuild]

param(
    [switch]$SkipBuild,
    [switch]$SkipWebBuild
)

$ErrorActionPreference = "Stop"

# Script location
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir
$ArchiCADDir = Join-Path $ProjectRoot "archicad"
$WebDir = Join-Path $ProjectRoot "web"
$InstallerDir = Join-Path $ProjectRoot "installer"
$DistDir = Join-Path $ProjectRoot "dist"

Write-Host "================================================" -ForegroundColor Cyan
Write-Host " IfcTester ArchiCAD Installer Build Script" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

# Check for Inno Setup
$InnoSetupPaths = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)

$ISCC = $null
foreach ($Path in $InnoSetupPaths) {
    if (Test-Path $Path) {
        $ISCC = $Path
        break
    }
}

if (-not $ISCC) {
    Write-Host "ERROR: Inno Setup not found!" -ForegroundColor Red
    Write-Host "Please download and install Inno Setup from: https://jrsoftware.org/isinfo.php" -ForegroundColor Yellow
    exit 1
}

Write-Host "Using Inno Setup: $ISCC" -ForegroundColor Green
Write-Host ""

# Build web application if needed
if (-not $SkipWebBuild) {
    Write-Host "Building web application..." -ForegroundColor Cyan
    
    Push-Location $WebDir
    try {
        # Install dependencies if needed
        if (-not (Test-Path "node_modules")) {
            Write-Host "Installing npm dependencies..." -ForegroundColor Gray
            npm install
        }
        
        # Download packages if needed
        $PackagesScript = Join-Path $WebDir "scripts\download-packages.ps1"
        if (Test-Path $PackagesScript) {
            Write-Host "Downloading Python packages..." -ForegroundColor Gray
            & $PackagesScript
        }
        
        # Build
        npm run build
        
        if ($LASTEXITCODE -ne 0) {
            throw "Web build failed!"
        }
    }
    finally {
        Pop-Location
    }
    
    Write-Host "Web application built successfully!" -ForegroundColor Green
    Write-Host ""
}

# Build ArchiCAD add-on if needed
if (-not $SkipBuild) {
    Write-Host "Building ArchiCAD add-on..." -ForegroundColor Cyan
    
    $BuildScript = Join-Path $ScriptDir "build-archicad.ps1"
    & $BuildScript -Configuration Release
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: ArchiCAD add-on build failed!" -ForegroundColor Red
        exit 1
    }
    
    Write-Host ""
}

# Verify required files exist
$RequiredFiles = @(
    (Join-Path $ArchiCADDir "Build\Release\IfcTesterArchiCAD.apx"),
    (Join-Path $WebDir "dist\index.html")
)

Write-Host "Verifying required files..." -ForegroundColor Cyan
foreach ($File in $RequiredFiles) {
    if (-not (Test-Path $File)) {
        Write-Host "ERROR: Required file not found: $File" -ForegroundColor Red
        Write-Host "Please run the build scripts first." -ForegroundColor Yellow
        exit 1
    }
}
Write-Host "All required files present." -ForegroundColor Green
Write-Host ""

# Create dist directory if needed
if (-not (Test-Path $DistDir)) {
    New-Item -ItemType Directory -Path $DistDir | Out-Null
}

# Create LICENSE file if it doesn't exist
$LicenseFile = Join-Path $ProjectRoot "LICENSE"
if (-not (Test-Path $LicenseFile)) {
    Write-Host "Creating LICENSE file..." -ForegroundColor Gray
    @"
MIT License

Copyright (c) 2025 Byggstyrning

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
"@ | Set-Content -Path $LicenseFile
}

# Build installer
Write-Host "Building installer..." -ForegroundColor Cyan
Write-Host ""

$InstallerScript = Join-Path $InstallerDir "IfcTesterArchiCAD.iss"

& $ISCC $InstallerScript

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "ERROR: Installer build failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "================================================" -ForegroundColor Green
Write-Host " Installer build completed successfully!" -ForegroundColor Green
Write-Host "================================================" -ForegroundColor Green
Write-Host ""

# List output files
$OutputFiles = Get-ChildItem $DistDir -Filter "*ArchiCAD*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 5
if ($OutputFiles) {
    Write-Host "Output files in dist/:" -ForegroundColor Cyan
    foreach ($File in $OutputFiles) {
        $Size = [math]::Round($File.Length / 1MB, 2)
        Write-Host "  $($File.Name) ($Size MB)" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "Done!" -ForegroundColor Green
