using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace IfcTesterRevit.Views;

/// <summary>
/// Isolates all WebView2 types into a single class so that the CLR only loads
/// WebView2 assemblies when this class is first referenced at runtime, which
/// happens only when the user explicitly opens the IfcTester panel.
/// </summary>
internal static class WebViewHost
{
    private static WebView2? _webView;

    public static void CreateAndAttach(System.Windows.Controls.Grid grid)
    {
        var webUrl = WebAppConfig.GetWebAppUrl();

        var webView = new WebView2
        {
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _webView = webView;

        webView.Loaded += async (sender, e) =>
        {
            try
            {
                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IfcTesterRevit",
                    "WebView2"
                );

                Directory.CreateDirectory(userDataFolder);

                var environmentOptions = new CoreWebView2EnvironmentOptions();

                var environment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: userDataFolder,
                    options: environmentOptions
                );

                await webView.EnsureCoreWebView2Async(environment);

                if (webView.CoreWebView2 != null)
                {
                    webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
                    webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
                    webView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = true;

                    ConfigureVirtualHost(webView);
                    ConfigureNavigation(webView, webUrl);

                    var apiUrl = WebAppConfig.GetApiUrl();
                    var separator = webUrl.Contains("?") ? "&" : "?";
                    var urlToNavigate = $"{webUrl}{separator}source=revit&api={Uri.EscapeDataString(apiUrl)}";
                    webView.CoreWebView2.Navigate(urlToNavigate);
                }
            }
            catch (Exception ex)
            {
                var errorText = new TextBlock
                {
                    Text = $"Error loading web page: {ex.Message}\n\nURL: {webUrl}",
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(20)
                };
                grid.Children.Clear();
                grid.Children.Add(errorText);
            }
        };

        grid.Children.Add(webView);
    }

    public static void Suspend()
    {
        if (_webView == null) return;
        _webView.Visibility = System.Windows.Visibility.Collapsed;
        try { _webView.CoreWebView2?.TrySuspendAsync(); } catch { }
    }

    public static void Resume()
    {
        if (_webView == null) return;
        try { _webView.CoreWebView2?.Resume(); } catch { }
        _webView.Visibility = System.Windows.Visibility.Visible;
    }

    private static void ConfigureNavigation(WebView2 webView, string webUrl)
    {
        webView.CoreWebView2.NavigationCompleted += async (sender, args) =>
        {
            if (!args.IsSuccess) return;
            try
            {
                var apiUrl = WebAppConfig.GetApiUrl();
                var script = $@"
                    (function() {{
                        const urlParams = new URLSearchParams(window.location.search);
                        if (!urlParams.get('api')) {{
                            urlParams.set('api', '{apiUrl}');
                            const newUrl = window.location.pathname + '?' + urlParams.toString();
                            window.history.replaceState({{}}, '', newUrl);
                            window.dispatchEvent(new CustomEvent('revitApiUrlSet', {{
                                detail: {{ apiUrl: '{apiUrl}' }}
                            }}));
                        }}
                        window.__REVIT_API_URL__ = '{apiUrl}';
                        if (window.Revit) {{
                            window.Revit.apiUrl = '{apiUrl}';
                            window.Revit.enabled = true;
                        }}
                    }})();
                ";
                await webView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error injecting API URL script: {ex.Message}");
            }
        };
    }

    private static void ConfigureVirtualHost(WebView2 webView)
    {
        var webAppFolder = WebAppConfig.GetWebAppFolder();
        if (webAppFolder == null || !Directory.Exists(webAppFolder)) return;

        try
        {
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "app.localhost", webAppFolder, CoreWebView2HostResourceAccessKind.Allow);

            webView.CoreWebView2.AddWebResourceRequestedFilter(
                "*app.localhost*", CoreWebView2WebResourceContext.All);

            webView.CoreWebView2.WebResourceRequested += (sender, args) =>
            {
                if (args.Request?.Uri == null || !args.Request.Uri.StartsWith("http://app.localhost"))
                    return;

                try
                {
                    var uri = new Uri(args.Request.Uri);
                    var relativePath = uri.AbsolutePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);

                    var needsCors = relativePath.EndsWith(".whl", StringComparison.OrdinalIgnoreCase) ||
                                  relativePath.Contains("worker", StringComparison.OrdinalIgnoreCase) ||
                                  relativePath.Contains("pyodide", StringComparison.OrdinalIgnoreCase);

                    if (!needsCors) return;

                    if (args.Request.Method == "OPTIONS")
                    {
                        args.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(
                            null, 200, "OK", CorsHeaders());
                        return;
                    }

                    var filePath = Path.GetFullPath(Path.Combine(webAppFolder, relativePath));
                    if (filePath.Contains("../") || filePath.Contains(@"..\"))
                        throw new ArgumentException("Invalid file path");

                    if (File.Exists(filePath))
                    {
                        var contentType = GetContentType(filePath);
                        args.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(
                            File.OpenRead(filePath), 200, "OK",
                            CorsHeaders() + $"Content-Type: {contentType}\r\n");
                    }
                    else
                    {
                        var errorBytes = System.Text.Encoding.UTF8.GetBytes($"File not found: {relativePath}");
                        args.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(
                            new MemoryStream(errorBytes), 404, "Not Found",
                            CorsHeaders() + "Content-Type: text/plain\r\n");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CORS] Error: {ex.Message}");
                    try
                    {
                        var errorBytes = System.Text.Encoding.UTF8.GetBytes($"Error: {ex.Message}");
                        args.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(
                            new MemoryStream(errorBytes), 500, "Internal Server Error",
                            CorsHeaders() + "Content-Type: text/plain\r\n");
                    }
                    catch { }
                }
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to set virtual host mapping: {ex.Message}");
        }
    }

    private static string CorsHeaders() =>
        "Access-Control-Allow-Origin: *\r\n" +
        "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
        "Access-Control-Allow-Headers: Content-Type\r\n";

    private static string GetContentType(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".whl" => "application/zip",
            ".js" => "application/javascript",
            ".mjs" => "application/javascript",
            ".wasm" => "application/wasm",
            ".json" => "application/json",
            ".html" => "text/html",
            ".css" => "text/css",
            ".py" => "text/x-python",
            _ => "application/octet-stream"
        };
}
