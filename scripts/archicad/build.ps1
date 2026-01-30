# IfcTester ArchiCAD Add-On - Build Script
# Builds the ArchiCAD add-on using CMake

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [ValidateSet("29")]
    [string]$ArchiCADVersion = "29",
    
    [switch]$SkipWebBuild
)

# Load common utilities
. "$PSScriptRoot\..\utils\common.ps1"

$paths = Get-ProjectPaths

Write-Header "IfcTester ArchiCAD - Build"
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "ArchiCAD Version: $ArchiCADVersion" -ForegroundColor Cyan
Write-Host ""

# Find DevKit
$devKitPath = Find-ArchiCADDevKit -Version $ArchiCADVersion
if (-not $devKitPath) {
    Write-ErrorMsg "ArchiCAD API Development Kit not found!"
    Write-Info "Set ARCHICAD_API_DEVKIT environment variable or install the DevKit"
    Write-Info "Download from: https://archicadapi.graphisoft.com/"
    exit 1
}
Write-Info "Using DevKit: $devKitPath"
Write-Host ""

$cmakeBuildDir = Join-Path $paths.ArchiCAD "cmake-build"
$totalSteps = 3
if ($SkipWebBuild) { $totalSteps-- }
$step = 0

# Step 1: Build Web App
if (-not $SkipWebBuild) {
    $step++
    Write-Step $step $totalSteps "Building web application..."
    
    if (-not (Build-WebApp -WebDir $paths.Web)) {
        exit 1
    }
    Write-Host ""
}

# Step 2: Configure CMake if needed
$step++
Write-Step $step $totalSteps "Configuring CMake..."

if (-not (Test-Path $cmakeBuildDir)) {
    Write-Info "Creating CMake build directory..."
    New-Item -ItemType Directory -Path $cmakeBuildDir -Force | Out-Null
    
    Push-Location $cmakeBuildDir
    try {
        Write-Info "Running CMake configure..."
        cmake .. -G "Visual Studio 17 2022" -A x64 -T v143 -DAC_API_DEVKIT_DIR="$devKitPath"
        
        if ($LASTEXITCODE -ne 0) {
            Write-ErrorMsg "CMake configure failed"
            exit 1
        }
        Write-Success "CMake configured"
    }
    finally {
        Pop-Location
    }
}
else {
    Write-Success "CMake already configured"
}
Write-Host ""

# Step 3: Build
$step++
Write-Step $step $totalSteps "Building add-on ($Configuration)..."

Push-Location $cmakeBuildDir
try {
    cmake --build . --config $Configuration
    
    if ($LASTEXITCODE -ne 0) {
        Write-ErrorMsg "Build failed"
        exit 1
    }
    
    $outputPath = Join-Path $cmakeBuildDir "$Configuration\IfcTesterArchiCAD.apx"
    if (Test-Path $outputPath) {
        $file = Get-Item $outputPath
        Write-Success "Build completed"
        Write-Info "Output: $outputPath"
        Write-Info "Size: $([math]::Round($file.Length / 1KB, 2)) KB"
    }
    else {
        Write-ErrorMsg "Build output not found"
        exit 1
    }
}
finally {
    Pop-Location
}

Write-Completed "Build Complete!"
