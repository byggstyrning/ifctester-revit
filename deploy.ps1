# Build and Deploy Script for IfcTester Revit Plugin
# This script builds both the web app and Revit plugin, then deploys them together

param(
    [string]$Configuration = "Debug R25",
    [switch]$SkipWebBuild = $false
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "IfcTester Revit - Build and Deploy Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Get script directory
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = $ScriptDir
$WebDir = Join-Path $RootDir "web"
$RevitDir = Join-Path $RootDir "revit"
$WebDistDir = Join-Path $WebDir "dist"
$RevitBinDir = Join-Path $RevitDir "bin\$Configuration"
$RevitPublishDir = Join-Path $RevitBinDir "publish"
$DeployDir = "$env:APPDATA\Autodesk\Revit\Addins\2025\IfcTesterRevit"
$BuiltDir = Join-Path $RevitDir "built"

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
    
    # Check if node_modules exists
    if (-not (Test-Path "node_modules")) {
        Write-Host "  Installing npm dependencies..." -ForegroundColor Gray
        npm install
        if ($LASTEXITCODE -ne 0) {
            Write-Host "ERROR: npm install failed" -ForegroundColor Red
            Pop-Location
            exit 1
        }
    }
    
    # Build the web app (warnings are suppressed via vite.config.js)
    Write-Host "  Running npm run build..." -ForegroundColor Gray
    # Redirect stderr to filter out warnings, keep stdout for errors
    npm run build 2>&1 | ForEach-Object {
        # Filter out vite-plugin-svelte warnings, keep everything else
        if ($_ -notmatch '\[vite-plugin-svelte\].*a11y_' -and $_ -notmatch '\[vite-plugin-svelte\].*css_unused') {
            # Only output if it's an error, success message, or important info
            if ($_ -match 'error|Error|ERROR|failed|Failed|FAILED|✓|built|transformed|rendering|computing') {
                $_
            }
        }
    } | Out-String | Write-Host -NoNewline
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Web app build failed" -ForegroundColor Red
        Pop-Location
        exit 1
    }
    
    Pop-Location
    
    if (-not (Test-Path $WebDistDir)) {
        Write-Host "ERROR: Web app dist folder not found after build" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "  ✓ Web app built successfully" -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "[1/6] Skipping web build (--SkipWebBuild flag set)" -ForegroundColor Yellow
    Write-Host ""
}

# Step 2: Build Revit Plugin
Write-Host "[2/6] Building Revit plugin..." -ForegroundColor Yellow

Push-Location $RevitDir

Write-Host "  Running dotnet build..." -ForegroundColor Gray
dotnet build "IfcTesterRevit.csproj" -c $Configuration --no-incremental
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Revit plugin build failed" -ForegroundColor Red
    Pop-Location
    exit 1
}

Pop-Location

Write-Host "  ✓ Revit plugin built successfully" -ForegroundColor Green
Write-Host ""

# Step 3: Copy web app to plugin publish directory
Write-Host "[3/6] Copying web app to plugin directory..." -ForegroundColor Yellow

# Find the actual publish directory (it may have a different name based on configuration)
$PublishAddinDir = Get-ChildItem -Path $RevitPublishDir -Directory -Filter "*addin" | Select-Object -First 1
if ($PublishAddinDir) {
    $PluginWebDir = Join-Path $PublishAddinDir.FullName "IfcTesterRevit\web"
    if (Test-Path $PluginWebDir) {
        Remove-Item $PluginWebDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $PluginWebDir -Force | Out-Null
    
    Write-Host "  Copying files from $WebDistDir to $PluginWebDir..." -ForegroundColor Gray
    Copy-Item -Path "$WebDistDir\*" -Destination $PluginWebDir -Recurse -Force
    
    Write-Host "  ✓ Web app copied to plugin directory" -ForegroundColor Green
} else {
    Write-Host "  WARNING: Could not find publish directory" -ForegroundColor Yellow
}

Write-Host ""

# Step 4: Copy web app to deployment directory
Write-Host "[4/6] Copying web app to deployment directory..." -ForegroundColor Yellow

if (Test-Path $DeployDir) {
    $DeployedWebDir = Join-Path $DeployDir "web"
    
    # Copy web app to deployment directory
    if (Test-Path $DeployedWebDir) {
        Remove-Item $DeployedWebDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $DeployedWebDir -Force | Out-Null
    
    Write-Host "  Copying files from $WebDistDir to $DeployedWebDir..." -ForegroundColor Gray
    Copy-Item -Path "$WebDistDir\*" -Destination $DeployedWebDir -Recurse -Force
    
    Write-Host "  ✓ Web app copied to deployment directory" -ForegroundColor Green
} else {
    Write-Host "WARNING: Deployment directory not found" -ForegroundColor Yellow
}

Write-Host ""

# Step 5: Copy everything to built folder for distribution
Write-Host "[5/6] Creating built folder for distribution..." -ForegroundColor Yellow

if (Test-Path $BuiltDir) {
    Remove-Item $BuiltDir -Recurse -Force
}
New-Item -ItemType Directory -Path $BuiltDir -Force | Out-Null

# Copy plugin files
$BuiltPluginDir = Join-Path $BuiltDir "IfcTesterRevit"
if ($PublishAddinDir) {
    Write-Host "  Copying plugin files from $($PublishAddinDir.FullName)..." -ForegroundColor Gray
    Copy-Item -Path "$($PublishAddinDir.FullName)\*" -Destination $BuiltPluginDir -Recurse -Force
    Write-Host "  ✓ Plugin files copied to built folder" -ForegroundColor Green
} else {
    Write-Host "  WARNING: Could not find publish directory to copy" -ForegroundColor Yellow
}

# Copy web app to built folder
$BuiltWebDir = Join-Path $BuiltPluginDir "web"
New-Item -ItemType Directory -Path $BuiltWebDir -Force | Out-Null
Write-Host "  Copying web app to built folder..." -ForegroundColor Gray
Copy-Item -Path "$WebDistDir\*" -Destination $BuiltWebDir -Recurse -Force
Write-Host "  ✓ Web app copied to built folder" -ForegroundColor Green

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Deployment Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Plugin deployed to: $DeployDir" -ForegroundColor White
Write-Host "Web app deployed to: $DeployedWebDir" -ForegroundColor White
Write-Host "Distribution package ready at: $BuiltDir" -ForegroundColor White
Write-Host ""
Write-Host "You can now launch Revit 2025 and use the plugin." -ForegroundColor White
Write-Host "The 'built' folder contains all files needed for distribution." -ForegroundColor White
Write-Host ""

