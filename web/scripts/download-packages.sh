#!/bin/bash
# Download Python packages for Pyodide (macOS/Linux version)

set -e

echo "========================================"
echo "Downloading Pyodide Packages"
echo "========================================"
echo ""

# Get script directory and navigate to web root
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
WEB_DIR="$(dirname "$SCRIPT_DIR")"
BIN_DIR="$WEB_DIR/public/worker/bin"

# Ensure directory exists
mkdir -p "$BIN_DIR"

echo "Output directory: $BIN_DIR"
echo ""

# Download ifctester from PyPI
echo "Checking latest ifctester version..."
IFCTESTER_JSON=$(curl -s "https://pypi.org/pypi/ifctester/json")
IFCTESTER_VERSION=$(echo "$IFCTESTER_JSON" | python3 -c "import sys, json; releases = [k for k in json.load(sys.stdin)['releases'].keys() if 'dev' not in k and 'a' not in k and 'b' not in k and 'rc' not in k]; print(sorted(releases, key=lambda x: [int(p) for p in x.split('.')])[-1])" 2>/dev/null || echo "0.8.3")
IFCTESTER_URL=$(echo "$IFCTESTER_JSON" | python3 -c "import sys, json; files = json.load(sys.stdin)['releases']['$IFCTESTER_VERSION']; wheel = next((f for f in files if f['filename'].endswith('.whl')), None); print(wheel['url'] if wheel else '')" 2>/dev/null)
IFCTESTER_FILENAME=$(basename "$IFCTESTER_URL" 2>/dev/null || echo "ifctester-$IFCTESTER_VERSION-py3-none-any.whl")

if [ -z "$IFCTESTER_URL" ]; then
    echo "  WARNING: Could not fetch version info, using fallback"
    IFCTESTER_URL="https://files.pythonhosted.org/packages/8c/98/98afa5fa347361b8d0f421b1c5059ef960a455f89b8235e6ceed33c0e796/ifctester-0.8.3-py3-none-any.whl"
    IFCTESTER_FILENAME="ifctester-0.8.3-py3-none-any.whl"
else
    echo "  Found ifctester version $IFCTESTER_VERSION"
fi

# Download odfpy from PyPI
echo "Checking latest odfpy version..."
ODFPY_JSON=$(curl -s "https://pypi.org/pypi/odfpy/json")
ODFPY_VERSION=$(echo "$ODFPY_JSON" | python3 -c "import sys, json, re; releases = [k for k in json.load(sys.stdin)['releases'].keys() if re.match(r'^\d+\.\d+\.\d+$', k)]; print(sorted(releases, key=lambda x: [int(p) for p in x.split('.')])[-1])" 2>/dev/null || echo "1.4.2")
ODFPY_URL=$(echo "$ODFPY_JSON" | python3 -c "import sys, json; files = json.load(sys.stdin)['releases']['$ODFPY_VERSION']; wheel = next((f for f in files if f['filename'].endswith('.whl')), None); print(wheel['url'] if wheel else '')" 2>/dev/null)
ODFPY_FILENAME=$(basename "$ODFPY_URL" 2>/dev/null || echo "odfpy-$ODFPY_VERSION-py2.py3-none-any.whl")

# odfpy doesn't publish wheels on PyPI, use custom wheel from IfcOpenShell repo
echo "  Using custom odfpy wheel from IfcOpenShell repo (PyPI has no wheels)"
ODFPY_URL="https://raw.githubusercontent.com/IfcOpenShell/IfcOpenShell/v0.8.0/src/ifctester/webapp/public/worker/bin/odfpy-1.4.2-py2.py3-none-any.whl"
ODFPY_FILENAME="odfpy-1.4.2-py2.py3-none-any.whl"

# Download ifctester
if [ -f "$BIN_DIR/$IFCTESTER_FILENAME" ]; then
    echo "[OK] ifctester already exists, skipping..."
else
    echo "Downloading ifctester..."
    if curl -L -o "$BIN_DIR/$IFCTESTER_FILENAME" "$IFCTESTER_URL"; then
        echo "  [OK] Downloaded ifctester"
    else
        echo "  [X] Failed to download ifctester"
    fi
fi

# Download ifcopenshell (WASM wheel)
# Using IfcOpenShell/wasm-wheels repo - the official source for Pyodide-compatible wheels
IFCOPENSHELL_FILENAME="ifcopenshell-0.8.3+34a1bc6-cp313-cp313-emscripten_4_0_9_wasm32.whl"
IFCOPENSHELL_URLS=(
    "https://raw.githubusercontent.com/IfcOpenShell/wasm-wheels/main/ifcopenshell-0.8.3%2B34a1bc6-cp313-cp313-emscripten_4_0_9_wasm32.whl"
    "https://github.com/IfcOpenShell/IfcOpenShell/releases/download/v0.8.3/ifcopenshell-0.8.3-cp313-cp313-emscripten_wasm32.whl"
)

if [ -f "$BIN_DIR/$IFCOPENSHELL_FILENAME" ]; then
    echo "[OK] ifcopenshell already exists, skipping..."
else
    echo "Downloading ifcopenshell (WASM wheel)..."
    downloaded=false
    for url in "${IFCOPENSHELL_URLS[@]}"; do
        echo "  Trying: $url"
        if curl -L -f -o "$BIN_DIR/$IFCOPENSHELL_FILENAME" "$url" 2>/dev/null; then
            filesize=$(ls -la "$BIN_DIR/$IFCOPENSHELL_FILENAME" | awk '{print $5}')
            echo "  [OK] Downloaded ifcopenshell ($filesize bytes)"
            downloaded=true
            break
        else
            echo "  [X] Failed"
            rm -f "$BIN_DIR/$IFCOPENSHELL_FILENAME"
        fi
    done
    
    if [ "$downloaded" = false ]; then
        echo "  [!] ifcopenshell could not be downloaded automatically."
        echo "  You may need to download it manually from:"
        echo "  https://github.com/IfcOpenShell/IfcOpenShell/releases"
    fi
fi

# Download odfpy
if ls "$BIN_DIR"/odfpy-*.whl 1>/dev/null 2>&1; then
    echo "[OK] odfpy already exists, skipping..."
else
    echo "Downloading odfpy..."
    if curl -L -o "$BIN_DIR/$ODFPY_FILENAME" "$ODFPY_URL"; then
        echo "  [OK] Downloaded odfpy"
    else
        echo "  [X] Failed to download odfpy"
    fi
fi

echo ""
echo "========================================"
echo "Download Complete"
echo "========================================"
echo ""
echo "Files in $BIN_DIR:"
ls -la "$BIN_DIR"
