using System.Linq;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
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
    private static IfcTesterRevitView? _dockableView;
    private bool _webViewSuspended;

    public override void OnStartup()
    {
        CreateRibbon();
        CreateDockablePane();

        Context.UiControlledApplication.DialogBoxShowing += OnDialogBoxShowing;
        Context.UiControlledApplication.Idling += OnIdling;
    }

    public override void OnShutdown()
    {
        Context.UiControlledApplication.DialogBoxShowing -= OnDialogBoxShowing;
        Context.UiControlledApplication.Idling -= OnIdling;

        _apiServer?.Stop();
        _apiServer?.Dispose();
    }

    /// <summary>
    /// Called from StartupCommand when the user first opens the panel.
    /// Starts the HTTP server and initializes WebView2 on demand so that
    /// WebView2 native DLLs are not loaded into the process until needed.
    /// </summary>
    public static void EnsureWebViewAndServer(UIApplication uiApp)
    {
        if (_dockableView != null && !_dockableView.IsWebViewInitialized)
        {
            _dockableView.InitializeWebView();
        }

        if (_apiServer == null)
        {
            _apiServer = new RevitApiServer(48881);
            _apiServer.Start(uiApp);
        }
    }

    private void OnDialogBoxShowing(object? sender, DialogBoxShowingEventArgs e)
    {
        _dockableView?.SuspendWebView();
        _webViewSuspended = true;
    }

    private void OnIdling(object? sender, IdlingEventArgs e)
    {
        if (_webViewSuspended)
        {
            _dockableView?.ResumeWebView();
            _webViewSuspended = false;
        }
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

    private void CreateDockablePane()
    {
        if (!DockablePane.PaneIsRegistered(new DockablePaneId(DockablePaneId)))
        {
            _dockableView = new IfcTesterRevitView();
            DockablePaneProvider
                .Register(Context.UiControlledApplication, DockablePaneId, "IfcTester")
                .SetConfiguration((data) =>
                {
                    data.FrameworkElement = _dockableView;
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
