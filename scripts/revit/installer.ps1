# IfcTester Revit Plugin - Installer Build Script
# Builds Release versions and creates Windows installer

param(
    [switch]$SkipBuild,
    [switch]$SkipSign,
    [string]$CertificatePassword = "IfcTester2025!"
)

# Load common utilities
. "$PSScriptRoot\..\utils\common.ps1"

$paths = Get-ProjectPaths

Write-Header "IfcTester Revit - Build Installer"

# Check for Inno Setup
$innoSetup = Find-InnoSetup
if (-not $innoSetup) {
    Write-ErrorMsg "Inno Setup not found!"
    Write-Info "Please install from: https://jrsoftware.org/isdl.php"
    exit 1
}
Write-Info "Using Inno Setup: $innoSetup"
Write-Host ""

$stagingDir = Join-Path $paths.Installer "staging"
$generatedDir = Join-Path $paths.Installer "generated"
$certificatePath = Join-Path $paths.Installer "certificate\IfcTesterRevit.pfx"

$totalSteps = 7
$step = 0

# Step 1: Certificate check
$step++
Write-Step $step $totalSteps "Checking code signing certificate..."

if (-not (Test-Path $certificatePath)) {
    Write-Info "Certificate not found, creating..."
    $createCertScript = Join-Path $paths.Scripts "utils\create-certificate.ps1"
    if (Test-Path $createCertScript) {
        & $createCertScript -CertificatePassword $CertificatePassword -OutputPath $certificatePath
    }
    else {
        Write-Warning "Certificate creation script not found, installer will not be signed"
    }
}
else {
    Write-Success "Certificate found"
}
Write-Host ""

# Step 2: Build web app
if (-not $SkipBuild) {
    $step++
    Write-Step $step $totalSteps "Building web application..."
    
    if (-not (Build-WebApp -WebDir $paths.Web -Force)) {
        exit 1
    }
    Write-Host ""
}

# Step 3: Build Revit R25
$step++
Write-Step $step $totalSteps "Building Revit plugin (Release R25)..."

Push-Location $paths.Revit
try {
    dotnet build "IfcTesterRevit.csproj" -c "Release R25" --no-incremental
    if ($LASTEXITCODE -ne 0) {
        Write-ErrorMsg "R25 build failed"
        exit 1
    }
    Write-Success "R25 built successfully"
}
finally {
    Pop-Location
}
Write-Host ""

# Step 4: Build Revit R26
$step++
Write-Step $step $totalSteps "Building Revit plugin (Release R26)..."

Push-Location $paths.Revit
try {
    dotnet build "IfcTesterRevit.csproj" -c "Release R26" --no-incremental
    if ($LASTEXITCODE -ne 0) {
        Write-ErrorMsg "R26 build failed"
        exit 1
    }
    Write-Success "R26 built successfully"
}
finally {
    Pop-Location
}
Write-Host ""

# Step 5: Prepare staging
$step++
Write-Step $step $totalSteps "Preparing staging directory..."

if (Test-Path $stagingDir) {
    Remove-Item $stagingDir -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

# Copy R25 build as base
$r25PublishDir = Join-Path $paths.Revit "bin\Release R25\publish"
$publishAddinDir = Get-ChildItem -Path $r25PublishDir -Directory -Filter "*addin" | Select-Object -First 1

if (-not $publishAddinDir) {
    Write-ErrorMsg "Could not find R25 publish directory"
    exit 1
}

$stagingPluginDir = Join-Path $stagingDir "IfcTesterRevit"
Copy-Item -Path "$($publishAddinDir.FullName)\*" -Destination $stagingPluginDir -Recurse -Force

# Clean up
Get-ChildItem -Path $stagingPluginDir -Filter "WebAec*" -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $stagingPluginDir -Filter "*.addin" -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue

# Copy web app
$webDistDir = Join-Path $paths.Web "dist"
$stagingWebDir = Join-Path $stagingPluginDir "web"
if (Test-Path $webDistDir) {
    New-Item -ItemType Directory -Path $stagingWebDir -Force | Out-Null
    Copy-Item -Path "$webDistDir\*" -Destination $stagingWebDir -Recurse -Force
}

Write-Success "Staging prepared"
Write-Host ""

# Step 6: Generate .addin files
$step++
Write-Step $step $totalSteps "Generating .addin files..."

if (-not (Test-Path $generatedDir)) {
    New-Item -ItemType Directory -Path $generatedDir -Force | Out-Null
}

$templatePath = Join-Path $paths.Installer "templates\IfcTesterRevit.addin.template"
if (Test-Path $templatePath) {
    $templateContent = Get-Content $templatePath -Raw
    
    # R25
    $r25Content = $templateContent -replace '\{ASSEMBLY_PATH\}', 'IfcTesterRevit\IfcTesterRevit.dll'
    $r25Content | Out-File -FilePath (Join-Path $generatedDir "IfcTesterRevit.2025.addin") -Encoding UTF8 -NoNewline
    Write-Info "Generated IfcTesterRevit.2025.addin"
    
    # R26
    $r26Content = $templateContent -replace '\{ASSEMBLY_PATH\}', 'IfcTesterRevit\IfcTesterRevit.dll'
    $r26Content | Out-File -FilePath (Join-Path $generatedDir "IfcTesterRevit.2026.addin") -Encoding UTF8 -NoNewline
    Write-Info "Generated IfcTesterRevit.2026.addin"
    
    Write-Success ".addin files generated"
}
else {
    Write-Warning "Template not found, skipping .addin generation"
}
Write-Host ""

# Step 7: Compile installer
$step++
Write-Step $step $totalSteps "Compiling installer..."

# Ensure dist directory exists
if (-not (Test-Path $paths.Dist)) {
    New-Item -ItemType Directory -Path $paths.Dist -Force | Out-Null
}

$issPath = Join-Path $paths.Installer "IfcTesterRevit.iss"
if (-not (Test-Path $issPath)) {
    Write-ErrorMsg "Inno Setup script not found: $issPath"
    exit 1
}

$issArgs = "/O`"$($paths.Dist)`" `"$issPath`""
$process = Start-Process -FilePath $innoSetup -ArgumentList $issArgs -Wait -NoNewWindow -PassThru

if ($process.ExitCode -ne 0) {
    Write-ErrorMsg "Inno Setup compilation failed"
    exit 1
}

Write-Success "Installer compiled"
Write-Host ""

# Find generated installer
$installerFile = Get-ChildItem -Path $paths.Dist -Filter "IfcTesterRevit-Setup-*.exe" | 
    Sort-Object LastWriteTime -Descending | 
    Select-Object -First 1

if (-not $installerFile) {
    Write-Warning "Could not find generated installer"
    exit 1
}

# Sign installer
if (-not $SkipSign -and (Test-Path $certificatePath)) {
    Write-Host "Signing installer..." -ForegroundColor Yellow
    
    $signTool = Find-SignTool
    if ($signTool) {
        $signArgs = @(
            "sign",
            "/f", "`"$certificatePath`"",
            "/p", "`"$CertificatePassword`"",
            "/t", "http://timestamp.digicert.com",
            "/fd", "SHA256",
            "`"$($installerFile.FullName)`""
        )
        
        $signProcess = Start-Process -FilePath $signTool -ArgumentList $signArgs -Wait -NoNewWindow -PassThru `
            -RedirectStandardOutput "$env:TEMP\signtool-output.txt" `
            -RedirectStandardError "$env:TEMP\signtool-error.txt"
        
        if ($signProcess.ExitCode -eq 0) {
            Write-Success "Installer signed"
        }
        else {
            Write-Warning "Signing failed (installer still usable)"
        }
    }
    else {
        Write-Warning "signtool.exe not found, skipping signing"
    }
}

Write-Completed "Installer Build Complete!"

Write-Host "Installer: $($installerFile.FullName)" -ForegroundColor White
Write-Host "Size: $([math]::Round($installerFile.Length / 1MB, 2)) MB" -ForegroundColor White
Write-Host ""
