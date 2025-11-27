#include "BrowserPalette.hpp"

namespace IfcTester::Archicad {
namespace {
const GS::Guid kPaletteGuid("{7F1D9BB7-8A08-4DDC-89F8-2A44AB8F6154}");
const char* kNotifySelectionScript =
    "if (window.__IfcTesterArchicad__ && typeof window.__IfcTesterArchicad__.selectionChanged === 'function') {"
    "window.__IfcTesterArchicad__.selectionChanged();"
    "} else if (typeof UpdateSelectedElements === 'function') {"
    "UpdateSelectedElements();"
    "}";
const char* kNotifyReadyScript =
    "if (window.__IfcTesterArchicad__ && typeof window.__IfcTesterArchicad__.onHostReady === 'function') {"
    "window.__IfcTesterArchicad__.onHostReady();"
    "}";

GS::UniString ExtractString(const GS::Ref<DG::JSBase>& jsValue)
{
    GS::Ref<DG::JSValue> value = GS::DynamicCast<DG::JSValue>(jsValue);
    if (value != nullptr && value->GetType() == DG::JSValue::STRING) {
        return value->GetString();
    }
    return GS::UniString();
}

template<typename Type>
GS::Ref<DG::JSBase> ToJsValue(const Type& value)
{
    return new DG::JSValue(value);
}

template<>
GS::Ref<DG::JSBase> ToJsValue(const BrowserPalette::ElementInfo& elem)
{
    GS::Ref<DG::JSArray> jsArray = new DG::JSArray();
    jsArray->AddItem(ToJsValue(elem.guidStr));
    jsArray->AddItem(ToJsValue(elem.ifcGuid));
    jsArray->AddItem(ToJsValue(elem.typeName));
    jsArray->AddItem(ToJsValue(elem.elementId));
    return jsArray;
}

template<typename Type>
GS::Ref<DG::JSBase> ToJsArray(const GS::Array<Type>& values)
{
    GS::Ref<DG::JSArray> jsArray = new DG::JSArray();
    for (const Type& value : values) {
        jsArray->AddItem(ToJsValue(value));
    }
    return jsArray;
}

} // namespace

GS::Ref<BrowserPalette> BrowserPalette::instance;

BrowserPalette::BrowserPalette()
    : DG::Palette(ACAPI_GetOwnResModule(), kPaletteResId, ACAPI_GetOwnResModule(), kPaletteGuid)
    , browser(GetReference(), kBrowserControlId)
    , config(WebAppConfig::Create())
{
    Attach(*this);
    BeginEventProcessing();
    InitBrowserControl();
}

BrowserPalette::~BrowserPalette()
{
    EndEventProcessing();
}

bool BrowserPalette::HasInstance()
{
    return instance != nullptr;
}

void BrowserPalette::CreateInstance()
{
    if (!HasInstance()) {
        instance = new BrowserPalette();
        ACAPI_KeepInMemory(true);
    }
}

BrowserPalette& BrowserPalette::GetInstance()
{
    DBASSERT(HasInstance());
    return *instance;
}

void BrowserPalette::DestroyInstance()
{
    if (instance != nullptr) {
        delete instance;
        instance = nullptr;
    }
}

void BrowserPalette::ShowPalette()
{
    DG::Palette::Show();
    SetMenuItemCheckedState(true);
    NotifyClientReady();
}

void BrowserPalette::HidePalette()
{
    DG::Palette::Hide();
    SetMenuItemCheckedState(false);
}

void BrowserPalette::InitBrowserControl()
{
    browser.LoadURL(config.resolvedUrl);
    RegisterJavaScriptBridge();
    NotifyClientReady();
}

void BrowserPalette::RegisterJavaScriptBridge()
{
    DG::JSObject* bridge = new DG::JSObject("ACAPI");

    bridge->AddItem(new DG::JSFunction("GetSelectedElements", [] (GS::Ref<DG::JSBase>) {
        return ToJsArray(CollectSelectedElements());
    }));

    bridge->AddItem(new DG::JSFunction("SelectElementByGuid", [] (GS::Ref<DG::JSBase> param) {
        const GS::UniString guid = ExtractString(param);
        return ToJsValue(SelectElementByGuid(guid));
    }));

    bridge->AddItem(new DG::JSFunction("GetHostInfo", [this] (GS::Ref<DG::JSBase>) {
        GS::Ref<DG::JSArray> info = new DG::JSArray();
        info->AddItem(ToJsValue(GS::UniString("Archicad")));
        info->AddItem(ToJsValue(config.resolvedUrl));
        info->AddItem(ToJsValue(config.usesDevServer));
        return info;
    }));

    bridge->AddItem(new DG::JSFunction("RefreshSelection", [] (GS::Ref<DG::JSBase>) {
        return ToJsArray(CollectSelectedElements());
    }));

    browser.RegisterAsynchJSObject(bridge);
}

void BrowserPalette::NotifyClientReady()
{
    browser.ExecuteJS(kNotifyReadyScript);
}

void BrowserPalette::NotifySelectionToClient() const
{
    browser.ExecuteJS(kNotifySelectionScript);
}

void BrowserPalette::SetMenuItemCheckedState(bool isChecked)
{
    API_MenuItemRef itemRef = {};
    GSFlags itemFlags = {};

    itemRef.menuResID = kMenuResId;
    itemRef.itemIndex = kBrowserMenuItemIndex;

    ACAPI_Interface(APIIo_GetMenuItemFlagsID, &itemRef, &itemFlags);
    if (isChecked) {
        itemFlags |= API_MenuItemChecked;
    } else {
        itemFlags &= ~API_MenuItemChecked;
    }
    ACAPI_Interface(APIIo_SetMenuItemFlagsID, &itemRef, &itemFlags);
}

void BrowserPalette::PanelResized(const DG::PanelResizeEvent& ev)
{
    BeginMoveResizeItems();
    browser.Resize(ev.GetHorizontalChange(), ev.GetVerticalChange());
    EndMoveResizeItems();
}

void BrowserPalette::PanelCloseRequested(const DG::PanelCloseRequestEvent&, bool* accepted)
{
    HidePalette();
    if (accepted != nullptr) {
        *accepted = true;
    }
}

GS::Array<BrowserPalette::ElementInfo> BrowserPalette::CollectSelectedElements()
{
    API_SelectionInfo selectionInfo;
    BNZeroMemory(&selectionInfo, sizeof(selectionInfo));
    GS::Array<API_Neig> selectedNeigs;
    ACAPI_Selection_Get(&selectionInfo, &selectedNeigs, false, false);
    BMKillHandle(reinterpret_cast<GSHandle*>(&selectionInfo.marquee.coords));

    GS::Array<ElementInfo> elements;

    for (const API_Neig& neig : selectedNeigs) {
        API_Elem_Head header = {};
        header.guid = neig.guid;
        if (ACAPI_Element_GetHeader(&header) != NoError) {
            continue;
        }

        ElementInfo info;
        info.guidStr = APIGuidToString(header.guid);
        ACAPI_Goodies(APIAny_GetElemTypeNameID, reinterpret_cast<void*>(header.typeID), &info.typeName);
        ACAPI_Database(APIDb_GetElementInfoStringID, &header.guid, &info.elementId);
        info.ifcGuid = ToIfcGuid(header.guid);
        elements.Push(info);
    }

    return elements;
}

bool BrowserPalette::SelectElementByGuid(const GS::UniString& guidValue)
{
    if (guidValue.IsEmpty()) {
        return false;
    }

    API_Guid apiGuid = APIGuidFromString(guidValue.ToCStr().Get());
    if (APIGuidIsNull(apiGuid)) {
        if (!TryConvertIfcGuid(guidValue, apiGuid)) {
            return false;
        }
    }

    API_Neig neig(apiGuid);
    return ACAPI_Element_Select({ neig }, false) == NoError;
}

bool BrowserPalette::TryConvertIfcGuid(const GS::UniString& guidValue, API_Guid& outGuid)
{
    const char* rawGuid = guidValue.ToCStr().Get();
    if (rawGuid == nullptr) {
        return false;
    }
    return ACAPI_Goodies(APIAny_IFCGuidToGuidID, const_cast<char*>(rawGuid), &outGuid) == NoError && !APIGuidIsNull(outGuid);
}

GS::UniString BrowserPalette::ToIfcGuid(const API_Guid& guid)
{
    char ifcGuid[64] = {};
    if (ACAPI_Goodies(APIAny_GuidToIFCGuidID, const_cast<API_Guid*>(&guid), ifcGuid) == NoError) {
        return GS::UniString(ifcGuid);
    }
    return GS::UniString();
}

GSErrCode __ACENV_CALL BrowserPalette::SelectionChangeHandler(const API_Neig*)
{
    if (HasInstance()) {
        GetInstance().NotifySelectionToClient();
    }
    return NoError;
}

GSErrCode __ACENV_CALL BrowserPalette::PaletteControlCallBack(Int32, API_PaletteMessageID messageId, GS::IntPtr param)
{
    switch (messageId) {
        case APIPalMsg_OpenPalette:
            if (!HasInstance()) {
                CreateInstance();
            }
            GetInstance().ShowPalette();
            break;

        case APIPalMsg_ClosePalette:
            if (HasInstance()) {
                GetInstance().HidePalette();
            }
            break;

        case APIPalMsg_IsPaletteVisible:
            if (param != 0) {
                *(reinterpret_cast<bool*>(param)) = HasInstance() && GetInstance().IsVisible();
            }
            break;

        default:
            break;
    }
    return NoError;
}

GSErrCode BrowserPalette::RegisterPaletteControlCallBack()
{
    return ACAPI_RegisterModelessWindow(
        GS::GenerateHashValue(kPaletteGuid),
        PaletteControlCallBack,
        API_PalEnabled_FloorPlan + API_PalEnabled_Section + API_PalEnabled_Elevation +
            API_PalEnabled_InteriorElevation + API_PalEnabled_3D + API_PalEnabled_Detail +
            API_PalEnabled_Worksheet + API_PalEnabled_Layout + API_PalEnabled_DocumentFrom3D,
        GSGuid2APIGuid(kPaletteGuid));
}

void ToggleBrowserPalette()
{
    if (BrowserPalette::HasInstance() && BrowserPalette::GetInstance().IsVisible()) {
        BrowserPalette::GetInstance().HidePalette();
    } else {
        if (!BrowserPalette::HasInstance()) {
            BrowserPalette::CreateInstance();
        }
        BrowserPalette::GetInstance().ShowPalette();
    }
}

} // namespace IfcTester::Archicad
