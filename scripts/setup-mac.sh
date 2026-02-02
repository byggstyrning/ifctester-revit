#!/bin/bash
# IfcTester - macOS Setup Script
# Downloads and installs prerequisites for building the ArchiCAD add-on

set -e

# Load common utilities
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/utils/common-mac.sh"

# DevKit info
DEVKIT_VERSION="29"
DEVKIT_BUILD="3100"
DEVKIT_URL="https://github.com/GRAPHISOFT/archicad-api-devkit/releases/download/29.3100/API.Development.Kit.MAC.29.3100.zip"
DEVKIT_INSTALL_DIR="$HOME/Library/Application Support/GRAPHISOFT/API Development Kit $DEVKIT_VERSION"

write_header "IfcTester - macOS Setup"

echo "This script will help you set up the development environment for building"
echo "the IfcTester ArchiCAD add-on on macOS."
echo ""

# ============================================================================
# Check existing tools
# ============================================================================

write_step 1 4 "Checking existing tools..."
echo ""

MISSING_TOOLS=()

# Xcode Command Line Tools
if check_xcode; then
    write_success "Xcode Command Line Tools: installed"
else
    write_warning "Xcode Command Line Tools: not found"
    MISSING_TOOLS+=("xcode")
fi

# CMake
if check_cmake; then
    write_success "CMake: $(cmake --version | head -1)"
else
    write_warning "CMake: not found"
    MISSING_TOOLS+=("cmake")
fi

# Node.js
if check_node; then
    write_success "Node.js: $(node --version)"
else
    write_warning "Node.js: not found"
    MISSING_TOOLS+=("node")
fi

# Python 3
if check_python3; then
    write_success "Python 3: $(python3 --version)"
else
    write_warning "Python 3: not found"
    MISSING_TOOLS+=("python3")
fi

echo ""

# ============================================================================
# Install missing tools
# ============================================================================

if [ ${#MISSING_TOOLS[@]} -gt 0 ]; then
    write_step 2 4 "Installing missing tools..."
    echo ""
    
    for tool in "${MISSING_TOOLS[@]}"; do
        case $tool in
            xcode)
                write_info "Installing Xcode Command Line Tools..."
                xcode-select --install 2>/dev/null || {
                    write_warning "Xcode tools installation launched - please complete the dialog"
                    write_info "After installation completes, run this script again"
                    exit 0
                }
                ;;
            cmake)
                write_info "CMake not found."
                echo ""
                echo "Please install CMake using one of these methods:"
                echo "  1. Homebrew: brew install cmake"
                echo "  2. Download from: https://cmake.org/download/"
                echo "  3. MacPorts: sudo port install cmake"
                echo ""
                ;;
            node)
                write_info "Node.js not found."
                echo ""
                echo "Please install Node.js using one of these methods:"
                echo "  1. Homebrew: brew install node"
                echo "  2. Download from: https://nodejs.org/"
                echo "  3. nvm: nvm install --lts"
                echo ""
                ;;
            python3)
                write_info "Python 3 not found."
                echo ""
                echo "Please install Python 3 using one of these methods:"
                echo "  1. Homebrew: brew install python3"
                echo "  2. Download from: https://www.python.org/downloads/"
                echo ""
                ;;
        esac
    done
    
    if [[ " ${MISSING_TOOLS[*]} " =~ " cmake " ]] || [[ " ${MISSING_TOOLS[*]} " =~ " node " ]]; then
        write_warning "Some tools are missing. Install them and run this script again."
        echo ""
    fi
else
    write_step 2 4 "All build tools installed"
    echo ""
fi

# ============================================================================
# Check/Install ArchiCAD DevKit
# ============================================================================

write_step 3 4 "Checking ArchiCAD API DevKit..."
echo ""

DEVKIT_PATH=$(find_archicad_devkit "$DEVKIT_VERSION")

if [ -n "$DEVKIT_PATH" ]; then
    write_success "DevKit $DEVKIT_VERSION found: $DEVKIT_PATH"
else
    write_warning "DevKit $DEVKIT_VERSION not found"
    echo ""
    
    if confirm_continue "Download and install ArchiCAD API DevKit $DEVKIT_VERSION.$DEVKIT_BUILD?"; then
        echo ""
        write_info "Downloading DevKit (73 MB)..."
        
        TEMP_DIR=$(mktemp -d)
        DOWNLOAD_FILE="$TEMP_DIR/devkit.zip"
        
        # Download with progress
        curl -L --progress-bar "$DEVKIT_URL" -o "$DOWNLOAD_FILE"
        
        if [ ! -f "$DOWNLOAD_FILE" ]; then
            write_error "Download failed"
            rm -rf "$TEMP_DIR"
            exit 1
        fi
        
        write_success "Download complete"
        write_info "Extracting..."
        
        # Create install directory
        mkdir -p "$DEVKIT_INSTALL_DIR"
        
        # Extract
        unzip -q "$DOWNLOAD_FILE" -d "$TEMP_DIR/extracted"
        
        # Find the extracted folder (may have different name)
        EXTRACTED_DIR=$(find "$TEMP_DIR/extracted" -maxdepth 1 -type d -name "API*" | head -1)
        
        if [ -z "$EXTRACTED_DIR" ]; then
            # If no API* folder, use the extracted directory itself
            EXTRACTED_DIR="$TEMP_DIR/extracted"
        fi
        
        # Copy contents to install directory
        cp -R "$EXTRACTED_DIR/"* "$DEVKIT_INSTALL_DIR/"
        
        # Cleanup
        rm -rf "$TEMP_DIR"
        
        write_success "DevKit installed to: $DEVKIT_INSTALL_DIR"
        
        # Verify installation
        if [ -f "$DEVKIT_INSTALL_DIR/Support/Inc/ACAPinc.h" ]; then
            write_success "DevKit verification passed"
        else
            write_warning "DevKit may not be correctly installed"
            write_info "Expected file not found: $DEVKIT_INSTALL_DIR/Support/Inc/ACAPinc.h"
        fi
    else
        echo ""
        write_info "You can download the DevKit manually from:"
        write_info "  $DEVKIT_URL"
        write_info ""
        write_info "Install to:"
        write_info "  $DEVKIT_INSTALL_DIR"
    fi
fi

echo ""

# ============================================================================
# Summary
# ============================================================================

write_step 4 4 "Setup Summary"
echo ""

ALL_READY=true

if check_xcode; then
    write_success "Xcode Command Line Tools"
else
    write_error "Xcode Command Line Tools - MISSING"
    ALL_READY=false
fi

if check_cmake; then
    write_success "CMake"
else
    write_error "CMake - MISSING"
    ALL_READY=false
fi

if check_node; then
    write_success "Node.js"
else
    write_error "Node.js - MISSING"
    ALL_READY=false
fi

if check_python3; then
    write_success "Python 3"
else
    write_error "Python 3 - MISSING"
    ALL_READY=false
fi

DEVKIT_PATH=$(find_archicad_devkit "$DEVKIT_VERSION")
if [ -n "$DEVKIT_PATH" ]; then
    write_success "ArchiCAD API DevKit $DEVKIT_VERSION"
else
    write_error "ArchiCAD API DevKit $DEVKIT_VERSION - MISSING"
    ALL_READY=false
fi

echo ""

if [ "$ALL_READY" = true ]; then
    write_completed "Setup Complete!"
    echo "You can now build the add-on with:"
    echo ""
    echo "  ./scripts/archicad/build-mac.sh"
    echo ""
    echo "Or use the interactive menu:"
    echo ""
    echo "  ./scripts/dev-mac.sh"
    echo ""
else
    write_warning "Some prerequisites are missing"
    echo ""
    echo "Please install the missing tools and run this script again."
    echo ""
fi
