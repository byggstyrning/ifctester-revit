#!/bin/bash
# IfcTester ArchiCAD Add-On - Build Script (macOS)
# Builds the ArchiCAD add-on using CMake and Xcode

set -e

# Parse arguments
CONFIGURATION="Release"
ARCHICAD_VERSION="29"
SKIP_WEB_BUILD=false
GENERATOR="Xcode"

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
        --skip-web-build|-s)
            SKIP_WEB_BUILD=true
            shift
            ;;
        --generator|-g)
            GENERATOR="$2"
            shift 2
            ;;
        --help|-h)
            echo "Usage: $0 [options]"
            echo ""
            echo "Options:"
            echo "  --configuration, -c  Build configuration (Debug|Release) [default: Release]"
            echo "  --version, -v        ArchiCAD version (29) [default: 29]"
            echo "  --skip-web-build, -s Skip building the web app"
            echo "  --generator, -g      CMake generator (Xcode|Unix Makefiles) [default: Xcode]"
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

write_header "IfcTester ArchiCAD - Build (macOS)"
echo -e "${CYAN}Configuration: $CONFIGURATION${NC}"
echo -e "${CYAN}ArchiCAD Version: $ARCHICAD_VERSION${NC}"
echo -e "${CYAN}Generator: $GENERATOR${NC}"
echo ""

# Check prerequisites
write_info "Checking prerequisites..."

if ! check_cmake; then
    write_error "CMake not found!"
    write_info "Install with: brew install cmake"
    exit 1
fi
write_success "CMake found: $(cmake --version | head -1)"

if ! check_xcode; then
    write_error "Xcode Command Line Tools not found!"
    write_info "Install with: xcode-select --install"
    exit 1
fi
write_success "Xcode tools found"

if ! check_python3; then
    write_error "Python 3 not found!"
    write_info "Install with: brew install python3"
    exit 1
fi
write_success "Python 3 found: $(python3 --version)"

echo ""

# Find DevKit
DEVKIT_PATH=$(find_archicad_devkit "$ARCHICAD_VERSION")
if [ -z "$DEVKIT_PATH" ]; then
    write_error "ArchiCAD API Development Kit not found!"
    write_info "Set ARCHICAD_API_DEVKIT environment variable or install the DevKit"
    write_info "Download from: https://archicadapi.graphisoft.com/"
    write_info ""
    write_info "Expected locations:"
    write_info "  ~/Library/Application Support/GRAPHISOFT/API Development Kit $ARCHICAD_VERSION"
    write_info "  /Applications/GRAPHISOFT/API Development Kit $ARCHICAD_VERSION"
    exit 1
fi
write_info "Using DevKit: $DEVKIT_PATH"
echo ""

CMAKE_BUILD_DIR="$ARCHICAD_DIR/cmake-build"

TOTAL_STEPS=3
if [ "$SKIP_WEB_BUILD" = true ]; then
    TOTAL_STEPS=2
fi
STEP=0

# Step 1: Build Web App
if [ "$SKIP_WEB_BUILD" = false ]; then
    STEP=$((STEP + 1))
    write_step $STEP $TOTAL_STEPS "Building web application..."
    
    if ! build_web_app "$WEB_DIR"; then
        exit 1
    fi
    echo ""
fi

# Step 2: Configure CMake if needed
STEP=$((STEP + 1))
write_step $STEP $TOTAL_STEPS "Configuring CMake..."

if [ ! -d "$CMAKE_BUILD_DIR" ]; then
    write_info "Creating CMake build directory..."
    mkdir -p "$CMAKE_BUILD_DIR"
    
    pushd "$CMAKE_BUILD_DIR" > /dev/null
    
    write_info "Running CMake configure with $GENERATOR..."
    
    if [ "$GENERATOR" = "Xcode" ]; then
        cmake .. -G "Xcode" -DAC_API_DEVKIT_DIR="$DEVKIT_PATH"
    else
        cmake .. -G "Unix Makefiles" -DCMAKE_BUILD_TYPE="$CONFIGURATION" -DAC_API_DEVKIT_DIR="$DEVKIT_PATH"
    fi
    
    if [ $? -ne 0 ]; then
        write_error "CMake configure failed"
        popd > /dev/null
        exit 1
    fi
    
    write_success "CMake configured"
    popd > /dev/null
else
    write_success "CMake already configured"
fi
echo ""

# Step 3: Build
STEP=$((STEP + 1))
write_step $STEP $TOTAL_STEPS "Building add-on ($CONFIGURATION)..."

pushd "$CMAKE_BUILD_DIR" > /dev/null

cmake --build . --config "$CONFIGURATION"

if [ $? -ne 0 ]; then
    write_error "Build failed"
    popd > /dev/null
    exit 1
fi

# Find output bundle
OUTPUT_PATH="$CMAKE_BUILD_DIR/$CONFIGURATION/IfcTesterArchiCAD.bundle"

if [ -d "$OUTPUT_PATH" ]; then
    BUNDLE_SIZE=$(get_dir_size_mb "$OUTPUT_PATH")
    write_success "Build completed"
    write_info "Output: $OUTPUT_PATH"
    write_info "Size: ${BUNDLE_SIZE} MB"
else
    write_error "Build output not found at: $OUTPUT_PATH"
    write_info "Checking alternative locations..."
    
    # Try to find the bundle
    FOUND_BUNDLE=$(find "$CMAKE_BUILD_DIR" -name "IfcTesterArchiCAD.bundle" -type d 2>/dev/null | head -1)
    if [ -n "$FOUND_BUNDLE" ]; then
        write_info "Found bundle at: $FOUND_BUNDLE"
        OUTPUT_PATH="$FOUND_BUNDLE"
    else
        write_error "Could not find built bundle"
        popd > /dev/null
        exit 1
    fi
fi

popd > /dev/null

write_completed "Build Complete!"
echo "Output: $OUTPUT_PATH"
