# Create Self-Signed Code Signing Certificate for IfcTester Revit Plugin
# This certificate is for testing purposes only

param(
    [string]$CertificatePassword = "IfcTester2025!",
    [string]$OutputPath = "installer\certificate\IfcTesterRevit.pfx"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Creating Self-Signed Code Signing Certificate" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Ensure output directory exists
$OutputDir = Split-Path -Parent $OutputPath
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# Check if certificate already exists
if (Test-Path $OutputPath) {
    Write-Host "Certificate already exists at: $OutputPath" -ForegroundColor Yellow
    $overwrite = Read-Host "Overwrite existing certificate? (y/N)"
    if ($overwrite -ne "y" -and $overwrite -ne "Y") {
        Write-Host "Skipping certificate creation." -ForegroundColor Yellow
        exit 0
    }
    Remove-Item $OutputPath -Force
}

Write-Host "Generating self-signed code signing certificate..." -ForegroundColor Yellow

# Create certificate parameters
$certParams = @{
    Subject = "CN=Byggstyrning IfcTester Revit Plugin, O=Byggstyrning, C=SE"
    Type = "CodeSigningCert"
    KeyUsage = "DigitalSignature"
    KeyAlgorithm = "RSA"
    KeyLength = 2048
    HashAlgorithm = "SHA256"
    CertStoreLocation = "Cert:\CurrentUser\My"
    NotAfter = (Get-Date).AddYears(5)
    FriendlyName = "IfcTester Revit Plugin Code Signing"
}

try {
    # Create the certificate
    $cert = New-SelfSignedCertificate @certParams
    
    Write-Host "Certificate created successfully!" -ForegroundColor Green
    Write-Host "  Thumbprint: $($cert.Thumbprint)" -ForegroundColor Gray
    Write-Host "  Subject: $($cert.Subject)" -ForegroundColor Gray
    Write-Host "  Valid until: $($cert.NotAfter)" -ForegroundColor Gray
    Write-Host ""
    
    # Export certificate to PFX file
    Write-Host "Exporting certificate to PFX file..." -ForegroundColor Yellow
    
    $securePassword = ConvertTo-SecureString -String $CertificatePassword -Force -AsPlainText
    
    Export-PfxCertificate -Cert $cert -FilePath $OutputPath -Password $securePassword | Out-Null
    
    Write-Host "Certificate exported to: $OutputPath" -ForegroundColor Green
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Certificate Creation Complete!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "IMPORTANT:" -ForegroundColor Yellow
    Write-Host "- This is a self-signed certificate for testing only" -ForegroundColor Yellow
    Write-Host "- Windows will show a warning when installing signed with this certificate" -ForegroundColor Yellow
    Write-Host "- For production, use a certificate from a trusted CA (DigiCert, Sectigo, etc.)" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Certificate password: $CertificatePassword" -ForegroundColor Gray
    Write-Host "Store this password securely - you'll need it to sign the installer" -ForegroundColor Gray
    Write-Host ""
    
    # Clean up - remove certificate from store (optional, comment out if you want to keep it)
    # Write-Host "Removing certificate from certificate store..." -ForegroundColor Yellow
    # Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)" -Force
    # Write-Host "Certificate removed from store (PFX file kept)" -ForegroundColor Green
    
} catch {
    Write-Host "ERROR: Failed to create certificate: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

