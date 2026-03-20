using System.Linq;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;

namespace IfcTesterRevit;

/// <summary>
///     Application entry point
/// </summary>
[UsedImplicitly]
public class Application : ExternalApplication
{
    private static RevitApiServer? _apiServer;
    private static int _port = 48881;

    public override void OnStartup()
    {
        CreateRibbon();
    }

    public override void OnShutdown()
    {
        _apiServer?.Stop();
        _apiServer?.Dispose();
    }

    /// <summary>
    /// Starts the HTTP server (if not already running) and opens the web app
    /// in the user's default browser. This avoids loading WebView2 (Chromium)
    /// DLLs into Revit's process, which conflict with Revit's built-in CefSharp
    /// and crash the External Data Manager (Manage Links).
    /// </summary>
    public static void OpenInBrowser(UIApplication uiApp)
    {
        if (_apiServer == null)
        {
            _apiServer = new RevitApiServer(_port);
            _apiServer.Start(uiApp);
        }

        var url = $"http://localhost:{_port}/?source=revit&api=http%3A%2F%2Flocalhost%3A{_port}";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
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
