# Pyodide Package Download - Summary

## Problem
When the web app is served locally via WebView2 (using `app.localhost` virtual host), Pyodide cannot fetch Python packages from PyPI because:
1. The web app runs in a local file context
2. Network requests to external URLs may be blocked
3. Micropip tries to download packages from PyPI which fails

## Solution
Download required Python packages during the build process and serve them locally.

## Implementation

### 1. Download Script
**File**: `web/scripts/download-packages.ps1`
- Automatically fetches the latest ifctester version from PyPI
- Downloads the wheel file to `public/worker/bin/`
- Can be run standalone or as part of the deployment script

### 2. Updated Worker
**File**: `web/src/modules/wasm/worker/worker.js`
- Tries to install ifctester from local file first (`config.wasm.ifctester_url`)
- Falls back to PyPI if local file not available
- Provides clear error messages if installation fails

### 3. Updated Config
**File**: `web/src/config.json`
- Added `ifctester_url` pointing to local wheel file
- Path: `/worker/bin/ifctester-0.8.3-py3-none-any.whl`

### 4. Deployment Integration
**File**: `deploy.ps1`
- Step 0: Downloads Python packages before building
- Ensures all required wheels are available locally

## Usage

### Standalone Download
```powershell
cd web
.\scripts\download-packages.ps1
```

### As Part of Deployment
```powershell
.\deploy.ps1
```
The deployment script automatically downloads packages before building.

## Files Structure

```
web/
├── public/
│   ├── worker/
│   │   ├── bin/
│   │   │   ├── ifcopenshell-0.8.3+bb329af-cp313-cp313-emscripten_4_0_9_wasm32.whl
│   │   │   ├── odfpy-1.4.2-py2.py3-none-any.whl
│   │   │   └── ifctester-0.8.3-py3-none-any.whl  # NEW
│   │   └── api.py
│   └── pyodide/
└── scripts/
    └── download-packages.ps1  # NEW
```

## How It Works

1. **Build Time**: `download-packages.ps1` downloads ifctester wheel to `public/worker/bin/`
2. **Vite Build**: Copies `public/` contents to `dist/` root
3. **Runtime**: Worker loads config.json, finds `ifctester_url`, and installs from local file
4. **Fallback**: If local file missing, tries PyPI URL (may fail in offline/local deployment)

## Benefits

- ✅ Works offline - no internet required at runtime
- ✅ Faster initialization - no network requests
- ✅ More reliable - no dependency on PyPI availability
- ✅ Version controlled - specific version is bundled

## Notes

- The ifctester wheel is ~26 KB
- Vite automatically copies `public/` to `dist/` during build
- The wheel file is served at `/worker/bin/ifctester-0.8.3-py3-none-any.whl` in the built app
- If you need to update ifctester, run `download-packages.ps1` again (it checks for latest version)


