# IfcTester ArchiCAD Add-On

IDS authoring and auditing tool for ArchiCAD. This add-on embeds the IfcTester web application within ArchiCAD, providing IFC model validation and IDS (Information Delivery Specification) tools.

## Features

- **Embedded Web Interface**: Uses ArchiCAD's Browser Control (CEF) to host the IfcTester web application
- **ArchiCAD Integration**: 
  - Select elements in ArchiCAD from IFC validation results
  - View selected elements' IFC properties
  - Export IFC directly from the panel
- **REST API Server**: Built-in HTTP server for communication between web interface and ArchiCAD API

## Requirements

### For Building

- **Visual Studio 2022** with C++ desktop development workload
- **ArchiCAD API Development Kit** (version 25, 26, or 27)
- **Windows 10/11** (64-bit)

### For Running

- **ArchiCAD 25, 26, or 27** (Windows)

## Building

### Prerequisites

1. Install Visual Studio 2022 with:
   - Desktop development with C++
   - Windows 10/11 SDK

2. Download the ArchiCAD API Development Kit from:
   https://archicadapi.graphisoft.com/

3. Set the `ARCHICAD_API_DEVKIT` environment variable to your DevKit path:
   ```powershell
   $env:ARCHICAD_API_DEVKIT = "C:\Program Files\GRAPHISOFT\API Development Kit 27"
   ```

### Build Steps

#### Using PowerShell Script

```powershell
# From project root
.\scripts\build-archicad.ps1 -Configuration Release -ArchiCADVersion 27
```

#### Using Visual Studio

1. Open `archicad\IfcTesterArchiCAD.vcxproj` in Visual Studio 2022
2. Set the `ARCHICAD_API_DEVKIT` environment variable in project properties
3. Build the solution (Release | x64)

### Build Output

The compiled add-on will be located at:
```
archicad\Build\Release\IfcTesterArchiCAD.apx
```

## Installation

### Manual Installation

1. Build the add-on (see above)
2. Copy `IfcTesterArchiCAD.apx` to your ArchiCAD Add-Ons folder:
   - Windows: `C:\Program Files\GRAPHISOFT\ARCHICAD 27\Add-Ons`
3. Copy the `web\dist` folder to `%LOCALAPPDATA%\IfcTesterArchiCAD\WebApp`
4. Restart ArchiCAD

### Using Installer

1. Run `IfcTesterArchiCAD-Setup-vX.X.X.exe`
2. Follow the installation wizard
3. The installer will automatically detect installed ArchiCAD versions

## Usage

1. Open ArchiCAD
2. Go to **Audit** > **IFC Testing** > **IfcTester Panel**
3. The IfcTester panel will open as a dockable palette
4. Use the web interface to:
   - Load IDS files
   - Validate IFC models
   - Select elements in ArchiCAD from validation results

## Architecture

```
archicad/
├── Src/
│   ├── Main.cpp                 # Add-On entry point (4 required functions)
│   ├── IfcTesterArchiCAD.hpp    # Main header with types and declarations
│   ├── BrowserPalette.cpp/hpp   # Browser control palette implementation
│   └── ArchiCADApiServer.cpp/hpp # HTTP REST API server
├── RFIX/                        # Non-localizable resources
│   └── IfcTesterArchiCADFix.grc # MDID resource
├── RINT/                        # Localizable resources
│   └── IfcTesterArchiCAD.grc    # Strings, menus, dialogs
├── RFIX.win/                    # Windows-specific resources
│   └── IfcTesterArchiCAD.rc2    # Windows resource file
└── Resources/Icons/             # Add-on icons
```

### Key Components

#### BrowserPalette
A dockable modeless dialog (palette) that hosts the embedded browser control. Uses ArchiCAD's DG::Browser class which wraps CEF (Chromium Embedded Framework).

#### JavaScript Bridge
The add-on registers an `ACAPI` JavaScript object in the browser that provides:
- `ACAPI.GetSelectedElements()` - Get information about selected elements
- `ACAPI.SelectElementByGUID(guid)` - Select an element by its IFC GUID
- `ACAPI.AddElementToSelection(guid)` - Add an element to the selection
- `ACAPI.RemoveElementFromSelection(guid)` - Remove an element from selection
- `ACAPI.GetIFCConfigurations()` - Get available IFC export configurations
- `ACAPI.ExportToIFC(configName)` - Export model to IFC

#### REST API Server
HTTP server (port 48882) providing REST endpoints:
- `GET /status` - Server status and connection check
- `GET /select-by-guid/{guid}` - Select element by IFC GUID
- `GET /ifc-configurations` - List available IFC export configurations
- `POST /export-ifc` - Export model to IFC format

## API Port Configuration

The ArchiCAD add-on uses port **48882** by default (different from Revit's port 48881 to allow both plugins to run simultaneously).

## Debugging

### JavaScript Debugging in CEF

1. Set the Windows registry key:
   ```
   HKEY_CURRENT_USER\SOFTWARE\GRAPHISOFT\Debug\DG
   CefDebugPort = 9222 (DWORD)
   ```
   Or set `CefUseFixedDebugPort = 1` (DWORD)

2. Start ArchiCAD and open the IfcTester panel
3. Open Chrome and navigate to `http://localhost:9222`
4. Select the IfcTester page to debug

### Add-On Debugging

1. Build with Debug configuration
2. Attach Visual Studio debugger to `ARCHICAD.exe`
3. Set breakpoints in C++ code

## Development Notes

### MDID Generation

The MDID (Module Identifier) in `RFIX/IfcTesterArchiCADFix.grc` uses placeholder values. For production, generate unique IDs at:
https://archicadapi.graphisoft.com/profile/add-ons

### Resource Compilation

GRC files are compiled using GRAPHISOFT's ResConv tool (included in the DevKit). The Visual Studio project runs this automatically in the pre-build step.

### Version Compatibility

The add-on is designed to work with ArchiCAD 25-27. Different versions may require:
- Rebuilding against the appropriate DevKit
- Minor API adjustments for deprecated/changed functions

## License

See the LICENSE file in the project root.

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Test with ArchiCAD
5. Submit a pull request

## Related Projects

- [IfcTester Revit Plugin](../revit/README.md) - Revit version of this plugin
- [IfcTester Web App](../web/README.md) - The web application hosted by this add-on
