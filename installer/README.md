# IfcTester Revit Plugin - Installer Build Instructions

This directory contains the files needed to build a Windows installer for the IfcTester Revit plugin.

## Prerequisites

1. **Inno Setup 6** - Download and install from: https://jrsoftware.org/isdl.php
   - Default installation path: `C:\Program Files (x86)\Inno Setup 6\`
   - Verify installation: The build script will check for `ISCC.exe`

2. **PowerShell 5.1 or later** - Required for build scripts

3. **.NET SDK** - Required to build the plugin

4. **Node.js and npm** - Required to build the web application

## Quick Start

### Build the Installer

From the project root directory, run:

```powershell
.\scripts\build-installer.ps1
```

This will:
1. Create a self-signed code signing certificate (if not exists)
2. Build the web application
3. Build the Revit plugin for R25 and R26 (Release mode)
4. Prepare staging directory with all files
5. Generate version-specific .addin manifest files
6. Compile the installer using Inno Setup
7. Sign the installer with the certificate

The installer will be created in the `dist/` directory: `IfcTesterRevit-Setup-v1.0.0.exe`

### Build Options

```powershell
# Skip certificate creation (use existing)
.\scripts\build-installer.ps1 -SkipCertificate

# Skip build steps (use existing builds)
.\scripts\build-installer.ps1 -SkipBuild

# Both
.\scripts\build-installer.ps1 -SkipBuild -SkipCertificate
```

## Manual Steps

### 1. Create Code Signing Certificate

If you need to create a new certificate manually:

```powershell
.\scripts\create-certificate.ps1
```

The certificate will be saved to `installer/certificate/IfcTesterRevit.pfx`

**Default password**: `IfcTester2025!`

**Note**: Self-signed certificates are for testing only. For production, use a certificate from a trusted CA (DigiCert, Sectigo, GlobalSign, etc.).

### 2. Build Plugin and Web App

Ensure the plugin and web app are built:

```powershell
# Build everything
.\deploy.ps1

# Or build Release versions manually
cd revit
dotnet build IfcTesterRevit.csproj -c "Release R25"
dotnet build IfcTesterRevit.csproj -c "Release R26"
cd ..\web
npm run build
```

### 3. Compile Installer Manually

If you want to compile the installer manually using Inno Setup:

1. Open `installer/IfcTesterRevit.iss` in Inno Setup Compiler
2. Click "Build" → "Compile"
3. The installer will be created in `dist/` directory

## Installer Features

- **Version Selection**: Users can select which Revit versions (2025, 2026) to install
- **Automatic Detection**: Checks if Revit is installed before allowing installation
- **Clean Uninstall**: Removes all plugin files and registry entries
- **Code Signing**: Installer is signed with certificate (self-signed for testing)

## Installation Paths

The installer places files in:

- **Revit 2025**: `%APPDATA%\Autodesk\Revit\Addins\2025\IfcTesterRevit\`
- **Revit 2026**: `%APPDATA%\Autodesk\Revit\Addins\2026\IfcTesterRevit\`

## File Structure

```
installer/
├── IfcTesterRevit.iss          # Main Inno Setup script
├── templates/
│   └── IfcTesterRevit.addin.template  # Template for .addin files
├── certificate/
│   └── IfcTesterRevit.pfx      # Code signing certificate (gitignored)
├── generated/                   # Generated .addin files (created during build)
│   ├── IfcTesterRevit.2025.addin
│   └── IfcTesterRevit.2026.addin
├── staging/                     # Staging directory (created during build)
│   └── IfcTesterRevit/         # Plugin files ready for packaging
└── README.md                    # This file
```

## Troubleshooting

### Inno Setup Not Found

If you get an error that Inno Setup is not found:
1. Install Inno Setup from https://jrsoftware.org/isdl.php
2. Or update the path in `scripts/build-installer.ps1`:
   ```powershell
   $InnoSetupPath = "C:\Your\Path\To\ISCC.exe"
   ```

### Certificate Issues

- **Certificate not found**: Run `.\scripts\create-certificate.ps1` first
- **Signing fails**: Ensure the certificate password matches in both scripts
- **Windows warning**: Self-signed certificates will trigger SmartScreen warnings - this is expected for testing

### Build Failures

- **Plugin build fails**: Ensure .NET SDK is installed and Revit API references are available
- **Web build fails**: Run `npm install` in the `web/` directory first
- **Missing files**: Ensure `deploy.ps1` has been run successfully at least once

## Production Deployment

For production deployment:

1. **Replace self-signed certificate** with a certificate from a trusted CA:
   - Purchase a code-signing certificate (DigiCert, Sectigo, GlobalSign, etc.)
   - Update certificate path and password in `IfcTesterRevit.iss`
   - Update certificate path in `build-installer.ps1`

2. **Update version number** in:
   - `installer/IfcTesterRevit.iss` (AppVersion)
   - `revit/IfcTesterRevit.csproj` (Version property)

3. **Test installation** on clean systems:
   - Test with both Revit versions
   - Test upgrade scenarios
   - Test uninstallation
   - Verify plugin loads correctly

4. **Distribute installer**:
   - Upload to your distribution platform
   - Provide installation instructions to users
   - Consider adding a digital signature verification guide

## Support

For issues or questions, contact: Byggstyrning

