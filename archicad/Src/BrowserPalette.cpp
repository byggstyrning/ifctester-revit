/**
 * IfcTester ArchiCAD Add-On
 * 
 * Browser Palette Implementation
 * Implements the dockable palette with browser control for the IfcTester web interface.
 * 
 * Compatible with ArchiCAD 27
 */

#include "BrowserPalette.hpp"
#include "ArchiCADApiServer.hpp"
#include <ctime>

namespace IfcTester {

// Static member initialization
const GS::Guid BrowserPalette::paletteGuid("A7B3C5D9-8E2F-4A6B-9C1D-3E5F7A8B9C0D");
BrowserPalette* BrowserPalette::instance = nullptr;
bool BrowserPalette::paletteRegistered = false;
Int32 BrowserPalette::registeredPaletteId = 0;

// Web app is served from the API server which also serves the WebApp folder
// Web Workers don't work with file:// URLs due to browser security restrictions
// The API server serves static files from the WebApp folder alongside the REST API

// ============================================================================
// Constructor / Destructor
// ============================================================================

BrowserPalette::BrowserPalette() :
    DG::Palette(ACAPI_GetOwnResModule(), BrowserPaletteResId, ACAPI_GetOwnResModule(), paletteGuid),
    browser(GetReference(), BrowserId)
{
    instance = this;
    
    // Attach panel observer
    Attach(*this);
    
    BeginEventProcessing();
    InitBrowserControl();
    
    // Register with ArchiCAD palette management
    if (!paletteRegistered) {
        GSFlags controlFlags = API_PalEnabled_FloorPlan | API_PalEnabled_Section | API_PalEnabled_Detail | 
                               API_PalEnabled_Worksheet | API_PalEnabled_Layout | API_PalEnabled_3D;
        API_Guid apiPaletteGuid = GSGuid2APIGuid(paletteGuid);
        ACAPI_RegisterModelessWindow(
            GetId(),
            PaletteControlCallback,
            controlFlags,
            apiPaletteGuid
        );
        
        registeredPaletteId = GetId();
        paletteRegistered = true;
    }
}

BrowserPalette::~BrowserPalette()
{
    EndEventProcessing();
    Detach(*this);
    instance = nullptr;
}

// ============================================================================
// Public Methods
// ============================================================================

bool BrowserPalette::IsVisible() const
{
    return DG::Palette::IsVisible();
}

void BrowserPalette::UpdateSelectedElementsOnHTML()
{
    // Execute JavaScript to update the selection info in the web interface
    browser.ExecuteJS("if (window.ACAPI && window.ACAPI.onSelectionChanged) { window.ACAPI.onSelectionChanged(); }");
}

BrowserPalette* BrowserPalette::GetInstance()
{
    return instance;
}

// ============================================================================
// Static Handlers
// ============================================================================

GSErrCode __ACENV_CALL BrowserPalette::SelectionChangeHandler(const API_Neig* /*selElemNeig*/)
{
    // Update the web interface when selection changes
    if (instance != nullptr && instance->IsVisible()) {
        instance->UpdateSelectedElementsOnHTML();
    }
    
    return NoError;
}

GSErrCode BrowserPalette::RegisterPaletteControlCallBack()
{
    // Callback registration is done in constructor
    return NoError;
}

GSErrCode __ACENV_CALL BrowserPalette::PaletteControlCallback(Int32 paletteId, API_PaletteMessageID messageID, GS::IntPtr param)
{
    UNUSED_PARAMETER(paletteId);
    
    switch (messageID) {
        case APIPalMsg_ClosePalette:
            if (instance != nullptr) {
                instance->Hide();
            }
            break;
            
        case APIPalMsg_HidePalette_Begin:
            if (instance != nullptr) {
                instance->Hide();
            }
            break;
            
        case APIPalMsg_HidePalette_End:
            // Can show again if needed
            break;
            
        case APIPalMsg_DisableItems_Begin:
            // Disable interactions during user input
            break;
            
        case APIPalMsg_DisableItems_End:
            // Re-enable interactions
            break;
            
        case APIPalMsg_OpenPalette:
            if (instance != nullptr) {
                instance->Show();
            }
            break;
            
        case APIPalMsg_IsPaletteVisible:
            if (param != 0) {
                bool* isVisible = reinterpret_cast<bool*>(param);
                *isVisible = (instance != nullptr && instance->IsVisible());
            }
            break;
            
        default:
            break;
    }
    
    return NoError;
}

// ============================================================================
// Browser Control
// ============================================================================

void BrowserPalette::InitBrowserControl()
{
    // Construct the URL - web app is served from the same API server
    // Add cache-busting parameter to ensure we get the latest version
    GS::UniString url = GS::UniString::Printf("http://127.0.0.1:%d/?t=%lld", ApiServerPort, (long long)std::time(nullptr));
    
    // Log the URL being loaded
    ACAPI_WriteReport("IfcTester Browser: Loading URL: %s", false, url.ToCStr().Get());
    ACAPI_WriteReport("IfcTester Browser: API Server Port: %d", false, ApiServerPort);
    
    // Load the web application URL
    browser.LoadURL(url);
    
    // Register the JavaScript API object
    RegisterACAPIJavaScriptObject();
    
    // Connect to loading state change event
    browser.onLoadingStateChange += [this](const DG::BrowserBase& source, const DG::BrowserLoadingStateChangeArg& arg) {
        this->OnBrowserLoadingStateChange(source, arg);
    };
}

void BrowserPalette::OnBrowserLoadingStateChange(const DG::BrowserBase& /*source*/, const DG::BrowserLoadingStateChangeArg& eventArg)
{
    // When loading finishes, update selection info
    if (!eventArg.isLoading) {
        UpdateSelectedElementsOnHTML();
    }
}

void BrowserPalette::RegisterACAPIJavaScriptObject()
{
    // Create the ACAPI JavaScript object
    // This allows the web application to call ArchiCAD functions
    GS::Ref<JS::Object> jsACAPI = new JS::Object("ACAPI");
    
    // GetSelectedElements - Returns array of selected element info
    jsACAPI->AddItem(GS::Ref<JS::Function>(new JS::Function("GetSelectedElements", [](GS::Ref<JS::Base>) {
        return ConvertToJavaScriptVariable(GetSelectedElements());
    })));
    
    // SelectElementByGUID - Selects an element by its GUID
    jsACAPI->AddItem(GS::Ref<JS::Function>(new JS::Function("SelectElementByGUID", [](GS::Ref<JS::Base> param) {
        GS::UniString guidStr = GetStringFromJavaScriptVariable(param);
        bool success = SelectElementByGUID(guidStr);
        return ConvertToJavaScriptVariable(success);
    })));
    
    // AddElementToSelection - Adds an element to the current selection
    jsACAPI->AddItem(GS::Ref<JS::Function>(new JS::Function("AddElementToSelection", [](GS::Ref<JS::Base> param) {
        GS::UniString guidStr = GetStringFromJavaScriptVariable(param);
        API_Guid guid = APIGuidFromString(guidStr.ToCStr().Get());
        GS::Array<API_Neig> elemsToSelect;
        elemsToSelect.Push(API_Neig(guid));
        GSErrCode err = ACAPI_Selection_Select(elemsToSelect, true);
        return ConvertToJavaScriptVariable(err == NoError);
    })));
    
    // RemoveElementFromSelection - Removes an element from the current selection
    jsACAPI->AddItem(GS::Ref<JS::Function>(new JS::Function("RemoveElementFromSelection", [](GS::Ref<JS::Base> param) {
        GS::UniString guidStr = GetStringFromJavaScriptVariable(param);
        API_Guid guid = APIGuidFromString(guidStr.ToCStr().Get());
        GS::Array<API_Neig> elemsToDeselect;
        elemsToDeselect.Push(API_Neig(guid));
        GSErrCode err = ACAPI_Selection_Select(elemsToDeselect, false);
        return ConvertToJavaScriptVariable(err == NoError);
    })));
    
    // GetIFCConfigurations - Returns available IFC export configurations
    jsACAPI->AddItem(GS::Ref<JS::Function>(new JS::Function("GetIFCConfigurations", [](GS::Ref<JS::Base>) {
        return ConvertToJavaScriptVariable(GetIFCExportConfigurations());
    })));
    
    // ExportToIFC - Exports the model to IFC
    jsACAPI->AddItem(GS::Ref<JS::Function>(new JS::Function("ExportToIFC", [](GS::Ref<JS::Base> param) {
        GS::UniString configName = GetStringFromJavaScriptVariable(param);
        GS::UniString outputPath;
        bool success = ExportToIFC(configName, outputPath);
        
        if (success) {
            return ConvertToJavaScriptVariable(outputPath);
        } else {
            return ConvertToJavaScriptVariable(GS::UniString(""));
        }
    })));
    
    // GetAPIServerStatus - Returns the status of the API server
    jsACAPI->AddItem(GS::Ref<JS::Function>(new JS::Function("GetAPIServerStatus", [](GS::Ref<JS::Base>) {
        GS::Ref<JS::Object> status = new JS::Object("status");
        status->AddItem("connected", GS::Ref<JS::Base>(new JS::Value(true)));
        status->AddItem("port", GS::Ref<JS::Base>(new JS::Value((Int32)ApiServerPort)));
        status->AddItem("version", GS::Ref<JS::Base>(new JS::Value("1.0.0")));
        return GS::Ref<JS::Base>(status);
    })));
    
    // Register the JavaScript object asynchronously
    browser.RegisterAsynchJSObject(jsACAPI);
}

// ============================================================================
// JavaScript Type Conversion
// ============================================================================

GS::Ref<JS::Base> BrowserPalette::ConvertToJavaScriptVariable(const GS::Array<ElementInfo>& elements)
{
    GS::Ref<JS::Array> jsArray = new JS::Array();
    
    for (const ElementInfo& elem : elements) {
        GS::Ref<JS::Object> jsElem = new JS::Object();
        jsElem->AddItem("guid", GS::Ref<JS::Base>(new JS::Value(elem.guidStr)));
        jsElem->AddItem("typeName", GS::Ref<JS::Base>(new JS::Value(elem.typeName)));
        jsElem->AddItem("elemID", GS::Ref<JS::Base>(new JS::Value(elem.elemID)));
        jsArray->AddItem(jsElem);
    }
    
    return jsArray;
}

GS::Ref<JS::Base> BrowserPalette::ConvertToJavaScriptVariable(const GS::Array<IFCConfiguration>& configs)
{
    GS::Ref<JS::Array> jsArray = new JS::Array();
    
    for (const IFCConfiguration& config : configs) {
        GS::Ref<JS::Object> jsConfig = new JS::Object();
        jsConfig->AddItem("name", GS::Ref<JS::Base>(new JS::Value(config.name)));
        jsConfig->AddItem("description", GS::Ref<JS::Base>(new JS::Value(config.description)));
        jsConfig->AddItem("version", GS::Ref<JS::Base>(new JS::Value(config.version)));
        jsArray->AddItem(jsConfig);
    }
    
    return jsArray;
}

GS::Ref<JS::Base> BrowserPalette::ConvertToJavaScriptVariable(bool value)
{
    return GS::Ref<JS::Base>(new JS::Value(value));
}

GS::Ref<JS::Base> BrowserPalette::ConvertToJavaScriptVariable(const GS::UniString& value)
{
    return GS::Ref<JS::Base>(new JS::Value(value));
}

GS::UniString BrowserPalette::GetStringFromJavaScriptVariable(GS::Ref<JS::Base> param)
{
    if (param == nullptr) {
        return GS::UniString();
    }
    
    JS::Value* jsValue = dynamic_cast<JS::Value*>(&*param);
    if (jsValue != nullptr && jsValue->GetType() == JS::Value::STRING) {
        return jsValue->GetString();
    }
    
    // Try to convert from array (first element)
    JS::Array* jsArray = dynamic_cast<JS::Array*>(&*param);
    if (jsArray != nullptr && !jsArray->GetItemArray().IsEmpty()) {
        JS::Value* firstVal = dynamic_cast<JS::Value*>(&*jsArray->GetItemArray()[0]);
        if (firstVal != nullptr && firstVal->GetType() == JS::Value::STRING) {
            return firstVal->GetString();
        }
    }
    
    return GS::UniString();
}

// ============================================================================
// Panel Observer Methods
// ============================================================================

void BrowserPalette::PanelResized(const DG::PanelResizeEvent& ev)
{
    // Resize browser to fill the palette (using official example pattern)
    BeginMoveResizeItems();
    browser.Resize(ev.GetHorizontalChange(), ev.GetVerticalChange());
    EndMoveResizeItems();
}

void BrowserPalette::PanelCloseRequested(const DG::PanelCloseRequestEvent& /*ev*/, bool* accepted)
{
    *accepted = true;
}

void BrowserPalette::PanelClosed(const DG::PanelCloseEvent& /*ev*/)
{
    // Just hide, don't destroy - allows reopening
    Hide();
}

} // namespace IfcTester
