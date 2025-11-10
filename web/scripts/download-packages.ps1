# PowerShell script to download Python packages for Pyodide
# This downloads wheels that are needed for offline/local deployment

param(
    [string]$OutputDir = "public\worker\bin"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Downloading Pyodide Packages" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Get script directory and navigate to web root
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$WebDir = Split-Path -Parent $ScriptDir  # Go up from scripts/ to web/
$BinDir = Join-Path $WebDir $OutputDir

# Check for existing wheel files in the old project location
$OldProjectBinDir = "C:\code\pyRevit Extensions\ifctester-next\public\worker\bin"

# Ensure directory exists
if (-not (Test-Path $BinDir)) {
    New-Item -ItemType Directory -Path $BinDir -Force | Out-Null
}

# Copy existing wheel files from old project if they exist
if (Test-Path $OldProjectBinDir) {
    Write-Host "Checking for existing wheel files in old project..." -ForegroundColor Gray
    $oldWheels = Get-ChildItem -Path $OldProjectBinDir -Filter "*.whl" -ErrorAction SilentlyContinue
    foreach ($wheel in $oldWheels) {
        $destPath = Join-Path $BinDir $wheel.Name
        if (-not (Test-Path $destPath)) {
            Write-Host "  Copying $($wheel.Name) from old project..." -ForegroundColor Gray
            Copy-Item -Path $wheel.FullName -Destination $destPath -Force
        }
    }
}

# Get latest ifctester version from PyPI
Write-Host "Checking latest ifctester version..." -ForegroundColor Gray
try {
    $pypiResponse = Invoke-RestMethod -Uri "https://pypi.org/pypi/ifctester/json" -ErrorAction Stop
    # Filter out dev versions and sort properly
    $validVersions = $pypiResponse.releases.PSObject.Properties | Where-Object { $_.Name -notlike "*dev*" -and $_.Name -notlike "*a*" -and $_.Name -notlike "*b*" -and $_.Name -notlike "*rc*" } | Sort-Object { try { [version]$_.Name } catch { [version]"0.0.0" } } -Descending
    $latestVersion = ($validVersions | Select-Object -First 1).Name
    $files = $pypiResponse.releases.$latestVersion
    $wheel = $files | Where-Object { $_.filename -like "*.whl" } | Select-Object -First 1
    
    if (-not $wheel) {
        throw "No wheel file found for ifctester $latestVersion"
    }
    
    $ifctesterUrl = $wheel.url
    $ifctesterFileName = $wheel.filename
    Write-Host "  Found ifctester version $latestVersion" -ForegroundColor Green
} catch {
    Write-Host "  WARNING: Could not fetch version info, using fallback" -ForegroundColor Yellow
    $ifctesterUrl = "https://files.pythonhosted.org/packages/8c/98/98afa5fa347361b8d0f421b1c5059ef960a455f89b8235e6ceed33c0e796/ifctester-0.8.3-py3-none-any.whl"
    $ifctesterFileName = "ifctester-0.8.3-py3-none-any.whl"
}

# Get latest odfpy version from PyPI
Write-Host "Checking latest odfpy version..." -ForegroundColor Gray
try {
    $odfpyResponse = Invoke-RestMethod -Uri "https://pypi.org/pypi/odfpy/json" -ErrorAction Stop
    # Filter to only numeric versions (x.y.z format)
    $validVersions = $odfpyResponse.releases.PSObject.Properties.Name | Where-Object { $_ -match '^\d+\.\d+\.\d+$' } | Sort-Object { [version]$_ } -Descending
    $odfpyVersion = $validVersions[0]
    $odfpyFiles = $odfpyResponse.releases.$odfpyVersion
    $odfpyWheel = $odfpyFiles | Where-Object { $_.filename -like "*.whl" } | Select-Object -First 1
    
    if ($odfpyWheel) {
        $odfpyUrl = $odfpyWheel.url
        $odfpyFileName = $odfpyWheel.filename
        Write-Host "  Found odfpy version $odfpyVersion" -ForegroundColor Green
    } else {
        throw "No wheel file found for odfpy $odfpyVersion"
    }
} catch {
    Write-Host "  WARNING: Could not fetch odfpy version info, using fallback" -ForegroundColor Yellow
    # Use a known working URL pattern - odfpy wheels are typically universal
    $odfpyUrl = "https://files.pythonhosted.org/packages/py2.py3/o/odfpy/odfpy-1.4.1-py2.py3-none-any.whl"
    $odfpyFileName = "odfpy-1.4.1-py2.py3-none-any.whl"
}

# Packages to download
$packages = @(
    @{
        Name = "ifctester"
        Url = $ifctesterUrl
        FileName = $ifctesterFileName
        Optional = $false
    },
    @{
        Name = "ifcopenshell"
        # Try multiple possible URLs - ifcopenshell wheels may be in different locations
        Urls = @(
            "https://github.com/IfcOpenShell/IfcOpenShell/releases/download/v0.8.3/ifcopenshell-0.8.3+bb329af-cp313-cp313-emscripten_4_0_9_wasm32.whl",
            "https://github.com/IfcOpenShell/IfcOpenShell/releases/download/v0.8.3/ifcopenshell-0.8.3-cp313-cp313-emscripten_wasm32.whl",
            "https://github.com/IfcOpenShell/IfcOpenShell/releases/download/v0.8.3/ifcopenshell-0.8.3-emscripten_wasm32.whl"
        )
        FileName = "ifcopenshell-0.8.3+bb329af-cp313-cp313-emscripten_4_0_9_wasm32.whl"
        Optional = $true  # Mark as optional since it may not be available
    },
    @{
        Name = "odfpy"
        # Accept both 1.4.1 and 1.4.2 versions
        FileNames = @("odfpy-1.4.2-py2.py3-none-any.whl", "odfpy-1.4.1-py2.py3-none-any.whl", $odfpyFileName)
        Url = $odfpyUrl
        FileName = "odfpy-1.4.2-py2.py3-none-any.whl"  # Prefer 1.4.2 if available
        Optional = $false
    }
)

foreach ($package in $packages) {
    # Check if any acceptable filename exists
    $fileNames = if ($package.FileNames) { $package.FileNames } else { @($package.FileName) }
    $existingFile = $null
    foreach ($fileName in $fileNames) {
        $checkPath = Join-Path $BinDir $fileName
        if (Test-Path $checkPath) {
            $existingFile = $checkPath
            break
        }
    }
    
    if ($existingFile) {
        Write-Host "✓ $($package.Name) already exists ($(Split-Path $existingFile -Leaf)), skipping..." -ForegroundColor Green
        continue
    }
    
    $filePath = Join-Path $BinDir $package.FileName
    
    Write-Host "Downloading $($package.Name)..." -ForegroundColor Yellow
    
    # Handle packages with multiple URL options
    $urls = if ($package.Urls) { $package.Urls } else { @($package.Url) }
    $downloaded = $false
    
    foreach ($url in $urls) {
        try {
            Write-Host "  Trying: $url" -ForegroundColor Gray
            $ProgressPreference = 'SilentlyContinue'
            Invoke-WebRequest -Uri $url -OutFile $filePath -UseBasicParsing -ErrorAction Stop
            
            if (Test-Path $filePath) {
                $fileSize = (Get-Item $filePath).Length / 1KB
                Write-Host "  ✓ Downloaded $($package.Name) ($([math]::Round($fileSize, 2)) KB)" -ForegroundColor Green
                $downloaded = $true
                break
            }
        } catch {
            Write-Host "  ✗ Failed: $($_.Exception.Message)" -ForegroundColor DarkYellow
            if (Test-Path $filePath) {
                Remove-Item $filePath -Force -ErrorAction SilentlyContinue
            }
        }
    }
    
    if (-not $downloaded) {
        if ($package.Optional) {
            Write-Host "  ⚠ $($package.Name) is optional and could not be downloaded. It may need to be provided manually." -ForegroundColor Yellow
            Write-Host "  Expected filename: $($package.FileName)" -ForegroundColor Gray
        } else {
            Write-Host "  ✗ Failed to download $($package.Name) from all available URLs" -ForegroundColor Red
        }
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Download Complete" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

