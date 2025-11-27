# Archicad Implementation Summary

This document summarizes the Archicad variant implementation for the IfcTester plugin.

## What Was Created

### 1. Archicad Add-in Structure (`/archicad/`)

- **IfcTesterArchicad.csproj**: Project file configured for Archicad 27-29
- **Application.cs**: Main entry point for the Archicad add-in
- **ArchicadApiServer.cs**: HTTP server (port 48882) for web app communication
- **IfcTesterArchicadView.cs**: WPF UserControl hosting WebView2 browser control
- **WebAppConfig.cs**: Configuration for web app URL and API server
- **StartupCommand.cs**: Command handler for menu actions
- **ArchicadUtilities.cs**: Placeholder for Archicad-specific utilities
- **IfcTesterArchicad.apx**: Archicad add-in manifest file
- **README.md**: Comprehensive documentation for the Archicad variant

### 2. Web Application Integration (`/web/src/modules/api/`)

- **archicad.svelte.js**: JavaScript module for Archicad integration
  - Connection management
  - Element selection by GUID
  - IFC export functionality
  - Configuration retrieval

### 3. Build and Deployment

- **deploy-archicad.ps1**: PowerShell script for building and deploying the Archicad add-in

## Key Differences from Revit Version

1. **Port**: Uses port 48882 (Revit uses 48881)
2. **Add-in Format**: Uses `.apx` manifest instead of `.addin`
3. **API Structure**: Archicad's API is fundamentally different from Revit's
4. **Installation Path**: 
   - Revit: `%APPDATA%\Autodesk\Revit\Addins\{version}\`
   - Archicad: `%APPDATA%\GRAPHISOFT\ARCHICAD {version} Add-Ons\`

## Implementation Status

### ✅ Completed

- [x] Project structure and configuration
- [x] HTTP API server with endpoints
- [x] WebView2 integration for browser control
- [x] Web app integration module (archicad.svelte.js)
- [x] Add-in manifest file (.apx)
- [x] Build and deployment script
- [x] Documentation

### ⚠️ Needs Implementation

The following areas contain placeholder code and need actual Archicad API integration:

1. **Application.cs**:
   - Menu creation (Archicad uses different menu system)
   - Dockable pane/palette registration
   - Initialization pattern adaptation

2. **ArchicadApiServer.cs**:
   - Element selection by ID (needs Archicad API calls)
   - Element selection by GUID (needs Archicad element search)
   - IFC export (needs Archicad's IFC exporter API)
   - IFC configuration retrieval (needs Archicad's IFC settings API)

3. **StartupCommand.cs**:
   - Palette visibility toggling (needs Archicad palette API)

4. **Threading Model**:
   - Archicad's threading model differs from Revit's `ExternalEvent`
   - Need to adapt to Archicad's thread-safe API call mechanism

## Next Steps

1. **Install Archicad SDK**: 
   - Ensure Archicad API Development Kit is installed
   - Update DLL paths in `IfcTesterArchicad.csproj` to match your installation

2. **Implement Archicad API Calls**:
   - Reference Archicad API documentation
   - Replace placeholder code with actual API calls
   - Test each endpoint individually

3. **Browser Control Integration**:
   - Consider using Archicad's native CEF browser control instead of WebView2
   - Or adapt WebView2 integration to work within Archicad's window system

4. **Testing**:
   - Test add-in loading in Archicad
   - Test web app connection
   - Test element selection
   - Test IFC export

## Resources

- [Archicad API Documentation](https://archicadapi.graphisoft.com/)
- [Browser Control and JavaScript Connection](https://archicadapi.graphisoft.com/browser-control-and-javascript-connection)
- Archicad API Development Kit (installed with Archicad)

## Notes

- The implementation follows the same architecture as the Revit version for consistency
- The web application is shared between Revit and Archicad variants
- Port 48882 is used to avoid conflicts with Revit's port 48881
- The code includes extensive comments indicating where Archicad-specific implementation is needed
