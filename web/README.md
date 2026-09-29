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

- **ifctester** 0.9.0: IFC validation library (pinned, must match `src/config.json`)
- **ifcopenshell** 0.8.5+a51b2c5 (Pyodide 0.28 wasm build): IFC file handling
- **odfpy**: ODF file support
- **shapely**, **sqlite3**: bundled with Pyodide (`sqlite3` is imported by ifctester >= 0.9's reporter)

Run `.\scripts\download-packages.ps1` (or `scripts/download-packages.sh`) to download/update packages.

### ifctester 0.9 / ifcopenshell compatibility

ifctester 0.9 calls `inst.get_attribute_category()`, `inst.get_argument_index()`,
`inst.get_inverse_attribute_names()` and `inst.declaration` directly on
`ifcopenshell.entity_instance`, but the available Pyodide 0.28 wasm build of ifcopenshell
(0.8.5+a51b2c5, the one the official IfcTester webapp bundles) only exposes them on
`inst.wrapped_data`. `public/worker/api.py` therefore installs a small shim
(`install_ifcopenshell_compat`) that is a no-op on builds that already provide them.
The newer 0.9.x wasm builds (`pyemscripten_2025_0`) could not be loaded under Pyodide 0.28
("Unable to resolve module path"), so they are not used yet. Remove the shim once a compatible
ifcopenshell wasm build is available.

### Code layout

The app follows the upstream IfcTester webapp (TypeScript, `npm run check` = tsc + svelte-check + biome).
Our Revit/ArchiCAD integrations (`src/modules/api/revit.svelte.js`, `archicad.svelte.js`) and
`src/lib/components/ui/copyable-text` remain plain JavaScript and are imported with an explicit `.js` extension.

## Tests (CI: `.github/workflows/web-tests.yml`)

Covers the web app / Pyodide / ifctester side only (not the Revit or ArchiCAD code).

| What | Command | Notes |
|---|---|---|
| Types, lint, version pins | `npm run check` | tsc, svelte-check, biome, `scripts/check-versions.mjs` |
| Native reference | `pip install -r tests/python/requirements.txt && pytest tests/python` | desktop ifctester 0.9 vs `tests/fixtures/expected-*.json` |
| In-browser audit | `bash scripts/download-packages.sh && npx playwright install chromium && npm run test:e2e` | real worker (Pyodide + wasm ifcopenshell + ifctester); must reproduce the native reference |

Fixtures live in `tests/fixtures` (IFC4 + IFC2X3 model, IDS files covering every facet type, prohibited and optional specs).
After an intentional ifctester behaviour change regenerate the expectations with
`python tests/python/generate_expected.py` and review the diff. A weekly job also runs the newest released
ifctester/ifcopenshell against the reference and warns when the compat shim in `public/worker/api.py` is no longer needed.

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
