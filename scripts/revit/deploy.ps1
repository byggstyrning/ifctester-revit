# IfcTester Revit Plugin - Deploy Script
# Builds and deploys to local Revit Add-ins folder for development

param(
    [ValidateSet("Debug R25", "Debug R26", "Debug R27", "Release R25", "Release R26", "Release R27")]
    [string]$Configuration = "Debug R25",
    
    [switch]$SkipBuild,
    [switch]$SkipWebBuild
)

# Load common utilities
. "$PSScriptRoot\..\utils\common.ps1"

$paths = Get-ProjectPaths

# Determine Revit version from configuration
$revitYear = switch -Regex ($Configuration) {
    "R25" { "2025" }
    "R26" { "2026" }
    "R27" { "2027" }
    default { "2025" }
}
$deployDir = "$env:APPDATA\Autodesk\Revit\Addins\$revitYear\IfcTesterRevit"

Write-Header "IfcTester Revit - Deploy"
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "Deploy to: $deployDir" -ForegroundColor Cyan
Write-Host ""

# Check if Revit is running
if (Test-RevitRunning) {
    Write-Warning "Revit is currently running!"
    Write-Info "Files may be locked. Consider closing Revit first."
    if (-not (Confirm-Continue)) {
        Write-Host "Aborted." -ForegroundColor Yellow
        exit 0
    }
}

$totalSteps = 4
if ($SkipBuild) { $totalSteps -= 2 }
if ($SkipWebBuild -and -not $SkipBuild) { $totalSteps -= 1 }
$step = 0

# Step 1: Build Web App
if (-not $SkipBuild -and -not $SkipWebBuild) {
    $step++
    Write-Step $step $totalSteps "Building web application..."
    
    if (-not (Build-WebApp -WebDir $paths.Web)) {
        exit 1
    }
    Write-Host ""
}

# Step 2: Build Revit Plugin
if (-not $SkipBuild) {
    $step++
    Write-Step $step $totalSteps "Building Revit plugin..."
    
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
    Write-Host ""
}

# Step 3: Copy plugin to deploy directory
$step++
Write-Step $step $totalSteps "Deploying plugin files..."

$publishDir = Join-Path $paths.Revit "bin\$Configuration\publish"
$publishAddinDir = Get-ChildItem -Path $publishDir -Directory -Filter "*addin" -ErrorAction SilentlyContinue | Select-Object -First 1

if (-not $publishAddinDir) {
    Write-ErrorMsg "Could not find publish directory at $publishDir"
    exit 1
}

$sourcePluginDir = Join-Path $publishAddinDir.FullName "IfcTesterRevit"

# Create deploy directory
if (-not (Test-Path $deployDir)) {
    New-Item -ItemType Directory -Path $deployDir -Force | Out-Null
    Write-Info "Created deploy directory"
}

# Copy plugin files
try {
    Copy-Item -Path "$sourcePluginDir\*" -Destination $deployDir -Recurse -Force -ErrorAction Stop
    Write-Success "Plugin files deployed"
}
catch {
    Write-ErrorMsg "Failed to copy plugin files: $($_.Exception.Message)"
    Write-Info "Make sure Revit is closed and try again."
    exit 1
}
Write-Host ""

# Step 4: Copy web app
$step++
Write-Step $step $totalSteps "Deploying web app..."

$webDistDir = Join-Path $paths.Web "dist"
$deployWebDir = Join-Path $deployDir "web"

if (Test-Path $webDistDir) {
    if (Test-Path $deployWebDir) {
        Remove-Item $deployWebDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $deployWebDir -Force | Out-Null
    Copy-Item -Path "$webDistDir\*" -Destination $deployWebDir -Recurse -Force
    Write-Success "Web app deployed"
}
else {
    Write-Warning "Web app dist folder not found, skipping"
}

Write-Completed "Deployment Complete!"

Write-Host "Plugin deployed to:" -ForegroundColor Cyan
Write-Host "  $deployDir" -ForegroundColor White
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Open Revit $revitYear" -ForegroundColor Gray
Write-Host "  2. Look for IfcTester in the Add-Ins tab" -ForegroundColor Gray
Write-Host ""
