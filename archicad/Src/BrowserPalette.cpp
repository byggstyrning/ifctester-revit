/**
 * IfcTester ArchiCAD Add-On
 * 
 * Browser Palette Implementation
 * Implements the dockable palette with browser control for the IfcTester web interface.
 */

#include "BrowserPalette.hpp"
#include "ArchiCADApiServer.hpp"

namespace IfcTester {

// Static member initialization
const GS::Guid BrowserPalette::paletteGuid("{A7B3C5D9-8E2F-4A6B-9C1D-3E5F7A8B9C0D}");
const char* BrowserPalette::siteURL = "http://app.localhost/index.html";
BrowserPalette* BrowserPalette::instance = nullptr;
bool BrowserPalette::paletteRegistered = false;
Int32 BrowserPalette::registeredPaletteId = 0;

// ============================================================================
// Constructor / Destructor
// ============================================================================

BrowserPalette::BrowserPalette() :
    DG::Palette(ACAPI_GetOwnResModule(), BrowserPaletteResId, ACAPI_GetOwnResModule(), paletteGuid),
    browser(GetReference(), BrowserId)
{
    instance = this;
    
    // Attach observers
    Attach(*this);
    browser.Attach(*this);
    
    BeginEventProcessing();
    InitBrowserControl();
    
    // Register with ArchiCAD palette management
    if (!paletteRegistered) {
        API_PaletteInfo paletteInfo;
        BNZeroMemory(&paletteInfo, sizeof(API_PaletteInfo));
        paletteInfo.refCon = (Int32)GetId();
        paletteInfo.paletteGuid = paletteGuid;
        
        ACAPI_RegisterModelessWindow(
            GetId(),
            PaletteControlCallback,
            API_PalEnabled_FloorPlan | API_PalEnabled_Section | API_PalEnabled_3DWindow | 
            API_PalEnabled_Detail | API_PalEnabled_Worksheet | API_PalEnabled_Layout
        );
        
        registeredPaletteId = GetId();
        paletteRegistered = true;
    }
}

BrowserPalette::~BrowserPalette()
{
    EndEventProcessing();
    browser.Detach(*this);
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
    UNUSED_PARAMETER(param);
    
    switch (messageID) {
        case APIPalMsg_HideSticky:
            if (instance != nullptr) {
                instance->Hide();
            }
            break;
            
        case APIPalMsg_ShowSticky:
            if (instance != nullptr) {
                instance->Show();
            }
            break;
            
        case APIPalMsg_DisableSticky:
            if (instance != nullptr) {
                instance->Disable();
            }
            break;
            
        case APIPalMsg_EnableSticky:
            if (instance != nullptr) {
                instance->Enable();
            }
            break;
            
        case APIPalMsg_IsPaletteVisible:
            return instance != nullptr && instance->IsVisible() ? 1 : 0;
            
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
    // Construct the URL with API server information
    GS::UniString url(siteURL);
    url += "?api=http://localhost:";
    url += GS::UniString::Printf("%d", ApiServerPort);
    
    // Load the web application URL
    browser.LoadURL(url.ToCStr().Get());
    
    // Register the JavaScript API object
    RegisterACAPIJavaScriptObject();
}

void BrowserPalette::RegisterACAPIJavaScriptObject()
{
    // Create the ACAPI JavaScript object
    // This allows the web application to call ArchiCAD functions
    DG::JSObject* jsACAPI = new DG::JSObject("ACAPI");
    
    // GetSelectedElements - Returns array of selected element info
    jsACAPI->AddItem(new DG::JSFunction("GetSelectedElements", [](GS::Ref<DG::JSBase>) {
        return ConvertToJavaScriptVariable(GetSelectedElements());
    }));
    
    // SelectElementByGUID - Selects an element by its GUID
    jsACAPI->AddItem(new DG::JSFunction("SelectElementByGUID", [](GS::Ref<DG::JSBase> param) {
        GS::UniString guidStr = GetStringFromJavaScriptVariable(param);
        bool success = SelectElementByGUID(guidStr);
        return ConvertToJavaScriptVariable(success);
    }));
    
    // AddElementToSelection - Adds an element to the current selection
    jsACAPI->AddItem(new DG::JSFunction("AddElementToSelection", [](GS::Ref<DG::JSBase> param) {
        GS::UniString guidStr = GetStringFromJavaScriptVariable(param);
        API_Guid guid = APIGuidFromString(guidStr.ToCStr().Get());
        GS::Array<API_Neig> elemsToSelect;
        elemsToSelect.Push(API_Neig(guid));
        GSErrCode err = ACAPI_Element_Select(elemsToSelect, true);
        return ConvertToJavaScriptVariable(err == NoError);
    }));
    
    // RemoveElementFromSelection - Removes an element from the current selection
    jsACAPI->AddItem(new DG::JSFunction("RemoveElementFromSelection", [](GS::Ref<DG::JSBase> param) {
        GS::UniString guidStr = GetStringFromJavaScriptVariable(param);
        API_Guid guid = APIGuidFromString(guidStr.ToCStr().Get());
        GS::Array<API_Neig> elemsToDeselect;
        elemsToDeselect.Push(API_Neig(guid));
        GSErrCode err = ACAPI_Element_Select(elemsToDeselect, false);
        return ConvertToJavaScriptVariable(err == NoError);
    }));
    
    // GetIFCConfigurations - Returns available IFC export configurations
    jsACAPI->AddItem(new DG::JSFunction("GetIFCConfigurations", [](GS::Ref<DG::JSBase>) {
        return ConvertToJavaScriptVariable(GetIFCExportConfigurations());
    }));
    
    // ExportToIFC - Exports the model to IFC
    jsACAPI->AddItem(new DG::JSFunction("ExportToIFC", [](GS::Ref<DG::JSBase> param) {
        GS::UniString configName = GetStringFromJavaScriptVariable(param);
        GS::UniString outputPath;
        bool success = ExportToIFC(configName, outputPath);
        
        if (success) {
            return ConvertToJavaScriptVariable(outputPath);
        } else {
            return ConvertToJavaScriptVariable(GS::UniString(""));
        }
    }));
    
    // GetAPIServerStatus - Returns the status of the API server
    jsACAPI->AddItem(new DG::JSFunction("GetAPIServerStatus", [](GS::Ref<DG::JSBase>) {
        DG::JSObject* status = new DG::JSObject("status");
        status->AddItem(new DG::JSValue("connected", new DG::JSBool(true)));
        status->AddItem(new DG::JSValue("port", new DG::JSNumber(ApiServerPort)));
        status->AddItem(new DG::JSValue("version", new DG::JSString("1.0.0")));
        return GS::Ref<DG::JSBase>(status);
    }));
    
    // Register the JavaScript object asynchronously
    browser.RegisterAsynchJSObject(jsACAPI);
}

// ============================================================================
// JavaScript Type Conversion
// ============================================================================

GS::Ref<DG::JSBase> BrowserPalette::ConvertToJavaScriptVariable(const GS::Array<ElementInfo>& elements)
{
    DG::JSArray* jsArray = new DG::JSArray();
    
    for (const ElementInfo& elem : elements) {
        DG::JSObject* jsElem = new DG::JSObject();
        jsElem->AddItem(new DG::JSValue("guid", new DG::JSString(elem.guidStr.ToCStr().Get())));
        jsElem->AddItem(new DG::JSValue("typeName", new DG::JSString(elem.typeName.ToCStr().Get())));
        jsElem->AddItem(new DG::JSValue("elemID", new DG::JSString(elem.elemID.ToCStr().Get())));
        jsArray->AddItem(jsElem);
    }
    
    return GS::Ref<DG::JSBase>(jsArray);
}

GS::Ref<DG::JSBase> BrowserPalette::ConvertToJavaScriptVariable(const GS::Array<IFCConfiguration>& configs)
{
    DG::JSArray* jsArray = new DG::JSArray();
    
    for (const IFCConfiguration& config : configs) {
        DG::JSObject* jsConfig = new DG::JSObject();
        jsConfig->AddItem(new DG::JSValue("name", new DG::JSString(config.name.ToCStr().Get())));
        jsConfig->AddItem(new DG::JSValue("description", new DG::JSString(config.description.ToCStr().Get())));
        jsConfig->AddItem(new DG::JSValue("version", new DG::JSString(config.version.ToCStr().Get())));
        jsArray->AddItem(jsConfig);
    }
    
    return GS::Ref<DG::JSBase>(jsArray);
}

GS::Ref<DG::JSBase> BrowserPalette::ConvertToJavaScriptVariable(bool value)
{
    return GS::Ref<DG::JSBase>(new DG::JSBool(value));
}

GS::Ref<DG::JSBase> BrowserPalette::ConvertToJavaScriptVariable(const GS::UniString& value)
{
    return GS::Ref<DG::JSBase>(new DG::JSString(value.ToCStr().Get()));
}

GS::UniString BrowserPalette::GetStringFromJavaScriptVariable(GS::Ref<DG::JSBase> param)
{
    if (param == nullptr) {
        return GS::UniString();
    }
    
    DG::JSString* jsString = dynamic_cast<DG::JSString*>(param.Get());
    if (jsString != nullptr) {
        return GS::UniString(jsString->Get());
    }
    
    // Try to convert from array (first element)
    DG::JSArray* jsArray = dynamic_cast<DG::JSArray*>(param.Get());
    if (jsArray != nullptr && jsArray->GetSize() > 0) {
        DG::JSString* firstStr = dynamic_cast<DG::JSString*>(jsArray->GetItem(0).Get());
        if (firstStr != nullptr) {
            return GS::UniString(firstStr->Get());
        }
    }
    
    return GS::UniString();
}

// ============================================================================
// Panel Observer Methods
// ============================================================================

void BrowserPalette::PanelResized(const DG::PanelResizeEvent& ev)
{
    // Resize browser to fill the palette
    short width = ev.GetHorizontalChange() + browser.GetWidth();
    short height = ev.GetVerticalChange() + browser.GetHeight();
    browser.Resize(width, height);
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

// ============================================================================
// Browser Observer Methods
// ============================================================================

void BrowserPalette::BrowserLoadFinished(const DG::BrowserLoadEvent& ev)
{
    if (ev.WasSuccessful()) {
        // Page loaded successfully, update selection info
        UpdateSelectedElementsOnHTML();
    }
}

} // namespace IfcTester
