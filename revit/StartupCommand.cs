using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;

namespace IfcTesterRevit;

/// <summary>
///     External command entry point. Toggles the modeless IfcTester window.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class StartupCommand : ExternalCommand
{
    public override void Execute()
    {
        IfcTesterRevit.Application.ToggleWindow(UiApplication);
    }
}
