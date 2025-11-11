# IfcTester Revit

A Revit plugin that integrates the IfcTester web application, providing a complete solution for IDS (Information Delivery Specification) authoring and auditing within Autodesk Revit.

## Overview

This repository combines the IfcTester Revit plugin (C#/.NET) with the IfcTester Next web application (Svelte/Vite), creating a seamless integration that allows users to author and validate exported IFC files against IDS specifications directly within Revit. The web application runs in a dockable pane using WebView2, while Python-based validation runs client-side via WebAssembly/Pyodide.

## Features

- **Revit Integration**: Dockable pane in Revit with WebView2 hosting the web application
- **IDS Authoring**: Create and edit IDS documents with a modern web interface
- **IFC Validation**: Validate IFC models against IDS specifications using WebAssembly/Pyodide
- **Element Selection**: Select elements in Revit by IfcGUID from validation results
- **HTTP API Server**: Local server (port 48881) for bidirectional communication between Revit and web app
- **Offline Support**: Python packages bundled locally for offline operation
- **Production Ready**: Embedded web app served via WebView2 virtual host mapping

## Prerequisites

### Development Environment

- **Windows** with Revit 2021-2026 installed (depending on configuration)
- **.NET SDK** - [Download here](https://dotnet.microsoft.com/en-us/download)
  - .NET SDK 8.0+ for Revit 2025
  - .NET Framework 4.8 for older Revit versions
- **Node.js 18+** and npm - [Download here](https://nodejs.org/en/download)
- **WebView2 Runtime** (usually installed with Windows)
- **Visual Studio Code** or your preferred IDE for C# and web development
  - C# Dev Kit extension (required, for C# development)
  - TailwindCSS extension (optional, but helpful for styling)
  - Prettier extension (optional, for code formatting)

### Knowledge Requirements

- Basic knowledge of JavaScript/TypeScript and web development (HTML, CSS)
- Familiarity with C# and .NET development

## Project Structure

```
ifctester-revit/
├── revit/              # Revit plugin (C#/.NET)
│   ├── Application.cs              # Entry point, creates ribbon and dockable pane
│   ├── IfcTesterRevitView.cs      # WPF UserControl hosting WebView2
│   ├── RevitApiServer.cs          # HTTP server for web app communication
│   ├── WebAppConfig.cs             # Configuration for web app URL
│   ├── StartupCommand.cs          # Ribbon button command
│   └── IfcTesterRevit.csproj      # Project file
├── web/                 # IfcTester Next web application (Svelte/Vite)
│   ├── src/
│   │   ├── modules/
│   │   │   ├── api/revit.svelte.js    # Revit integration module
│   │   │   └── wasm/worker/           # WebAssembly worker for Pyodide
│   │   └── pages/Home/IdsViewer.svelte  # Main IDS viewer/editor
│   ├── public/
│   │   ├── worker/bin/             # Python wheel files (ifctester, etc.)
│   │   └── pyodide/                # Pyodide runtime
│   ├── scripts/
│   │   └── download-packages.ps1   # Downloads Python packages for Pyodide
│   └── package.json
├── scripts/
│   └── deploy.ps1                  # Unified build and deployment script
└── README.md                       # This file
```

## Quick Start

### 1. Clone and Setup

```powershell
git clone <repository-url>
cd ifctester-revit
```

### 2. Build and Deploy (Recommended)

The easiest way to get started is using the deployment script:

```powershell
.\scripts\deploy.ps1 -Configuration "Debug R25"
```

This script will:
1. Download required Python packages (ifctester wheel) for Pyodide
2. Build the web application
3. Build the Revit plugin
4. Copy the web app to the plugin directory
5. Deploy everything to `%APPDATA%\Autodesk\Revit\Addins\2025\IfcTesterRevit\`

**Note**: Make sure Revit is closed before running the deployment script.

### 3. Manual Setup (Development)

#### Build the Revit Plugin

```powershell
cd revit
dotnet build IfcTesterRevit.csproj -c "Debug R25"
```

The plugin will be automatically deployed to:
`%APPDATA%\Autodesk\Revit\Addins\2025\IfcTesterRevit\`

#### Start the Web Application

```powershell
cd web
npm install
npm run dev
```

The web app will start on `http://localhost:5173/`

### 4. Configure the Plugin (Optional)

The plugin automatically detects the web app URL. By default, it uses:
- **Development**: `http://localhost:5173/` (when running `npm run dev`)
- **Production**: Embedded web app served locally via WebView2 virtual host mapping

To set a custom URL, create a config file:
```powershell
$configPath = "$env:LOCALAPPDATA\IfcTesterRevit\webapp.config"
New-Item -ItemType Directory -Force -Path (Split-Path $configPath)
Set-Content -Path $configPath -Value "http://your-server:5173/"
```

### 5. Launch Revit

1. Open Revit 2025
2. Navigate to the **Add-ins** tab in the ribbon
3. Click the **IfcTester** button in the **Audit** panel
4. The dockable pane will open showing the web application

## Development

### Revit Plugin Architecture

The Revit plugin consists of several key components:

- **Application.cs**: Entry point that creates the ribbon panel and dockable pane on startup
- **IfcTesterRevitView.cs**: WPF UserControl that hosts the WebView2 control and handles navigation
- **RevitApiServer.cs**: HTTP server (port 48881) that exposes Revit API endpoints for the web app
- **WebAppConfig.cs**: Configuration system that determines the web app URL from environment variables, config files, or build settings
- **StartupCommand.cs**: External command that toggles the dockable pane visibility

### Web Application Architecture

The web application (`web/`) is a Svelte-based IDS authoring tool:

- **src/modules/api/revit.svelte.js**: Revit integration module that communicates with the Revit API server
- **src/pages/Home/IdsViewer.svelte**: Main IDS viewer/editor component
- **src/modules/wasm/worker/**: WebAssembly worker that runs Pyodide for Python-based validation
- Uses Vite for development and building

### API Communication

The Revit plugin exposes a local HTTP API on port 48881:

- `GET /status` - Check server status
- `GET /select-by-guid/<guid>` - Select element in Revit by IfcGUID

The web app automatically receives the API URL via JavaScript injection when the WebView2 loads.

### Pyodide Package Management

Python packages (like `ifctester`) are downloaded during the build process and served locally:

- **Download Script**: `web/scripts/download-packages.ps1` automatically fetches the latest ifctester version from PyPI
- **Storage**: Wheel files are stored in `web/public/worker/bin/`
- **Runtime**: The worker tries to install from local files first, falling back to PyPI if needed
- **Benefits**: Works offline, faster initialization, more reliable

The deployment script automatically downloads packages before building, ensuring all required wheels are available locally.

## Building for Production

### Using the Deployment Script

The easiest way to build for production:

```powershell
.\scripts\deploy.ps1 -Configuration "Release R25"
```

Or skip the web build if it's already built:

```powershell
.\scripts\deploy.ps1 -Configuration "Release R25" -SkipWebBuild
```

This script:
1. Downloads required Python packages (ifctester wheel) for Pyodide
2. Builds the web application (unless `-SkipWebBuild` is specified)
3. Builds the Revit plugin
4. Copies the web app to the plugin directory
5. Deploys everything to Revit's Addins folder
6. Creates a `built` folder in the revit directory with all files for easy distribution

The web app is embedded in the plugin and served locally via WebView2's virtual host mapping (`app.localhost`), so no external server is needed in production.

### Manual Production Build

#### Web Application

```powershell
cd web
npm run build
```

This creates a `dist/` folder with static assets, including the Python wheel files from `public/`.

#### Revit Plugin

```powershell
cd revit
dotnet build IfcTesterRevit.csproj -c "Release R25"
```

The deployment script automatically copies the `dist/` folder to the plugin directory, so the plugin can serve the web app locally using WebView2's virtual host mapping.

## Configuration

### Web App URL

The plugin determines the web app URL in this order:
1. Environment variable `WEB_APP_URL`
2. Config file at `%LOCALAPPDATA%\IfcTesterRevit\webapp.config`
3. Build configuration (Debug = localhost:5173, Release = embedded via virtual host)

### API Server Port

The Revit API server port can be changed via:
- Environment variable `REVIT_API_URL` (full URL including port)
- Default: `http://localhost:48881`

## Troubleshooting

### Web app doesn't load

1. **Development mode**: Ensure the dev server is running (`npm run dev` in `web/` directory)
2. Check that port 5173 is accessible
3. Verify the URL in `WebAppConfig.cs` matches your dev server
4. **Production mode**: Check that the `web` folder exists in the plugin directory with all files

### Element selection doesn't work

1. Ensure the Revit API server is running (starts automatically with plugin)
2. Check that elements have IfcGUID parameters set
3. Verify the web app is connected (check connection status in UI)
4. Check browser console for errors

### Port conflicts

If port 48881 is already in use:
1. Stop the conflicting process
2. Or change the port in `RevitApiServer.cs` and update `WebAppConfig.GetApiUrl()`

### Python packages not loading

1. Ensure `download-packages.ps1` was run during build
2. Check that wheel files exist in `web/public/worker/bin/`
3. Verify the config.json has the correct `ifctester_url` path
4. Check browser console for Pyodide errors

## Project History

This project was created by merging two existing projects:

### Source Projects

- **IfcTester Next Web Application**: The web application (`web/`) is based on [ifctester-next](https://github.com/theseyan/ifctester-next) by [theseyan](https://github.com/theseyan), which provides IDS authoring and auditing capabilities in a modern web interface.

- **Revit Plugin Base**: The Revit plugin architecture is based on the [aectech-2025-nyc-web-aec](https://github.com/vwnd/aectech-2025-nyc-web-aec) repository by [vwnd](https://github.com/vwnd), which demonstrates WebView2 integration patterns for Revit and Rhino plugins.

### Merge Process

The merge included:

- ✅ Moving ifctester-next into ifctester-revit/web/
- ✅ Adapting the Revit plugin architecture from the aectech-2025-nyc-web-aec base
- ✅ Creating unified configuration system (`WebAppConfig.cs`)
- ✅ Setting up Pyodide package download system for offline Python package support
- ✅ Creating unified build and deployment script
- ✅ Comprehensive documentation

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

