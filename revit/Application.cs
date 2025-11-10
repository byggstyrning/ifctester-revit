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
        var panel = Application.CreatePanel("Audit", "IfcTester");

        panel.AddPushButton<StartupCommand>("IfcTester")
            .SetImage("/IfcTesterRevit;component/Resources/Icons/IfcTester16.png")
            .SetLargeImage("/IfcTesterRevit;component/Resources/Icons/IfcTester32.png");
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