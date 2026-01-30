# IfcTester ArchiCAD Add-On - Deploy Script
# Builds and deploys to local ArchiCAD Add-Ons folder for development

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [ValidateSet("29")]
    [string]$ArchiCADVersion = "29",
    
    [switch]$SkipBuild,
    [switch]$SkipWebBuild
)

# Load common utilities
. "$PSScriptRoot\..\utils\common.ps1"

$paths = Get-ProjectPaths

$addOnsFolder = "$env:APPDATA\Graphisoft\ArchiCAD $ArchiCADVersion\Add-Ons"
$targetFolder = Join-Path $addOnsFolder "IfcTesterArchiCAD"

Write-Header "IfcTester ArchiCAD - Deploy"
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "ArchiCAD Version: $ArchiCADVersion" -ForegroundColor Cyan
Write-Host "Deploy to: $targetFolder" -ForegroundColor Cyan
Write-Host ""

# Check if ArchiCAD is running
if (Test-ArchiCADRunning) {
    Write-Warning "ArchiCAD is currently running!"
    Write-Info "The add-on file may be locked. Consider closing ArchiCAD first."
    if (-not (Confirm-Continue)) {
        Write-Host "Aborted." -ForegroundColor Yellow
        exit 0
    }
}

$totalSteps = 4
if ($SkipBuild) { $totalSteps = 3 }
$step = 0

# Step 1: Build (if not skipped)
if (-not $SkipBuild) {
    $step++
    Write-Step $step $totalSteps "Building add-on..."
    
    $buildScript = Join-Path $PSScriptRoot "build.ps1"
    
    if ($SkipWebBuild) {
        & $buildScript -Configuration $Configuration -ArchiCADVersion $ArchiCADVersion -SkipWebBuild
    } else {
        & $buildScript -Configuration $Configuration -ArchiCADVersion $ArchiCADVersion
    }
    
    if ($LASTEXITCODE -ne 0) {
        Write-ErrorMsg "Build failed"
        exit 1
    }
    Write-Host ""
}

# Build paths
$cmakeBuildDir = Join-Path $paths.ArchiCAD "cmake-build"
$buildOutputPath = Join-Path $cmakeBuildDir "$Configuration\IfcTesterArchiCAD.apx"
$buildWebAppPath = Join-Path $cmakeBuildDir "$Configuration\WebApp"
$buildPdbPath = Join-Path $cmakeBuildDir "$Configuration\IfcTesterArchiCAD.pdb"

# Check build exists
if (-not (Test-Path $buildOutputPath)) {
    Write-ErrorMsg "Build output not found: $buildOutputPath"
    Write-Info "Run without -SkipBuild to build first"
    exit 1
}

# Step 2: Create target folder
$step++
Write-Step $step $totalSteps "Preparing target folder..."

if (-not (Test-Path $targetFolder)) {
    New-Item -ItemType Directory -Path $targetFolder -Force | Out-Null
    Write-Success "Created: $targetFolder"
}
else {
    Write-Info "Target folder exists"
}
Write-Host ""

# Step 3: Copy .apx file
$step++
Write-Step $step $totalSteps "Copying add-on file..."

$targetApxPath = Join-Path $targetFolder "IfcTesterArchiCAD.apx"
try {
    Copy-Item $buildOutputPath $targetApxPath -Force
    $file = Get-Item $targetApxPath
    Write-Success "Copied add-on ($([math]::Round($file.Length / 1KB, 2)) KB)"
}
catch {
    Write-ErrorMsg "Failed to copy: $($_.Exception.Message)"
    Write-Info "Make sure ArchiCAD is closed"
    exit 1
}

# Copy PDB if exists (for debugging)
if (Test-Path $buildPdbPath) {
    try {
        Copy-Item $buildPdbPath (Join-Path $targetFolder "IfcTesterArchiCAD.pdb") -Force
        Write-Info "Copied debug symbols (PDB)"
    }
    catch {
        Write-Warning "Could not copy PDB file"
    }
}
Write-Host ""

# Step 4: Copy WebApp
$step++
Write-Step $step $totalSteps "Copying WebApp folder..."

$targetWebAppPath = Join-Path $targetFolder "WebApp"

if (Test-Path $buildWebAppPath) {
    try {
        if (Test-Path $targetWebAppPath) {
            Remove-Item $targetWebAppPath -Recurse -Force
        }
        Copy-Item $buildWebAppPath $targetWebAppPath -Recurse -Force
        Write-Success "Copied WebApp folder"
    }
    catch {
        Write-Warning "Failed to copy WebApp: $($_.Exception.Message)"
    }
}
else {
    Write-Warning "WebApp folder not found at build location"
    Write-Info "Expected: $buildWebAppPath"
}

Write-Completed "Deployment Complete!"

Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Restart ArchiCAD (if running)" -ForegroundColor Gray
Write-Host "  2. Open Window > Palettes > Report" -ForegroundColor Gray
Write-Host "  3. Look for: 'IfcTester ArchiCAD Add-On v1.1.0'" -ForegroundColor Gray
Write-Host "  4. Look for: 'IfcTester: API server started on http://127.0.0.1:48882'" -ForegroundColor Gray
Write-Host ""
