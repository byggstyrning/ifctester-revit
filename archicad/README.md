# IfcTester for Archicad

An Archicad add-in that integrates the IfcTester web application, providing a complete solution for IDS (Information Delivery Specification) authoring and auditing within Graphisoft Archicad.

## Overview

This repository contains the Archicad variant of the IfcTester plugin. It provides the same functionality as the Revit version but is specifically adapted for Archicad's API and architecture.

## Features

- **Archicad Integration**: Browser control (CEF) hosting the web application
- **IDS Authoring**: Create and edit IDS documents with a modern web interface
- **IFC Validation**: Validate IFC models against IDS specifications using WebAssembly/Pyodide
- **Element Selection**: Select elements in Archicad by IfcGUID from validation results
- **HTTP API Server**: Local server (port 48882) for bidirectional communication between Archicad and web app
- **Offline Support**: Python packages bundled locally for offline operation
- **Production Ready**: Embedded web app served via browser control

## Prerequisites

### Development Environment

- **Windows** with Archicad 27-29 installed
- **.NET SDK** - [Download here](https://dotnet.microsoft.com/en-us/download)
  - .NET SDK 8.0+ for Archicad 29
  - .NET Framework 4.8 for Archicad 27-28
- **Archicad API Development Kit** - Must be installed from Archicad installation
- **Node.js 18+** and npm - [Download here](https://nodejs.org/en/download)
- **WebView2 Runtime** (usually installed with Windows)
- **Visual Studio Code** or your preferred IDE for C# and web development

### Knowledge Requirements

- Basic knowledge of JavaScript/TypeScript and web development (HTML, CSS)
- Familiarity with C# and .NET development
- Understanding of Archicad API (different from Revit API)

## Project Structure

```
ifctester-archicad/
├── archicad/              # Archicad add-in (C#/.NET)
│   ├── Application.cs              # Entry point, creates menu and dockable pane
│   ├── IfcTesterArchicadView.cs     # WPF UserControl hosting WebView2
│   ├── ArchicadApiServer.cs         # HTTP server for web app communication
│   ├── WebAppConfig.cs               # Configuration for web app URL
│   ├── StartupCommand.cs            # Menu command handler
│   ├── IfcTesterArchicad.csproj    # Project file
│   └── IfcTesterArchicad.apx        # Archicad add-in manifest
├── web/                    # IfcTester Next web application (shared with Revit)
│   ├── src/
│   │   ├── modules/
│   │   │   ├── api/archicad.svelte.js    # Archicad integration module
│   │   │   └── api/revit.svelte.js        # Revit integration module (shared)
│   │   └── pages/Home/IdsViewer.svelte   # Main IDS viewer/editor
│   └── package.json
└── README.md                           # This file
```

## Differences from Revit Version

1. **API Server Port**: Uses port 48882 (Revit uses 48881)
2. **Add-in Format**: Uses `.apx` manifest file instead of `.addin`
3. **API Structure**: Archicad's API is different from Revit's API
4. **Browser Control**: Archicad uses CEF (Chromium Embedded Framework) natively, but we use WebView2 for consistency
5. **Element Selection**: Archicad's element selection API differs from Revit

## Quick Start

### 1. Setup Archicad SDK References

The project file references Archicad SDK DLLs. You need to:

1. Install Archicad with API Development Kit
2. Update the DLL paths in `IfcTesterArchicad.csproj` to match your Archicad installation
3. The typical path is: `C:\Program Files\GRAPHISOFT\ARCHICAD {version}\API Development Kit\Bin\`

### 2. Build the Add-in

```powershell
cd archicad
dotnet build IfcTesterArchicad.csproj -c "Debug AC28"
```

### 3. Install the Add-in

Copy the built files to Archicad's add-in directory:
- **Archicad 27**: `%APPDATA%\GRAPHISOFT\ARCHICAD 27 Add-Ons\`
- **Archicad 28**: `%APPDATA%\GRAPHISOFT\ARCHICAD 28 Add-Ons\`
- **Archicad 29**: `%APPDATA%\GRAPHISOFT\ARCHICAD 29 Add-Ons\`

Copy:
- `IfcTesterArchicad.dll` → `IfcTesterArchicad\IfcTesterArchicad.dll`
- `IfcTesterArchicad.apx` → `IfcTesterArchicad.apx`
- `web\` folder → `IfcTesterArchicad\web\`

### 4. Start the Web Application

```powershell
cd web
npm install
npm run dev
```

The web app will start on `http://localhost:5173/`

### 5. Launch Archicad

1. Open Archicad
2. The add-in should load automatically
3. Access the IfcTester from the menu or palette

## Development Notes

### Archicad API Integration

The current implementation is a **template/starter** that needs to be completed with actual Archicad API calls. Key areas that need implementation:

1. **Application.cs**: 
   - Implement actual Archicad menu creation
   - Implement actual dockable pane/palette registration
   - Adapt to Archicad's initialization pattern

2. **ArchicadApiServer.cs**:
   - Implement element selection using Archicad API
   - Implement IFC export using Archicad's IFC exporter
   - Implement element search by GUID

3. **ArchicadUtilities.cs**:
   - Add helper functions for Archicad-specific operations

### Browser Control

Archicad natively supports CEF (Chromium Embedded Framework) browser control. However, this implementation uses WebView2 for consistency with the Revit version. You may want to adapt it to use Archicad's native browser control for better integration.

### Threading Model

Archicad's threading model is different from Revit's. Revit uses `ExternalEvent` for thread-safe API calls, but Archicad may use a different mechanism. The current implementation needs to be adapted to Archicad's threading requirements.

## API Communication

The Archicad plugin exposes a local HTTP API on port 48882:

- `GET /status` - Check server status
- `GET /select-by-guid/<guid>` - Select element in Archicad by IfcGUID
- `GET /ifc-configurations` - Get available IFC export configurations
- `POST /export-ifc` - Export IFC file with specified configuration

The web app automatically receives the API URL via JavaScript injection when the browser control loads.

## Configuration

### Web App URL

The plugin determines the web app URL in this order:
1. Environment variable `WEB_APP_URL`
2. Config file at `%LOCALAPPDATA%\IfcTesterArchicad\webapp.config`
3. Build configuration (Debug = localhost:5173, Release = embedded via virtual host)

### API Server Port

The Archicad API server port can be changed via:
- Environment variable `ARCHICAD_API_URL` (full URL including port)
- Default: `http://localhost:48882`

## Troubleshooting

### Add-in doesn't load

1. Check that the `.apx` file is in the correct location
2. Verify that all DLL dependencies are present
3. Check Archicad's add-in manager for error messages
4. Ensure the Archicad API Development Kit is installed

### Web app doesn't load

1. **Development mode**: Ensure the dev server is running (`npm run dev` in `web/` directory)
2. Check that port 5173 is accessible
3. Verify the URL in `WebAppConfig.cs` matches your dev server
4. **Production mode**: Check that the `web` folder exists in the add-in directory

### Element selection doesn't work

1. Ensure the Archicad API server is running (starts automatically with add-in)
2. Check that elements have IfcGUID properties set
3. Verify the web app is connected (check connection status in UI)
4. Check browser console for errors

### Port conflicts

If port 48882 is already in use:
1. Stop the conflicting process
2. Or change the port in `ArchicadApiServer.cs` and update `WebAppConfig.GetApiUrl()`

## Building for Production

### Manual Production Build

#### Web Application

```powershell
cd web
npm run build
```

#### Archicad Add-in

```powershell
cd archicad
dotnet build IfcTesterArchicad.csproj -c "Release AC28"
```

Then copy the built files to the Archicad add-in directory as described in the Quick Start section.

## License

MIT License

Copyright (c) 2025 Byggstyrning

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
