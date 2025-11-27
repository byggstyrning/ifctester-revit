/**
 * IfcTester ArchiCAD Add-On
 * 
 * Browser Palette Header
 * Defines the dockable palette with browser control for the IfcTester web interface.
 */

#ifndef BROWSER_PALETTE_HPP
#define BROWSER_PALETTE_HPP

#include "IfcTesterArchiCAD.hpp"
#include "DGModule.hpp"
#include "DGBrowser.hpp"
#include "DGDialog.hpp"

namespace IfcTester {

/**
 * BrowserPalette class
 * Implements a dockable modeless dialog (palette) with an embedded browser control
 * that hosts the IfcTester web application.
 */
class BrowserPalette : public DG::Palette,
                       public DG::PanelObserver,
                       public DG::BrowserObserver
{
public:
    /**
     * Constructor
     * Creates the palette and initializes the browser control
     */
    BrowserPalette();
    
    /**
     * Destructor
     */
    virtual ~BrowserPalette();
    
    /**
     * Check if the palette is visible
     */
    bool IsVisible() const;
    
    /**
     * Update the selection information in the web interface
     */
    void UpdateSelectedElementsOnHTML();
    
    /**
     * Selection change handler
     * Called when selection changes in ArchiCAD
     */
    static GSErrCode __ACENV_CALL SelectionChangeHandler(const API_Neig* selElemNeig);
    
    /**
     * Register palette control callback
     */
    static GSErrCode RegisterPaletteControlCallBack();
    
    /**
     * Get the singleton instance
     */
    static BrowserPalette* GetInstance();

protected:
    // DG::PanelObserver overrides
    virtual void PanelResized(const DG::PanelResizeEvent& ev) override;
    virtual void PanelCloseRequested(const DG::PanelCloseRequestEvent& ev, bool* accepted) override;
    virtual void PanelClosed(const DG::PanelCloseEvent& ev) override;
    
    // DG::BrowserObserver overrides
    virtual void BrowserLoadFinished(const DG::BrowserLoadEvent& ev) override;

private:
    /**
     * Initialize the browser control
     * Sets up the URL, registers JavaScript objects, and configures communication
     */
    void InitBrowserControl();
    
    /**
     * Register the ACAPI JavaScript object
     * This allows JavaScript in the web page to call ArchiCAD API functions
     */
    void RegisterACAPIJavaScriptObject();
    
    /**
     * Convert element info array to JavaScript variable
     */
    static GS::Ref<DG::JSBase> ConvertToJavaScriptVariable(const GS::Array<ElementInfo>& elements);
    
    /**
     * Convert IFC configurations to JavaScript variable
     */
    static GS::Ref<DG::JSBase> ConvertToJavaScriptVariable(const GS::Array<IFCConfiguration>& configs);
    
    /**
     * Convert boolean to JavaScript variable
     */
    static GS::Ref<DG::JSBase> ConvertToJavaScriptVariable(bool value);
    
    /**
     * Convert string to JavaScript variable
     */
    static GS::Ref<DG::JSBase> ConvertToJavaScriptVariable(const GS::UniString& value);
    
    /**
     * Get string from JavaScript variable
     */
    static GS::UniString GetStringFromJavaScriptVariable(GS::Ref<DG::JSBase> param);
    
    /**
     * Palette control callback
     */
    static GSErrCode __ACENV_CALL PaletteControlCallback(Int32 paletteId, API_PaletteMessageID messageID, GS::IntPtr param);
    
    // Browser control
    DG::Browser browser;
    
    // Palette GUID for registration
    static const GS::Guid paletteGuid;
    
    // URL for the web application
    static const char* siteURL;
    
    // Singleton instance
    static BrowserPalette* instance;
    
    // Palette registered flag
    static bool paletteRegistered;
    
    // Palette ID for callbacks
    static Int32 registeredPaletteId;
};

} // namespace IfcTester

#endif // BROWSER_PALETTE_HPP
