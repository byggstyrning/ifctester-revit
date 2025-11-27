#include "APIEnvir.h"
#include "ACAPinc.h"

#include "BrowserPalette.hpp"

using namespace IfcTester::Archicad;

namespace {

void TogglePalette()
{
    ToggleBrowserPalette();
}

}

GSErrCode __ACENV_CALL MenuCommandHandler(const API_MenuParams* menuParams)
{
    if (menuParams == nullptr) {
        return NoError;
    }

    if (menuParams->menuItemRef.menuResID == kMenuResId && menuParams->menuItemRef.itemIndex == kBrowserMenuItemIndex) {
        TogglePalette();
    }

    return NoError;
}

API_AddonType __ACDLL_CALL CheckEnvironment(API_EnvirParams* envir)
{
    if (envir == nullptr) {
        return APIAddon_Preload;
    }

    RSGetIndString(&envir->addOnInfo.name, kAddOnInfoResId, 1, ACAPI_GetOwnResModule());
    RSGetIndString(&envir->addOnInfo.description, kAddOnInfoResId, 2, ACAPI_GetOwnResModule());

    return APIAddon_Preload;
}

GSErrCode __ACDLL_CALL RegisterInterface(void)
{
    return ACAPI_Register_Menu(kMenuResId, 0, MenuCode_UserDef, MenuFlag_Default);
}

GSErrCode __ACENV_CALL Initialize(void)
{
    GSErrCode err = ACAPI_Install_MenuHandler(kMenuResId, MenuCommandHandler);
    if (DBERROR(err != NoError)) {
        return err;
    }

    err = ACAPI_Notify_CatchSelectionChange(BrowserPalette::SelectionChangeHandler);
    if (DBERROR(err != NoError)) {
        return err;
    }

    err = BrowserPalette::RegisterPaletteControlCallBack();
    if (DBERROR(err != NoError)) {
        return err;
    }

    return NoError;
}

GSErrCode __ACENV_CALL FreeData(void)
{
    BrowserPalette::DestroyInstance();
    return NoError;
}
