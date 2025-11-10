# IfcTester Revit - Unified Project

This repository combines the IfcTester Revit plugin with the IfcTester Next web application, providing a complete solution for IDS (Information Delivery Specification) authoring and auditing within Revit.

## Project Structure

```
ifctester-revit/
├── revit/          # Revit plugin (C#/.NET)
├── web/            # IfcTester Next web application (Svelte/Vite)
```

## Features

- **Revit Integration**: Dockable pane in Revit with WebView2 hosting the web application
- **IDS Authoring**: Create and edit IDS documents with a modern web interface
- **IFC Validation**: Validate IFC models against IDS specifications using WebAssembly/Pyodide
- **Element Selection**: Select elements in Revit by IfcGUID from validation results
- **HTTP API Server**: Local server (port 48881) for communication between Revit and web app

## Prerequisites

- .NET SDK (8.0+ for Revit 2025, .NET Framework 4.8 for older versions)
- Node.js 18+ and npm
- Revit 2021-2026 (depending on configuration)
- WebView2 Runtime (usually installed with Windows)

## Quick Start

### 1. Build the Revit Plugin

```powershell
cd revit
dotnet build IfcTesterRevit.csproj -c "Debug R25"
```

The plugin will be automatically deployed to:
`%APPDATA%\Autodesk\Revit\Addins\2025\IfcTesterRevit\`

### 2. Start the Web Application

```powershell
cd web
npm install
npm run dev
```

The web app will start on `http://localhost:5173/`

### 3. Configure the Plugin (Optional)

The plugin automatically detects the web app URL. By default, it uses:
- **Development**: `http://localhost:5173/` (when running `npm run dev`)
- **Production**: Can be configured via:
  - Environment variable: `WEB_APP_URL`
  - Config file: `%LOCALAPPDATA%\IfcTesterRevit\webapp.config`

To set a custom URL, create the config file:
```powershell
$configPath = "$env:LOCALAPPDATA\IfcTesterRevit\webapp.config"
New-Item -ItemType Directory -Force -Path (Split-Path $configPath)
Set-Content -Path $configPath -Value "http://your-server:5173/"
```

### 4. Launch Revit

1. Open Revit 2025
2. Click the "Execute" button in the IfcTesterRevit ribbon panel
3. The dockable pane will open showing the web application

## Development

### Revit Plugin

The Revit plugin consists of:
- **Application.cs**: Entry point, creates ribbon and dockable pane
- **IfcTesterRevitView.cs**: WPF UserControl hosting WebView2
- **RevitApiServer.cs**: HTTP server for web app communication
- **WebAppConfig.cs**: Configuration for web app URL

### Web Application

The web application (`web/`) is a Svelte-based IDS authoring tool:
- **src/modules/api/revit.svelte.js**: Revit integration module
- **src/pages/Home/IdsViewer.svelte**: Main IDS viewer/editor
- Uses Vite for development and building

### API Communication

The Revit plugin exposes a local HTTP API on port 48881:

- `GET /status` - Check server status
- `GET /select-by-guid/<guid>` - Select element in Revit by IfcGUID

The web app automatically receives the API URL via JavaScript injection.

## Building for Production

### Using the Deployment Script

The easiest way to build for production:

```powershell
.\deploy.ps1 -Configuration "Release R25"
```

This script:
1. Downloads required Python packages (ifctester wheel) for Pyodide
2. Builds the web application
3. Builds the Revit plugin
4. Copies the web app to the plugin directory
5. Deploys everything to Revit's Addins folder

The web app is embedded in the plugin and served locally via WebView2's virtual host mapping (no external server needed).

**Note**: Make sure Revit is closed before running the deployment script, as it needs to overwrite files in the Addins directory.

### Manual Production Build

#### Web Application

```powershell
cd web
npm run build
```

This creates a `dist/` folder with static assets.

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
3. Build configuration (Debug = localhost:5173, Release = configurable)

### API Server Port

The Revit API server port can be changed via:
- Environment variable `REVIT_API_URL` (full URL including port)
- Default: `http://localhost:48881`

## Troubleshooting

### Web app doesn't load

1. Ensure the dev server is running (`npm run dev` in `web/` directory)
2. Check that port 5173 is accessible
3. Verify the URL in `WebAppConfig.cs` matches your dev server

### Element selection doesn't work

1. Ensure the Revit API server is running (starts automatically with plugin)
2. Check that elements have IfcGUID parameters set
3. Verify the web app is connected (check connection status in UI)

### Port conflicts

If port 48881 is already in use:
1. Stop the conflicting process
2. Or change the port in `RevitApiServer.cs` and update `WebAppConfig.GetApiUrl()`

## License

[Add your license here]

## Contributing

[Add contribution guidelines here]
