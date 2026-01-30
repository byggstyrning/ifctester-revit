# IfcTester Revit Plugin - Build Script
# Builds the Revit plugin for specified configuration

param(
    [ValidateSet("Debug R25", "Debug R26", "Release R25", "Release R26")]
    [string]$Configuration = "Debug R25",
    
    [switch]$SkipWebBuild
)

# Load common utilities
. "$PSScriptRoot\..\utils\common.ps1"

$paths = Get-ProjectPaths

Write-Header "IfcTester Revit - Build"
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host ""

$totalSteps = if ($SkipWebBuild) { 1 } else { 2 }
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

# Step 2: Build Revit Plugin
$step++
Write-Step $step $totalSteps "Building Revit plugin ($Configuration)..."

Push-Location $paths.Revit
try {
    Write-Info "Running dotnet build..."
    dotnet build "IfcTesterRevit.csproj" -c $Configuration --no-incremental
    
    if ($LASTEXITCODE -ne 0) {
        Write-ErrorMsg "Revit plugin build failed"
        exit 1
    }
    
    Write-Success "Revit plugin built successfully"
}
finally {
    Pop-Location
}

Write-Completed "Build Complete!"

# Show output location
$outputDir = Join-Path $paths.Revit "bin\$Configuration"
Write-Host "Output: $outputDir" -ForegroundColor Cyan
Write-Host ""
