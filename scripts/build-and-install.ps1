# IfcTester ArchiCAD Add-On Build and Install Script
# Builds the add-on, copies it to ArchiCAD Add-Ons folder, and verifies installation

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [ValidateSet("25", "26", "27", "29")]
    [string]$ArchiCADVersion = "27",
    
    [switch]$SkipBuild,
    [switch]$SkipCopy,
    [switch]$SkipVerify
)

$ErrorActionPreference = "Stop"

# Script location
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir
$ArchiCADDir = Join-Path $ProjectRoot "archicad"
$WebDir = Join-Path $ProjectRoot "web"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " IfcTester ArchiCAD Build & Install" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "ArchiCAD Version: $ArchiCADVersion" -ForegroundColor Cyan
Write-Host ""

# ============================================================================
# Step 1: Build Web App (if needed)
# ============================================================================

if (-not $SkipBuild) {
    Write-Host "Step 1: Building Web App..." -ForegroundColor Yellow
    Write-Host ""
    
    $WebDistPath = Join-Path $WebDir "dist"
    $WebIndexPath = Join-Path $WebDistPath "index.html"
    
    if (-not (Test-Path $WebIndexPath) -or (Get-Item $WebIndexPath).LastWriteTime -lt (Get-Date).AddMinutes(-5)) {
        Write-Host "Web app dist folder missing or outdated. Building..." -ForegroundColor Gray
        
        Push-Location $WebDir
        try {
            if (-not (Test-Path "node_modules")) {
                Write-Host "Installing npm dependencies..." -ForegroundColor Gray
                npm install
                if ($LASTEXITCODE -ne 0) {
                    Write-Host "ERROR: npm install failed!" -ForegroundColor Red
                    exit 1
                }
            }
            
            Write-Host "Building web app..." -ForegroundColor Gray
            npm run build
            if ($LASTEXITCODE -ne 0) {
                Write-Host "ERROR: Web app build failed!" -ForegroundColor Red
                exit 1
            }
            Write-Host "✓ Web app built successfully" -ForegroundColor Green
        } finally {
            Pop-Location
        }
    } else {
        Write-Host "✓ Web app already built (dist folder exists)" -ForegroundColor Green
    }
    Write-Host ""
}

# ============================================================================
# Step 2: Build ArchiCAD Add-On
# ============================================================================

if (-not $SkipBuild) {
    Write-Host "Step 2: Building ArchiCAD Add-On..." -ForegroundColor Yellow
    Write-Host ""
    
    $CMakeBuildDir = Join-Path $ArchiCADDir "cmake-build"
    
    # Check if CMake build directory exists
    if (-not (Test-Path $CMakeBuildDir)) {
        Write-Host "CMake build directory not found. Running CMake configure..." -ForegroundColor Yellow
        
        # Try to find DevKit automatically
        $DevKitPath = $env:ARCHICAD_API_DEVKIT
        if (-not $DevKitPath) {
            $PossiblePaths = @(
                "C:\Program Files\GRAPHISOFT\API Development Kit $ArchiCADVersion",
                "C:\Program Files (x86)\GRAPHISOFT\API Development Kit $ArchiCADVersion",
                "$env:USERPROFILE\GRAPHISOFT\API Development Kit $ArchiCADVersion",
                "C:\code\archicad-api\API.Development.Kit.WIN.$ArchiCADVersion.3100",
                "C:\code\archicad-api\API.Development.Kit.WIN.$ArchiCADVersion.6003"
            )
            
            foreach ($Path in $PossiblePaths) {
                if (Test-Path $Path) {
                    $DevKitPath = $Path
                    Write-Host "Found DevKit at: $DevKitPath" -ForegroundColor Green
                    break
                }
            }
        }
        
        if (-not $DevKitPath) {
            Write-Host "ERROR: Could not find ArchiCAD API Development Kit!" -ForegroundColor Red
            Write-Host "Please set ARCHICAD_API_DEVKIT environment variable or install the DevKit." -ForegroundColor Yellow
            exit 1
        }
        
        Push-Location $ArchiCADDir
        try {
            New-Item -ItemType Directory -Path $CMakeBuildDir -Force | Out-Null
            Push-Location $CMakeBuildDir
            
            Write-Host "Running CMake configure..." -ForegroundColor Gray
            cmake .. -G "Visual Studio 17 2022" -A x64 -T v142 -DAC_API_DEVKIT_DIR="$DevKitPath"
            
            if ($LASTEXITCODE -ne 0) {
                Write-Host "ERROR: CMake configure failed!" -ForegroundColor Red
                exit 1
            }
            
            Pop-Location
        } finally {
            Pop-Location
        }
    }
    
    # Build the add-on
    Push-Location $CMakeBuildDir
    try {
        Write-Host "Building add-on (Configuration: $Configuration)..." -ForegroundColor Gray
        cmake --build . --config $Configuration
        
        if ($LASTEXITCODE -ne 0) {
            Write-Host "ERROR: Build failed!" -ForegroundColor Red
            exit 1
        }
        
        $BuildOutputPath = Join-Path $CMakeBuildDir "$Configuration\IfcTesterArchiCAD.apx"
        if (Test-Path $BuildOutputPath) {
            $BuildFile = Get-Item $BuildOutputPath
            Write-Host "✓ Build completed successfully" -ForegroundColor Green
            Write-Host "  Output: $BuildOutputPath" -ForegroundColor Gray
            Write-Host "  Size: $([math]::Round($BuildFile.Length / 1KB, 2)) KB" -ForegroundColor Gray
            Write-Host "  Modified: $($BuildFile.LastWriteTime)" -ForegroundColor Gray
        } else {
            Write-Host "ERROR: Build output not found!" -ForegroundColor Red
            exit 1
        }
    } finally {
        Pop-Location
    }
    Write-Host ""
}

# ============================================================================
# Step 3: Copy to ArchiCAD Add-Ons Folder
# ============================================================================

if (-not $SkipCopy) {
    Write-Host "Step 3: Copying to ArchiCAD Add-Ons folder..." -ForegroundColor Yellow
    Write-Host ""
    
    # Build paths
    $BuildOutputPath = Join-Path $ArchiCADDir "cmake-build\$Configuration\IfcTesterArchiCAD.apx"
    $BuildWebAppPath = Join-Path $ArchiCADDir "cmake-build\$Configuration\WebApp"
    
    # ArchiCAD Add-Ons folder
    $AddOnsFolder = "$env:APPDATA\Graphisoft\ArchiCAD $ArchiCADVersion\Add-Ons"
    $TargetFolder = Join-Path $AddOnsFolder "IfcTesterArchiCAD"
    $TargetPath = Join-Path $TargetFolder "IfcTesterArchiCAD.apx"
    $TargetWebAppPath = Join-Path $TargetFolder "WebApp"
    
    # Check if build exists
    if (-not (Test-Path $BuildOutputPath)) {
        Write-Host "ERROR: Build output not found at:" -ForegroundColor Red
        Write-Host "  $BuildOutputPath" -ForegroundColor Gray
        Write-Host ""
        Write-Host "Please build the project first (remove -SkipBuild flag)." -ForegroundColor Yellow
        exit 1
    }
    
    # Check if ArchiCAD is running
    $archicadProcesses = Get-Process -Name "Archicad" -ErrorAction SilentlyContinue
    if ($archicadProcesses) {
        Write-Host "WARNING: ArchiCAD is currently running!" -ForegroundColor Yellow
        Write-Host "The add-on file may be locked. Please close ArchiCAD first." -ForegroundColor Yellow
        Write-Host ""
        $response = Read-Host "Continue anyway? (y/N)"
        if ($response -ne "y" -and $response -ne "Y") {
            Write-Host "Aborted by user." -ForegroundColor Yellow
            exit 0
        }
    }
    
    # Create target folder
    if (-not (Test-Path $TargetFolder)) {
        New-Item -ItemType Directory -Path $TargetFolder -Force | Out-Null
        Write-Host "✓ Created target folder: $TargetFolder" -ForegroundColor Green
    }
    
    # Copy .apx file
    try {
        Copy-Item $BuildOutputPath $TargetPath -Force
        $fileInfo = Get-Item $TargetPath
        Write-Host "✓ Copied add-on file" -ForegroundColor Green
        Write-Host "  To: $TargetPath" -ForegroundColor Gray
        Write-Host "  Size: $([math]::Round($fileInfo.Length / 1KB, 2)) KB" -ForegroundColor Gray
    } catch {
        Write-Host "ERROR: Failed to copy add-on file!" -ForegroundColor Red
        Write-Host "  $($_.Exception.Message)" -ForegroundColor Gray
        Write-Host ""
        Write-Host "Make sure ArchiCAD is closed and try again." -ForegroundColor Yellow
        exit 1
    }
    
    # Copy WebApp folder
    if (Test-Path $BuildWebAppPath) {
        try {
            if (Test-Path $TargetWebAppPath) {
                Remove-Item $TargetWebAppPath -Recurse -Force
            }
            Copy-Item $BuildWebAppPath $TargetWebAppPath -Recurse -Force
            Write-Host "✓ Copied WebApp folder" -ForegroundColor Green
            Write-Host "  To: $TargetWebAppPath" -ForegroundColor Gray
        } catch {
            Write-Host "WARNING: Failed to copy WebApp folder!" -ForegroundColor Yellow
            Write-Host "  $($_.Exception.Message)" -ForegroundColor Gray
        }
    } else {
        Write-Host "⚠ WARNING: WebApp folder not found at build location!" -ForegroundColor Yellow
        Write-Host "  Expected: $BuildWebAppPath" -ForegroundColor Gray
    }
    Write-Host ""
}

# ============================================================================
# Step 4: Verify Installation
# ============================================================================

if (-not $SkipVerify) {
    Write-Host "Step 4: Verifying installation..." -ForegroundColor Yellow
    Write-Host ""
    
    $BuildOutputPath = Join-Path $ArchiCADDir "cmake-build\$Configuration\IfcTesterArchiCAD.apx"
    $AddOnsFolder = "$env:APPDATA\Graphisoft\ArchiCAD $ArchiCADVersion\Add-Ons"
    $InstalledPath = Join-Path $AddOnsFolder "IfcTesterArchiCAD\IfcTesterArchiCAD.apx"
    
    if (-not (Test-Path $BuildOutputPath)) {
        Write-Host "⚠ Build output not found, skipping verification" -ForegroundColor Yellow
    } elseif (-not (Test-Path $InstalledPath)) {
        Write-Host "⚠ Installed version not found, skipping verification" -ForegroundColor Yellow
    } else {
        $BuildFile = Get-Item $BuildOutputPath
        $InstalledFile = Get-Item $InstalledPath
        
        $BuildTime = $BuildFile.LastWriteTime
        $InstalledTime = $InstalledFile.LastWriteTime
        
        if ($BuildTime -gt $InstalledTime) {
            Write-Host "⚠ WARNING: Build is NEWER than installed version!" -ForegroundColor Yellow
            Write-Host "  Build time:    $BuildTime" -ForegroundColor Gray
            Write-Host "  Installed time: $InstalledTime" -ForegroundColor Gray
        } elseif ($BuildTime -eq $InstalledTime) {
            Write-Host "✓ Versions match (same modification time)" -ForegroundColor Green
        } else {
            Write-Host "⚠ Installed version is newer than build" -ForegroundColor Yellow
        }
        
        if ($BuildFile.Length -eq $InstalledFile.Length) {
            Write-Host "✓ File sizes match" -ForegroundColor Green
        } else {
            Write-Host "⚠ File sizes differ!" -ForegroundColor Yellow
            Write-Host "  Build: $($BuildFile.Length) bytes" -ForegroundColor Gray
            Write-Host "  Installed: $($InstalledFile.Length) bytes" -ForegroundColor Gray
        }
    }
    Write-Host ""
}

# ============================================================================
# Summary
# ============================================================================

Write-Host "========================================" -ForegroundColor Green
Write-Host " Build & Install Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Restart ArchiCAD (if it was running)" -ForegroundColor Gray
Write-Host "  2. Open Window → Palettes → Report" -ForegroundColor Gray
Write-Host "  3. Look for these messages:" -ForegroundColor Gray
Write-Host "     - 'IfcTester ArchiCAD Add-On v1.0.0 (Built: ...)'" -ForegroundColor White
Write-Host "     - 'IfcTester: API server started on http://127.0.0.1:48882'" -ForegroundColor White
Write-Host ""
Write-Host "If you see 'Failed to start API server', check:" -ForegroundColor Yellow
Write-Host "  - Port 48882 availability: netstat -ano | findstr :48882" -ForegroundColor Gray
Write-Host "  - Error messages in the Report window" -ForegroundColor Gray
Write-Host ""
Write-Host "To verify installation later, run:" -ForegroundColor Cyan
Write-Host "  .\scripts\verify-build.ps1 -Configuration $Configuration -ArchiCADVersion $ArchiCADVersion" -ForegroundColor Gray
Write-Host ""

