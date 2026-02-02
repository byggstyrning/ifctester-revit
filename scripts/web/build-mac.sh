#!/bin/bash
# IfcTester Web App - Build Script (macOS)
# Builds the web application for production

set -e

# Parse arguments
FORCE=false
SKIP_PACKAGES=false

while [[ $# -gt 0 ]]; do
    case $1 in
        --force|-f)
            FORCE=true
            shift
            ;;
        --skip-packages)
            SKIP_PACKAGES=true
            shift
            ;;
        --help|-h)
            echo "Usage: $0 [options]"
            echo ""
            echo "Options:"
            echo "  --force, -f       Force rebuild even if dist is recent"
            echo "  --skip-packages   Skip downloading Python packages"
            echo "  --help, -h        Show this help message"
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

write_header "IfcTester Web App - Build"

# Check prerequisites
if ! check_node; then
    write_error "Node.js not found!"
    write_info "Install with: brew install node"
    exit 1
fi

if ! check_npm; then
    write_error "npm not found!"
    exit 1
fi

TOTAL_STEPS=3
if [ "$SKIP_PACKAGES" = true ]; then
    TOTAL_STEPS=2
fi
STEP=0

# Step 1: Install dependencies
STEP=$((STEP + 1))
write_step $STEP $TOTAL_STEPS "Checking dependencies..."

pushd "$WEB_DIR" > /dev/null

if [ ! -d "node_modules" ]; then
    write_info "Installing npm dependencies..."
    npm install
    if [ $? -ne 0 ]; then
        write_error "npm install failed"
        exit 1
    fi
    write_success "Dependencies installed"
else
    write_success "Dependencies already installed"
fi

popd > /dev/null
echo ""

# Step 2: Download Python packages (for Pyodide)
if [ "$SKIP_PACKAGES" = false ]; then
    STEP=$((STEP + 1))
    write_step $STEP $TOTAL_STEPS "Checking Python packages..."
    
    DOWNLOAD_SCRIPT="$WEB_DIR/scripts/download-packages.js"
    if [ -f "$DOWNLOAD_SCRIPT" ]; then
        pushd "$WEB_DIR" > /dev/null
        
        # Check if packages already exist
        PYODIDE_DIR="$WEB_DIR/public/pyodide"
        if [ -f "$PYODIDE_DIR/pyodide.mjs" ]; then
            write_success "Python packages already downloaded"
        else
            write_info "Downloading Python packages..."
            node scripts/download-packages.js
            write_success "Python packages downloaded"
        fi
        
        popd > /dev/null
    else
        write_warning "Download script not found, packages may need internet access at runtime"
    fi
    echo ""
fi

# Step 3: Build
STEP=$((STEP + 1))
write_step $STEP $TOTAL_STEPS "Building web app..."

pushd "$WEB_DIR" > /dev/null

write_info "Running npm run build..."
npm run build

if [ $? -ne 0 ]; then
    write_error "Build failed"
    exit 1
fi

DIST_PATH="$WEB_DIR/dist"
INDEX_PATH="$DIST_PATH/index.html"

if [ ! -f "$INDEX_PATH" ]; then
    write_error "Build failed - dist/index.html not found"
    exit 1
fi

FILE_COUNT=$(count_files "$DIST_PATH")
TOTAL_SIZE=$(get_dir_size_mb "$DIST_PATH")

write_success "Build completed"
write_info "Output: $DIST_PATH"
write_info "Files: $FILE_COUNT"
write_info "Total size: ${TOTAL_SIZE} MB"

popd > /dev/null

write_completed "Web App Build Complete!"
