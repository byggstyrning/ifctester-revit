# Revit Integration for ifctester-next

This document explains how to use the Revit integration with the hosted ifctester-next application at `validate.byggstyrning.se`.

## Overview

The Revit integration allows you to:
1. Export IFC files from Revit
2. **Validate them in the browser** using WebAssembly/Pyodide (not in Revit)
3. Select elements in Revit directly from the validation report

**Important**: Validation happens entirely in the browser using WebAssembly/Pyodide. Revit only provides:
- The IFC file (via HTTP endpoint)
- Element selection (via HTTP POST endpoint)

## Setup

### 1. Ensure pyRevit Routes API is Running

The pyValidator extension must be installed and running in Revit. This provides the HTTP API endpoints needed for communication.

### 2. Configure the Hosted URL

The pyRevit script is configured to use `http://validate.byggstyrning.se` (or `https://validate.byggstyrning.se` if HTTPS is configured).

### 3. How It Works

1. **Export IFC**: Click "Export & Validate" in Revit
   - The IFC file is exported to a temp directory
   - The file is served via pyRevit Routes API at `http://localhost:48884/ifc-validator/ifc/<filename>`

2. **Open Validation Page**: The browser opens `validate.byggstyrning.se` with:
   - `?api=http://localhost:48884/ifc-validator` - API endpoint URL for element selection
   - `?ifc=http://localhost:48884/ifc-validator/ifc/<filename>` - IFC file URL (optional, auto-loaded)

3. **Load IFC**: 
   - The web app automatically loads the IFC file from Revit's local server
   - The IFC is loaded into browser memory using WebAssembly/Pyodide

4. **Validate**: 
   - Create or load an IDS document
   - Click "Run Revit Audit" to validate the IFC against the IDS
   - **Validation happens entirely in the browser** using WebAssembly/Pyodide

5. **Select Elements**: 
   - Click on any GlobalId in the validation report
   - The element will be selected and zoomed to in Revit via the pyRevit Routes API

## API Endpoints

The pyRevit extension provides these endpoints:

- `GET /ifc-validator/ifc/<filename>` - Serve IFC files
- `POST /ifc-validator/select-element/` - Select element by IFC GUID

The integration also uses the standard pyRevit Routes API endpoint:

- `GET /routes/status` - Health check (standard pyRevit endpoint)

**Note**: There is NO `/validate-ids/` endpoint because validation happens in the browser, not in Revit.

## Hosting at validate.byggstyrning.se

### Option 1: Cloudflare Pages (Recommended)

1. Build the ifctester-next application:
   ```bash
   cd C:\code\ifctester-next
   npm install
   npm run build
   ```

2. Deploy to Cloudflare Pages:
   - Connect your GitHub repository
   - Set build command: `npm run build`
   - Set output directory: `dist`
   - Set custom domain: `validate.byggstyrning.se`

### Option 2: Static Hosting

1. Build the application:
   ```bash
   cd C:\code\ifctester-next
   npm install
   npm run build
   ```

2. Upload the `dist` folder contents to your web server

3. Configure your web server to serve the files with proper CORS headers

### Option 3: Vite Dev Server (Development)

For development/testing:
```bash
cd C:\code\ifctester-next
npm install
npm run dev
```

## Troubleshooting

### CORS Issues

If you see CORS errors:
- Ensure pyRevit Routes API is running
- Check that the API URL in the browser matches `http://localhost:48884/ifc-validator`
- Verify CORS headers are set in pyRevit routes (already configured)

### Element Selection Not Working

- Verify Revit integration is enabled (check URL for `?api=` parameter)
- Check browser console for errors
- Ensure pyRevit Routes API is accessible
- Verify the element exists in the current Revit document

### IFC File Not Loading

- Check that IFC was exported successfully
- Verify the file exists at `%TEMP%\pyrevit_ifc_exports\`
- Check pyRevit Routes API logs for errors
- Verify the IFC URL in browser address bar

### Validation Not Working

- Ensure WebAssembly/Pyodide is loading correctly
- Check browser console for errors
- Verify IDS document is valid
- Check that IFC file loaded successfully

## Development

To modify the integration:

1. **Revit API Module**: `C:\code\ifctester-next\src\modules\api\revit.svelte.js`
   - Handles connection to Revit
   - Loads IFC files from Revit
   - Calls browser-based validation
   - Selects elements in Revit

2. **UI Component**: `C:\code\ifctester-next\src\components\AppToolbar.svelte`
   - Revit integration tab
   - Connection status
   - Audit button

3. **Report Viewer**: `C:\code\ifctester-next\src\pages\Home\IdsViewer.svelte`
   - Clickable GlobalId cells for element selection

4. **pyRevit Routes**: `pyValidator.extension\startup.py`
   - `/ping/` - Health check
   - `/ifc/<filename>` - Serve IFC files
   - `/select-element/` - Select elements

## Notes

- Validation happens entirely in the browser using WebAssembly/Pyodide
- Revit only provides IFC files and element selection
- Element selection requires the element to exist in the current Revit document
- IFC GUIDs must match between the IFC file and Revit elements
- The integration automatically loads the IFC file from Revit when the page opens

