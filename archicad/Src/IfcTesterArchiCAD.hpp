/**
 * IfcTester ArchiCAD Add-On
 * 
 * Header file for the IfcTester ArchiCAD plugin.
 * Provides IDS authoring and auditing tools for ArchiCAD.
 * 
 * Based on the ArchiCAD Browser Control API documentation.
 */

#ifndef IFCTESTER_ARCHICAD_HPP
#define IFCTESTER_ARCHICAD_HPP

// ArchiCAD API Headers
#include "ACAPinc.h"
#include "DGModule.hpp"
#include "DGBrowser.hpp"
#include "DGDialog.hpp"

// Standard Headers
#include <string>
#include <vector>
#include <map>
#include <functional>
#include <memory>

// Resource IDs
constexpr short AddOnInfoResId          = 32000;
constexpr short BrowserPaletteMenuResId = 32500;
constexpr short BrowserPaletteMenuItemIndex = 1;
constexpr short BrowserPaletteResId     = 32500;
constexpr short BrowserId               = 1;

// API Server Port (matches Revit implementation)
constexpr int ApiServerPort             = 48882; // Different port from Revit (48881)

// Forward declarations
namespace IfcTester {
    class BrowserPalette;
    class ArchiCADApiServer;
}

/**
 * Element information structure
 * Used to pass element data between ArchiCAD and the web interface
 */
struct ElementInfo {
    GS::UniString guidStr;      // Element GUID as string
    GS::UniString typeName;     // Element type name
    GS::UniString elemID;       // Element ID string
    API_ElemTypeID typeID;      // Element type ID
};

/**
 * IFC Configuration structure
 */
struct IFCConfiguration {
    GS::UniString name;
    GS::UniString description;
    GS::UniString version;      // IFC2x3, IFC4, etc.
};

// Global functions
void ShowOrHideBrowserPalette();
bool IsBrowserPaletteVisible();
GS::Array<ElementInfo> GetSelectedElements();
bool SelectElementByGUID(const GS::UniString& guidStr);
bool SelectElementByID(const API_Guid& guid);
GS::Array<IFCConfiguration> GetIFCExportConfigurations();
bool ExportToIFC(const GS::UniString& configName, GS::UniString& outputPath);

#endif // IFCTESTER_ARCHICAD_HPP
