using System;
using System.IO;
using System.Linq;
using IfcTesterArchicad.Views;

namespace IfcTesterArchicad;

/// <summary>
///     Application entry point for Archicad add-in
///     Note: This is a template implementation. The actual Archicad API interfaces
///     need to be imported from the Archicad SDK based on the version being targeted.
///     Archicad uses a different API structure than Revit.
/// </summary>
public class Application
{
    public static Guid DockablePaneId = new("1FD2B40B-B3FA-4676-92A0-BC3F71E2059E");
    private static ArchicadApiServer? _apiServer;

    /// <summary>
    /// Called when Archicad loads the add-in
    /// This method name and signature may vary based on Archicad version
    /// </summary>
    public void Initialize()
    {
        CreateMenu();
        CreateDockablePane();
        
        // Start the Archicad API server
        _apiServer = new ArchicadApiServer(48882); // Use different port than Revit
        _apiServer.Start();
    }

    /// <summary>
    /// Called when Archicad unloads the add-in
    /// </summary>
    public void Terminate()
    {
        // Stop the API server when Archicad shuts down
        _apiServer?.Stop();
        _apiServer?.Dispose();
    }

    private void CreateMenu()
    {
        // Create menu item in Archicad
        // Note: Archicad menu creation is different from Revit
        // Archicad uses menu registration through the .apx manifest file
        // Additional menu items can be added programmatically here if needed
        try
        {
            // Archicad menu creation would go here
            // The exact API depends on the Archicad version
            // Example (pseudo-code):
            // MenuManager.RegisterMenuItem("IfcTester", "IfcTester", typeof(StartupCommand));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to create menu: {ex.Message}");
        }
    }

    private void CreateDockablePane()
    {
        // Register dockable pane in Archicad
        // Archicad uses a different system than Revit for dockable panes
        // The browser control can be embedded in a palette window
        try
        {
            // Dockable pane registration would go here
            // This depends on Archicad's specific API
            // Example (pseudo-code):
            // PaletteManager.RegisterPalette(DockablePaneId, "IfcTester", new IfcTesterArchicadView());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to create dockable pane: {ex.Message}");
        }
    }
}
