# Build Installer Script for IfcTester Revit Plugin
# Builds Release versions for R25 and R26, then creates Windows installer

param(
    [switch]$SkipBuild = $false,
    [switch]$SkipCertificate = $false,
    [string]$CertificatePassword = "IfcTester2025!"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "IfcTester Revit - Installer Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Get script directory
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$RevitDir = Join-Path $RootDir "revit"
$WebDir = Join-Path $RootDir "web"
$InstallerDir = Join-Path $RootDir "installer"
$StagingDir = Join-Path $InstallerDir "staging"
$GeneratedDir = Join-Path $InstallerDir "generated"
$DistDir = Join-Path $RootDir "dist"
$CertificatePath = Join-Path $InstallerDir "certificate\IfcTesterRevit.pfx"
$InnoSetupPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

# Check Inno Setup
if (-not (Test-Path $InnoSetupPath)) {
    Write-Host "ERROR: Inno Setup not found at: $InnoSetupPath" -ForegroundColor Red
    Write-Host "Please install Inno Setup from: https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
    exit 1
}

# Step 1: Create certificate if needed
if (-not $SkipCertificate) {
    Write-Host "[1/7] Checking code signing certificate..." -ForegroundColor Yellow
    
    if (-not (Test-Path $CertificatePath)) {
        Write-Host "  Certificate not found, creating new one..." -ForegroundColor Gray
        $CreateCertScript = Join-Path $RootDir "scripts\create-certificate.ps1"
        if (Test-Path $CreateCertScript) {
            & $CreateCertScript -CertificatePassword $CertificatePassword -OutputPath $CertificatePath
            if ($LASTEXITCODE -ne 0) {
                Write-Host "ERROR: Failed to create certificate" -ForegroundColor Red
                exit 1
            }
        } else {
            Write-Host "ERROR: Certificate creation script not found" -ForegroundColor Red
            exit 1
        }
    } else {
        Write-Host "  Certificate found: $CertificatePath" -ForegroundColor Green
    }
    Write-Host ""
} else {
    Write-Host "[1/7] Skipping certificate check (--SkipCertificate flag set)" -ForegroundColor Yellow
    Write-Host ""
}

# Step 2: Build web application
if (-not $SkipBuild) {
    Write-Host "[2/7] Building web application..." -ForegroundColor Yellow
    
    if (-not (Test-Path $WebDir)) {
        Write-Host "ERROR: Web directory not found at $WebDir" -ForegroundColor Red
        exit 1
    }
    
    Push-Location $WebDir
    
    if (-not (Test-Path "node_modules")) {
        Write-Host "  Installing npm dependencies..." -ForegroundColor Gray
        npm install
        if ($LASTEXITCODE -ne 0) {
            Write-Host "ERROR: npm install failed" -ForegroundColor Red
            Pop-Location
            exit 1
        }
    }
    
    Write-Host "  Running npm run build..." -ForegroundColor Gray
    npm run build 2>&1 | ForEach-Object {
        if ($_ -match 'error|Error|ERROR|failed|Failed|FAILED|✓|built|transformed|rendering|computing') {
            $_
        }
    } | Out-String | Write-Host -NoNewline
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Web app build failed" -ForegroundColor Red
        Pop-Location
        exit 1
    }
    
    Pop-Location
    Write-Host "  ✓ Web app built successfully" -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "[2/7] Skipping web build (--SkipBuild flag set)" -ForegroundColor Yellow
    Write-Host ""
}

# Step 3: Build Revit plugin for R25
Write-Host "[3/7] Building Revit plugin for R25 (Release)..." -ForegroundColor Yellow
Push-Location $RevitDir

Write-Host "  Running dotnet build..." -ForegroundColor Gray
dotnet build "IfcTesterRevit.csproj" -c "Release R25" --no-incremental
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Revit plugin build failed for R25" -ForegroundColor Red
    Pop-Location
    exit 1
}

Pop-Location
Write-Host "  ✓ Revit plugin R25 built successfully" -ForegroundColor Green
Write-Host ""

# Step 4: Build Revit plugin for R26
Write-Host "[4/7] Building Revit plugin for R26 (Release)..." -ForegroundColor Yellow
Push-Location $RevitDir

Write-Host "  Running dotnet build..." -ForegroundColor Gray
dotnet build "IfcTesterRevit.csproj" -c "Release R26" --no-incremental
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Revit plugin build failed for R26" -ForegroundColor Red
    Pop-Location
    exit 1
}

Pop-Location
Write-Host "  ✓ Revit plugin R26 built successfully" -ForegroundColor Green
Write-Host ""

# Step 5: Prepare staging directory
Write-Host "[5/7] Preparing staging directory..." -ForegroundColor Yellow

# Clean staging directory
if (Test-Path $StagingDir) {
    Remove-Item $StagingDir -Recurse -Force
}
New-Item -ItemType Directory -Path $StagingDir -Force | Out-Null

# Copy R25 build (we'll use R25 as base, R26 should be similar)
$R25PublishDir = Join-Path $RevitDir "bin\Release R25\publish"
$PublishAddinDir = Get-ChildItem -Path $R25PublishDir -Directory -Filter "*addin" | Select-Object -First 1

if (-not $PublishAddinDir) {
    Write-Host "ERROR: Could not find publish directory for R25" -ForegroundColor Red
    exit 1
}

$StagingPluginDir = Join-Path $StagingDir "IfcTesterRevit"
Write-Host "  Copying plugin files from $($PublishAddinDir.FullName)..." -ForegroundColor Gray
Copy-Item -Path "$($PublishAddinDir.FullName)\*" -Destination $StagingPluginDir -Recurse -Force

# Remove old WebAec files if they exist
Write-Host "  Cleaning up old WebAec files..." -ForegroundColor Gray
Get-ChildItem -Path $StagingPluginDir -Filter "WebAec*" -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue

# Ensure web app is copied
$WebDistDir = Join-Path $WebDir "dist"
if (Test-Path $WebDistDir) {
    $StagingWebDir = Join-Path $StagingPluginDir "web"
    if (Test-Path $StagingWebDir) {
        Remove-Item $StagingWebDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $StagingWebDir -Force | Out-Null
    Write-Host "  Copying web app files..." -ForegroundColor Gray
    Copy-Item -Path "$WebDistDir\*" -Destination $StagingWebDir -Recurse -Force
}

Write-Host "  ✓ Staging directory prepared" -ForegroundColor Green
Write-Host ""

# Step 6: Generate version-specific .addin files
Write-Host "[6/7] Generating version-specific .addin files..." -ForegroundColor Yellow

if (-not (Test-Path $GeneratedDir)) {
    New-Item -ItemType Directory -Path $GeneratedDir -Force | Out-Null
}

$TemplatePath = Join-Path $InstallerDir "templates\IfcTesterRevit.addin.template"
if (-not (Test-Path $TemplatePath)) {
    Write-Host "ERROR: Template file not found: $TemplatePath" -ForegroundColor Red
    exit 1
}

$TemplateContent = Get-Content $TemplatePath -Raw

# Generate R25 .addin file
$R25AddinContent = $TemplateContent -replace '\{ASSEMBLY_PATH\}', 'IfcTesterRevit\IfcTesterRevit.dll'
$R25AddinPath = Join-Path $GeneratedDir "IfcTesterRevit.2025.addin"
$R25AddinContent | Out-File -FilePath $R25AddinPath -Encoding UTF8 -NoNewline
Write-Host "  Generated: IfcTesterRevit.2025.addin" -ForegroundColor Gray

# Generate R26 .addin file
$R26AddinContent = $TemplateContent -replace '\{ASSEMBLY_PATH\}', 'IfcTesterRevit\IfcTesterRevit.dll'
$R26AddinPath = Join-Path $GeneratedDir "IfcTesterRevit.2026.addin"
$R26AddinContent | Out-File -FilePath $R26AddinPath -Encoding UTF8 -NoNewline
Write-Host "  Generated: IfcTesterRevit.2026.addin" -ForegroundColor Gray

Write-Host "  ✓ .addin files generated" -ForegroundColor Green
Write-Host ""

# Step 7: Compile Inno Setup script
Write-Host "[7/8] Compiling installer with Inno Setup..." -ForegroundColor Yellow

$IssPath = Join-Path $InstallerDir "IfcTesterRevit.iss"
if (-not (Test-Path $IssPath)) {
    Write-Host "ERROR: Inno Setup script not found: $IssPath" -ForegroundColor Red
    exit 1
}

Push-Location $InstallerDir
Write-Host "  Running ISCC..." -ForegroundColor Gray

$IssArgs = "/O`"$DistDir`" `"$IssPath`""
$process = Start-Process -FilePath $InnoSetupPath -ArgumentList $IssArgs -Wait -NoNewWindow -PassThru

if ($process.ExitCode -ne 0) {
    Write-Host "ERROR: Inno Setup compilation failed with exit code $($process.ExitCode)" -ForegroundColor Red
    Pop-Location
    exit 1
}

Pop-Location

# Find the generated installer
$InstallerFile = Get-ChildItem -Path $DistDir -Filter "IfcTesterRevit-Setup-*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1

if ($InstallerFile) {
    Write-Host "  ✓ Installer compiled successfully" -ForegroundColor Green
    Write-Host ""
    
    # Step 8: Sign the installer
    Write-Host "[8/8] Signing installer..." -ForegroundColor Yellow
    
    if (Test-Path $CertificatePath) {
        try {
            # Use signtool to sign the installer - try to find it
            $SignToolPath = $null
            $PossiblePaths = @(
                "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe",
                "${env:ProgramFiles(x86)}\Windows Kits\10\bin\x64\signtool.exe",
                "${env:ProgramFiles}\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe",
                "${env:ProgramFiles}\Windows Kits\10\bin\x64\signtool.exe"
            )
            
            # Also search for latest version
            $KitsPath = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
            if (Test-Path $KitsPath) {
                $LatestVersion = Get-ChildItem -Path $KitsPath -Directory | Sort-Object Name -Descending | Select-Object -First 1
                if ($LatestVersion) {
                    $PossiblePaths += @(
                        "$($LatestVersion.FullName)\x64\signtool.exe",
                        "$($LatestVersion.FullName)\x86\signtool.exe"
                    )
                }
            }
            
            foreach ($path in $PossiblePaths) {
                if (Test-Path $path) {
                    $SignToolPath = $path
                    break
                }
            }
            
            if (Test-Path $SignToolPath) {
                Write-Host "  Signing with signtool..." -ForegroundColor Gray
                $signArgs = @(
                    "sign",
                    "/f", "`"$CertificatePath`"",
                    "/p", "`"$CertificatePassword`"",
                    "/t", "http://timestamp.digicert.com",
                    "/fd", "SHA256",
                    "`"$($InstallerFile.FullName)`""
                )
                
                $signProcess = Start-Process -FilePath $SignToolPath -ArgumentList $signArgs -Wait -NoNewWindow -PassThru -RedirectStandardOutput "$env:TEMP\signtool-output.txt" -RedirectStandardError "$env:TEMP\signtool-error.txt"
                
                if ($signProcess.ExitCode -eq 0) {
                    Write-Host "  ✓ Installer signed successfully" -ForegroundColor Green
                } else {
                    $errorOutput = Get-Content "$env:TEMP\signtool-error.txt" -ErrorAction SilentlyContinue
                    Write-Host "  WARNING: Signing failed (exit code: $($signProcess.ExitCode))" -ForegroundColor Yellow
                    if ($errorOutput) {
                        Write-Host "  Error: $errorOutput" -ForegroundColor Yellow
                    }
                    Write-Host "  Installer created but not signed. You can sign it manually later." -ForegroundColor Yellow
                }
            } else {
                Write-Host "  WARNING: signtool.exe not found. Installer created but not signed." -ForegroundColor Yellow
                Write-Host "  To sign manually, install Windows SDK and use signtool.exe" -ForegroundColor Yellow
            }
        } catch {
            Write-Host "  WARNING: Failed to sign installer: $($_.Exception.Message)" -ForegroundColor Yellow
            Write-Host "  Installer created but not signed. You can sign it manually later." -ForegroundColor Yellow
        }
    } else {
        Write-Host "  WARNING: Certificate not found. Installer created but not signed." -ForegroundColor Yellow
    }
    
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Installer Build Complete!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Installer location: $($InstallerFile.FullName)" -ForegroundColor White
    Write-Host "File size: $([math]::Round($InstallerFile.Length / 1MB, 2)) MB" -ForegroundColor White
    Write-Host ""
    Write-Host "NOTE: The installer is signed with a self-signed certificate." -ForegroundColor Yellow
    Write-Host "Windows may show a warning when running it. This is expected for testing." -ForegroundColor Yellow
    Write-Host ""
} else {
    Write-Host "WARNING: Could not find generated installer file" -ForegroundColor Yellow
}

