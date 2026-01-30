# IfcTester - Development Script
# Interactive menu for common development tasks

param(
    [ValidateSet("revit", "archicad", "web", "")]
    [string]$Target = "",
    
    [ValidateSet("build", "deploy", "status", "")]
    [string]$Action = ""
)

$ErrorActionPreference = "Stop"

# Get script paths
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

function Show-Banner {
    Clear-Host
    Write-Host ""
    Write-Host "  ========================================" -ForegroundColor Cyan
    Write-Host "       IfcTester Development Tools" -ForegroundColor Cyan
    Write-Host "  ========================================" -ForegroundColor Cyan
    Write-Host ""
}

function Show-MainMenu {
    Write-Host "  Select target:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "    [1] Revit Plugin" -ForegroundColor White
    Write-Host "    [2] ArchiCAD Add-On" -ForegroundColor White
    Write-Host "    [3] Web App" -ForegroundColor White
    Write-Host ""
    Write-Host "    [Q] Quit" -ForegroundColor Gray
    Write-Host ""
}

function Show-RevitMenu {
    Write-Host "  Revit Plugin:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "    [1] Build (Debug R25)" -ForegroundColor White
    Write-Host "    [2] Build (Debug R26)" -ForegroundColor White
    Write-Host "    [3] Deploy (Debug R25)" -ForegroundColor White
    Write-Host "    [4] Deploy (Debug R26)" -ForegroundColor White
    Write-Host "    [5] Build Installer (Release)" -ForegroundColor White
    Write-Host ""
    Write-Host "    [B] Back" -ForegroundColor Gray
    Write-Host "    [Q] Quit" -ForegroundColor Gray
    Write-Host ""
}

function Show-ArchiCADMenu {
    Write-Host "  ArchiCAD Add-On:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "    [1] Build (Debug)" -ForegroundColor White
    Write-Host "    [2] Build (Release)" -ForegroundColor White
    Write-Host "    [3] Deploy (Debug)" -ForegroundColor White
    Write-Host "    [4] Deploy (Release)" -ForegroundColor White
    Write-Host "    [5] Build Installer" -ForegroundColor White
    Write-Host "    [6] Check Status" -ForegroundColor White
    Write-Host ""
    Write-Host "    [B] Back" -ForegroundColor Gray
    Write-Host "    [Q] Quit" -ForegroundColor Gray
    Write-Host ""
}

function Show-WebMenu {
    Write-Host "  Web App:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "    [1] Build" -ForegroundColor White
    Write-Host "    [2] Dev Server (npm run dev)" -ForegroundColor White
    Write-Host ""
    Write-Host "    [B] Back" -ForegroundColor Gray
    Write-Host "    [Q] Quit" -ForegroundColor Gray
    Write-Host ""
}

function Invoke-RevitAction {
    param([string]$Choice)
    
    switch ($Choice) {
        "1" {
            & "$ScriptDir\revit\build.ps1" -Configuration "Debug R25"
        }
        "2" {
            & "$ScriptDir\revit\build.ps1" -Configuration "Debug R26"
        }
        "3" {
            & "$ScriptDir\revit\deploy.ps1" -Configuration "Debug R25"
        }
        "4" {
            & "$ScriptDir\revit\deploy.ps1" -Configuration "Debug R26"
        }
        "5" {
            & "$ScriptDir\revit\installer.ps1"
        }
        default {
            return $false
        }
    }
    return $true
}

function Invoke-ArchiCADAction {
    param([string]$Choice)
    
    switch ($Choice) {
        "1" {
            & "$ScriptDir\archicad\build.ps1" -Configuration Debug
        }
        "2" {
            & "$ScriptDir\archicad\build.ps1" -Configuration Release
        }
        "3" {
            & "$ScriptDir\archicad\deploy.ps1" -Configuration Debug
        }
        "4" {
            & "$ScriptDir\archicad\deploy.ps1" -Configuration Release
        }
        "5" {
            & "$ScriptDir\archicad\installer.ps1"
        }
        "6" {
            & "$ScriptDir\archicad\status.ps1"
        }
        default {
            return $false
        }
    }
    return $true
}

function Invoke-WebAction {
    param([string]$Choice)
    
    $webDir = Join-Path (Split-Path -Parent $ScriptDir) "web"
    
    switch ($Choice) {
        "1" {
            & "$ScriptDir\web\build.ps1"
        }
        "2" {
            Write-Host ""
            Write-Host "Starting dev server..." -ForegroundColor Yellow
            Write-Host "Press Ctrl+C to stop" -ForegroundColor Gray
            Write-Host ""
            Push-Location $webDir
            try {
                npm run dev
            }
            finally {
                Pop-Location
            }
        }
        default {
            return $false
        }
    }
    return $true
}

function Wait-ForKey {
    Write-Host ""
    Write-Host "  Press any key to continue..." -ForegroundColor Gray
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
}

# Handle direct invocation with parameters
if ($Target -and $Action) {
    switch ($Target) {
        "revit" {
            switch ($Action) {
                "build" { & "$ScriptDir\revit\build.ps1" }
                "deploy" { & "$ScriptDir\revit\deploy.ps1" }
            }
        }
        "archicad" {
            switch ($Action) {
                "build" { & "$ScriptDir\archicad\build.ps1" }
                "deploy" { & "$ScriptDir\archicad\deploy.ps1" }
                "status" { & "$ScriptDir\archicad\status.ps1" }
            }
        }
        "web" {
            switch ($Action) {
                "build" { & "$ScriptDir\web\build.ps1" }
            }
        }
    }
    exit 0
}

# Interactive menu loop
$running = $true
$currentMenu = "main"

while ($running) {
    Show-Banner
    
    switch ($currentMenu) {
        "main" {
            Show-MainMenu
            $choice = Read-Host "  Choice"
            
            switch ($choice.ToUpper()) {
                "1" { $currentMenu = "revit" }
                "2" { $currentMenu = "archicad" }
                "3" { $currentMenu = "web" }
                "Q" { $running = $false }
            }
        }
        "revit" {
            Show-RevitMenu
            $choice = Read-Host "  Choice"
            
            switch ($choice.ToUpper()) {
                "B" { $currentMenu = "main" }
                "Q" { $running = $false }
                default {
                    if (Invoke-RevitAction -Choice $choice) {
                        Wait-ForKey
                    }
                }
            }
        }
        "archicad" {
            Show-ArchiCADMenu
            $choice = Read-Host "  Choice"
            
            switch ($choice.ToUpper()) {
                "B" { $currentMenu = "main" }
                "Q" { $running = $false }
                default {
                    if (Invoke-ArchiCADAction -Choice $choice) {
                        Wait-ForKey
                    }
                }
            }
        }
        "web" {
            Show-WebMenu
            $choice = Read-Host "  Choice"
            
            switch ($choice.ToUpper()) {
                "B" { $currentMenu = "main" }
                "Q" { $running = $false }
                default {
                    if (Invoke-WebAction -Choice $choice) {
                        Wait-ForKey
                    }
                }
            }
        }
    }
}

Write-Host ""
Write-Host "  Goodbye!" -ForegroundColor Cyan
Write-Host ""
