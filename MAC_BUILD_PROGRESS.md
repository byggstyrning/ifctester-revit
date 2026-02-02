# macOS Build Progress for IfcTester ArchiCAD 29

**Date:** February 2, 2026  
**Status:** Build Successful - Deployed to ArchiCAD 29 with all WASM wheels

## Summary

Successfully created macOS build scripts and compiled the IfcTester ArchiCAD add-on for AC29. The universal binary (arm64 + x86_64) was built successfully with all required Python WASM wheels.

### Python Wheels (Fixed Feb 2, 2026)

All required wheels are now properly downloaded and bundled:

| Package | Version | Size | Source |
|---------|---------|------|--------|
| ifcopenshell | 0.8.3+34a1bc6 (cp313/emscripten 4.0.9) | 10 MB | IfcOpenShell/wasm-wheels |
| ifctester | 0.8.4 | 22 MB | PyPI |
| odfpy | 1.4.2 | 134 KB | IfcOpenShell repo (custom wheel) |

**Key fixes:**
- ifcopenshell WASM wheel now downloads from `IfcOpenShell/wasm-wheels` repo (GitHub releases don't have WASM builds)
- odfpy uses custom wheel from IfcOpenShell repo (PyPI doesn't publish odfpy wheels)
- Updated `web/src/config.json` to match actual wheel filenames
- Updated `web/scripts/download-packages.sh` with correct URLs

---

## Completed Tasks

### 1. Repository Setup
- Cloned https://github.com/byggstyrning/ifctester-revit
- Repository contains both Revit and ArchiCAD plugins

### 2. Prerequisites Installed
All build tools were installed to `~/.local/bin`:

| Tool | Version | Status |
|------|---------|--------|
| Xcode Command Line Tools | 17.0 | ✅ Pre-installed |
| CMake | 3.28.1 | ✅ Installed |
| Node.js | 20.11.0 | ✅ Installed |
| npm | 10.2.4 | ✅ Installed |
| Python 3 | 3.9.6 | ✅ Pre-installed |
| ArchiCAD API DevKit 29 | 29.3100 | ✅ Downloaded & installed |

**DevKit Location:** `~/Library/Application Support/GRAPHISOFT/API Development Kit 29`

### 3. macOS Build Scripts Created

New scripts added to `scripts/`:

| Script | Purpose |
|--------|---------|
| `scripts/setup-mac.sh` | Interactive setup - downloads DevKit, checks prerequisites |
| `scripts/dev-mac.sh` | Interactive development menu (like Windows dev.ps1) |
| `scripts/utils/common-mac.sh` | Shared utilities for all mac scripts |
| `scripts/web/build-mac.sh` | Build the Svelte/Vite web application |
| `scripts/archicad/build-mac.sh` | Build ArchiCAD add-on with CMake/Xcode |
| `scripts/archicad/deploy-mac.sh` | Deploy to ArchiCAD Add-Ons folder |
| `scripts/archicad/installer-mac.sh` | Create DMG installer |

### 4. Source Code Cross-Platform Fixes

Modified for macOS compatibility:

**`archicad/Src/ArchiCADApiServer.hpp`**
- Added POSIX socket headers (`sys/socket.h`, `netinet/in.h`, etc.)
- Defined cross-platform socket types (`SOCKET`, `INVALID_SOCKET`, `closesocket`)
- Wrapped Windows message types (`HWND`, `WM_USER`) in `#ifdef _WIN32`

**`archicad/Src/ArchiCADApiServer.cpp`**
- Added POSIX socket initialization (no WSAStartup needed)
- Added `fcntl()` for non-blocking mode (replaces `ioctlsocket`)
- Added `struct timeval` for socket timeouts (replaces `DWORD`)
- Fixed error handling with `errno` instead of `WSAGetLastError()`
- Platform-specific path separators (`/` vs `\`)

**`archicad/Src/Main.cpp`**
- Wrapped Windows message window code in `#ifdef _WIN32`
- Added macOS stubs for `CreateMessageWindow()` / `DestroyMessageWindow()`
- Added macOS-specific WebApp path search (inside `.bundle/Contents/Resources/`)
- Fixed path separators for file operations

**`archicad/CMakeLists.txt`**
- Fixed framework linking to use full paths instead of `-framework` flags
- Changed `target_link_libraries` to use keyword signatures (PUBLIC)
- Frameworks are now linked as: `target_link_libraries(target PUBLIC "/path/to/Framework.framework")`

### 5. Build Results

**Web App:** ✅ Built successfully
- Output: `web/dist/` (28 files, 18 MB)

**ArchiCAD Add-On:** ✅ Compiled and linked successfully
- Output: `archicad/cmake-build/Release/IfcTesterArchiCAD.bundle`
- Architecture: Universal binary (arm64 + x86_64)
- Size: ~900 KB

**Code Signing:** ✅ Disabled for development
- Automatic code signing disabled in CMakeLists.txt (`.whl` files cannot be signed)
- For distribution, sign manually after build with ad-hoc signing

---

## Remaining Tasks

### Completed
1. ~~**Fix code signing**~~ ✅ Done - Disabled automatic code signing in CMakeLists.txt
2. ~~**Fix Python wheel downloads**~~ ✅ Done - Updated URLs to use wasm-wheels repo and IfcOpenShell custom odfpy wheel
3. ~~**Build and deploy**~~ ✅ Done - Universal binary deployed to ArchiCAD 29 Add-Ons folder

### To Test
4. **Test the add-on** - Load in ArchiCAD 29 and verify functionality:
   - Open ArchiCAD 29
   - Check Window > Palettes > Report for startup messages
   - Look for "IfcTester ArchiCAD Add-On v1.1.0"
   - Look for "IfcTester: API server started on http://127.0.0.1:48882"

### Completed (Feb 2, 2026)
5. ~~**Implement macOS message queue**~~ ✅ Done - Implemented Grand Central Dispatch (GCD) for main thread callbacks
   - Export and selection requests now use `dispatch_async(dispatch_get_main_queue(), ...)` 
   - This triggers `ProcessExportQueue()` and `ProcessSelectionQueue()` on the main thread
   - Matches the Windows behavior using message passing

### Medium Priority
6. **Create DMG installer** - The `installer-mac.sh` script is ready but needs testing

### Low Priority
7. **Add notarization support** - For distribution outside the Mac App Store

---

## How to Build

```bash
# 1. Set up PATH (add to ~/.zshrc for persistence)
export PATH="$HOME/.local/bin:$PATH"

# 2. Build web app
./scripts/web/build-mac.sh

# 3. Build ArchiCAD add-on
./scripts/archicad/build-mac.sh

# 4. Deploy to ArchiCAD (optional)
./scripts/archicad/deploy-mac.sh
```

Or use the interactive menu:
```bash
./scripts/dev-mac.sh
```

---

## Installation Location

The built bundle should be copied to:
```
~/Library/Application Support/GRAPHISOFT/ArchiCAD 29/Add-Ons/IfcTesterArchiCAD/
```

---

## Known Issues

1. ~~**Code signing fails**~~ - Fixed: Disabled automatic signing in CMakeLists.txt
2. **Selection from web app** won't work on macOS yet - Windows message passing not ported
3. **Export from web app** won't work on macOS yet - same reason as above

---

## File Changes Summary

```
Modified:
  archicad/CMakeLists.txt           - Framework linking fixes, disabled code signing, fixed WebApp path
  archicad/Src/ArchiCADApiServer.hpp - Cross-platform socket/message types
  archicad/Src/ArchiCADApiServer.cpp - POSIX socket implementation + GCD main thread dispatch
  archicad/Src/Main.cpp             - macOS message window stubs, path fixes, GCD logging
  web/src/config.json               - Updated wheel filenames to match actual downloads
  web/scripts/download-packages.sh  - Fixed URLs for wasm-wheels and odfpy

Added:
  scripts/setup-mac.sh              - Setup script
  scripts/dev-mac.sh                - Interactive dev menu
  scripts/utils/common-mac.sh       - Shared utilities
  scripts/web/build-mac.sh          - Web build script
  scripts/archicad/build-mac.sh     - ArchiCAD build script
  scripts/archicad/deploy-mac.sh    - Deployment script
  scripts/archicad/installer-mac.sh - DMG installer script
  MAC_BUILD_PROGRESS.md             - This file

Key macOS changes (Feb 2, 2026):
  - Added #include <dispatch/dispatch.h> for GCD
  - QueueSelectionRequest() now uses dispatch_async() to call ProcessSelectionQueue() on main thread
  - QueueExportRequest() now uses dispatch_async() to call ProcessExportQueue() on main thread
```
