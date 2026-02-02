#!/bin/bash
# IfcTester ArchiCAD Add-On - Deploy Script (macOS)
# Builds and deploys to local ArchiCAD Add-Ons folder for development

set -e

# Parse arguments
CONFIGURATION="Release"
ARCHICAD_VERSION="29"
SKIP_BUILD=false
SKIP_WEB_BUILD=false

while [[ $# -gt 0 ]]; do
    case $1 in
        --configuration|-c)
            CONFIGURATION="$2"
            shift 2
            ;;
        --version|-v)
            ARCHICAD_VERSION="$2"
            shift 2
            ;;
        --skip-build)
            SKIP_BUILD=true
            shift
            ;;
        --skip-web-build|-s)
            SKIP_WEB_BUILD=true
            shift
            ;;
        --help|-h)
            echo "Usage: $0 [options]"
            echo ""
            echo "Options:"
            echo "  --configuration, -c  Build configuration (Debug|Release) [default: Release]"
            echo "  --version, -v        ArchiCAD version (29) [default: 29]"
            echo "  --skip-build         Skip building (only copy existing build)"
            echo "  --skip-web-build, -s Skip building the web app"
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

ADDONS_FOLDER=$(get_archicad_addons_path "$ARCHICAD_VERSION")
TARGET_FOLDER="$ADDONS_FOLDER/IfcTesterArchiCAD"

write_header "IfcTester ArchiCAD - Deploy (macOS)"
echo -e "${CYAN}Configuration: $CONFIGURATION${NC}"
echo -e "${CYAN}ArchiCAD Version: $ARCHICAD_VERSION${NC}"
echo -e "${CYAN}Deploy to: $TARGET_FOLDER${NC}"
echo ""

# Check if ArchiCAD is running
if is_archicad_running; then
    write_warning "ArchiCAD is currently running!"
    write_info "The add-on files may be locked. Consider closing ArchiCAD first."
    if ! confirm_continue; then
        echo "Aborted."
        exit 0
    fi
fi

TOTAL_STEPS=3
if [ "$SKIP_BUILD" = false ]; then
    TOTAL_STEPS=4
fi
STEP=0

# Step 1: Build (if not skipped)
if [ "$SKIP_BUILD" = false ]; then
    STEP=$((STEP + 1))
    write_step $STEP $TOTAL_STEPS "Building add-on..."
    
    BUILD_SCRIPT="$SCRIPT_DIR/build-mac.sh"
    
    BUILD_ARGS=("--configuration" "$CONFIGURATION" "--version" "$ARCHICAD_VERSION")
    if [ "$SKIP_WEB_BUILD" = true ]; then
        BUILD_ARGS+=("--skip-web-build")
    fi
    
    "$BUILD_SCRIPT" "${BUILD_ARGS[@]}"
    
    if [ $? -ne 0 ]; then
        write_error "Build failed"
        exit 1
    fi
    echo ""
fi

# Build paths
CMAKE_BUILD_DIR="$ARCHICAD_DIR/cmake-build"
BUILD_OUTPUT_PATH="$CMAKE_BUILD_DIR/$CONFIGURATION/IfcTesterArchiCAD.bundle"

# Check build exists
if [ ! -d "$BUILD_OUTPUT_PATH" ]; then
    write_error "Build output not found: $BUILD_OUTPUT_PATH"
    write_info "Run without --skip-build to build first"
    
    # Try to find it
    FOUND_BUNDLE=$(find "$CMAKE_BUILD_DIR" -name "IfcTesterArchiCAD.bundle" -type d 2>/dev/null | head -1)
    if [ -n "$FOUND_BUNDLE" ]; then
        write_info "Found bundle at: $FOUND_BUNDLE"
        BUILD_OUTPUT_PATH="$FOUND_BUNDLE"
    else
        exit 1
    fi
fi

# Step 2: Create target folder
STEP=$((STEP + 1))
write_step $STEP $TOTAL_STEPS "Preparing target folder..."

if [ ! -d "$TARGET_FOLDER" ]; then
    mkdir -p "$TARGET_FOLDER"
    write_success "Created: $TARGET_FOLDER"
else
    write_info "Target folder exists"
fi
echo ""

# Step 3: Copy .bundle file
STEP=$((STEP + 1))
write_step $STEP $TOTAL_STEPS "Copying add-on bundle..."

TARGET_BUNDLE_PATH="$TARGET_FOLDER/IfcTesterArchiCAD.bundle"

# Remove old bundle if exists
if [ -d "$TARGET_BUNDLE_PATH" ]; then
    rm -rf "$TARGET_BUNDLE_PATH"
fi

# Copy new bundle
cp -R "$BUILD_OUTPUT_PATH" "$TARGET_BUNDLE_PATH"

if [ -d "$TARGET_BUNDLE_PATH" ]; then
    BUNDLE_SIZE=$(get_dir_size_mb "$TARGET_BUNDLE_PATH")
    write_success "Copied add-on bundle (${BUNDLE_SIZE} MB)"
else
    write_error "Failed to copy bundle"
    exit 1
fi
echo ""

# Step 4: Copy WebApp (if not already in bundle)
STEP=$((STEP + 1))
write_step $STEP $TOTAL_STEPS "Verifying WebApp folder..."

# On Mac, WebApp should be inside the bundle at Contents/Resources/WebApp
BUNDLE_WEBAPP="$TARGET_BUNDLE_PATH/Contents/Resources/WebApp"
if [ -d "$BUNDLE_WEBAPP" ]; then
    WEBAPP_FILES=$(count_files "$BUNDLE_WEBAPP")
    write_success "WebApp folder present in bundle ($WEBAPP_FILES files)"
else
    write_warning "WebApp folder not found in bundle"
    write_info "Expected: $BUNDLE_WEBAPP"
    
    # Try to copy from web/dist
    if [ -d "$WEB_DIR/dist" ]; then
        write_info "Copying WebApp from web/dist..."
        mkdir -p "$BUNDLE_WEBAPP"
        cp -R "$WEB_DIR/dist/"* "$BUNDLE_WEBAPP/"
        write_success "WebApp copied to bundle"
    fi
fi

write_completed "Deployment Complete!"

echo -e "${CYAN}Next steps:${NC}"
echo -e "  ${GRAY}1. Restart ArchiCAD (if running)${NC}"
echo -e "  ${GRAY}2. Open Window > Palettes > Report${NC}"
echo -e "  ${GRAY}3. Look for: 'IfcTester ArchiCAD Add-On v1.1.0'${NC}"
echo -e "  ${GRAY}4. Look for: 'IfcTester: API server started on http://127.0.0.1:48882'${NC}"
echo ""
echo "Installed to: $TARGET_BUNDLE_PATH"
