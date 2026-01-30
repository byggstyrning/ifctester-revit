# IfcTester ArchiCAD Add-On - Installer Build Script
# Builds the add-on and creates an installer package

param(
    [switch]$SkipBuild,
    [switch]$SkipWebBuild
)

# Load common utilities
. "$PSScriptRoot\..\utils\common.ps1"

$paths = Get-ProjectPaths

Write-Header "IfcTester ArchiCAD - Build Installer"

# Check for Inno Setup
$innoSetup = Find-InnoSetup
if (-not $innoSetup) {
    Write-ErrorMsg "Inno Setup not found!"
    Write-Info "Please install from: https://jrsoftware.org/isinfo.php"
    exit 1
}
Write-Info "Using Inno Setup: $innoSetup"
Write-Host ""

$totalSteps = 4
if ($SkipBuild) { $totalSteps = 2 }
$step = 0

# Step 1: Build web app
if (-not $SkipBuild -and -not $SkipWebBuild) {
    $step++
    Write-Step $step $totalSteps "Building web application..."
    
    if (-not (Build-WebApp -WebDir $paths.Web -Force)) {
        exit 1
    }
    Write-Host ""
}

# Step 2: Build ArchiCAD add-on
if (-not $SkipBuild) {
    $step++
    Write-Step $step $totalSteps "Building ArchiCAD add-on..."
    
    $buildScript = Join-Path $PSScriptRoot "build.ps1"
    & $buildScript -Configuration Release -SkipWebBuild
    
    if ($LASTEXITCODE -ne 0) {
        Write-ErrorMsg "ArchiCAD build failed"
        exit 1
    }
    Write-Host ""
}

# Verify required files
$step++
Write-Step $step $totalSteps "Verifying required files..."

$requiredFiles = @(
    (Join-Path $paths.ArchiCAD "cmake-build\Release\IfcTesterArchiCAD.apx"),
    (Join-Path $paths.Web "dist\index.html")
)

foreach ($file in $requiredFiles) {
    if (-not (Test-Path $file)) {
        Write-ErrorMsg "Required file not found: $file"
        exit 1
    }
}
Write-Success "All required files present"
Write-Host ""

# Ensure dist directory exists
if (-not (Test-Path $paths.Dist)) {
    New-Item -ItemType Directory -Path $paths.Dist -Force | Out-Null
}

# Create LICENSE file if missing
$licenseFile = Join-Path $paths.Root "LICENSE"
if (-not (Test-Path $licenseFile)) {
    Write-Info "Creating LICENSE file..."
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
"@ | Set-Content -Path $licenseFile
}

# Compile installer
$step++
Write-Step $step $totalSteps "Compiling installer..."

$issPath = Join-Path $paths.Installer "IfcTesterArchiCAD.iss"
if (-not (Test-Path $issPath)) {
    Write-ErrorMsg "Inno Setup script not found: $issPath"
    exit 1
}

& $innoSetup $issPath

if ($LASTEXITCODE -ne 0) {
    Write-ErrorMsg "Installer compilation failed"
    exit 1
}

Write-Completed "Installer Build Complete!"

# List output
$outputFiles = Get-ChildItem $paths.Dist -Filter "*ArchiCAD*.exe" | 
    Sort-Object LastWriteTime -Descending | 
    Select-Object -First 3

if ($outputFiles) {
    Write-Host "Output files:" -ForegroundColor Cyan
    foreach ($file in $outputFiles) {
        $size = [math]::Round($file.Length / 1MB, 2)
        Write-Host "  $($file.Name) ($size MB)" -ForegroundColor White
    }
}
Write-Host ""
