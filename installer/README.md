# IfcTester Plugin - Installer Build Instructions

This directory contains the files needed to build Windows installers for the IfcTester plugins (Revit and ArchiCAD).

## Prerequisites

### Common Requirements
1. **Inno Setup 6** - Download and install from: https://jrsoftware.org/isdl.php
   - Default installation path: `C:\Program Files (x86)\Inno Setup 6\`
   - Verify installation: The build script will check for `ISCC.exe`

2. **PowerShell 5.1 or later** - Required for build scripts

3. **Node.js and npm** - Required to build the web application

### Revit Plugin Requirements
4. **.NET SDK 8.0** - Required to build the Revit plugin for Revit 2025/2026
5. **.NET SDK 10.0** - Required to build the Revit plugin for Revit 2027

### ArchiCAD Add-On Requirements
5. **Visual Studio 2022** with C++ desktop development workload
   - Platform toolset v143 required
   
6. **ArchiCAD API Development Kit** (for ArchiCAD 29)
   - Download from: https://archicadapi.graphisoft.com/
   - Set environment variable: `ARCHICAD_API_DEVKIT=C:\path\to\API.Development.Kit.WIN.29.3100`

## Quick Start

### Build Both Installers

```powershell
# Set ArchiCAD API path (required for ArchiCAD build)
$env:ARCHICAD_API_DEVKIT = "C:\code\archicad-api\API.Development.Kit.WIN.29.3100"

# Build Revit installer
.\scripts\build-installer.ps1

# Build ArchiCAD installer
.\scripts\build-archicad-installer.ps1
```

### Build Revit Installer Only

```powershell
.\scripts\build-installer.ps1
```

### Build ArchiCAD Installer Only

```powershell
$env:ARCHICAD_API_DEVKIT = "C:\code\archicad-api\API.Development.Kit.WIN.29.3100"
.\scripts\build-archicad-installer.ps1
```

## Build Options

### Revit Installer Options

```powershell
# Skip certificate creation (use existing)
.\scripts\build-installer.ps1 -SkipCertificate

# Skip build steps (use existing builds)
.\scripts\build-installer.ps1 -SkipBuild

# Both
.\scripts\build-installer.ps1 -SkipBuild -SkipCertificate
```

### ArchiCAD Installer Options

```powershell
# Skip building add-on (use existing)
.\scripts\build-archicad-installer.ps1 -SkipBuild

# Skip building web app (use existing)
.\scripts\build-archicad-installer.ps1 -SkipWebBuild

# Both
.\scripts\build-archicad-installer.ps1 -SkipBuild -SkipWebBuild
```

## Troubleshooting

### Revit Build Fails: "Access to path 'IfcTesterRevit.dll' is denied"

This occurs when Revit is running and has the DLL locked. **Solutions:**

1. **Close Revit** before building, OR

2. **Build without deploying** to the locked folder:
   ```powershell
   # Build without deploying to Revit addins folder
   cd revit
   dotnet build "IfcTesterRevit.csproj" -c "Release R25" /p:DeployRevitAddin=false
   dotnet publish "IfcTesterRevit.csproj" -c "Release R25" /p:DeployRevitAddin=false
   dotnet build "IfcTesterRevit.csproj" -c "Release R26" /p:DeployRevitAddin=false
   dotnet publish "IfcTesterRevit.csproj" -c "Release R26" /p:DeployRevitAddin=false
   cd ..
   
   # Then manually prepare staging and run Inno Setup
   # (See manual build steps below)
   ```

### ArchiCAD Build Fails: "ARCHICAD_API_DEVKIT not set"

Set the environment variable before building:
```powershell
$env:ARCHICAD_API_DEVKIT = "C:\path\to\API.Development.Kit.WIN.29.3100"
```

### ArchiCAD Build Fails: "VC++ 2022 v143 toolset is required"

The ArchiCAD 29 API requires:
- Visual Studio 2022 with C++ workload
- Platform toolset v143

The vcxproj should have `<PlatformToolset>v143</PlatformToolset>`.

### ArchiCAD Build Fails: C++20 Template Errors

The ArchiCAD 29 API requires C++20. Ensure the vcxproj has:
```xml
<LanguageStandard>stdcpp20</LanguageStandard>
```

### ArchiCAD Build Fails: Unresolved IFC Symbols

Ensure these are in the vcxproj:
- Include directories: `$(ArchiCADDevKitPath)\Support\Modules\IFCInOutAPI` and `$(ArchiCADDevKitPath)\Support\Modules\ArchicadAPI`
- Link libraries: `IFCInOutAPIImp.LIB` and `ArchicadAPIImp.LIB`

### Inno Setup Not Found

1. Install Inno Setup from https://jrsoftware.org/isdl.php
2. Or update the path in the build scripts

### Certificate Issues

- **Certificate not found**: Run `.\scripts\create-certificate.ps1` first
- **Signing fails**: Ensure the certificate password matches
- **Windows warning**: Self-signed certificates will trigger SmartScreen warnings

## Manual Build Steps

### Revit Plugin (When Revit is Running)

```powershell
# 1. Build without deploying
cd revit
dotnet build "IfcTesterRevit.csproj" -c "Release R25" --no-incremental /p:DeployRevitAddin=false
dotnet publish "IfcTesterRevit.csproj" -c "Release R25" /p:DeployRevitAddin=false
dotnet build "IfcTesterRevit.csproj" -c "Release R26" --no-incremental /p:DeployRevitAddin=false
dotnet publish "IfcTesterRevit.csproj" -c "Release R26" /p:DeployRevitAddin=false
dotnet build "IfcTesterRevit.csproj" -c "Release R27" --no-incremental /p:DeployRevitAddin=false
dotnet publish "IfcTesterRevit.csproj" -c "Release R27" /p:DeployRevitAddin=false
cd ..

# 2. Prepare staging directory
# R25/R26 share the net8.0-windows build; R27 uses a separate net10.0-windows build.
$StagingDir = "installer\staging"
if (Test-Path $StagingDir) { Remove-Item $StagingDir -Recurse -Force }
New-Item -ItemType Directory -Path $StagingDir -Force
Copy-Item -Path "revit\bin\Release R25\publish\*\IfcTesterRevit\*" -Destination "$StagingDir\IfcTesterRevit" -Recurse -Force
Copy-Item -Path "revit\bin\Release R27\publish\*\IfcTesterRevit\*" -Destination "$StagingDir\IfcTesterRevit-R27" -Recurse -Force
Get-ChildItem -Path $StagingDir -Filter "*.addin" -Recurse | Remove-Item -Force
Copy-Item -Path "web\dist\*" -Destination "$StagingDir\IfcTesterRevit\web" -Recurse -Force
Copy-Item -Path "web\dist\*" -Destination "$StagingDir\IfcTesterRevit-R27\web" -Recurse -Force

# 3. Generate .addin files
$Template = Get-Content "installer\templates\IfcTesterRevit.addin.template" -Raw
$Addin = $Template -replace '\{ASSEMBLY_PATH\}', 'IfcTesterRevit\IfcTesterRevit.dll'
$Addin | Out-File "installer\generated\IfcTesterRevit.2025.addin" -Encoding UTF8 -NoNewline
$Addin | Out-File "installer\generated\IfcTesterRevit.2026.addin" -Encoding UTF8 -NoNewline
$Addin | Out-File "installer\generated\IfcTesterRevit.2027.addin" -Encoding UTF8 -NoNewline

# 4. Run Inno Setup
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "/Odist" "installer\IfcTesterRevit.iss"
```

### ArchiCAD Add-On

```powershell
# 1. Set API DevKit path
$env:ARCHICAD_API_DEVKIT = "C:\code\archicad-api\API.Development.Kit.WIN.29.3100"

# 2. Build add-on
.\scripts\build-archicad.ps1 -Configuration Release -ArchiCADVersion 29

# 3. Build web app (if not already built)
cd web
npm run build
cd ..

# 4. Run Inno Setup
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "installer\IfcTesterArchiCAD.iss"
```

## Output Files

| Installer | Location | Size |
|-----------|----------|------|
| Revit | `dist\IfcTesterRevit-Setup-v1.1.0.exe` | ~45 MB |
| ArchiCAD (Windows) | `dist\IfcTesterArchiCAD-Setup-win-v1.1.0.exe` | ~35 MB |
| ArchiCAD (macOS) | `dist\IfcTesterArchiCAD-Setup-mac-v1.1.0.dmg` | ~45 MB |

## Installation Paths

### Revit
Starting with v1.2.0 the installer ships plugin files to the all-users location:

- **Revit 2025**: `%ProgramData%\Autodesk\Revit\Addins\2025\IfcTesterRevit\`
- **Revit 2026**: `%ProgramData%\Autodesk\Revit\Addins\2026\IfcTesterRevit\`
- **Revit 2027**: `%ProgramData%\Autodesk\Revit\Addins\2027\IfcTesterRevit\`

### ArchiCAD
- **ArchiCAD 29**: `%APPDATA%\Graphisoft\Add-Ons 29\IfcTesterArchiCAD\`

## File Structure

```
installer/
├── IfcTesterRevit.iss           # Revit Inno Setup script
├── IfcTesterArchiCAD.iss        # ArchiCAD Inno Setup script
├── templates/
│   └── IfcTesterRevit.addin.template  # Template for Revit .addin files
├── certificate/
│   └── IfcTesterRevit.pfx       # Code signing certificate (gitignored)
├── generated/                    # Generated files (created during build)
│   ├── IfcTesterRevit.2025.addin
│   ├── IfcTesterRevit.2026.addin
│   └── IfcTesterRevit.2027.addin
├── staging/                      # Staging directory (created during build)
│   ├── IfcTesterRevit/          # Plugin files for Revit 2025/2026 (net8.0-windows)
│   └── IfcTesterRevit-R27/      # Plugin files for Revit 2027 (net10.0-windows)
└── README.md                     # This file
```

## ArchiCAD API Requirements

The ArchiCAD add-on requires specific API configuration:

| Setting | Value |
|---------|-------|
| ArchiCAD Version | 29 |
| Platform Toolset | v143 (VS 2022) |
| C++ Standard | C++20 |
| Required Libraries | IFCInOutAPIImp.LIB, ArchicadAPIImp.LIB |

## Production Deployment

For production deployment:

1. **Replace self-signed certificate** with a certificate from a trusted CA
2. **Update version numbers** in:
   - `installer/IfcTesterRevit.iss`
   - `installer/IfcTesterArchiCAD.iss`
   - `revit/IfcTesterRevit.csproj`
3. **Test installation** on clean systems
4. **Distribute installers**

## Support

For issues or questions, contact: Byggstyrning
