# Quick Status Check Script for IfcTester ArchiCAD Add-On

Write-Host "=== IfcTester Add-On Status Check ===" -ForegroundColor Cyan
Write-Host ""

# Check if port is listening
Write-Host "1. Checking API server port..." -ForegroundColor Yellow
$portCheck = netstat -ano | findstr :48882
if ($portCheck) {
    Write-Host "   ✓ Port 48882 is LISTENING" -ForegroundColor Green
    $portCheck | ForEach-Object { Write-Host "   $_" -ForegroundColor Gray }
} else {
    Write-Host "   ✗ Port 48882 is NOT listening" -ForegroundColor Red
    Write-Host "   The API server is not running" -ForegroundColor Yellow
}

Write-Host ""

# Check if add-on file exists
Write-Host "2. Checking add-on installation..." -ForegroundColor Yellow
$addonPath = "$env:APPDATA\Graphisoft\ArchiCAD 27\Add-Ons\IfcTesterArchiCAD\IfcTesterArchiCAD.apx"
if (Test-Path $addonPath) {
    $file = Get-Item $addonPath
    Write-Host "   ✓ Add-on file found" -ForegroundColor Green
    Write-Host "   Location: $addonPath" -ForegroundColor Gray
    Write-Host "   Size: $([math]::Round($file.Length / 1KB, 2)) KB" -ForegroundColor Gray
    Write-Host "   Modified: $($file.LastWriteTime)" -ForegroundColor Gray
} else {
    Write-Host "   ✗ Add-on file NOT found" -ForegroundColor Red
    Write-Host "   Expected: $addonPath" -ForegroundColor Gray
}

Write-Host ""

# Test API server connection
Write-Host "3. Testing API server connection..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "http://localhost:48882/status" -TimeoutSec 2 -ErrorAction Stop -UseBasicParsing
    Write-Host "   ✓ API server is responding" -ForegroundColor Green
    Write-Host "   Status Code: $($response.StatusCode)" -ForegroundColor Gray
    Write-Host "   Response: $($response.Content)" -ForegroundColor Gray
} catch {
    Write-Host "   ✗ API server is NOT responding" -ForegroundColor Red
    Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "=== Status Check Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "If the API server is running, the add-on is loaded successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "To find the Report window in ArchiCAD:" -ForegroundColor Cyan
Write-Host "  - Try: Window → Palettes → Report" -ForegroundColor Gray
Write-Host "  - Or: View → Palettes → Report" -ForegroundColor Gray
Write-Host "  - Or: File → Info → Action Center → Reports" -ForegroundColor Gray
Write-Host ""
Write-Host "You can also test the API server in your browser:" -ForegroundColor Cyan
Write-Host "  http://localhost:48882/status" -ForegroundColor White

