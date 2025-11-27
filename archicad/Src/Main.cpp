/**
 * IfcTester ArchiCAD Add-On
 * 
 * Main entry point for the ArchiCAD plugin.
 * Implements the 4 required Add-On functions:
 * - CheckEnvironment
 * - RegisterInterface
 * - Initialize
 * - FreeData
 */

#include "IfcTesterArchiCAD.hpp"
#include "BrowserPalette.hpp"
#include "ArchiCADApiServer.hpp"

// Global palette instance
static std::unique_ptr<IfcTester::BrowserPalette> gBrowserPalette;

// Global API server instance
static std::unique_ptr<IfcTester::ArchiCADApiServer> gApiServer;

/**
 * Menu command handler
 * Called when user selects a menu item
 */
GSErrCode __ACENV_CALL MenuCommandHandler(const API_MenuParams* menuParams)
{
    switch (menuParams->menuItemRef.menuResID) {
        case BrowserPaletteMenuResId:
            switch (menuParams->menuItemRef.itemIndex) {
                case BrowserPaletteMenuItemIndex:
                    ShowOrHideBrowserPalette();
                    break;
            }
            break;
    }

    return NoError;
}

/**
 * Show or hide the browser palette
 */
void ShowOrHideBrowserPalette()
{
    if (gBrowserPalette == nullptr) {
        gBrowserPalette = std::make_unique<IfcTester::BrowserPalette>();
    }

    if (gBrowserPalette->IsVisible()) {
        gBrowserPalette->Hide();
    } else {
        gBrowserPalette->Show();
        gBrowserPalette->BringToFront();
    }
}

/**
 * Check if browser palette is visible
 */
bool IsBrowserPaletteVisible()
{
    return gBrowserPalette != nullptr && gBrowserPalette->IsVisible();
}

/**
 * Get selected elements information
 */
GS::Array<ElementInfo> GetSelectedElements()
{
    API_SelectionInfo selectionInfo;
    GS::Array<API_Neig> selNeigs;
    
    GSErrCode err = ACAPI_Selection_Get(&selectionInfo, &selNeigs, false, false);
    BMKillHandle((GSHandle*)&selectionInfo.marquee.coords);

    GS::Array<ElementInfo> selectedElements;
    
    if (err != NoError) {
        return selectedElements;
    }

    for (const API_Neig& neig : selNeigs) {
        API_Elem_Head elemHead = {};
        elemHead.guid = neig.guid;
        
        err = ACAPI_Element_GetHeader(&elemHead);
        if (err != NoError) {
            continue;
        }

        ElementInfo elemInfo;
        elemInfo.guidStr = APIGuidToString(elemHead.guid);
        elemInfo.typeID = elemHead.typeID;
        
        // Get element type name
        ACAPI_Goodies(APIAny_GetElemTypeNameID, (void*)elemHead.typeID, &elemInfo.typeName);
        
        // Get element ID string
        ACAPI_Database(APIDb_GetElementInfoStringID, &elemHead.guid, &elemInfo.elemID);
        
        selectedElements.Push(elemInfo);
    }

    return selectedElements;
}

/**
 * Select element by GUID string
 */
bool SelectElementByGUID(const GS::UniString& guidStr)
{
    API_Guid guid = APIGuidFromString(guidStr.ToCStr().Get());
    return SelectElementByID(guid);
}

/**
 * Select element by API_Guid
 */
bool SelectElementByID(const API_Guid& guid)
{
    GS::Array<API_Neig> elemsToSelect;
    elemsToSelect.Push(API_Neig(guid));
    
    GSErrCode err = ACAPI_Element_Select(elemsToSelect, true);
    return err == NoError;
}

/**
 * Get available IFC export configurations
 */
GS::Array<IFCConfiguration> GetIFCExportConfigurations()
{
    GS::Array<IFCConfiguration> configurations;
    
    // Standard IFC configurations in ArchiCAD
    // Note: ArchiCAD provides these through the IFC Translator settings
    
    IFCConfiguration config2x3;
    config2x3.name = "IFC 2x3";
    config2x3.description = "IFC 2x3 Coordination View 2.0";
    config2x3.version = "IFC2x3";
    configurations.Push(config2x3);
    
    IFCConfiguration config4;
    config4.name = "IFC 4";
    config4.description = "IFC 4 Reference View";
    config4.version = "IFC4";
    configurations.Push(config4);
    
    IFCConfiguration config4DTV;
    config4DTV.name = "IFC 4 Design Transfer";
    config4DTV.description = "IFC 4 Design Transfer View";
    config4DTV.version = "IFC4";
    configurations.Push(config4DTV);
    
    // Try to get actual translator configurations from ArchiCAD
    // This requires accessing the IFC Translator settings
    API_Attribute attrib;
    BNZeroMemory(&attrib, sizeof(API_Attribute));
    
    // Enumerate available IFC translators
    // Note: Actual implementation depends on ArchiCAD version
    // For now, we return the standard configurations
    
    return configurations;
}

/**
 * Export model to IFC
 */
bool ExportToIFC(const GS::UniString& configName, GS::UniString& outputPath)
{
    // Create temporary output path
    IO::Location tempFolder;
    API_SpecFolderID specFolderID = API_TemporaryFolderID;
    
    GSErrCode err = ACAPI_Environment(APIEnv_GetSpecFolderID, &specFolderID, &tempFolder);
    if (err != NoError) {
        return false;
    }
    
    // Generate unique filename
    GS::UniString timestamp = GS::UniString::Printf("IfcTester_Export_%lld", 
        (long long)std::time(nullptr));
    GS::UniString filename = timestamp + ".ifc";
    
    IO::Location outputLocation(tempFolder);
    outputLocation.AppendToLocal(IO::Name(filename));
    
    // Set up IFC export options
    API_IFCTranslatorIdentifier translator;
    BNZeroMemory(&translator, sizeof(API_IFCTranslatorIdentifier));
    
    // Find matching translator
    // Note: In a full implementation, we would enumerate available translators
    // and match by configName
    
    // Set up export parameters
    API_IFCRelationshipData ifcRelData;
    BNZeroMemory(&ifcRelData, sizeof(API_IFCRelationshipData));
    
    // Export using ACAPI_Automate
    // Note: The exact API call depends on ArchiCAD version
    // ArchiCAD 23+ uses ACAPI_CallUndoableCommand for exports
    
    IO::Location fileLoc(outputLocation);
    API_IOParams ioParams;
    BNZeroMemory(&ioParams, sizeof(API_IOParams));
    ioParams.fileLoc = &fileLoc;
    ioParams.ioMethod = API_Export_IFC;
    
    err = ACAPI_Automate(APIDo_SaveID, &ioParams, nullptr);
    
    if (err == NoError) {
        outputLocation.ToPath(&outputPath);
        return true;
    }
    
    return false;
}

// ============================================================================
// Required ArchiCAD Add-On Functions
// ============================================================================

/**
 * CheckEnvironment
 * Called when ArchiCAD starts to check if the Add-On can run
 */
API_AddonType __ACDLL_CALL CheckEnvironment(API_EnvirParams* envir)
{
    RSGetIndString(&envir->addOnInfo.name, AddOnInfoResId, 1, ACAPI_GetOwnResModule());
    RSGetIndString(&envir->addOnInfo.description, AddOnInfoResId, 2, ACAPI_GetOwnResModule());

    return APIAddon_Preload;
}

/**
 * RegisterInterface
 * Called to register menus, dialogs, and other UI elements
 */
GSErrCode __ACDLL_CALL RegisterInterface(void)
{
    // Register the menu
    GSErrCode err = ACAPI_Register_Menu(BrowserPaletteMenuResId, 0, MenuCode_UserDef, MenuFlag_Default);
    if (err != NoError) {
        return err;
    }

    return NoError;
}

/**
 * Initialize
 * Called when the Add-On is loaded
 */
GSErrCode __ACENV_CALL Initialize(void)
{
    // Install menu handler
    GSErrCode err = ACAPI_Install_MenuHandler(BrowserPaletteMenuResId, MenuCommandHandler);
    if (err != NoError) {
        return err;
    }

    // Register selection change notification
    err = ACAPI_Notify_CatchSelectionChange(IfcTester::BrowserPalette::SelectionChangeHandler);
    if (err != NoError) {
        return err;
    }

    // Register palette control callback
    err = IfcTester::BrowserPalette::RegisterPaletteControlCallBack();
    if (err != NoError) {
        return err;
    }

    // Start the API server for web communication
    gApiServer = std::make_unique<IfcTester::ArchiCADApiServer>(ApiServerPort);
    gApiServer->Start();

    return NoError;
}

/**
 * FreeData
 * Called when the Add-On is unloaded
 */
GSErrCode __ACENV_CALL FreeData(void)
{
    // Stop and clean up API server
    if (gApiServer != nullptr) {
        gApiServer->Stop();
        gApiServer.reset();
    }

    // Clean up palette
    if (gBrowserPalette != nullptr) {
        gBrowserPalette.reset();
    }

    return NoError;
}
