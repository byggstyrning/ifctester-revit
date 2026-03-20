using System.Linq;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;
using IfcTesterRevit.Views;

namespace IfcTesterRevit;

/// <summary>
///     Application entry point
/// </summary>
[UsedImplicitly]
public class Application : ExternalApplication
{
    private static RevitApiServer? _apiServer;
    private static IfcTesterWindow? _window;

    public override void OnStartup()
    {
        CreateRibbon();
    }

    public override void OnShutdown()
    {
        try { _window?.ForceClose(); } catch { }
        _window = null;

        _apiServer?.Stop();
        _apiServer?.Dispose();
    }

    public static void ToggleWindow(UIApplication uiApp)
    {
        if (_window != null && _window.IsVisible)
        {
            _window.Hide();
            return;
        }

        if (_apiServer == null)
        {
            _apiServer = new RevitApiServer(48881);
            _apiServer.Start(uiApp);
        }

        if (_window == null)
        {
            _window = new IfcTesterWindow();
            _window.Owner = null;
        }

        _window.Show();
        _window.Activate();
    }

    private void CreateRibbon()
    {
        RibbonPanel? existingPanel = null;

        try
        {
            var panels = Context.UiControlledApplication.GetRibbonPanels(Tab.AddIns);
            existingPanel = panels.FirstOrDefault(p => p.Name == "Audit");
        }
        catch
        {
        }

        RibbonPanel panel;
        if (existingPanel != null)
        {
            panel = existingPanel;
        }
        else
        {
            panel = Context.UiControlledApplication.CreateRibbonPanel(Tab.AddIns, "Audit");
        }

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
}
