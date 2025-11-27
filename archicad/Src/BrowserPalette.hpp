#pragma once

#include "APIEnvir.h"
#include "ACAPinc.h"
#include "DGModule.hpp"

#include "WebAppConfig.hpp"

namespace IfcTester::Archicad {

constexpr short kAddOnInfoResId = 42000;
constexpr short kMenuResId = 42010;
constexpr short kPaletteResId = 42020;
constexpr short kBrowserMenuItemIndex = 1;
constexpr short kBrowserControlId = 1;

class BrowserPalette final : public DG::Palette,
                             public DG::PanelObserver {
public:
    struct ElementInfo {
        GS::UniString guidStr;
        GS::UniString ifcGuid;
        GS::UniString typeName;
        GS::UniString elementId;
    };

    static bool HasInstance();
    static void CreateInstance();
    static BrowserPalette& GetInstance();
    static void DestroyInstance();

    static GSErrCode RegisterPaletteControlCallBack();
    static GSErrCode __ACENV_CALL SelectionChangeHandler(const API_Neig*);

    void ShowPalette();
    void HidePalette();

private:
    BrowserPalette();
    ~BrowserPalette() override;

    void InitBrowserControl();
    void RegisterJavaScriptBridge();
    void NotifyClientReady();
    void NotifySelectionToClient() const;
    void SetMenuItemCheckedState(bool isChecked);

    static GS::Array<ElementInfo> CollectSelectedElements();
    static bool SelectElementByGuid(const GS::UniString& guidValue);
    static bool TryConvertIfcGuid(const GS::UniString& guidValue, API_Guid& outGuid);
    static GS::UniString ToIfcGuid(const API_Guid& guid);

    void PanelResized(const DG::PanelResizeEvent& ev) override;
    void PanelCloseRequested(const DG::PanelCloseRequestEvent& ev, bool* accepted) override;

    static GSErrCode __ACENV_CALL PaletteControlCallBack(Int32 paletteId, API_PaletteMessageID messageId, GS::IntPtr param);

    static GS::Ref<BrowserPalette> instance;
    DG::Browser browser;
    WebAppConfig config;
};

void ToggleBrowserPalette();

} // namespace IfcTester::Archicad
