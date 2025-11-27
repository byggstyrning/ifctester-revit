# IfcTester ArchiCAD Add-On Build Script
# Builds the ArchiCAD add-on for Windows
#
# Prerequisites:
# - Visual Studio 2022 (with C++ desktop development workload)
# - ArchiCAD API Development Kit
# - Set ARCHICAD_API_DEVKIT environment variable to DevKit path
#
# Usage:
#   .\build-archicad.ps1 [-Configuration <Debug|Release>] [-ArchiCADVersion <25|26|27>]

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [ValidateSet("25", "26", "27")]
    [string]$ArchiCADVersion = "27"
)

$ErrorActionPreference = "Stop"

# Script location
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir
$ArchiCADDir = Join-Path $ProjectRoot "archicad"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " IfcTester ArchiCAD Add-On Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check for ArchiCAD API DevKit
$DevKitPath = $env:ARCHICAD_API_DEVKIT
if (-not $DevKitPath) {
    Write-Host "WARNING: ARCHICAD_API_DEVKIT environment variable not set!" -ForegroundColor Yellow
    Write-Host "Please set it to your ArchiCAD API Development Kit path." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Example paths:" -ForegroundColor Gray
    Write-Host "  C:\Program Files\GRAPHISOFT\API Development Kit $ArchiCADVersion" -ForegroundColor Gray
    Write-Host ""
    
    # Try to find DevKit automatically
    $PossiblePaths = @(
        "C:\Program Files\GRAPHISOFT\API Development Kit $ArchiCADVersion",
        "C:\Program Files (x86)\GRAPHISOFT\API Development Kit $ArchiCADVersion",
        "$env:USERPROFILE\GRAPHISOFT\API Development Kit $ArchiCADVersion"
    )
    
    foreach ($Path in $PossiblePaths) {
        if (Test-Path $Path) {
            $DevKitPath = $Path
            Write-Host "Found DevKit at: $DevKitPath" -ForegroundColor Green
            break
        }
    }
    
    if (-not $DevKitPath) {
        Write-Host "ERROR: Could not find ArchiCAD API Development Kit!" -ForegroundColor Red
        Write-Host "Please download it from: https://archicadapi.graphisoft.com/" -ForegroundColor Yellow
        exit 1
    }
}

Write-Host "Using ArchiCAD API DevKit: $DevKitPath" -ForegroundColor Green
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "ArchiCAD Version: $ArchiCADVersion" -ForegroundColor Cyan
Write-Host ""

# Find MSBuild
$MSBuildPaths = @(
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
)

$MSBuild = $null
foreach ($Path in $MSBuildPaths) {
    if (Test-Path $Path) {
        $MSBuild = $Path
        break
    }
}

if (-not $MSBuild) {
    Write-Host "ERROR: Could not find MSBuild!" -ForegroundColor Red
    Write-Host "Please install Visual Studio 2022 with C++ desktop development workload." -ForegroundColor Yellow
    exit 1
}

Write-Host "Using MSBuild: $MSBuild" -ForegroundColor Green
Write-Host ""

# Set environment variable for the build
$env:ARCHICAD_API_DEVKIT = $DevKitPath

# Build the project
Write-Host "Building IfcTester ArchiCAD Add-On..." -ForegroundColor Cyan
Write-Host ""

$ProjectFile = Join-Path $ArchiCADDir "IfcTesterArchiCAD.vcxproj"

& $MSBuild $ProjectFile `
    /p:Configuration=$Configuration `
    /p:Platform=x64 `
    /p:ArchiCADVersion=$ArchiCADVersion `
    /t:Rebuild `
    /v:minimal

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "ERROR: Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " Build completed successfully!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""

$OutputPath = Join-Path $ArchiCADDir "Build\$Configuration\IfcTesterArchiCAD.apx"
Write-Host "Output: $OutputPath" -ForegroundColor Cyan
Write-Host ""

# Check if output exists
if (Test-Path $OutputPath) {
    $FileInfo = Get-Item $OutputPath
    Write-Host "File size: $([math]::Round($FileInfo.Length / 1KB, 2)) KB" -ForegroundColor Gray
    Write-Host "Modified: $($FileInfo.LastWriteTime)" -ForegroundColor Gray
} else {
    Write-Host "WARNING: Output file not found at expected location!" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "To install the add-on:" -ForegroundColor Cyan
Write-Host "1. Copy IfcTesterArchiCAD.apx to your ArchiCAD Add-Ons folder" -ForegroundColor Gray
Write-Host "2. Restart ArchiCAD" -ForegroundColor Gray
Write-Host "3. Find 'IfcTester Panel' in the 'Audit > IFC Testing' menu" -ForegroundColor Gray
