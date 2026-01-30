# Create Self-Signed Code Signing Certificate
# This certificate is for testing purposes only

param(
    [string]$CertificatePassword = "IfcTester2025!",
    [string]$OutputPath = ""
)

# Load common utilities if available
$commonPath = Join-Path $PSScriptRoot "common.ps1"
if (Test-Path $commonPath) {
    . $commonPath
    $paths = Get-ProjectPaths
    if (-not $OutputPath) {
        $OutputPath = Join-Path $paths.Installer "certificate\IfcTesterRevit.pfx"
    }
}
else {
    if (-not $OutputPath) {
        $OutputPath = "installer\certificate\IfcTesterRevit.pfx"
    }
}

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Create Code Signing Certificate" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Ensure output directory exists
$outputDir = Split-Path -Parent $OutputPath
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# Check if certificate exists
if (Test-Path $OutputPath) {
    Write-Host "Certificate already exists: $OutputPath" -ForegroundColor Yellow
    $overwrite = Read-Host "Overwrite? (y/N)"
    if ($overwrite -ne "y" -and $overwrite -ne "Y") {
        Write-Host "Skipped." -ForegroundColor Yellow
        exit 0
    }
    Remove-Item $OutputPath -Force
}

Write-Host "Generating certificate..." -ForegroundColor Yellow

$certParams = @{
    Subject           = "CN=Byggstyrning IfcTester, O=Byggstyrning, C=SE"
    Type              = "CodeSigningCert"
    KeyUsage          = "DigitalSignature"
    KeyAlgorithm      = "RSA"
    KeyLength         = 2048
    HashAlgorithm     = "SHA256"
    CertStoreLocation = "Cert:\CurrentUser\My"
    NotAfter          = (Get-Date).AddYears(5)
    FriendlyName      = "IfcTester Code Signing"
}

try {
    $cert = New-SelfSignedCertificate @certParams
    
    Write-Host "  Thumbprint: $($cert.Thumbprint)" -ForegroundColor Gray
    Write-Host "  Valid until: $($cert.NotAfter)" -ForegroundColor Gray
    
    # Export to PFX
    $securePassword = ConvertTo-SecureString -String $CertificatePassword -Force -AsPlainText
    Export-PfxCertificate -Cert $cert -FilePath $OutputPath -Password $securePassword | Out-Null
    
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host " Certificate Created!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Output: $OutputPath" -ForegroundColor White
    Write-Host "Password: $CertificatePassword" -ForegroundColor White
    Write-Host ""
    Write-Host "NOTE: This is a self-signed certificate for testing." -ForegroundColor Yellow
    Write-Host "Windows will show warnings when running signed installers." -ForegroundColor Yellow
    Write-Host "For production, use a certificate from a trusted CA." -ForegroundColor Yellow
    Write-Host ""
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
