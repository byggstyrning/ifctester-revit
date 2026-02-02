#!/bin/bash
# IfcTester - Interactive Development Menu (macOS)
# Provides a menu-driven interface for all build and deploy operations

set -e

# Load common utilities
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/utils/common-mac.sh"

show_menu() {
    clear
    echo -e "${CYAN}========================================${NC}"
    echo -e "${CYAN}  IfcTester Development Menu (macOS)${NC}"
    echo -e "${CYAN}========================================${NC}"
    echo ""
    echo -e "${YELLOW}ArchiCAD Add-On:${NC}"
    echo "  1) Build ArchiCAD add-on (Release)"
    echo "  2) Build ArchiCAD add-on (Debug)"
    echo "  3) Deploy to ArchiCAD Add-Ons folder"
    echo "  4) Build DMG installer"
    echo ""
    echo -e "${YELLOW}Web Application:${NC}"
    echo "  5) Build web app"
    echo "  6) Start web dev server"
    echo ""
    echo -e "${YELLOW}Utilities:${NC}"
    echo "  7) Check prerequisites"
    echo "  8) Clean build artifacts"
    echo "  9) Open ArchiCAD Add-Ons folder"
    echo ""
    echo "  q) Quit"
    echo ""
    echo -n "Select option: "
}

check_prerequisites() {
    write_header "Checking Prerequisites"
    
    echo -e "${YELLOW}Required tools:${NC}"
    echo ""
    
    # CMake
    if check_cmake; then
        write_success "CMake: $(cmake --version | head -1)"
    else
        write_error "CMake: Not found (brew install cmake)"
    fi
    
    # Xcode
    if check_xcode; then
        write_success "Xcode tools: $(xcode-select -p)"
    else
        write_error "Xcode tools: Not found (xcode-select --install)"
    fi
    
    # Node
    if check_node; then
        write_success "Node.js: $(node --version)"
    else
        write_error "Node.js: Not found (brew install node)"
    fi
    
    # npm
    if check_npm; then
        write_success "npm: $(npm --version)"
    else
        write_error "npm: Not found"
    fi
    
    # Python3
    if check_python3; then
        write_success "Python 3: $(python3 --version)"
    else
        write_error "Python 3: Not found (brew install python3)"
    fi
    
    echo ""
    echo -e "${YELLOW}ArchiCAD DevKit:${NC}"
    echo ""
    
    DEVKIT=$(find_archicad_devkit "29")
    if [ -n "$DEVKIT" ]; then
        write_success "DevKit 29: $DEVKIT"
    else
        write_error "DevKit 29: Not found"
        write_info "Download from: https://archicadapi.graphisoft.com/"
        write_info "Install to: ~/Library/Application Support/GRAPHISOFT/API Development Kit 29"
    fi
    
    echo ""
    echo -e "${YELLOW}ArchiCAD Installation:${NC}"
    echo ""
    
    AC_APP=$(get_archicad_app_path "29")
    if [ -n "$AC_APP" ]; then
        write_success "ArchiCAD 29: $AC_APP"
    else
        write_warning "ArchiCAD 29: Not found"
    fi
    
    echo ""
}

clean_build() {
    write_header "Clean Build Artifacts"
    
    echo "This will remove:"
    echo "  - archicad/cmake-build/"
    echo "  - web/dist/"
    echo "  - web/node_modules/"
    echo "  - dist/"
    echo ""
    
    if ! confirm_continue "Are you sure?"; then
        echo "Aborted."
        return
    fi
    
    echo ""
    
    if [ -d "$ARCHICAD_DIR/cmake-build" ]; then
        rm -rf "$ARCHICAD_DIR/cmake-build"
        write_success "Removed archicad/cmake-build/"
    fi
    
    if [ -d "$WEB_DIR/dist" ]; then
        rm -rf "$WEB_DIR/dist"
        write_success "Removed web/dist/"
    fi
    
    if [ -d "$WEB_DIR/node_modules" ]; then
        rm -rf "$WEB_DIR/node_modules"
        write_success "Removed web/node_modules/"
    fi
    
    if [ -d "$DIST_DIR" ]; then
        rm -rf "$DIST_DIR"
        write_success "Removed dist/"
    fi
    
    write_completed "Clean Complete!"
}

open_addons_folder() {
    ADDONS_PATH=$(get_archicad_addons_path "29")
    
    if [ -d "$ADDONS_PATH" ]; then
        open "$ADDONS_PATH"
        write_success "Opened: $ADDONS_PATH"
    else
        write_warning "Add-Ons folder not found"
        write_info "Expected: $ADDONS_PATH"
        
        if confirm_continue "Create the folder?"; then
            mkdir -p "$ADDONS_PATH"
            open "$ADDONS_PATH"
            write_success "Created and opened: $ADDONS_PATH"
        fi
    fi
}

start_web_dev() {
    write_header "Starting Web Dev Server"
    
    pushd "$WEB_DIR" > /dev/null
    
    if [ ! -d "node_modules" ]; then
        write_info "Installing npm dependencies..."
        npm install
    fi
    
    echo ""
    write_info "Starting dev server at http://localhost:5173"
    write_info "Press Ctrl+C to stop"
    echo ""
    
    npm run dev
    
    popd > /dev/null
}

# Main loop
while true; do
    show_menu
    read -r choice
    
    case $choice in
        1)
            "$SCRIPT_DIR/archicad/build-mac.sh" --configuration Release
            echo ""
            echo "Press Enter to continue..."
            read -r
            ;;
        2)
            "$SCRIPT_DIR/archicad/build-mac.sh" --configuration Debug
            echo ""
            echo "Press Enter to continue..."
            read -r
            ;;
        3)
            "$SCRIPT_DIR/archicad/deploy-mac.sh"
            echo ""
            echo "Press Enter to continue..."
            read -r
            ;;
        4)
            "$SCRIPT_DIR/archicad/installer-mac.sh"
            echo ""
            echo "Press Enter to continue..."
            read -r
            ;;
        5)
            "$SCRIPT_DIR/web/build-mac.sh"
            echo ""
            echo "Press Enter to continue..."
            read -r
            ;;
        6)
            start_web_dev
            ;;
        7)
            check_prerequisites
            echo ""
            echo "Press Enter to continue..."
            read -r
            ;;
        8)
            clean_build
            echo ""
            echo "Press Enter to continue..."
            read -r
            ;;
        9)
            open_addons_folder
            echo ""
            echo "Press Enter to continue..."
            read -r
            ;;
        q|Q)
            echo "Goodbye!"
            exit 0
            ;;
        *)
            echo "Invalid option"
            sleep 1
            ;;
    esac
done
