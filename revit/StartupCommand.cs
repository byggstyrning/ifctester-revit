using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;

namespace WebAecRevit;

/// <summary>
///     External command entry point
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class StartupCommand : ExternalCommand
{
    public override void Execute()
    {
        var panel = UiApplication.GetDockablePane(new DockablePaneId(WebAecRevit.Application.DockablePaneId));

        if (panel is not null)
        {
            if (!panel.IsShown())
            {
                panel.Show();
            }
            else
            {
                panel.Hide();
            }
        }
    }
}