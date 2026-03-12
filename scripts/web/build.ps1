# IfcTester Web App - Build Script
# Builds the web application for production

param(
    [switch]$Force,
    [switch]$SkipPackages
)

# Load common utilities
. "$PSScriptRoot\..\utils\common.ps1"

$paths = Get-ProjectPaths

Write-Header "IfcTester Web App - Build"

$totalSteps = 3
if ($SkipPackages) { $totalSteps-- }
$step = 0

# Step 1: Install dependencies
$step++
Write-Step $step $totalSteps "Checking dependencies..."

Push-Location $paths.Web
try {
    if (-not (Test-Path "node_modules")) {
        Write-Info "Installing npm dependencies..."
        npm install
        if ($LASTEXITCODE -ne 0) {
            Write-ErrorMsg "npm install failed"
            exit 1
        }
        Write-Success "Dependencies installed"
    }
    else {
        Write-Success "Dependencies already installed"
    }
}
finally {
    Pop-Location
}
Write-Host ""

# Step 2: Download Python packages
if (-not $SkipPackages) {
    $step++
    Write-Step $step $totalSteps "Downloading Python packages..."
    
    $downloadScript = Join-Path $paths.Web "scripts\download-packages.ps1"
    if (Test-Path $downloadScript) {
        Push-Location $paths.Web
        try {
            & $downloadScript
            Write-Success "Python packages downloaded"
        }
        finally {
            Pop-Location
        }
    }
    else {
        Write-Warning "Download script not found, packages may need internet access at runtime"
    }
    
    # Verify critical wheel files exist before building
    $binDir = Join-Path $paths.Web "public\worker\bin"
    $requiredWheels = @(
        "ifcopenshell-0.8.3+34a1bc6-cp313-cp313-emscripten_4_0_9_wasm32.whl",
        "odfpy-1.4.2-py2.py3-none-any.whl"
    )
    $missingWheels = @()
    foreach ($wheel in $requiredWheels) {
        $wheelPath = Join-Path $binDir $wheel
        if (-not (Test-Path $wheelPath)) {
            $missingWheels += $wheel
        }
    }
    if ($missingWheels.Count -gt 0) {
        Write-ErrorMsg "Required wheel files missing from $binDir :"
        foreach ($w in $missingWheels) {
            Write-ErrorMsg "  - $w"
        }
        Write-ErrorMsg "Run the download script manually or check your internet connection."
        exit 1
    }
    Write-Success "All required wheel files verified"
    Write-Host ""
}

# Step 3: Build
$step++
Write-Step $step $totalSteps "Building web app..."

Push-Location $paths.Web
try {
    # Run build using cmd to avoid PowerShell npm wrapper issues with stderr
    Write-Info "Running npm run build..."
    cmd /c "npm run build 2>&1" | ForEach-Object {
        if ($_ -match 'error|Error|ERROR|failed|Failed|FAILED|built|transformed|rendering|computing|vite') {
            Write-Host "    $_" -ForegroundColor Gray
        }
    }
    
    # Check if dist was created (more reliable than exit code with warnings)
    $distPath = Join-Path $paths.Web "dist"
    $indexPath = Join-Path $distPath "index.html"
    
    if (-not (Test-Path $indexPath)) {
        Write-ErrorMsg "Build failed - dist/index.html not found"
        exit 1
    }
    
    $fileCount = (Get-ChildItem $distPath -Recurse -File).Count
    $totalSize = (Get-ChildItem $distPath -Recurse -File | Measure-Object -Property Length -Sum).Sum
    Write-Success "Build completed"
    Write-Info "Output: $distPath"
    Write-Info "Files: $fileCount"
    Write-Info "Total size: $([math]::Round($totalSize / 1MB, 2)) MB"
}
finally {
    Pop-Location
}

Write-Completed "Web App Build Complete!"
