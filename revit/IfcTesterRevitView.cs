using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace IfcTesterRevit.Views;

/// <summary>
/// Dockable pane host. Contains NO WebView2 type references so that the CLR
/// does not probe or load WebView2 assemblies when the class is instantiated
/// at Revit startup. All browser work is delegated to <see cref="WebViewHost"/>
/// which is only touched after the user explicitly opens the panel.
/// Supports full create/destroy cycles to keep WebView2 out of Revit when the
/// panel is hidden.
/// </summary>
public sealed class IfcTesterRevitView : UserControl
{
    private readonly System.Windows.Controls.Grid _grid;
    private bool _initialized;

    public bool IsWebViewInitialized => _initialized;

    public IfcTesterRevitView()
    {
        _grid = new System.Windows.Controls.Grid();
        _grid.Background = System.Windows.Media.Brushes.White;
        ShowPlaceholder();
        Content = _grid;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void InitializeWebView()
    {
        if (_initialized) return;
        _initialized = true;
        _grid.Children.Clear();
        WebViewHost.CreateAndAttach(_grid);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void DestroyWebView()
    {
        if (!_initialized) return;
        _initialized = false;
        WebViewHost.DestroyAndDetach();
        _grid.Children.Clear();
        ShowPlaceholder();
    }

    public void SuspendWebView()
    {
        if (!_initialized) return;
        SuspendCore();
    }

    public void ResumeWebView()
    {
        if (!_initialized) return;
        ResumeCore();
    }

    private void ShowPlaceholder()
    {
        _grid.Children.Add(new TextBlock
        {
            Text = "Click the IfcTester button to load the panel.",
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(20)
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void SuspendCore() => WebViewHost.Suspend();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ResumeCore() => WebViewHost.Resume();
}
