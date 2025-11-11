using System.Linq;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.Decorators;
using Nice3point.Revit.Toolkit.External;
using IfcTesterRevit.Views;

namespace IfcTesterRevit;

/// <summary>
///     Application entry point
/// </summary>
[UsedImplicitly]
public class Application : ExternalApplication
{
    public static Guid DockablePaneId = new("0FD2B40B-B3FA-4676-92A0-BC3F71E2059D");
    private static RevitApiServer? _apiServer;

    public override void OnStartup()
    {
        CreateRibbon();
        CreateDockablePane();
        
        // Start the Revit API server
        _apiServer = new RevitApiServer(48881);
        _apiServer.Start(Context.UiApplication);
    }

    public override void OnShutdown()
    {
        // Stop the API server when Revit shuts down
        _apiServer?.Stop();
        _apiServer?.Dispose();
    }

    private void CreateRibbon()
    {
        // Use Tab enum instead of string - Tab.AddIns is the correct way
        RibbonPanel? existingPanel = null;
        
        try
        {
            var panels = Context.UiControlledApplication.GetRibbonPanels(Tab.AddIns);
            existingPanel = panels.FirstOrDefault(p => p.Name == "Audit");
        }
        catch
        {
            // Panel doesn't exist yet, will create it
        }
        
        RibbonPanel panel;
        if (existingPanel != null)
        {
            panel = existingPanel;
        }
        else
        {
            // Create new "Audit" panel in Add-ins tab using Tab enum
            panel = Context.UiControlledApplication.CreateRibbonPanel(Tab.AddIns, "Audit");
        }

        // Add the button to the panel
        var pushButtonData = new PushButtonData(
            "IfcTester",
            "IfcTester",
            System.Reflection.Assembly.GetExecutingAssembly().Location,
            typeof(StartupCommand).FullName
        );

        pushButtonData.Image = new System.Windows.Media.Imaging.BitmapImage(
            new Uri("pack://application:,,,/IfcTesterRevit;component/Resources/Icons/IfcTester16.png")
        );
        pushButtonData.LargeImage = new System.Windows.Media.Imaging.BitmapImage(
            new Uri("pack://application:,,,/IfcTesterRevit;component/Resources/Icons/IfcTester32.png")
        );

        panel.AddItem(pushButtonData);
    }

    private void CreateDockablePane()
    {
        if (!DockablePane.PaneIsRegistered(new DockablePaneId(DockablePaneId)))
            {
                // Register the dockable pane
                DockablePaneProvider
                    .Register(Context.UiControlledApplication, DockablePaneId, "IfcTester")
                    .SetConfiguration((data) =>
                    {
                        data.FrameworkElement = new IfcTesterRevitView();
                        data.InitialState = new DockablePaneState
                        {
                            DockPosition = DockPosition.Right,
                            MinimumHeight = 900,
                            MinimumWidth = 450,
                        };
                    });
            }
    }
}