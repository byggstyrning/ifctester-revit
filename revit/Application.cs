using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.Decorators;
using Nice3point.Revit.Toolkit.External;
using WebAecRevit.Views;

namespace WebAecRevit;

/// <summary>
///     Application entry point
/// </summary>
[UsedImplicitly]
public class Application : ExternalApplication
{
    public static Guid DockablePaneId = new("0FD2B40B-B3FA-4676-92A0-BC3F71E2059D");

    public override void OnStartup()
    {
        CreateRibbon();
        CreateDockablePane();
    }

    private void CreateRibbon()
    {
        var panel = Application.CreatePanel("Commands", "WebAecRevit");

        panel.AddPushButton<StartupCommand>("Execute")
            .SetImage("/WebAecRevit;component/Resources/Icons/RibbonIcon16.png")
            .SetLargeImage("/WebAecRevit;component/Resources/Icons/RibbonIcon32.png");
    }

    private void CreateDockablePane()
    {
        if (!DockablePane.PaneIsRegistered(new DockablePaneId(DockablePaneId)))
            {
                // Register the dockable pane
                DockablePaneProvider
                    .Register(Context.UiControlledApplication, DockablePaneId, "WebAecRevit")
                    .SetConfiguration((data) =>
                    {
                        data.FrameworkElement = new WebAecRevitView();
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