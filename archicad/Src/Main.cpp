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
 * Handles WM_IFCTESTER_PROCESS_QUEUE and WM_IFCTESTER_PROCESS_EXPORT messages from background threads
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
            
        case IfcTester::WM_IFCTESTER_PROCESS_EXPORT:
            // Process export queue on the main thread
            if (gApiServer != nullptr) {
                ACAPI_WriteReport("IfcTester: Processing export queue on main thread", false);
                gApiServer->ProcessExportQueue();
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
GSErrCode MenuCommandHandler (const API_MenuParams *menuParams)
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
    // Note: IFC GUIDs are case-sensitive (Base64 encoding)
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
    
    // Get actual translator configurations from ArchiCAD
    GS::Array<API_IFCTranslatorIdentifier> ifcTranslators;
    GSErrCode err = ACAPI_IFC_GetIFCExportTranslatorsList(ifcTranslators);
    
    if (err == NoError) {
        for (const auto& translator : ifcTranslators) {
            IFCConfiguration config;
            config.name = translator.name;
            config.description = translator.name; // Description not available in identifier
            config.version = "IFC"; // Version not available in identifier
            configurations.Push(config);
        }
    }
    
    // If no translators found (or error), add a default fallback
    if (configurations.IsEmpty()) {
        IFCConfiguration configDefault;
        configDefault.name = "Default (Preview)";
        configDefault.description = "Default IFC Translator";
        configDefault.version = "IFC";
        configurations.Push(configDefault);
    }
    
    return configurations;
}

/**
 * Export model to IFC
 */
bool ExportToIFC(const GS::UniString& configName, GS::UniString& outputPath, GS::UniString* errorMessage)
{
    // CRITICAL: Check Demo Mode IMMEDIATELY, before any other operations
    // This must be the VERY FIRST thing in the function to prevent any possibility
    // of the save operation being called in Demo Mode
    // Call the API function directly in the condition to ensure it's evaluated immediately
    UInt32 protectionMode = ACAPI_Licensing_GetProtectionMode();
    
    // CRITICAL: Check Demo Mode IMMEDIATELY, before any other operations
    // This must be the VERY FIRST thing in the function to prevent any possibility
    // of the save operation being called in Demo Mode
    // Call the API function directly in the condition to ensure it's evaluated immediately
    if ((ACAPI_Licensing_GetProtectionMode() & APIPROT_DEMO_MASK) != 0) {
        GS::UniString msg = "Cannot export IFC in Demo Mode";
        if (errorMessage) {
            *errorMessage = msg;
        }
        return false;
    }
    
    try {
        // Debug logging for protection mode
        ACAPI_WriteReport("IfcTester: ExportToIFC called - checking protection mode...", false);
        UInt32 protectionMode = ACAPI_Licensing_GetProtectionMode();
        ACAPI_WriteReport("IfcTester: Protection Mode value: %u (Hex: 0x%X)", false, protectionMode, protectionMode);
        ACAPI_WriteReport("IfcTester: APIPROT_DEMO_MASK value: 0x%X", false, APIPROT_DEMO_MASK);
        UInt32 demoCheck = protectionMode & APIPROT_DEMO_MASK;
        ACAPI_WriteReport("IfcTester: Demo Mode check result: %u (0x%X) - %s", false, demoCheck, demoCheck, (demoCheck != 0) ? "DEMO MODE DETECTED" : "NOT DEMO MODE");
        
        // Double-check (should never hit this if first check worked)
        if (protectionMode & APIPROT_DEMO_MASK) {
            GS::UniString msg = "Cannot export IFC in Demo Mode";
            if (errorMessage) {
                *errorMessage = msg;
            }
            return false;
        }
        
        ACAPI_WriteReport("IfcTester: Protection mode check passed, continuing with export...", false);

        ACAPI_WriteReport("IfcTester: Starting IFC export...", false);

        // Create temporary output path
        ACAPI_WriteReport("IfcTester: Getting temporary folder...", false);
        IO::Location tempFolder;
        API_SpecFolderID specFolderID = API_TemporaryFolderID;
        
        GSErrCode err = ACAPI_ProjectSettings_GetSpecFolder(&specFolderID, &tempFolder);
        if (err != NoError) {
            ACAPI_WriteReport("IfcTester: Failed to get temporary folder (error %d)", false, err);
            if (errorMessage) *errorMessage = "Failed to get temporary folder";
            return false;
        }
        ACAPI_WriteReport("IfcTester: Temporary folder obtained", false);
        
        // Generate unique filename
        GS::UniString timestamp = GS::UniString::Printf("IfcTester_Export_%lld", 
            (long long)std::time(nullptr));
        GS::UniString filename = timestamp + ".ifc";
        
        IO::Location outputLocation(tempFolder);
        outputLocation.AppendToLocal(IO::Name(filename));
        
        // Get IFC translators
        ACAPI_WriteReport("IfcTester: Getting IFC translators list...", false);
        GS::Array<API_IFCTranslatorIdentifier> ifcTranslators;
        err = ACAPI_IFC_GetIFCExportTranslatorsList(ifcTranslators);
        if (err != NoError || ifcTranslators.IsEmpty()) {
            ACAPI_WriteReport("IfcTester: No IFC translators found (error %d)", false, err);
            if (errorMessage) *errorMessage = "No IFC translators found";
            return false;
        }
        ACAPI_WriteReport("IfcTester: Found %d IFC translator(s)", false, ifcTranslators.GetSize());
        
        // Find the requested translator
        API_IFCTranslatorIdentifier translator = ifcTranslators[0]; // Default to first
        bool found = false;
        
        // If configName is provided, look for it
        if (!configName.IsEmpty() && configName != "Default (Preview)") {
            for (const auto& tr : ifcTranslators) {
                if (tr.name == configName) {
                    translator = tr;
                    found = true;
                    break;
                }
            }
            
            if (!found) {
                ACAPI_WriteReport("IfcTester: Warning - Translator '%s' not found, using default '%s'", 
                    false, configName.ToCStr().Get(), translator.name.ToCStr().Get());
            } else {
                 ACAPI_WriteReport("IfcTester: Using translator '%s'", false, translator.name.ToCStr().Get());
            }
        } else {
            ACAPI_WriteReport("IfcTester: Using default translator '%s'", false, translator.name.ToCStr().Get());
        }
    
        // Double-check Demo Mode right before save operation
        protectionMode = ACAPI_Licensing_GetProtectionMode();
        if (protectionMode & APIPROT_DEMO_MASK) {
            GS::UniString msg = "Cannot export IFC in Demo Mode";
            if (errorMessage) {
                *errorMessage = msg;
            }
            return false;
        }
        
        // Set up file save parameters
        ACAPI_WriteReport("IfcTester: Setting up file save parameters...", false);
        API_FileSavePars fileSavePars = {};
        fileSavePars.file = &outputLocation;
        fileSavePars.fileTypeID = APIFType_IfcFile;
        
        // Set up IFC save parameters
        API_SavePars_Ifc ifcSavePars = {};
        ifcSavePars.translatorIdentifier = translator;
        
        // FINAL SAFETY CHECK - Do not call ACAPI_ProjectOperation_Save in Demo Mode
        // The assertion failure happens INSIDE the save call, so we must prevent calling it
        protectionMode = ACAPI_Licensing_GetProtectionMode();
        ACAPI_WriteReport("IfcTester: Final protection mode check before save: %u (0x%X)", false, protectionMode, protectionMode);
        if (protectionMode & APIPROT_DEMO_MASK) {
            GS::UniString msg = "Cannot export IFC in Demo Mode";
            if (errorMessage) {
                *errorMessage = msg;
            }
            return false;
        }
        
        // Perform export
        ACAPI_WriteReport("IfcTester: Calling ACAPI_ProjectOperation_Save...", false);
        
        // Wrap the save call in additional protection
        // Some error codes from ACAPI_ProjectOperation_Save might trigger assertions
        err = ACAPI_ProjectOperation_Save(&fileSavePars, &ifcSavePars, nullptr);
        
        // Check for specific error codes that indicate Demo Mode restrictions
        // According to ACAPI_Automate.h documentation:
        // - APIERR_REFUSEDCMD is returned when "you are running a demo version of Archicad"
        // - Error -2130312312 (0x81000088) also appears to be a Demo Mode restriction
        if (err != NoError) {
            ACAPI_WriteReport("IfcTester: ACAPI_ProjectOperation_Save returned error %d (0x%X)", false, err, (unsigned int)err);
            
            // DIRECT CHECK: If error code is -2130312312, treat as Demo Mode immediately
            if (err == -2130312312) {
                GS::UniString msg = "Cannot export IFC in Demo Mode";
                if (errorMessage) {
                    *errorMessage = msg;
                }
                return false;
            }
            
            // Explicit checks for Demo Mode error codes
            // Error code -2130312312 (0x81000088) is the Demo Mode restriction error
            const GSErrCode DEMO_MODE_ERROR_CODE = -2130312312;
            const UInt32 DEMO_MODE_ERROR_HEX = 0x81000088;
            
            bool isRefusedCmd = (err == APIERR_REFUSEDCMD);
            bool isDemoErrorCode = (err == DEMO_MODE_ERROR_CODE);
            bool isDemoErrorHex = ((UInt32)err == DEMO_MODE_ERROR_HEX);
            bool isDemoMode = (protectionMode & APIPROT_DEMO_MASK) != 0;
            
            ACAPI_WriteReport("IfcTester: Error check - APIERR_REFUSEDCMD: %s, Demo error code (dec): %s, Demo error code (hex): %s, Demo mode bit: %s", 
                false, isRefusedCmd ? "YES" : "NO", isDemoErrorCode ? "YES" : "NO", isDemoErrorHex ? "YES" : "NO", isDemoMode ? "YES" : "NO");
            
            // Check if this is a Demo Mode related error
            // APIERR_REFUSEDCMD is the documented error code for Demo Mode restrictions
            // Error code -2130312312 (0x81000088) is also seen in Demo Mode
            if (isRefusedCmd || isDemoErrorCode || isDemoErrorHex || isDemoMode) {
                GS::UniString msg = "Cannot export IFC in Demo Mode";
                if (errorMessage) {
                    *errorMessage = msg;
                }
                return false;
            }
            
            // Other errors
            ACAPI_WriteReport("IfcTester: Failed to export IFC (error %d) - not a Demo Mode error", false, err);
            if (errorMessage) {
                *errorMessage = GS::UniString::Printf("Failed to export IFC (error %d)", err);
            }
            return false;
        }
        
        // Success path
        ACAPI_WriteReport("IfcTester: ACAPI_ProjectOperation_Save succeeded", false);
        outputLocation.ToPath(&outputPath);
        ACAPI_WriteReport("IfcTester: Successfully exported IFC to: %s", false, outputPath.ToCStr().Get());
        return true;
        
        return false;
    } catch (const std::exception& e) {
        ACAPI_WriteReport("IfcTester: Exception in ExportToIFC: %s", true, e.what());
        if (errorMessage) {
            *errorMessage = GS::UniString::Printf("Exception: %s", e.what());
        }
        return false;
    } catch (...) {
        ACAPI_WriteReport("IfcTester: Unknown exception in ExportToIFC", true);
        if (errorMessage) {
            *errorMessage = "Unknown exception occurred during IFC export";
        }
        return false;
    }
}

// ============================================================================
// Required ArchiCAD Add-On Functions
// ============================================================================

/**
 * CheckEnvironment
 * Called when ArchiCAD starts to check if the Add-On can run
 */
API_AddonType CheckEnvironment (API_EnvirParams* envir)
{
    RSGetIndString(&envir->addOnInfo.name, AddOnInfoResId, 1, ACAPI_GetOwnResModule());
    RSGetIndString(&envir->addOnInfo.description, AddOnInfoResId, 2, ACAPI_GetOwnResModule());

    return APIAddon_Normal;
}

/**
 * RegisterInterface
 * Called to register menus, dialogs, and other UI elements
 */
GSErrCode RegisterInterface (void)
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
GSErrCode Initialize (void)
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
GSErrCode FreeData (void)
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
