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

// IFC API Headers
#include "ACAPI/IFCObjectAccessor.hpp"
#include "ACAPI/IFCObjectID.hpp"

// Standard library
#include <fstream>

// Global palette instance
static std::unique_ptr<IfcTester::BrowserPalette> gBrowserPalette;

// Global API server instance
static std::unique_ptr<IfcTester::ArchiCADApiServer> gApiServer;

// Hidden message window for thread-safe communication
static HWND gMessageWindow = nullptr;
static const wchar_t* MESSAGE_WINDOW_CLASS = L"IfcTesterMessageWindow";

/**
 * Window procedure for the hidden message window
 * Handles WM_IFCTESTER_PROCESS_QUEUE messages from background threads
 */
LRESULT CALLBACK MessageWindowProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg) {
        case IfcTester::WM_IFCTESTER_PROCESS_QUEUE:
            // Process selection queue on the main thread
            if (gApiServer != nullptr) {
                ACAPI_WriteReport("IfcTester: Processing selection queue on main thread", false);
                gApiServer->ProcessSelectionQueue();
            }
            return 0;
            
        case WM_DESTROY:
            PostQuitMessage(0);
            return 0;
            
        default:
            return DefWindowProc(hwnd, msg, wParam, lParam);
    }
}

/**
 * Create the hidden message window
 * Called during initialization on the main thread
 */
bool CreateMessageWindow()
{
    // Register window class
    WNDCLASSEXW wc = {};
    wc.cbSize = sizeof(WNDCLASSEXW);
    wc.lpfnWndProc = MessageWindowProc;
    wc.hInstance = GetModuleHandle(nullptr);
    wc.lpszClassName = MESSAGE_WINDOW_CLASS;
    
    if (!RegisterClassExW(&wc)) {
        DWORD error = GetLastError();
        // Class might already be registered, that's okay
        if (error != ERROR_CLASS_ALREADY_EXISTS) {
            ACAPI_WriteReport("IfcTester: Failed to register message window class (error %d)", false, error);
            return false;
        }
    }
    
    // Create hidden window (HWND_MESSAGE creates a message-only window)
    gMessageWindow = CreateWindowExW(
        0,                          // Extended style
        MESSAGE_WINDOW_CLASS,       // Class name
        L"IfcTesterMessageHandler", // Window name
        0,                          // Style
        0, 0, 0, 0,                 // Position and size (not relevant for message-only)
        HWND_MESSAGE,               // Parent (message-only window)
        nullptr,                    // Menu
        GetModuleHandle(nullptr),   // Instance
        nullptr                     // Additional data
    );
    
    if (gMessageWindow == nullptr) {
        DWORD error = GetLastError();
        ACAPI_WriteReport("IfcTester: Failed to create message window (error %d)", false, error);
        return false;
    }
    
    ACAPI_WriteReport("IfcTester: Message window created successfully", false);
    return true;
}

/**
 * Destroy the hidden message window
 * Called during cleanup
 */
void DestroyMessageWindow()
{
    if (gMessageWindow != nullptr) {
        DestroyWindow(gMessageWindow);
        gMessageWindow = nullptr;
    }
    
    UnregisterClassW(MESSAGE_WINDOW_CLASS, GetModuleHandle(nullptr));
}

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
        elemInfo.type = elemHead.type;
        
        // Get element type name
        ACAPI_Element_GetElemTypeName(elemHead.type, elemInfo.typeName);
        
        // Get element ID string  
        ACAPI_Element_GetElementInfoString(&elemHead.guid, &elemInfo.elemID);
        
        selectedElements.Push(elemInfo);
    }

    return selectedElements;
}

/**
 * Select element by GUID string
 * Supports both IFC GlobalId (22 chars) and ArchiCAD API_Guid
 * 
 * IMPORTANT: This function must be called from the main thread.
 * If called from a background thread, wrap it in ACAPI_CallUndoableCommand.
 */
bool SelectElementByGUID(const GS::UniString& guidStr)
{
    // Validate input
    if (guidStr.IsEmpty()) {
        ACAPI_WriteReport("IfcTester: Empty GUID string provided", false);
        return false;
    }
    
    // Try to find by IFC GlobalId first (matches IFC_Test example pattern)
    try {
        IFCAPI::IfcGloballyUniqueId globalId = guidStr;
        auto elementsResult = IFCAPI::GetObjectAccessor().FindElementsByGlobalId(globalId);
        
        if (elementsResult.IsOk()) {
            GS::Array<API_Neig> neigs;
            const auto& elementIDs = elementsResult.Unwrap();
            
            for (const auto& elementID : elementIDs) {
                auto elementGuid = IFCAPI::GetObjectAccessor().GetAPIElementID(elementID);
                if (elementGuid.IsOk()) {
                    API_Neig neig{};
                    neig.guid = elementGuid.Unwrap();
                    neigs.Push(neig);
                }
            }
            
            if (!neigs.IsEmpty()) {
                // Deselect all first (as per IFC_Test example)
                GSErrCode err = ACAPI_Selection_DeselectAll();
                if (err != NoError) {
                    ACAPI_WriteReport("IfcTester: Failed to deselect all (error %d)", false, err);
                    // Continue anyway - selection might still work
                }
                
                err = ACAPI_Selection_Select(neigs, true);
                if (err != NoError) {
                    ACAPI_WriteReport("IfcTester: Failed to select elements by IFC GlobalId (error %d)", false, err);
                    return false;
                }
                return true;
            } else {
                ACAPI_WriteReport("IfcTester: No elements found for IFC GlobalId '%s'", false, guidStr.ToCStr().Get());
            }
        } else {
            ACAPI_WriteReport("IfcTester: FindElementsByGlobalId failed for GUID '%s': %s", false, 
                guidStr.ToCStr().Get(), elementsResult.UnwrapErr().text.c_str());
        }
    } catch (...) {
        ACAPI_WriteReport("IfcTester: Exception in SelectElementByGUID (IFC lookup)", false);
    }
    
    // Fallback to ArchiCAD API_Guid if IFC lookup fails
    // This handles cases where the web app might send an internal API_Guid
    try {
        API_Guid guid = APIGuidFromString(guidStr.ToCStr().Get());
        if (guid != APINULLGuid) {
            return SelectElementByID(guid);
        }
    } catch (...) {
        ACAPI_WriteReport("IfcTester: Exception in SelectElementByGUID (fallback)", false);
    }
    
    return false;
}

/**
 * Select element by API_Guid
 * 
 * IMPORTANT: This function must be called from the main thread.
 */
bool SelectElementByID(const API_Guid& guid)
{
    // Deselect all first (as per IFC_Test example)
    GSErrCode err = ACAPI_Selection_DeselectAll();
    if (err != NoError) {
        ACAPI_WriteReport("IfcTester: Failed to deselect all (error %d)", false, err);
    }
    
    GS::Array<API_Neig> neigs;
    API_Neig neig{};
    neig.guid = guid;
    neigs.Push(neig);
    
    err = ACAPI_Selection_Select(neigs, true);
    if (err != NoError) {
        ACAPI_WriteReport("IfcTester: Failed to select element by GUID (error %d)", false, err);
        return false;
    }
    return true;
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
 * Note: Full IFC export implementation requires complex setup with translators
 * For IfcTester, users typically load pre-exported IFC files
 */
bool ExportToIFC(const GS::UniString& configName, GS::UniString& outputPath)
{
    UNUSED_PARAMETER(configName);
    
    // Create temporary output path
    IO::Location tempFolder;
    API_SpecFolderID specFolderID = API_TemporaryFolderID;
    
    GSErrCode err = ACAPI_ProjectSettings_GetSpecFolder(&specFolderID, &tempFolder);
    if (err != NoError) {
        return false;
    }
    
    // Generate unique filename
    GS::UniString timestamp = GS::UniString::Printf("IfcTester_Export_%lld", 
        (long long)std::time(nullptr));
    GS::UniString filename = timestamp + ".ifc";
    
    IO::Location outputLocation(tempFolder);
    outputLocation.AppendToLocal(IO::Name(filename));
    
    // Get IFC translators
    GS::Array<API_IFCTranslatorIdentifier> ifcTranslators;
    err = ACAPI_IFC_GetIFCExportTranslatorsList(ifcTranslators);
    if (err != NoError || ifcTranslators.IsEmpty()) {
        return false;
    }
    
    // Use the first available translator (usually the preview translator)
    API_IFCTranslatorIdentifier& translator = ifcTranslators[0];
    
    // Set up file save parameters
    API_FileSavePars fileSavePars = {};
    fileSavePars.file = &outputLocation;
    
    // Set up IFC save parameters
    API_SavePars_Ifc ifcSavePars = {};
    ifcSavePars.translatorIdentifier = translator;
    
    // Perform export
    err = ACAPI_ProjectOperation_Save(&fileSavePars, &ifcSavePars, nullptr);
    
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

    return APIAddon_Normal;
}

/**
 * RegisterInterface
 * Called to register menus, dialogs, and other UI elements
 */
GSErrCode __ACDLL_CALL RegisterInterface(void)
{
    // Register the menu
    GSErrCode err = ACAPI_MenuItem_RegisterMenu(BrowserPaletteMenuResId, 0, MenuCode_UserDef, MenuFlag_Default);
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
    GSErrCode err = ACAPI_MenuItem_InstallMenuHandler(BrowserPaletteMenuResId, MenuCommandHandler);
    if (err != NoError) {
        return err;
    }

    // Register selection change notification
    err = ACAPI_Notification_CatchSelectionChange(IfcTester::BrowserPalette::SelectionChangeHandler);
    if (err != NoError) {
        return err;
    }

    // Register palette control callback
    err = IfcTester::BrowserPalette::RegisterPaletteControlCallBack();
    if (err != NoError) {
        return err;
    }

    // Log add-on version and build info
    ACAPI_WriteReport("IfcTester ArchiCAD Add-On v%s (Built: %s %s)", false, AddOnVersion, BuildDate, BuildTime);
    
    // Create the hidden message window for thread-safe communication
    // This must be done on the main thread before starting the API server
    if (!CreateMessageWindow()) {
        ACAPI_WriteReport("IfcTester: Warning - Failed to create message window. Selection from web app may not work.", false);
    }
    
    // Start the API server for web communication
    gApiServer = std::make_unique<IfcTester::ArchiCADApiServer>(ApiServerPort);
    
    // Set the message window handle on the server for thread-safe communication
    if (gMessageWindow != nullptr) {
        gApiServer->SetMessageWindowHandle(gMessageWindow);
        ACAPI_WriteReport("IfcTester: Message window handle set on API server", false);
    }
    
    // Get the add-on's location and set WebApp path
    IO::Location addOnLocation;
    GSErrCode locErr = ACAPI_GetOwnLocation(&addOnLocation);
    if (locErr == NoError) {
        // Get the .apx file path
        GS::UniString apxPath;
        addOnLocation.ToPath(&apxPath);
        ACAPI_WriteReport("IfcTester: Add-on .apx file location: %s", false, apxPath.ToCStr().Get());
        
        // Try multiple possible WebApp locations
        GS::UniString webAppPath;
        bool found = false;
        
        // Option 1: WebApp folder in the same directory as .apx file
        IO::Location webAppLocation1(addOnLocation);
        webAppLocation1.DeleteLastLocalName(); // Remove .apx filename
        webAppLocation1.AppendToLocal(IO::Name("WebApp"));
        webAppLocation1.ToPath(&webAppPath);
        
        ACAPI_WriteReport("IfcTester: Trying WebApp path (option 1): %s", false, webAppPath.ToCStr().Get());
        GS::UniString indexPath1 = webAppPath;
        indexPath1 += "\\index.html";
        std::ifstream testFile1(indexPath1.ToCStr().Get());
        if (testFile1) {
            testFile1.close();
            found = true;
            ACAPI_WriteReport("IfcTester: WebApp folder found at: %s", false, webAppPath.ToCStr().Get());
        } else {
            // Option 2: WebApp folder in Build\Release (for development)
            // Try to find Build\Release\WebApp relative to the .apx location
            IO::Location buildLocation(addOnLocation);
            buildLocation.DeleteLastLocalName(); // Remove .apx filename
            buildLocation.DeleteLastLocalName(); // Go up one more level
            buildLocation.AppendToLocal(IO::Name("Build"));
            buildLocation.AppendToLocal(IO::Name("Release"));
            buildLocation.AppendToLocal(IO::Name("WebApp"));
            buildLocation.ToPath(&webAppPath);
            
            ACAPI_WriteReport("IfcTester: Trying WebApp path (option 2): %s", false, webAppPath.ToCStr().Get());
            GS::UniString indexPath2 = webAppPath;
            indexPath2 += "\\index.html";
            std::ifstream testFile2(indexPath2.ToCStr().Get());
            if (testFile2) {
                testFile2.close();
                found = true;
                ACAPI_WriteReport("IfcTester: WebApp folder found at: %s", false, webAppPath.ToCStr().Get());
            }
        }
        
        if (found) {
            gApiServer->SetWebAppPath(webAppPath);
            ACAPI_WriteReport("IfcTester: WebApp path set successfully to: %s", false, webAppPath.ToCStr().Get());
        } else {
            ACAPI_WriteReport("IfcTester: ERROR - WebApp folder not found in any expected location!", false);
            ACAPI_WriteReport("IfcTester: Please ensure WebApp folder exists next to the .apx file", false);
        }
    } else {
        ACAPI_WriteReport("IfcTester: ERROR - Could not determine add-on location (error %d)", false, locErr);
    }
    
    bool serverStarted = gApiServer->Start();
    
    if (!serverStarted) {
        // Log error - server failed to start (port might be in use)
        // Note: In production, you might want to try a different port
        ACAPI_WriteReport("IfcTester: Failed to start API server on port %d", false, ApiServerPort);
    } else {
        ACAPI_WriteReport("IfcTester: API server started on http://127.0.0.1:%d", false, ApiServerPort);
        ACAPI_WriteReport("IfcTester: Thread-safe selection queue enabled via Windows messages", false);
    }

    return NoError;
}

/**
 * FreeData
 * Called when the Add-On is unloaded
 */
GSErrCode __ACENV_CALL FreeData(void)
{
    // Stop and clean up API server first (before destroying message window)
    if (gApiServer != nullptr) {
        gApiServer->Stop();
        gApiServer.reset();
    }
    
    // Destroy the message window
    DestroyMessageWindow();

    // Clean up palette
    if (gBrowserPalette != nullptr) {
        gBrowserPalette.reset();
    }

    return NoError;
}
