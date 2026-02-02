#!/bin/bash
# IfcTester ArchiCAD Add-On - Installer Build Script (macOS)
# Builds the add-on and creates a DMG installer package

set -e

# Parse arguments
SKIP_BUILD=false
SKIP_WEB_BUILD=false
SIGN_IDENTITY=""
NOTARIZE=false

while [[ $# -gt 0 ]]; do
    case $1 in
        --skip-build)
            SKIP_BUILD=true
            shift
            ;;
        --skip-web-build|-s)
            SKIP_WEB_BUILD=true
            shift
            ;;
        --sign)
            SIGN_IDENTITY="$2"
            shift 2
            ;;
        --notarize)
            NOTARIZE=true
            shift
            ;;
        --help|-h)
            echo "Usage: $0 [options]"
            echo ""
            echo "Options:"
            echo "  --skip-build         Skip building (use existing build)"
            echo "  --skip-web-build, -s Skip building the web app"
            echo "  --sign IDENTITY      Code signing identity (Developer ID Application: ...)"
            echo "  --notarize           Notarize the DMG (requires Apple Developer account)"
            echo "  --help, -h           Show this help message"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Load common utilities
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../utils/common-mac.sh"

# Version info
APP_NAME="IfcTester for ArchiCAD"
APP_VERSION="1.1.0"
DMG_NAME="IfcTesterArchiCAD-v${APP_VERSION}-mac"

write_header "IfcTester ArchiCAD - Build Installer (macOS)"

# Check for create-dmg (optional but recommended)
HAS_CREATE_DMG=false
if command -v create-dmg &> /dev/null; then
    HAS_CREATE_DMG=true
    write_info "Using create-dmg for enhanced DMG creation"
else
    write_warning "create-dmg not found, using basic hdiutil"
    write_info "For better DMG appearance, install: brew install create-dmg"
fi
echo ""

TOTAL_STEPS=5
if [ "$SKIP_BUILD" = true ]; then
    TOTAL_STEPS=3
fi
STEP=0

# Step 1: Build web app
if [ "$SKIP_BUILD" = false ] && [ "$SKIP_WEB_BUILD" = false ]; then
    STEP=$((STEP + 1))
    write_step $STEP $TOTAL_STEPS "Building web application..."
    
    if ! build_web_app "$WEB_DIR" "true"; then
        exit 1
    fi
    echo ""
fi

# Step 2: Build ArchiCAD add-on
if [ "$SKIP_BUILD" = false ]; then
    STEP=$((STEP + 1))
    write_step $STEP $TOTAL_STEPS "Building ArchiCAD add-on..."
    
    BUILD_SCRIPT="$SCRIPT_DIR/build-mac.sh"
    "$BUILD_SCRIPT" --configuration Release --skip-web-build
    
    if [ $? -ne 0 ]; then
        write_error "ArchiCAD build failed"
        exit 1
    fi
    echo ""
fi

# Verify required files
STEP=$((STEP + 1))
write_step $STEP $TOTAL_STEPS "Verifying required files..."

CMAKE_BUILD_DIR="$ARCHICAD_DIR/cmake-build"
BUNDLE_PATH="$CMAKE_BUILD_DIR/Release/IfcTesterArchiCAD.bundle"

# Try to find bundle if not at expected location
if [ ! -d "$BUNDLE_PATH" ]; then
    FOUND_BUNDLE=$(find "$CMAKE_BUILD_DIR" -name "IfcTesterArchiCAD.bundle" -type d 2>/dev/null | head -1)
    if [ -n "$FOUND_BUNDLE" ]; then
        BUNDLE_PATH="$FOUND_BUNDLE"
    fi
fi

if [ ! -d "$BUNDLE_PATH" ]; then
    write_error "Required file not found: IfcTesterArchiCAD.bundle"
    write_info "Expected: $CMAKE_BUILD_DIR/Release/IfcTesterArchiCAD.bundle"
    exit 1
fi

if [ ! -f "$WEB_DIR/dist/index.html" ]; then
    write_error "Required file not found: web/dist/index.html"
    exit 1
fi

write_success "All required files present"
write_info "Bundle: $BUNDLE_PATH"
echo ""

# Ensure dist directory exists
mkdir -p "$DIST_DIR"

# Create staging directory for DMG contents
STEP=$((STEP + 1))
write_step $STEP $TOTAL_STEPS "Preparing DMG contents..."

STAGING_DIR="$DIST_DIR/dmg-staging"
rm -rf "$STAGING_DIR"
mkdir -p "$STAGING_DIR"

# Copy bundle to staging
cp -R "$BUNDLE_PATH" "$STAGING_DIR/"

# Create README for the DMG
cat > "$STAGING_DIR/README.txt" << 'EOF'
IfcTester for ArchiCAD
======================

Installation Instructions:

1. Copy the "IfcTesterArchiCAD.bundle" to your ArchiCAD Add-Ons folder:
   
   ~/Library/Application Support/GRAPHISOFT/ArchiCAD 29/Add-Ons/

2. Restart ArchiCAD

3. Access the add-on from: Audit > IFC Testing > IfcTester Panel

For more information, visit:
https://github.com/byggstyrning/ifctester-revit

EOF

# Create symbolic link to Add-Ons folder (for easy drag-and-drop)
ADDONS_PATH="$HOME/Library/Application Support/GRAPHISOFT/ArchiCAD 29/Add-Ons"
if [ -d "$ADDONS_PATH" ]; then
    ln -sf "$ADDONS_PATH" "$STAGING_DIR/ArchiCAD 29 Add-Ons"
    write_info "Added shortcut to Add-Ons folder"
fi

# Create uninstall script
cat > "$STAGING_DIR/Uninstall.command" << 'EOF'
#!/bin/bash
# Uninstall IfcTester for ArchiCAD

echo "Uninstalling IfcTester for ArchiCAD..."

ADDONS_PATH="$HOME/Library/Application Support/GRAPHISOFT/ArchiCAD 29/Add-Ons/IfcTesterArchiCAD.bundle"

if [ -d "$ADDONS_PATH" ]; then
    rm -rf "$ADDONS_PATH"
    echo "Removed: $ADDONS_PATH"
    echo "Uninstall complete!"
else
    echo "IfcTester not found at expected location."
fi

echo ""
echo "Press any key to close..."
read -n 1
EOF
chmod +x "$STAGING_DIR/Uninstall.command"

write_success "DMG contents prepared"
echo ""

# Create DMG
STEP=$((STEP + 1))
write_step $STEP $TOTAL_STEPS "Creating DMG installer..."

DMG_OUTPUT="$DIST_DIR/${DMG_NAME}.dmg"

# Remove existing DMG
rm -f "$DMG_OUTPUT"

if [ "$HAS_CREATE_DMG" = true ]; then
    # Use create-dmg for a nice looking DMG
    create-dmg \
        --volname "$APP_NAME" \
        --volicon "$ARCHICAD_DIR/Resources/Icons/IfcTester.png" \
        --window-pos 200 120 \
        --window-size 600 400 \
        --icon-size 100 \
        --icon "IfcTesterArchiCAD.bundle" 150 190 \
        --icon "README.txt" 450 190 \
        --hide-extension "IfcTesterArchiCAD.bundle" \
        --app-drop-link 450 190 \
        "$DMG_OUTPUT" \
        "$STAGING_DIR" \
        2>/dev/null || {
            # Fallback if create-dmg fails
            write_warning "create-dmg failed, falling back to hdiutil"
            HAS_CREATE_DMG=false
        }
fi

if [ "$HAS_CREATE_DMG" = false ]; then
    # Basic DMG creation with hdiutil
    hdiutil create -volname "$APP_NAME" \
        -srcfolder "$STAGING_DIR" \
        -ov -format UDZO \
        "$DMG_OUTPUT"
fi

if [ ! -f "$DMG_OUTPUT" ]; then
    write_error "Failed to create DMG"
    exit 1
fi

# Code signing (if identity provided)
if [ -n "$SIGN_IDENTITY" ]; then
    write_info "Code signing DMG..."
    codesign --sign "$SIGN_IDENTITY" --timestamp "$DMG_OUTPUT"
    write_success "DMG signed"
fi

# Notarization (if requested)
if [ "$NOTARIZE" = true ]; then
    if [ -z "$SIGN_IDENTITY" ]; then
        write_warning "Notarization requires code signing. Skipping."
    else
        write_info "Notarizing DMG (this may take a few minutes)..."
        write_info "Note: Requires Apple Developer account and App Store Connect credentials"
        # xcrun notarytool submit "$DMG_OUTPUT" --wait --keychain-profile "AC_PASSWORD"
        write_warning "Notarization not implemented - requires Apple Developer credentials"
    fi
fi

# Cleanup staging
rm -rf "$STAGING_DIR"

write_completed "Installer Build Complete!"

# Show output info
DMG_SIZE=$(get_file_size_mb "$DMG_OUTPUT")
echo -e "${CYAN}Output:${NC}"
echo -e "  ${GRAY}$DMG_OUTPUT${NC}"
echo -e "  ${GRAY}Size: ${DMG_SIZE} MB${NC}"
echo ""
echo -e "${CYAN}To install:${NC}"
echo -e "  ${GRAY}1. Open the DMG${NC}"
echo -e "  ${GRAY}2. Drag IfcTesterArchiCAD.bundle to the Add-Ons folder${NC}"
echo -e "  ${GRAY}3. Restart ArchiCAD${NC}"
echo ""
