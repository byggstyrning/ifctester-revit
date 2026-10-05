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
- **Pset builder**: Build the Revit user-defined property set file for an IDS from the IDS and the open model
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

### Element inspector

The second button in the *Select* column of the audit tables opens the element in a panel on the right: its
IFC attributes and property sets next to the Revit parameters they come from, joined by the export setup's
property set file. Opened from a requirement, the panel scrolls to the property that requirement checks.

The two sides show next to each other, IFC in blue on the left and Revit in amber on the right, under one
filter. Hovering a row lights up its partner on the other side; clicking a parameter name jumps to it. The
panel is resized by dragging its left edge (the width is remembered in the browser).

- **IFC**: one row per IFC property of the element and its type (read from the loaded IFC with
  `get_element_properties` in `public/worker/api.py`), and one per mapping-file line that reaches the element
  without a property in the IFC. A line reaches the element when its set lists the element's IFC class or a
  supertype of it (`IfcElement` reaches `IfcWall`), or its type's. Each row shows the IFC value, the Revit
  parameter(s) the line reads (instance `I` or type `T`, a repeated line's fallbacks dimmed) and their values,
  and a status named by its cause: *Exported*, *Differs* (a text parameter holds another value than the IFC),
  *No parameter in Revit* (the pset file reads a parameter the element and its type lack), *Empty in Revit*,
  *Not exported* (Revit has a value the IFC lacks: an older export, or another pset file), *Entity case*
  (the set names the class in the wrong case, which the exporter skips silently) and *Not in pset file*
  (the property came from elsewhere, such as Revit's own sets).
- **Revit**: every instance and type parameter by group, with shared and read-only marked, and the IFC
  properties each one feeds. *Mapped only* keeps the parameters the mapping files read.

Revit is asked with `POST /element-parameters`, with the export setup and property set file override the
audited export was made with, as write-back does. Without an add-in that reports `"element-inspector"`, the
panel shows the IFC side only.

**Linking (editing the pset file).** Click a property on the IFC side and a parameter on the Revit side;
the bar at the bottom says what would change, then *Link* does it:

- a property with a line in the file gets that line's parameter column rewritten (the first line of a
  fallback chain); a property without one gets a new line, with a data type to pick, in a set of the same
  name that already reaches the element. Nothing else in the file changes: tabs, comments and line ends stay.
- a built-in parameter is written as `BuiltInParameter.<NAME>`, which does not depend on Revit's language.
- a set that also lists other classes is split by default (*Only IfcWindow*): the element's class leaves
  the set's header and gets its own copy of the whole set, with the change, right after it, marked by a
  comment; the other classes keep the set as it was. *All N classes in the set* changes the shared line
  instead. A class that is in the set only through a supertype (`IfcElement`) cannot be split off, because a
  set cannot leave one subclass out; the bar says so.
- *One set per class* (next to the file name) rewrites the whole draft that way at once: every set listing
  several classes becomes one set per class with a copy of its lines, so every later link changes one class
  only. Sets with one class, comments and lines before the first set stay.
- warnings: a type parameter goes into a set without type entities while the setup's *Use type properties
  in instance property sets* is off, the parameter is empty.
- a set whose entity is in the wrong case gets a *Fix case* button that rewrites its header.

The edits collect in a draft of the file (`GET /pset-files/read`; a file that is not UTF-8 is refused, since
saving it as UTF-8 would garble å, ä and ö). Revit reads the draft in place of the file, so the panel shows
the effect at once: a fixed property typically turns *Not exported* until the next export. *Review* lists
each line old and new, *Undo* takes the last edit back, and *Save* writes the draft next to the original as
`<name>-edited.txt` (or any path; replacing an existing file is confirmed) with `POST /pset-files/save`.
*Use for next export* sets it as the toolbar's property set file override, so *Export IFC* and *Run Audit*
check the result.

The code is in `src/modules/api/inspector.svelte.ts` (state, API calls, the join and link planning, no UI),
`src/modules/api/psetDraft.svelte.ts` (the draft), `src/modules/psetBuilder/psetEdit.ts` (line edits) and
`src/components/inspector/ElementInspector.svelte`.

### Pset builder

The IDS says which property sets and properties a delivery must contain; a Revit user-defined property
set file only says which Revit parameter each of them is read from. The **Pset builder** mode (next to
Editor and Viewer, for the active IDS) lets a designer write that file from the IDS and their own model, then
export with it and audit. The project's official pset files are not used as a reference.

1. **Rows from the IDS**: one row per required `Property` facet whose property set and name are simple values,
   merged across specifications. The data type comes from `@dataType` (`IFCLABEL` gives `Label`,
   `IFCPOSITIVELENGTHMEASURE` `PositiveLength`, every type the exporter knows); without one the row gets `Label`
   and is marked. The entities come from the specification's applicability entity facets, with their names
   taken from the IFC schema (IFC2X3 or IFC4, picked in the builder; default: the selected export setup's
   schema, otherwise IFC2X3). Entity names the schema lacks are flagged. What cannot become a row is listed
   under *Not covered* with the reason: attribute, classification, material and part-of requirements,
   a restriction or pattern on the property set or name, prohibited properties, and applicability by more
   than an entity (the set then applies to all elements of the entity).
2. **Suggest from model** (`POST /pset-suggestions`): for each row, the Revit parameters the exporter would read
   it from by name (spaces and case ignored, as the exporter does), then `<Pset>.<Property>`; nothing when
   nothing matches, never a fuzzy guess. Only elements whose IFC class is one of the row's entities are
   scanned (IfcExportAs, then the document's IFC category mapping); when none is found every model element
   is, and the row says so. The row shows the parameter, instance or type, the coverage and a warning
   for a project parameter (Revit 2025 and 2026 export them, checked in real exports; older versions
   were not checked), a read-only parameter or low coverage.
   Unmapped rows are red. A type parameter moves the row to the type entity (`IfcWall` becomes `IfcWallType`,
   `IfcDoorStyle` in IFC2X3), because the exporter applies a set to the entities it lists and ignores the I/T
   column.
3. **Editing**: type a parameter or pick one of the model's (`GET /model-parameters`), or `BuiltInParameter.X`;
   *+ fallback* adds a parameter that becomes a repeated line (the exporter's fallback chain); I/T switches
   instance and type; the entity list and data type can be edited; the checkbox leaves a row out. Rows the user
   edited are not overwritten by a later *Suggest from model*.
4. **Generate**: the preview shows the file: one `PropertySet:` block per property set, instance/type and entity
   list, tab separated, CRLF, with a header naming the IDS and the date. An unmapped row is written as a
   comment (`#<TAB>Name<TAB>Type<TAB>(no Revit parameter)`) so the gap shows; a row without entities is not written.
5. **Save and use**: *Save* writes it on the Revit machine (`POST /pset-files/save`, UTF-8 without BOM like the
   project's pset files; default `%LOCALAPPDATA%\IfcTesterRevit\psets\<IDS title> pset.txt`, the path is
   editable, an existing file is only replaced after *Overwrite*). *Use for next export* saves and sets the
   property set file override, so the next *Export IFC* uses it. *Download* works without Revit.
6. **Keep work**: the rows are kept in localStorage per IDS title and version. *Open pset file...* reads an
   existing file into the table (the matching rows take its mappings, other properties are added as rows).

Without an add-in that reports `"pset-builder"`, the builder opens for drafting: Suggest, Save and Use for next
export are hidden; Open, Download and the preview work.

The code is in `src/modules/psetBuilder/psetFile.ts` (IDS to rows, generator, parser, no UI),
`src/modules/api/psetBuilder.svelte.ts` (state, API calls, persistence) and `src/pages/Home/PsetBuilder.svelte`.
Entity names come from `get_entity_tree` in `public/worker/api.py`.

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
| Pset builder | same `npm run test:e2e` (`tests/e2e/pset-builder.spec.ts`) | the generator against the real schemas (entity casing, instance/type split, fallbacks, data types, round trip through the parser), and the UI against a mocked Revit API: suggestions, editing, save with overwrite, use for the next export, kept work, and an older add-in |

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
