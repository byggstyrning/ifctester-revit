# IfcTester Next - Web Application

The "next" version of IDS authoring and auditing on the web, integrated with Revit.

## Quick Start

### Development

```bash
npm install
npm run dev
```

The dev server runs on `http://localhost:5173` (or `http://10.13.42.120:5173` for network access).

### Production Build

```bash
npm run build
```

Outputs to `dist/` folder.

## Features

- **IDS Authoring**: Create and edit Information Delivery Specifications
- **IFC Validation**: Validate IFC models against IDS requirements using WebAssembly/Pyodide
- **Revit Integration**: Select elements in Revit directly from validation reports
- **Browser-Based**: Runs entirely in the browser - no server required

## Revit Integration

The web app integrates with Revit through a local HTTP API server running in the Revit plugin.

### How It Works

1. **Export IFC**: Revit plugin exports IFC and opens the web app
2. **Load IFC**: Web app loads the IFC file from Revit's local server
3. **Validate**: Validation runs in the browser using WebAssembly/Pyodide
4. **Select Elements**: Click elements in the report to select them in Revit

### Configuration

The Revit plugin automatically injects the API URL into the web app. The API server runs on `http://localhost:48881` by default.

### Element Selection

Elements are selected by their IFC GlobalId (GUID), not by Revit Element ID. This ensures accurate selection even when IFC files are modified.

## Project Structure

```
web/
├── src/
│   ├── modules/
│   │   ├── api/          # API integrations (Revit, Bonsai, IDS)
│   │   └── wasm/         # WebAssembly/Pyodide worker
│   ├── pages/            # Svelte page components
│   └── components/       # Reusable UI components
├── public/
│   ├── pyodide/          # Pyodide runtime files
│   └── worker/           # Python worker files and wheels
├── scripts/
│   └── download-packages.ps1  # Downloads Python packages for offline use
└── dist/                 # Build output
```

## Python Packages

The app uses Pyodide to run Python code in the browser. Required packages are downloaded during build:

- **ifctester**: IFC validation library
- **ifcopenshell**: IFC file handling
- **odfpy**: ODF file support
- **shapely**: Geometry operations

Run `.\scripts\download-packages.ps1` to download/update packages.

## Deployment

See the root `README.md` for deployment instructions using the unified deployment script.

## Troubleshooting

### Pyodide Packages Not Loading

- Ensure packages are downloaded: `.\scripts\download-packages.ps1`
- Check that `public/worker/bin/` contains the required `.whl` files
- Verify the build includes `public/` folder contents

### Revit Integration Not Working

- Verify Revit plugin is installed and running
- Check API server is accessible at `http://localhost:48881`
- Look for API URL in browser address bar (`?api=...`)
- Check browser console for errors

### Element Selection Fails

- Ensure elements have valid IFC GlobalId
- Check Revit plugin logs for errors
- Verify element exists in current Revit document

## Development Notes

- **Framework**: Svelte 5
- **Build Tool**: Vite
- **Styling**: Tailwind CSS
- **Python Runtime**: Pyodide (WebAssembly)

## Documentation

For detailed technical documentation, see:
- Root `README.md` - Project overview and deployment
- `PYODIDE_PACKAGES.md` - Python package management
- Revit plugin documentation in `../revit/`
