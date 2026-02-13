# Common utilities for IfcTester build scripts
# This file is dot-sourced by other scripts to provide shared functionality

$ErrorActionPreference = "Stop"

# ============================================================================
# Paths
# ============================================================================

function Get-ProjectRoot {
    $scriptPath = $PSScriptRoot
    # Navigate up from utils/ to scripts/ to project root
    return (Split-Path -Parent (Split-Path -Parent $scriptPath))
}

function Get-ProjectPaths {
    $root = Get-ProjectRoot
    return @{
        Root        = $root
        Revit       = Join-Path $root "revit"
        ArchiCAD    = Join-Path $root "archicad"
        Web         = Join-Path $root "web"
        Installer   = Join-Path $root "installer"
        Dist        = Join-Path $root "dist"
        Scripts     = Join-Path $root "scripts"
    }
}

# ============================================================================
# Console Output
# ============================================================================

function Write-Header {
    param([string]$Title)
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host " $Title" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
}

function Write-Step {
    param(
        [int]$Number,
        [int]$Total,
        [string]$Message
    )
    Write-Host "[$Number/$Total] $Message" -ForegroundColor Yellow
}

function Write-Success {
    param([string]$Message)
    Write-Host "  [OK] $Message" -ForegroundColor Green
}

function Write-Warning {
    param([string]$Message)
    Write-Host "  [!] $Message" -ForegroundColor Yellow
}

function Write-ErrorMsg {
    param([string]$Message)
    Write-Host "  [X] $Message" -ForegroundColor Red
}

function Write-Info {
    param([string]$Message)
    Write-Host "  $Message" -ForegroundColor Gray
}

function Write-Completed {
    param([string]$Title = "Complete!")
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host " $Title" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
}

# ============================================================================
# Tool Detection
# ============================================================================

function Find-MSBuild {
    $paths = @(
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
    )
    
    foreach ($path in $paths) {
        if (Test-Path $path) {
            return $path
        }
    }
    return $null
}

function Find-InnoSetup {
    $paths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )
    
    foreach ($path in $paths) {
        if (Test-Path $path) {
            return $path
        }
    }
    return $null
}

function Find-SignTool {
    $possiblePaths = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe",
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\x64\signtool.exe",
        "${env:ProgramFiles}\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe",
        "${env:ProgramFiles}\Windows Kits\10\bin\x64\signtool.exe"
    )
    
    # Search for latest version
    $kitsPath = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (Test-Path $kitsPath) {
        $latestVersion = Get-ChildItem -Path $kitsPath -Directory | Sort-Object Name -Descending | Select-Object -First 1
        if ($latestVersion) {
            $possiblePaths += @(
                "$($latestVersion.FullName)\x64\signtool.exe",
                "$($latestVersion.FullName)\x86\signtool.exe"
            )
        }
    }
    
    foreach ($path in $possiblePaths) {
        if (Test-Path $path) {
            return $path
        }
    }
    return $null
}

function Find-ArchiCADDevKit {
    param([string]$Version = "29")
    
    # Check environment variable first
    if ($env:ARCHICAD_API_DEVKIT -and (Test-Path $env:ARCHICAD_API_DEVKIT)) {
        return $env:ARCHICAD_API_DEVKIT
    }
    
    $paths = @(
        "C:\code\archicad-api\API.Development.Kit.WIN.$Version.3100",
        "C:\Program Files\GRAPHISOFT\API Development Kit $Version",
        "C:\Program Files (x86)\GRAPHISOFT\API Development Kit $Version",
        "$env:USERPROFILE\GRAPHISOFT\API Development Kit $Version"
    )
    
    foreach ($path in $paths) {
        if (Test-Path $path) {
            return $path
        }
    }
    return $null
}

# ============================================================================
# Web App Build
# ============================================================================

function Build-WebApp {
    param(
        [string]$WebDir,
        [switch]$Force
    )
    
    $distPath = Join-Path $WebDir "dist"
    $indexPath = Join-Path $distPath "index.html"
    
    # Check if rebuild needed
    if (-not $Force -and (Test-Path $indexPath)) {
        $lastBuild = (Get-Item $indexPath).LastWriteTime
        if ($lastBuild -gt (Get-Date).AddMinutes(-5)) {
            Write-Info "Web app already built (dist folder is recent)"
            return $true
        }
    }
    
    Push-Location $WebDir
    try {
        # Install dependencies if needed
        if (-not (Test-Path "node_modules")) {
            Write-Info "Installing npm dependencies..."
            $previousEap = $ErrorActionPreference
            $ErrorActionPreference = "Continue"
            try {
                cmd /c "npm install"
                $npmInstallExitCode = $LASTEXITCODE
            }
            finally {
                $ErrorActionPreference = $previousEap
            }

            if ($npmInstallExitCode -ne 0) {
                Write-ErrorMsg "npm install failed"
                return $false
            }
        }
        
        # Download Python packages if script exists
        $downloadScript = Join-Path $WebDir "scripts\download-packages.ps1"
        if (Test-Path $downloadScript) {
            Write-Info "Downloading Python packages..."
            & $downloadScript
        }
        
        # Build
        Write-Info "Running npm run build..."
        $previousEap = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        try {
            cmd /c "npm run build" 2>&1 | ForEach-Object {
                if ($_ -match 'error|Error|ERROR|failed|Failed|FAILED|built|transformed|rendering|computing') {
                    Write-Host "    $_" -ForegroundColor Gray
                }
            }
            $npmBuildExitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousEap
        }
        
        if ($npmBuildExitCode -ne 0) {
            Write-ErrorMsg "Web app build failed"
            return $false
        }
        
        Write-Success "Web app built successfully"
        return $true
    }
    finally {
        Pop-Location
    }
}

# ============================================================================
# Process Checks
# ============================================================================

function Test-RevitRunning {
    $processes = Get-Process -Name "Revit" -ErrorAction SilentlyContinue
    return $null -ne $processes
}

function Test-ArchiCADRunning {
    $processes = Get-Process -Name "Archicad" -ErrorAction SilentlyContinue
    return $null -ne $processes
}

function Confirm-Continue {
    param([string]$Message = "Continue anyway?")
    $response = Read-Host "$Message (y/N)"
    return ($response -eq "y" -or $response -eq "Y")
}

# ============================================================================
# File Operations
# ============================================================================

function Copy-DirectoryContents {
    param(
        [string]$Source,
        [string]$Destination,
        [switch]$Clean
    )
    
    if ($Clean -and (Test-Path $Destination)) {
        Remove-Item $Destination -Recurse -Force
    }
    
    if (-not (Test-Path $Destination)) {
        New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    }
    
    Copy-Item -Path "$Source\*" -Destination $Destination -Recurse -Force
}

# Functions are available via dot-sourcing
