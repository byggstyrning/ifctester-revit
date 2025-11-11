# Build Installer Command Guide

## Purpose
This command instructs Cursor to run the installer build script for the IfcTester Revit Plugin project, creating a Windows installer using Inno Setup.

## When to Use
Run this command when you need to:
- Build a Windows installer (.exe) for distribution to users
- Create Release builds for both Revit 2025 and 2026
- Package the plugin and web app into a single installer
- Generate a signed installer with a code-signing certificate

## How to Execute

### Basic Usage
When the user requests to build the installer, run the PowerShell script located at:
```
C:\code\ifctester-revit\scripts\build-installer.ps1
```

### Command
```powershell
& "C:\code\ifctester-revit\scripts\build-installer.ps1"
```

### With Parameters
The script accepts the following parameters:

1. **SkipBuild** (optional flag)
   Skip building the plugin and web app (use existing builds)
   ```powershell
   & "C:\code\ifctester-revit\scripts\build-installer.ps1" -SkipBuild
   ```

2. **SkipCertificate** (optional flag)
   Skip creating/checking the code-signing certificate
   ```powershell
   & "C:\code\ifctester-revit\scripts\build-installer.ps1" -SkipCertificate
   ```

3. **CertificatePassword** (optional, default: "IfcTester2025!")
   Password for the code-signing certificate
   ```powershell
   & "C:\code\ifctester-revit\scripts\build-installer.ps1" -CertificatePassword "YourPassword"
   ```

## What the Script Does

1. Checks for code-signing certificate (creates if missing, unless `-SkipCertificate`)
2. Builds the web application using npm
3. Builds the Revit plugin for R25 (Release configuration)
4. Builds the Revit plugin for R26 (Release configuration)
5. Prepares staging directory with all plugin files and web app
6. Generates version-specific `.addin` files for R25 and R26
7. Compiles the installer using Inno Setup
8. Signs the installer with the code-signing certificate (if available)
9. Outputs the final installer to `dist\IfcTesterRevit-Setup-v1.0.0.exe`

## Prerequisites

- **Inno Setup 6** must be installed at: `C:\Program Files (x86)\Inno Setup 6\ISCC.exe`
- **Windows SDK** (for signtool.exe) - usually installed with Visual Studio
- **.NET SDK** (for building the plugin)
- **Node.js and npm** (for building the web app)

## Output

The installer will be created at:
```
C:\code\ifctester-revit\dist\IfcTesterRevit-Setup-v1.0.0.exe
```

The installer:
- Detects installed Revit versions (2025 and/or 2026)
- Allows user to select which versions to install
- Installs plugin files to `%APPDATA%\Autodesk\Revit\Addins\{version}\IfcTesterRevit`
- Includes all dependencies and web app files
- Creates uninstaller for easy removal

## Notes for Cursor

- Always run this from PowerShell (not cmd)
- The script stops on any error (`$ErrorActionPreference = "Stop"`)
- Certificate creation requires administrator privileges
- The installer is signed with a self-signed certificate (for testing)
- For production, use a certificate from a trusted Certificate Authority
- The installer file size is approximately 23-25 MB

## Example Usage in Cursor

When a user says:
- "Build the installer"
- "Create the Windows installer"
- "Build the setup file"
- "Package for distribution"
- "Make an installer"

Execute:
```powershell
& "C:\code\ifctester-revit\scripts\build-installer.ps1"
```

## Troubleshooting

- **Inno Setup not found**: Install Inno Setup 6 from https://jrsoftware.org/isinfo.php
- **signtool.exe not found**: Install Windows SDK or Visual Studio Build Tools
- **Certificate errors**: Run PowerShell as Administrator when creating certificate
- **Build errors**: Ensure all dependencies are installed and paths are correct

