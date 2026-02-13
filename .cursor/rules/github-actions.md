# GitHub Actions CI/CD - Implementation Notes

## Overview

This project uses GitHub Actions to build plugins for Revit (Windows) and ArchiCAD (Windows + macOS). All workflows live in `.github/workflows/`.

## Project Architecture

```
ifctester-revit/
├── web/            Svelte/Vite web app (shared by all plugins)
├── revit/          Revit C# plugin (.NET 8 / .NET Framework 4.8)
├── archicad/       ArchiCAD C++ add-on (CMake)
├── installer/      Inno Setup scripts + templates
├── dist/           Local installer output (gitignored contents mixed with tracked old exes)
├── scripts/        PowerShell/shell build scripts
└── .github/workflows/
    ├── build-windows.yml   Revit plugin + installer (working)
    └── build-mac.yml       ArchiCAD macOS (needs fixes)
```

## Working Workflow: `build-windows.yml`

**Status:** Fully working, builds Revit plugin + Inno Setup installer.

### Pipeline Steps

1. **Checkout** → `actions/checkout@v4`
2. **Read version** → Extract `AppVersion` from `installer/IfcTesterRevit.iss` for artifact naming
3. **Setup Node.js 18** → `actions/setup-node@v4` (no built-in cache, see gotchas)
4. **Cache npm** → `actions/cache@v4` keyed on `web/package.json`
5. **Setup .NET 8** → `actions/setup-dotnet@v4`
6. **npm install** → `web/` directory (not `npm ci`, see gotchas)
7. **Download Pyodide packages** → Runs `web/scripts/download-packages.ps1` to fetch Python wheels
8. **Build web app** → `npm run build` in `web/`
9. **Build Revit R25** → `dotnet build -c "Release R25"`
10. **Build Revit R26** → `dotnet build -c "Release R26"`
11. **Prepare staging** → Copy publish output + web dist into `installer/staging/`
12. **Generate .addin files** → Template substitution from `installer/templates/`
13. **Install Inno Setup** → `choco install innosetup`
14. **Compile installer** → `ISCC.exe` outputs to clean `installer-output/` directory
15. **Upload artifact** → Named `IfcTesterRevit-Setup-v{version}`, 30-day retention

### Key Decisions & Gotchas

#### `npm install` vs `npm ci`
- **`package-lock.json` is gitignored** (`.gitignore` line: `**/package-lock.json`)
- Therefore `npm ci` fails on CI since the lock file doesn't exist
- Use `npm install` instead

#### Node.js cache
- The built-in `cache: 'npm'` option in `actions/setup-node` fails on Windows when `cache-dependency-path` points to a missing file
- Use a separate `actions/cache@v4` step with `path: ~/.npm` keyed on `web/package.json`

#### .NET configuration-dependent target framework
- The `.csproj` uses conditional `<TargetFramework>` based on configuration name (e.g., `"Release R25"` → `net8.0-windows`)
- A bare `dotnet restore` without `-c` fails because it can't determine the target framework
- Solution: skip separate restore, let `dotnet build` handle restore implicitly

#### Pyodide Python packages
- The web app bundles Pyodide + Python wheel files (~34 MB) for offline IFC validation
- These wheels live in `web/public/worker/bin/` which is **gitignored by `**/bin/`**
- The `web/scripts/download-packages.ps1` script downloads them from PyPI/GitHub Releases
- **Must run this script in CI before `npm run build`**, otherwise the installer is ~34 MB smaller than expected

#### Installer artifact isolation
- The `dist/` folder in git contains old installer exes from previous versions
- Output the new installer to a separate `installer-output/` directory (not `dist/`)
- This ensures `upload-artifact` only picks up the newly built exe

#### Revit publish directory structure
- `Nice3point.Revit.Build.Tasks` creates: `revit/bin/Release R25/publish/Revit 2025 Release R25 addin/IfcTesterRevit/`
- The staging step finds this directory with: `Get-ChildItem -Filter "*addin"`

#### Code signing
- Skipped in CI (no certificate configured)
- To enable: store `.pfx` as a GitHub encrypted secret, add signtool step after installer build

## Existing Workflow: `build-mac.yml`

**Status:** Not working. Needs the same fixes applied.

### Known Issues to Fix

1. **Uses `npm ci`** → Should use `npm install` (same `package-lock.json` issue)
2. **Uses built-in Node.js cache** with `cache-dependency-path: web/package-lock.json` → Will fail
3. **Missing Pyodide download step** → Need to run `web/scripts/download-packages.sh` (the shell version)
4. **ArchiCAD DevKit not available** → The API DevKit is not publicly available as a package; requires either:
   - A self-hosted runner with the DevKit installed
   - Caching the DevKit as a build artifact
   - Downloading from a private location
5. **No installer step** → `installer/IfcTesterArchiCAD.iss` exists but isn't compiled in the workflow

## Future: ArchiCAD Windows Build

Does not exist yet. Would need:
- `windows-latest` runner
- CMake + MSVC build (Visual Studio 2022 is pre-installed on `windows-latest`)
- ArchiCAD API DevKit (same availability issue as macOS)
- Inno Setup for `installer/IfcTesterArchiCAD.iss`

## Build Timing Reference (Windows)

From successful Run #6 (`build-windows.yml`):
- Total: ~2.5 minutes
- Setup (.NET + Node.js): ~45s
- Web build (install + download + build): ~45s
- Revit build (R25 + R26): ~30s
- Installer (Inno Setup install + compile): ~30s

## Trigger Configuration

Both workflows use path-filtered triggers:
```yaml
push:
  branches: [main, develop]
  paths: ['relevant-dir/**', '.github/workflows/this-file.yml']
pull_request:
  branches: [main, develop]
  paths: ['relevant-dir/**', '.github/workflows/this-file.yml']
workflow_dispatch:  # Manual trigger
```

Add temporary branch names (e.g., `fix/revit-installer-detection`) to `push.branches` when testing workflow changes on a feature branch. Remove them before merging.

## Actions Used

| Action | Version | Purpose |
|--------|---------|---------|
| `actions/checkout` | v4 | Clone repo |
| `actions/setup-node` | v4 | Node.js 18 |
| `actions/setup-dotnet` | v4 | .NET 8 SDK |
| `actions/setup-python` | v5 | Python 3 (macOS build) |
| `actions/cache` | v4 | npm cache |
| `actions/upload-artifact` | v4 | Build artifacts, 30-day retention |

Inno Setup installed via Chocolatey (`choco install innosetup`) on Windows runners.
