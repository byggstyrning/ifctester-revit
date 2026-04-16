# Build Installer Command Guide

## Purpose
This command instructs Cursor to build Windows installers for the IfcTester plugins (Revit and ArchiCAD).

## When to Use
Run this command when you need to:
- Build Windows installers (.exe) for distribution to users
- Create Release builds for Revit 2025/2026/2027 and ArchiCAD 29
- Package the plugins and web app into installers

## Quick Reference

### Build Both Installers
```powershell
# Set ArchiCAD API path first
$env:ARCHICAD_API_DEVKIT = "C:\code\archicad-api\API.Development.Kit.WIN.29.3100"

# Build Revit installer
& "C:\code\ifctester-revit\scripts\build-installer.ps1" -SkipCertificate

# Build ArchiCAD installer  
& "C:\code\ifctester-revit\scripts\build-archicad-installer.ps1"
```

### Build Revit Only
```powershell
& "C:\code\ifctester-revit\scripts\build-installer.ps1"
```

### Build ArchiCAD Only
```powershell
$env:ARCHICAD_API_DEVKIT = "C:\code\archicad-api\API.Development.Kit.WIN.29.3100"
& "C:\code\ifctester-revit\scripts\build-archicad-installer.ps1"
```

## Prerequisites

### Common
- **Inno Setup 6** at `C:\Program Files (x86)\Inno Setup 6\ISCC.exe`
- **Node.js and npm** for web app build

### Revit
- **.NET SDK 8.0** for Revit 2025/2026 plugin build
- **.NET SDK 10.0** for Revit 2027 plugin build

### ArchiCAD
- **Visual Studio 2022** with C++ desktop workload (v143 toolset)
- **ArchiCAD API DevKit 29** - Set `ARCHICAD_API_DEVKIT` environment variable

## Parameters

### Revit Installer (`build-installer.ps1`)
| Parameter | Description |
|-----------|-------------|
| `-SkipBuild` | Skip building plugin and web app |
| `-SkipCertificate` | Skip certificate creation/check |
| `-CertificatePassword` | Password for code-signing certificate |

### ArchiCAD Installer (`build-archicad-installer.ps1`)
| Parameter | Description |
|-----------|-------------|
| `-SkipBuild` | Skip building the add-on |
| `-SkipWebBuild` | Skip building the web app |

## Common Issue: Revit DLL Locked

**Error**: `Access to path 'IfcTesterRevit.dll' is denied`

**Cause**: Revit is running and has the DLL file locked.

**Solution**: Build without deploying to the locked folder:

```powershell
# Build Revit plugin without deploying
Push-Location "C:\code\ifctester-revit\revit"
dotnet build "IfcTesterRevit.csproj" -c "Release R25" --no-incremental /p:DeployRevitAddin=false
dotnet publish "IfcTesterRevit.csproj" -c "Release R25" /p:DeployRevitAddin=false
dotnet build "IfcTesterRevit.csproj" -c "Release R26" --no-incremental /p:DeployRevitAddin=false
dotnet publish "IfcTesterRevit.csproj" -c "Release R26" /p:DeployRevitAddin=false
dotnet build "IfcTesterRevit.csproj" -c "Release R27" --no-incremental /p:DeployRevitAddin=false
dotnet publish "IfcTesterRevit.csproj" -c "Release R27" /p:DeployRevitAddin=false
Pop-Location

# Prepare staging directory (R25/R26 share, R27 is separate because of net10.0-windows)
$StagingDir = "C:\code\ifctester-revit\installer\staging"
if (Test-Path $StagingDir) { Remove-Item $StagingDir -Recurse -Force }
New-Item -ItemType Directory -Path $StagingDir -Force | Out-Null

$r25Publish = (Get-ChildItem "C:\code\ifctester-revit\revit\bin\Release R25\publish" -Directory -Filter "*addin" | Select-Object -First 1).FullName
$r27Publish = (Get-ChildItem "C:\code\ifctester-revit\revit\bin\Release R27\publish" -Directory -Filter "*addin" | Select-Object -First 1).FullName

Copy-Item -Path "$r25Publish\IfcTesterRevit\*" -Destination "$StagingDir\IfcTesterRevit" -Recurse -Force
Copy-Item -Path "$r27Publish\IfcTesterRevit\*" -Destination "$StagingDir\IfcTesterRevit-R27" -Recurse -Force
Get-ChildItem -Path $StagingDir -Filter "*.addin" -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue
Copy-Item -Path "C:\code\ifctester-revit\web\dist\*" -Destination "$StagingDir\IfcTesterRevit\web" -Recurse -Force
Copy-Item -Path "C:\code\ifctester-revit\web\dist\*" -Destination "$StagingDir\IfcTesterRevit-R27\web" -Recurse -Force

# Generate .addin files
$GeneratedDir = "C:\code\ifctester-revit\installer\generated"
if (-not (Test-Path $GeneratedDir)) { New-Item -ItemType Directory -Path $GeneratedDir -Force | Out-Null }
$Template = Get-Content "C:\code\ifctester-revit\installer\templates\IfcTesterRevit.addin.template" -Raw
$Addin = $Template -replace '\{ASSEMBLY_PATH\}', 'IfcTesterRevit\IfcTesterRevit.dll'
$Addin | Out-File "$GeneratedDir\IfcTesterRevit.2025.addin" -Encoding UTF8 -NoNewline
$Addin | Out-File "$GeneratedDir\IfcTesterRevit.2026.addin" -Encoding UTF8 -NoNewline
$Addin | Out-File "$GeneratedDir\IfcTesterRevit.2027.addin" -Encoding UTF8 -NoNewline

# Run Inno Setup
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "/OC:\code\ifctester-revit\dist" "C:\code\ifctester-revit\installer\IfcTesterRevit.iss"
```

## Output

| Installer | Location | Size |
|-----------|----------|------|
| Revit | `dist\IfcTesterRevit-Setup-v1.1.0.exe` | ~45 MB |
| ArchiCAD (Windows) | `dist\IfcTesterArchiCAD-Setup-win-v1.1.0.exe` | ~35 MB |
| ArchiCAD (macOS) | `dist\IfcTesterArchiCAD-Setup-mac-v1.1.0.dmg` | ~45 MB |

## Example Usage in Cursor

When a user says:
- "Build the installer" / "Build releases" → Build both
- "Build Revit installer" → Build Revit only
- "Build ArchiCAD installer" → Build ArchiCAD only

## ArchiCAD Build Requirements

The ArchiCAD 29 API requires specific configuration:

| Requirement | Value |
|-------------|-------|
| Platform Toolset | v143 (Visual Studio 2022) |
| C++ Standard | C++20 (`stdcpp20`) |
| IFC Libraries | IFCInOutAPIImp.LIB, ArchicadAPIImp.LIB |
| Include Paths | IFCInOutAPI, ArchicadAPI modules |

## Troubleshooting

| Error | Solution |
|-------|----------|
| Inno Setup not found | Install from https://jrsoftware.org/isdl.php |
| ARCHICAD_API_DEVKIT not set | Set env var to DevKit path |
| v143 toolset required | Install VS 2022 with C++ workload |
| C++20 template errors | Ensure `<LanguageStandard>stdcpp20</LanguageStandard>` in vcxproj |
| IFC unresolved symbols | Add IFCInOutAPI and ArchicadAPI to includes and libs |
| DLL access denied | Revit is running - use `/p:DeployRevitAddin=false` workaround |
