# IfcTester ArchiCAD Add-On - Status Check
# Checks if the add-on is installed and API server is running

param(
    [ValidateSet("29")]
    [string]$ArchiCADVersion = "29"
)

# Load common utilities
. "$PSScriptRoot\..\utils\common.ps1"

Write-Header "IfcTester ArchiCAD - Status Check"

$addOnsFolder = "$env:APPDATA\Graphisoft\ArchiCAD $ArchiCADVersion\Add-Ons"
$addonPath = Join-Path $addOnsFolder "IfcTesterArchiCAD\IfcTesterArchiCAD.apx"
$webAppPath = Join-Path $addOnsFolder "IfcTesterArchiCAD\WebApp"

# 1. Check add-on installation
Write-Host "1. Add-on Installation" -ForegroundColor Yellow
if (Test-Path $addonPath) {
    $file = Get-Item $addonPath
    Write-Success "Add-on file found"
    Write-Info "Location: $addonPath"
    Write-Info "Size: $([math]::Round($file.Length / 1KB, 2)) KB"
    Write-Info "Modified: $($file.LastWriteTime)"
}
else {
    Write-ErrorMsg "Add-on file NOT found"
    Write-Info "Expected: $addonPath"
}
Write-Host ""

# 2. Check WebApp folder
Write-Host "2. WebApp Folder" -ForegroundColor Yellow
if (Test-Path $webAppPath) {
    Write-Success "WebApp folder found"
    $fileCount = (Get-ChildItem $webAppPath -Recurse -File).Count
    Write-Info "Files: $fileCount"
}
else {
    Write-ErrorMsg "WebApp folder NOT found"
    Write-Info "Expected: $webAppPath"
}
Write-Host ""

# 3. Check if port is listening
Write-Host "3. API Server Port" -ForegroundColor Yellow
$portCheck = netstat -ano | Select-String ":48882"
if ($portCheck) {
    Write-Success "Port 48882 is LISTENING"
    $portCheck | ForEach-Object { Write-Info $_.ToString().Trim() }
}
else {
    Write-Warning "Port 48882 is NOT listening"
    Write-Info "The API server may not be running"
}
Write-Host ""

# 4. Test API connection
Write-Host "4. API Server Connection" -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "http://localhost:48882/status" -TimeoutSec 2 -ErrorAction Stop -UseBasicParsing
    Write-Success "API server is responding"
    Write-Info "Status: $($response.StatusCode)"
    Write-Info "Response: $($response.Content)"
}
catch {
    Write-ErrorMsg "API server is NOT responding"
    Write-Info "Error: $($_.Exception.Message)"
}
Write-Host ""

# 5. Check ArchiCAD process
Write-Host "5. ArchiCAD Process" -ForegroundColor Yellow
if (Test-ArchiCADRunning) {
    Write-Success "ArchiCAD is running"
    $processes = Get-Process -Name "Archicad" -ErrorAction SilentlyContinue
    foreach ($proc in $processes) {
        Write-Info "PID: $($proc.Id), Started: $($proc.StartTime)"
    }
}
else {
    Write-Info "ArchiCAD is not running"
}

Write-Completed "Status Check Complete"

Write-Host "Troubleshooting:" -ForegroundColor Cyan
Write-Host "  - If API server not running, restart ArchiCAD" -ForegroundColor Gray
Write-Host "  - Check Window > Palettes > Report for error messages" -ForegroundColor Gray
Write-Host "  - Test API in browser: http://localhost:48882/status" -ForegroundColor Gray
Write-Host ""
