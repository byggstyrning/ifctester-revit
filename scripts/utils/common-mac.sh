#!/bin/bash
# Common utilities for IfcTester build scripts (macOS)
# This file is sourced by other scripts to provide shared functionality

set -e

# ============================================================================
# Paths
# ============================================================================

get_script_dir() {
    local source="${BASH_SOURCE[0]}"
    while [ -h "$source" ]; do
        local dir="$(cd -P "$(dirname "$source")" && pwd)"
        source="$(readlink "$source")"
        [[ $source != /* ]] && source="$dir/$source"
    done
    echo "$(cd -P "$(dirname "$source")" && pwd)"
}

get_project_root() {
    local script_dir
    script_dir="$(get_script_dir)"
    # Navigate up from utils/ to scripts/ to project root
    echo "$(cd "$script_dir/../.." && pwd)"
}

# Project paths as variables (call init_paths first)
init_paths() {
    PROJECT_ROOT="$(get_project_root)"
    REVIT_DIR="$PROJECT_ROOT/revit"
    ARCHICAD_DIR="$PROJECT_ROOT/archicad"
    WEB_DIR="$PROJECT_ROOT/web"
    INSTALLER_DIR="$PROJECT_ROOT/installer"
    DIST_DIR="$PROJECT_ROOT/dist"
    SCRIPTS_DIR="$PROJECT_ROOT/scripts"
}

# ============================================================================
# Console Output
# ============================================================================

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[0;33m'
CYAN='\033[0;36m'
GRAY='\033[0;90m'
NC='\033[0m' # No Color

write_header() {
    local title="$1"
    echo ""
    echo -e "${CYAN}========================================${NC}"
    echo -e "${CYAN} $title${NC}"
    echo -e "${CYAN}========================================${NC}"
    echo ""
}

write_step() {
    local number="$1"
    local total="$2"
    local message="$3"
    echo -e "${YELLOW}[$number/$total] $message${NC}"
}

write_success() {
    local message="$1"
    echo -e "  ${GREEN}[OK]${NC} $message"
}

write_warning() {
    local message="$1"
    echo -e "  ${YELLOW}[!]${NC} $message"
}

write_error() {
    local message="$1"
    echo -e "  ${RED}[X]${NC} $message"
}

write_info() {
    local message="$1"
    echo -e "  ${GRAY}$message${NC}"
}

write_completed() {
    local title="${1:-Complete!}"
    echo ""
    echo -e "${GREEN}========================================${NC}"
    echo -e "${GREEN} $title${NC}"
    echo -e "${GREEN}========================================${NC}"
    echo ""
}

# ============================================================================
# Tool Detection
# ============================================================================

find_archicad_devkit() {
    local version="${1:-29}"
    
    # Check environment variable first
    if [ -n "$ARCHICAD_API_DEVKIT" ] && [ -d "$ARCHICAD_API_DEVKIT" ]; then
        echo "$ARCHICAD_API_DEVKIT"
        return 0
    fi
    
    # Common macOS paths
    local paths=(
        "$HOME/Library/Application Support/GRAPHISOFT/API Development Kit $version"
        "/Applications/GRAPHISOFT/API Development Kit $version"
        "/Users/Shared/GRAPHISOFT/API Development Kit $version"
        "$HOME/GRAPHISOFT/API Development Kit $version"
    )
    
    for path in "${paths[@]}"; do
        if [ -d "$path" ] && [ -f "$path/Support/Inc/ACAPinc.h" ]; then
            echo "$path"
            return 0
        fi
    done
    
    return 1
}

check_cmake() {
    if command -v cmake &> /dev/null; then
        return 0
    fi
    return 1
}

check_xcode() {
    if xcode-select -p &> /dev/null; then
        return 0
    fi
    return 1
}

check_node() {
    if command -v node &> /dev/null; then
        return 0
    fi
    return 1
}

check_npm() {
    if command -v npm &> /dev/null; then
        return 0
    fi
    return 1
}

check_python3() {
    if command -v python3 &> /dev/null; then
        return 0
    fi
    return 1
}

# ============================================================================
# Web App Build
# ============================================================================

build_web_app() {
    local web_dir="$1"
    local force="${2:-false}"
    
    local dist_path="$web_dir/dist"
    local index_path="$dist_path/index.html"
    
    # Check if rebuild needed
    if [ "$force" != "true" ] && [ -f "$index_path" ]; then
        local last_build
        last_build=$(stat -f %m "$index_path" 2>/dev/null || echo 0)
        local now
        now=$(date +%s)
        local age=$((now - last_build))
        
        if [ $age -lt 300 ]; then
            write_info "Web app already built (dist folder is recent)"
            return 0
        fi
    fi
    
    pushd "$web_dir" > /dev/null
    
    # Install dependencies if needed
    if [ ! -d "node_modules" ]; then
        write_info "Installing npm dependencies..."
        npm install
        if [ $? -ne 0 ]; then
            write_error "npm install failed"
            popd > /dev/null
            return 1
        fi
    fi
    
    # Build
    write_info "Running npm run build..."
    npm run build
    
    if [ $? -ne 0 ]; then
        write_error "Web app build failed"
        popd > /dev/null
        return 1
    fi
    
    write_success "Web app built successfully"
    popd > /dev/null
    return 0
}

# ============================================================================
# Process Checks
# ============================================================================

is_archicad_running() {
    if pgrep -x "ARCHICAD" > /dev/null 2>&1; then
        return 0
    fi
    # Also check for different naming conventions
    if pgrep -i "archicad" > /dev/null 2>&1; then
        return 0
    fi
    return 1
}

confirm_continue() {
    local message="${1:-Continue anyway?}"
    echo -n "$message (y/N) "
    read -r response
    if [[ "$response" =~ ^[Yy]$ ]]; then
        return 0
    fi
    return 1
}

# ============================================================================
# File Operations
# ============================================================================

get_file_size_kb() {
    local file="$1"
    if [ -f "$file" ]; then
        local size
        size=$(stat -f %z "$file" 2>/dev/null || stat --printf="%s" "$file" 2>/dev/null)
        echo $((size / 1024))
    else
        echo 0
    fi
}

get_file_size_mb() {
    local file="$1"
    if [ -f "$file" ]; then
        local size
        size=$(stat -f %z "$file" 2>/dev/null || stat --printf="%s" "$file" 2>/dev/null)
        echo "scale=2; $size / 1048576" | bc
    else
        echo 0
    fi
}

get_dir_size_mb() {
    local dir="$1"
    if [ -d "$dir" ]; then
        du -sm "$dir" 2>/dev/null | cut -f1
    else
        echo 0
    fi
}

count_files() {
    local dir="$1"
    if [ -d "$dir" ]; then
        find "$dir" -type f | wc -l | tr -d ' '
    else
        echo 0
    fi
}

# ============================================================================
# ArchiCAD Paths (macOS specific)
# ============================================================================

get_archicad_addons_path() {
    local version="${1:-29}"
    echo "$HOME/Library/Application Support/GRAPHISOFT/ArchiCAD $version/Add-Ons"
}

get_archicad_app_path() {
    local version="${1:-29}"
    # Check common locations
    local paths=(
        "/Applications/GRAPHISOFT/ARCHICAD $version/ARCHICAD $version.app"
        "/Applications/ARCHICAD $version/ARCHICAD $version.app"
        "/Applications/ARCHICAD $version.app"
    )
    
    for path in "${paths[@]}"; do
        if [ -d "$path" ]; then
            echo "$path"
            return 0
        fi
    done
    
    return 1
}

# Initialize paths when sourced
init_paths
