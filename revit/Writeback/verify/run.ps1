<#
.SYNOPSIS
    Runs the write-back checks inside a real Revit, headless, through pyRevit.

.DESCRIPTION
    Works on a copy of the sources in the output folder. First runs purecheck, a console check
    of the classes that need no Revit. Then builds the add-in under another assembly name, so it
    cannot be mistaken for an installed IfcTester add-in that Revit has already loaded, and
    never deploys it. Then starts a separate Revit twice with `pyrevit run`: model_check.py
    (GlobalId lookup against a real IFC export, resolve, apply) and http_check.py (the HTTP
    endpoints). Each Revit run takes a few minutes. A Revit session that is already open is
    left alone.

    Needs pyRevit attached to the Revit version (`pyrevit attach master default <year>`).

.PARAMETER Year
    Revit version to run in: 2025 or 2026.

.PARAMETER Out
    Working folder for the build, the test model, the exported IFC files and the logs.

.EXAMPLE
    powershell -File revit\Writeback\verify\run.ps1 -Year 2026
#>
param(
    [ValidateSet('2025', '2026')]
    [string]$Year = '2026',
    [string]$Out = (Join-Path $env:TEMP 'ifctester-wb-verify')
)

$ErrorActionPreference = 'Stop'

$revitDir = Split-Path (Split-Path $PSScriptRoot)
$copy = Join-Path $Out 'project'
$configuration = "Debug R$($Year.Substring(2))"

New-Item -ItemType Directory -Force $Out | Out-Null
if (Test-Path $copy) { Remove-Item $copy -Recurse -Force }
New-Item -ItemType Directory $copy | Out-Null
Get-ChildItem $revitDir -Exclude bin, obj | Copy-Item -Destination $copy -Recurse

$failed = $false

Write-Host 'Running purecheck...'
$pure = dotnet run --project (Join-Path $copy 'Writeback\verify\purecheck\purecheck.csproj') -v q
if ($LASTEXITCODE -eq 0) {
    Write-Host "  $($pure | Select-Object -Last 1) ($(@($pure | Select-String '^ok ').Count) checks)"
}
else {
    $failed = $true
    $pure | Where-Object { $_ -notmatch '^ok ' } | ForEach-Object { Write-Host "  $_" }
}

$build = dotnet build (Join-Path $copy 'IfcTesterRevit.csproj') -c $configuration -p:DeployRevitAddin=false -p:AssemblyName=IfcTesterRevitWbTest -v q -nologo -clp:ErrorsOnly
if ($LASTEXITCODE -ne 0) {
    $build | ForEach-Object { Write-Host $_ }
    throw "Build failed ($configuration)."
}

$env:IFCTESTER_WB_OUT = $Out
$env:IFCTESTER_WB_DLL = Join-Path $copy "bin\$configuration\IfcTesterRevitWbTest.dll"

foreach ($check in 'model', 'http') {
    $log = Join-Path $Out "$check-log-$Year.txt"
    if (Test-Path $log) { Remove-Item $log }

    Write-Host "Running ${check}_check.py in Revit $Year..."
    # pyrevit's exit code is 0 even when the script throws; the log is the result.
    pyrevit run (Join-Path $PSScriptRoot "${check}_check.py") "--revit=$Year" | Out-Null

    $last = if (Test-Path $log) { Get-Content $log -Tail 1 -Encoding UTF8 } else { 'no log written' }
    if ($last -eq 'ALL PASSED') {
        $passed = @(Select-String -Path $log -Pattern '^ok ').Count
        Write-Host "  ALL PASSED ($passed checks)  $log"
    }
    else {
        $failed = $true
        Write-Host "  FAILED  $log"
        if (Test-Path $log) { Select-String -Path $log -Pattern '^FAIL|^EXCEPTION|^FAILED' -Context 0, 12 | ForEach-Object { Write-Host $_ } }
    }
}

if ($failed) { exit 1 }
