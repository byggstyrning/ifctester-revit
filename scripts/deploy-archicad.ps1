# Build and Deploy Script for IfcTester Archicad Add-in
# This script builds both the web app and Archicad add-in, then deploys them together

param(
    [string]$Configuration = "Debug AC28",
    [switch]$SkipWebBuild = $false
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "IfcTester Archicad - Build and Deploy Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Get script directory
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$WebDir = Join-Path $RootDir "web"
$ArchicadDir = Join-Path $RootDir "archicad"
$WebDistDir = Join-Path $WebDir "dist"
$ArchicadBinDir = Join-Path $ArchicadDir "bin\$Configuration"
$DeployDir = "$env:APPDATA\GRAPHISOFT\ARCHICAD 28 Add-Ons\IfcTesterArchicad"
$BuiltDir = Join-Path $ArchicadDir "built"

# Extract Archicad version from configuration
$ArchicadVersion = if ($Configuration -match "AC(\d+)") { $matches[1] } else { "28" }
$DeployDir = "$env:APPDATA\GRAPHISOFT\ARCHICAD $ArchicadVersion Add-Ons\IfcTesterArchicad"

# Step 0: Download Python packages for Pyodide
Write-Host "[0/6] Downloading Python packages..." -ForegroundColor Yellow

$DownloadScript = Join-Path $WebDir "scripts\download-packages.ps1"
if (Test-Path $DownloadScript) {
    Push-Location $WebDir
    & $DownloadScript
    Pop-Location
    Write-Host "  ✓ Python packages downloaded" -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "  WARNING: Download script not found, packages may need internet access" -ForegroundColor Yellow
    Write-Host ""
}

# Step 1: Build Web Application
if (-not $SkipWebBuild) {
    Write-Host "[1/6] Building web application..." -ForegroundColor Yellow
    
    if (-not (Test-Path $WebDir)) {
        Write-Host "ERROR: Web directory not found at $WebDir" -ForegroundColor Red
        exit 1
    }
    
    Push-Location $WebDir
    
    if (-not (Test-Path "node_modules")) {
        Write-Host "  Installing npm dependencies..." -ForegroundColor Gray
        npm install
        if ($LASTEXITCODE -ne 0) {
            Write-Host "ERROR: npm install failed" -ForegroundColor Red
            Pop-Location
            exit 1
        }
    }
    
    Write-Host "  Building web app..." -ForegroundColor Gray
    npm run build
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Web build failed" -ForegroundColor Red
        Pop-Location
        exit 1
    }
    
    Pop-Location
    Write-Host "  ✓ Web application built" -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "[1/6] Skipping web build (using existing build)" -ForegroundColor Yellow
    Write-Host ""
}

# Step 2: Build Archicad Add-in
Write-Host "[2/6] Building Archicad add-in..." -ForegroundColor Yellow

if (-not (Test-Path $ArchicadDir)) {
    Write-Host "ERROR: Archicad directory not found at $ArchicadDir" -ForegroundColor Red
    exit 1
}

Push-Location $ArchicadDir

Write-Host "  Building with configuration: $Configuration" -ForegroundColor Gray
dotnet build IfcTesterArchicad.csproj -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Archicad build failed" -ForegroundColor Red
    Pop-Location
    exit 1
}

Pop-Location
Write-Host "  ✓ Archicad add-in built" -ForegroundColor Green
Write-Host ""

# Step 3: Create deployment directory structure
Write-Host "[3/6] Creating deployment directory..." -ForegroundColor Yellow

if (-not (Test-Path $DeployDir)) {
    New-Item -ItemType Directory -Path $DeployDir -Force | Out-Null
    Write-Host "  Created directory: $DeployDir" -ForegroundColor Gray
}

# Also create the parent Add-Ons directory if it doesn't exist
$AddOnsDir = Split-Path -Parent $DeployDir
if (-not (Test-Path $AddOnsDir)) {
    New-Item -ItemType Directory -Path $AddOnsDir -Force | Out-Null
    Write-Host "  Created Add-Ons directory: $AddOnsDir" -ForegroundColor Gray
}

Write-Host "  ✓ Deployment directory ready" -ForegroundColor Green
Write-Host ""

# Step 4: Copy web app to add-in directory
Write-Host "[4/6] Copying web application..." -ForegroundColor Yellow

$WebTargetDir = Join-Path $DeployDir "web"
if (Test-Path $WebTargetDir) {
    Remove-Item $WebTargetDir -Recurse -Force
}

if (-not (Test-Path $WebDistDir)) {
    Write-Host "ERROR: Web dist directory not found at $WebDistDir" -ForegroundColor Red
    Write-Host "  Run 'npm run build' in the web directory first" -ForegroundColor Yellow
    exit 1
}

Copy-Item -Path $WebDistDir -Destination $WebTargetDir -Recurse -Force
Write-Host "  ✓ Web application copied" -ForegroundColor Green
Write-Host ""

# Step 5: Copy add-in files
Write-Host "[5/6] Copying add-in files..." -ForegroundColor Yellow

# Copy DLL
$DllSource = Join-Path $ArchicadBinDir "IfcTesterArchicad.dll"
if (Test-Path $DllSource) {
    Copy-Item -Path $DllSource -Destination $DeployDir -Force
    Write-Host "  ✓ DLL copied" -ForegroundColor Green
} else {
    Write-Host "  WARNING: DLL not found at $DllSource" -ForegroundColor Yellow
}

# Copy PDB (for debugging)
$PdbSource = Join-Path $ArchicadBinDir "IfcTesterArchicad.pdb"
if (Test-Path $PdbSource) {
    Copy-Item -Path $PdbSource -Destination $DeployDir -Force
    Write-Host "  ✓ PDB copied" -ForegroundColor Green
}

# Copy APX manifest to Add-Ons directory (not in subfolder)
$ApxSource = Join-Path $ArchicadDir "IfcTesterArchicad.apx"
if (Test-Path $ApxSource) {
    Copy-Item -Path $ApxSource -Destination $AddOnsDir -Force
    Write-Host "  ✓ APX manifest copied" -ForegroundColor Green
} else {
    Write-Host "  WARNING: APX manifest not found at $ApxSource" -ForegroundColor Yellow
}

# Copy resources (icons, etc.)
$ResourcesSource = Join-Path $ArchicadDir "Resources"
if (Test-Path $ResourcesSource) {
    $ResourcesTarget = Join-Path $DeployDir "Resources"
    if (Test-Path $ResourcesTarget) {
        Remove-Item $ResourcesTarget -Recurse -Force
    }
    Copy-Item -Path $ResourcesSource -Destination $ResourcesTarget -Recurse -Force
    Write-Host "  ✓ Resources copied" -ForegroundColor Green
}

Write-Host ""

# Step 6: Create built package
Write-Host "[6/6] Creating built package..." -ForegroundColor Yellow

if (Test-Path $BuiltDir) {
    Remove-Item $BuiltDir -Recurse -Force
}
New-Item -ItemType Directory -Path $BuiltDir -Force | Out-Null

# Copy everything to built directory
Copy-Item -Path $DeployDir -Destination (Join-Path $BuiltDir "IfcTesterArchicad") -Recurse -Force
Copy-Item -Path (Join-Path $AddOnsDir "IfcTesterArchicad.apx") -Destination $BuiltDir -Force

Write-Host "  ✓ Built package created at: $BuiltDir" -ForegroundColor Green
Write-Host ""

# Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Deployment Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Add-in deployed to: $DeployDir" -ForegroundColor White
Write-Host "APX manifest at: $AddOnsDir\IfcTesterArchicad.apx" -ForegroundColor White
Write-Host "Built package at: $BuiltDir" -ForegroundColor White
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Restart Archicad to load the add-in" -ForegroundColor White
Write-Host "  2. Access IfcTester from the menu or palette" -ForegroundColor White
Write-Host ""
