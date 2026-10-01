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
- **Revit Write-back**: Type the correct value next to a failed element and apply it to the open Revit model
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

### Property set file override

*Export Active View as IFC* exports with a named IFC export setup. When the setup's user-defined property
set file lives on a drive this PC cannot reach, Revit skips it without a word and the export has no project
property sets. When the add-in reports `"export-overrides"` in `GET /status` capabilities, the toolbar shows
under the setup dropdown:

- the setup's own property set file (`GET /ifc-configuration-files?name=`), marked *not found* when missing;
- a dropdown of the `*.txt` files in a folder (`GET /pset-files?dir=`), default
  `\\bim-byggp1.hogerklick.bim\H29\BIM-tools\pset`, editable under *Folder* and remembered in localStorage;
- a text field for any other path;
- the same, collapsed, for the parameter mapping table.

A chosen file is sent with `POST /export-ifc` as `psetFile` / `parameterMappingFile`. The add-in puts it on its
temporary copy of the setup only; the setup saved in the model never changes. A missing override file fails the
export rather than exporting without it. `GET /export-status/{id}` reports the files the export read
(`exportFiles`) and a `warning` when the setup's own file is missing; the page shows that in one line under the
controls. A model exported with an override is labelled *Pset override: &lt;file&gt;* in the model list and above
the audit results, so it is not mistaken for the delivery export. Against an older add-in the controls stay
hidden and the export is sent as before.

### Write-back

When the connected add-in reports `"capabilities": ["writeback"]` in `GET /status`, the failed-elements
tables in the audit results get a **Fix in Revit** column.

1. **Enter fixes**: type a value next to a failed element, or use *Set all N failed to* for a whole
   requirement. A requirement with one allowed value prefills it; an enumeration is offered as suggestions.
2. **Review**: *Review changes* asks Revit (`POST /resolve-parameters`) which parameter on which element each
   change would write to, and shows the current and the new value. With several candidate parameters the
   user picks one. Type parameters are marked, because they change every instance of the type. The request
   names the setup and any property set file override the audited export was made with, so Revit reads the
   same mapping files the export did (without them it falls back to its own last export).
3. **Apply**: *Apply to Revit* sends the reviewed changes (`POST /apply-changes`). Revit writes them in one
   transaction, so one Ctrl+Z undoes them. Changes Revit refuses stay pending with its message.
4. **Confirm**: *Re-export and re-audit* runs the normal export with the configuration selected in the toolbar,
   and with its property set file override if one is chosen.

In scope are `Property` requirements that name one property set and one property, and `Attribute`
requirements for `Name`, `Description`, `ObjectType` and `LongName`. Every other failure shows the reason
in place of an input. Pending changes belong to one audit and are dropped when it is replaced.

The code is in `src/modules/api/writeback.svelte.ts` (state and the two API calls) and
`src/components/writeback/`.

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

Covers the web app / Pyodide / ifctester side only (not the Revit or ArchiCAD code; the write-back spec mocks the Revit API).

| What | Command | Notes |
|---|---|---|
| Types, lint, version pins | `npm run check` | tsc, svelte-check, biome, `scripts/check-versions.mjs` |
| Native reference | `pip install -r tests/python/requirements.txt && pytest tests/python` | desktop ifctester 0.9 vs `tests/fixtures/expected-*.json` |
| In-browser audit | `bash scripts/download-packages.sh && npx playwright install chromium && npm run test:e2e` | real worker (Pyodide + wasm ifcopenshell + ifctester); must reproduce the native reference |
| Revit write-back | same `npm run test:e2e` (`tests/e2e/writeback.spec.ts`) | the real UI against a mocked Revit API (Playwright route interception); asserts the bodies sent to `/resolve-parameters` and `/apply-changes` |
| Pset file override | same `npm run test:e2e` (`tests/e2e/export-overrides.spec.ts`) | mocked Revit API; asserts `psetFile` in the `/export-ifc` and `/resolve-parameters` bodies, the status line and the override labels, and that an older add-in gets no controls |

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
