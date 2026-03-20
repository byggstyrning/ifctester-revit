using System;
using System.Linq;
using System.Windows.Interop;
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
    private int _modalLevel;

    public override void OnStartup()
    {
        CreateRibbon();
        CreateDockablePane();
        
        _apiServer = new RevitApiServer(48881);
        _apiServer.Start(Context.UiApplication);

        Context.UiControlledApplication.DialogBoxShowing += OnDialogBoxShowing;
        Context.UiControlledApplication.Idling += OnIdling;

        // ComponentDispatcher catches ANY modal loop (TaskDialogs, Win32 dialogs, 
        // WPF windows, and custom windows from overridden commands like Manage Links)
        // that may trigger problematic focus/layout events in WebView2.
        ComponentDispatcher.EnterThreadModal += OnEnterThreadModal;
        ComponentDispatcher.LeaveThreadModal += OnLeaveThreadModal;
    }

    public override void OnShutdown()
    {
        Context.UiControlledApplication.DialogBoxShowing -= OnDialogBoxShowing;
        Context.UiControlledApplication.Idling -= OnIdling;
        
        ComponentDispatcher.EnterThreadModal -= OnEnterThreadModal;
        ComponentDispatcher.LeaveThreadModal -= OnLeaveThreadModal;

        _apiServer?.Stop();
        _apiServer?.Dispose();
    }

    private void OnEnterThreadModal(object? sender, EventArgs e)
    {
        _modalLevel++;
        if (!_webViewSuspended)
        {
            _dockableView?.SuspendWebView();
            _webViewSuspended = true;
        }
    }

    private void OnLeaveThreadModal(object? sender, EventArgs e)
    {
        _modalLevel = Math.Max(0, _modalLevel - 1);
        // We restore on next Idling to ensure Revit is truly ready
    }

    private void OnDialogBoxShowing(object? sender, DialogBoxShowingEventArgs e)
    {
        // Safety net for standard Revit dialogs
        if (!_webViewSuspended)
        {
            _dockableView?.SuspendWebView();
            _webViewSuspended = true;
        }
    }

    private void OnIdling(object? sender, IdlingEventArgs e)
    {
        if (_webViewSuspended && _modalLevel == 0)
        {
            _dockableView?.ResumeWebView();
            _webViewSuspended = false;
        }
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