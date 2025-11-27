# IfcTester for BIM Applications

A suite of plugins that integrate the IfcTester web application with major BIM software, providing a complete solution for IDS (Information Delivery Specification) authoring and auditing within your design environment.

[<img width="1920" height="920" alt="image" src="https://github.com/user-attachments/assets/90ba3e0b-4f1c-47da-af93-05f7b3c476c9" />](https://github.com/user-attachments/assets/1cb93a9a-b7c7-4e77-a6ff-db6ccd17be35)

## Supported Platforms

| Platform | Status | Documentation |
|----------|--------|---------------|
| **Autodesk Revit** | ✅ Ready | [Revit Plugin Guide](revit/README.md) |
| **GRAPHISOFT ArchiCAD** | 🚧 In Development | [ArchiCAD Add-On Guide](archicad/README.md) |

## Overview

This repository combines the IfcTester web application (Svelte/Vite) with native plugins for BIM software, creating a seamless integration that allows users to author and validate exported IFC files against IDS specifications directly within their design environment. The web application runs in an embedded browser, while Python-based validation runs client-side via WebAssembly/Pyodide.

## Features

- **BIM Software Integration**: 
  - Dockable panels/palettes in Revit and ArchiCAD
  - Select elements from IFC validation results
  - Export IFC directly from the plugin
- **IDS Authoring**: Create and edit IDS documents with a modern web interface
- **IFC Validation**: Validate IFC models against IDS specifications using WebAssembly/Pyodide
- **HTTP API Server**: Local server for bidirectional communication
- **Offline Support**: Python packages bundled locally for offline operation

## Project Structure

```
ifctester/
├── revit/               # Revit plugin (C#/.NET)
│   ├── Application.cs   # Entry point
│   ├── RevitApiServer.cs # HTTP API server
│   └── IfcTesterRevit.csproj
├── archicad/            # ArchiCAD add-on (C++)
│   ├── Src/
│   │   ├── Main.cpp     # Add-on entry point
│   │   ├── BrowserPalette.cpp # Browser control UI
│   │   └── ArchiCADApiServer.cpp # HTTP API server
│   ├── RFIX/            # Non-localizable resources
│   ├── RINT/            # Localizable resources
│   └── IfcTesterArchiCAD.vcxproj
├── web/                 # IfcTester web application (Svelte/Vite)
│   ├── src/
│   │   └── modules/api/ # BIM software integration modules
│   └── public/worker/   # Pyodide and Python packages
├── installer/           # Installer configurations
│   ├── IfcTesterRevit.iss    # Revit installer
│   └── IfcTesterArchiCAD.iss # ArchiCAD installer
├── scripts/             # Build and deployment scripts
│   ├── deploy.ps1       # Revit deployment
│   └── build-archicad.ps1 # ArchiCAD build
└── IfcTester.sln        # Combined Visual Studio solution
```

## Quick Start

### Revit Plugin

```powershell
# Build and deploy for Revit 2025
.\scripts\deploy.ps1 -Configuration "Release R25"
```

See [Revit Plugin Documentation](revit/README.md) for detailed instructions.

### ArchiCAD Add-On

```powershell
# Set ArchiCAD API DevKit path
$env:ARCHICAD_API_DEVKIT = "C:\Program Files\GRAPHISOFT\API Development Kit 27"

# Build for ArchiCAD 27
.\scripts\build-archicad.ps1 -Configuration Release -ArchiCADVersion 27
```

See [ArchiCAD Add-On Documentation](archicad/README.md) for detailed instructions.

## Prerequisites

### Common Requirements

- **Windows 10/11** (64-bit)
- **Node.js 18+** and npm - [Download here](https://nodejs.org/)

### Revit Plugin

- **.NET SDK 8.0+** - [Download here](https://dotnet.microsoft.com/)
- **Autodesk Revit 2021-2026**
- **WebView2 Runtime** (usually pre-installed)

### ArchiCAD Add-On

- **Visual Studio 2022** with C++ desktop development workload
- **ArchiCAD API Development Kit** - [Download here](https://archicadapi.graphisoft.com/)
- **GRAPHISOFT ArchiCAD 25-27**

## API Communication

Both plugins expose a local HTTP API for communication with the web interface:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/status` | GET | Check server status |
| `/select-by-guid/{guid}` | GET | Select element by IFC GUID |
| `/ifc-configurations` | GET | List IFC export configurations |
| `/export-ifc` | POST | Export model to IFC |

**Default Ports:**
- Revit: `48881`
- ArchiCAD: `48882`

## Building the Web Application

The web application is shared between all plugins:

```powershell
cd web
npm install
npm run build
```

The built files in `web/dist/` are embedded in each plugin.

## Installers

### Creating Installers

1. **Revit Installer**:
   ```powershell
   # Requires Inno Setup
   iscc installer\IfcTesterRevit.iss
   ```

2. **ArchiCAD Installer**:
   ```powershell
   # Requires Inno Setup
   iscc installer\IfcTesterArchiCAD.iss
   ```

## Development

### Web Application Development

```powershell
cd web
npm run dev
```

The dev server runs at `http://localhost:5173/`. Plugins can be configured to use this URL during development.

### Debugging

- **Revit**: Attach Visual Studio debugger to `Revit.exe`
- **ArchiCAD**: Attach Visual Studio debugger to `ARCHICAD.exe`
- **Web**: Use browser DevTools or CEF debug port (see platform-specific docs)

## Project History

This project combines several open-source efforts:

- **IfcTester Next Web Application**: Based on [ifctester-next](https://github.com/theseyan/ifctester-next) by theseyan
- **Revit Plugin Architecture**: Based on [aectech-2025-nyc-web-aec](https://github.com/vwnd/aectech-2025-nyc-web-aec) by vwnd
- **ArchiCAD Integration**: Based on the [ArchiCAD Browser Control API](https://archicadapi.graphisoft.com/browser-control-and-javascript-connection)

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Test with the target BIM software
5. Submit a pull request

## License

MIT License - See [LICENSE](LICENSE) for details.

Copyright (c) 2025 Byggstyrning

---

## Platform-Specific Documentation

- [Revit Plugin Guide](revit/README.md) - Detailed Revit integration documentation
- [ArchiCAD Add-On Guide](archicad/README.md) - Detailed ArchiCAD integration documentation
