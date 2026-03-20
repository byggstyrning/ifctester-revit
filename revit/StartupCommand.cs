using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;

namespace IfcTesterRevit;

/// <summary>
///     External command entry point. Toggles the dockable pane and manages
///     the WebView2 lifecycle: created on show, destroyed on hide.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class StartupCommand : ExternalCommand
{
    public override void Execute()
    {
        var panel = UiApplication.GetDockablePane(new DockablePaneId(IfcTesterRevit.Application.DockablePaneId));

        if (panel is not null)
        {
            if (!panel.IsShown())
            {
                IfcTesterRevit.Application.EnsureWebViewAndServer(UiApplication);
                panel.Show();
            }
            else
            {
                IfcTesterRevit.Application.TearDownWebViewAndServer();
                panel.Hide();
            }
        }
    }
}
