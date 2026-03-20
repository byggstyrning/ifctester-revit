using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace IfcTesterRevit.Views;

public sealed class IfcTesterRevitView : UserControl
{
    private readonly string WebUrl = WebAppConfig.GetWebAppUrl();
    private System.Windows.Controls.Grid _grid;
    private WebView2? _webView;
    private bool _initialized;

    public bool IsWebViewInitialized => _initialized;

    public void SuspendWebView()
    {
        if (_webView == null) return;
        _webView.Visibility = System.Windows.Visibility.Collapsed;
        try { _webView.CoreWebView2?.TrySuspendAsync(); } catch { }
    }

    public void ResumeWebView()
    {
        if (_webView == null) return;
        try { _webView.CoreWebView2?.Resume(); } catch { }
        _webView.Visibility = System.Windows.Visibility.Visible;
    }

    public IfcTesterRevitView()
    {
        _grid = new System.Windows.Controls.Grid();
        _grid.Background = System.Windows.Media.Brushes.White;

        var placeholder = new TextBlock
        {
            Text = "Click the IfcTester button to load the panel.",
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(20)
        };
        _grid.Children.Add(placeholder);

        Content = _grid;
    }

    /// <summary>
    /// Creates the WebView2 control on first use. Deferring initialization keeps
    /// WebView2's native DLLs out of the Revit process until they're actually
    /// needed, avoiding conflicts with add-ins like External Data Manager.
    /// </summary>
    public void InitializeWebView()
    {
        if (_initialized) return;
        _initialized = true;

        _grid.Children.Clear();

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
                    
                    var webAppFolder = WebAppConfig.GetWebAppFolder();
                    if (webAppFolder != null && Directory.Exists(webAppFolder))
                    {
                        try
                        {
                            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                                "app.localhost",
                                webAppFolder,
                                CoreWebView2HostResourceAccessKind.Allow
                            );
                            System.Diagnostics.Debug.WriteLine($"Mapped app.localhost to {webAppFolder}");
                            
                            webView.CoreWebView2.AddWebResourceRequestedFilter("*app.localhost*", CoreWebView2WebResourceContext.All);
                            
                            webView.CoreWebView2.WebResourceRequested += (sender, args) =>
                            {
                                if (args.Request != null && args.Request.Uri != null && args.Request.Uri.StartsWith("http://app.localhost"))
                                {
                                    try
                                    {
                                        var requestUri = args.Request.Uri;
                                        System.Diagnostics.Debug.WriteLine($"[CORS] Intercepted request: {requestUri}");
                                        
                                        var uri = new Uri(requestUri);
                                        var absolutePath = uri.AbsolutePath.TrimStart('/');
                                        var relativePath = absolutePath.Replace('/', Path.DirectorySeparatorChar);
                                        
                                        System.Diagnostics.Debug.WriteLine($"[CORS] Decoded relative path: {relativePath}");
                                        
                                        var needsCors = relativePath.EndsWith(".whl", StringComparison.OrdinalIgnoreCase) ||
                                                      relativePath.Contains("worker", StringComparison.OrdinalIgnoreCase) ||
                                                      relativePath.Contains("pyodide", StringComparison.OrdinalIgnoreCase);
                                        
                                        if (!needsCors)
                                        {
                                            System.Diagnostics.Debug.WriteLine($"[CORS] Passing through (no CORS needed): {relativePath}");
                                            return;
                                        }
                                        
                                        if (args.Request.Method == "OPTIONS")
                                        {
                                            System.Diagnostics.Debug.WriteLine($"[CORS] Handling OPTIONS preflight for: {relativePath}");
                                            var headers = "Access-Control-Allow-Origin: *\r\n" +
                                                         "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                                                         "Access-Control-Allow-Headers: Content-Type\r\n" +
                                                         "Access-Control-Max-Age: 86400\r\n";
                                            args.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(
                                                null, 200, "OK", headers
                                            );
                                            return;
                                        }
                                        
                                        var filePath = Path.Combine(webAppFolder, relativePath);
                                        filePath = Path.GetFullPath(filePath);
                                        
                                        System.Diagnostics.Debug.WriteLine($"[CORS] Looking for file: {filePath}");
                                        System.Diagnostics.Debug.WriteLine($"[CORS] File exists: {File.Exists(filePath)}");
                                        
                                        if (filePath == null || filePath.Contains("../") || filePath.Contains(@"..\"))
                                        {
                                            throw new ArgumentException("Invalid file path");
                                        }
                                        if (File.Exists(filePath))
                                        {
                                            System.Diagnostics.Debug.WriteLine($"[CORS] Serving file with CORS headers: {filePath}");
                                            var fileStream = File.OpenRead(filePath);
                                            var contentType = GetContentType(filePath);
                                            
                                            var responseHeaders = "Access-Control-Allow-Origin: *\r\n" +
                                                                 "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                                                                 "Access-Control-Allow-Headers: Content-Type\r\n" +
                                                                 $"Content-Type: {contentType}\r\n";
                                            
                                            args.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(
                                                fileStream, 200, "OK", responseHeaders
                                            );
                                        }
                                        else
                                        {
                                            System.Diagnostics.Debug.WriteLine($"[CORS] File not found: {filePath}");
                                            if (filePath == null || filePath.Contains("../") || filePath.Contains(@"..\"))
                                            {
                                                throw new ArgumentException("Invalid file path");
                                            }
                                            var binDir = Path.GetDirectoryName(filePath);
                                            if (Directory.Exists(binDir))
                                            {
                                                var availableFiles = Directory.GetFiles(binDir, "*.whl");
                                                System.Diagnostics.Debug.WriteLine($"[CORS] Available .whl files in directory: {string.Join(", ", availableFiles.Select(f => Path.GetFileName(f)))}");
                                            }
                                            
                                            var errorHeaders = "Access-Control-Allow-Origin: *\r\n" +
                                                             "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                                                             "Access-Control-Allow-Headers: Content-Type\r\n" +
                                                             "Content-Type: text/plain\r\n";
                                            var errorMessage = $"File not found: {relativePath}";
                                            var errorBytes = System.Text.Encoding.UTF8.GetBytes(errorMessage);
                                            args.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(
                                                new System.IO.MemoryStream(errorBytes), 404, "Not Found", errorHeaders
                                            );
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[CORS] Error handling request: {ex.Message}\n{ex.StackTrace}");
                                        try
                                        {
                                            var errorHeaders = "Access-Control-Allow-Origin: *\r\n" +
                                                             "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                                                             "Access-Control-Allow-Headers: Content-Type\r\n" +
                                                             "Content-Type: text/plain\r\n";
                                            var errorMessage = $"Error: {ex.Message}";
                                            var errorBytes = System.Text.Encoding.UTF8.GetBytes(errorMessage);
                                            args.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(
                                                new System.IO.MemoryStream(errorBytes), 500, "Internal Server Error", errorHeaders
                                            );
                                        }
                                        catch
                                        {
                                        }
                                    }
                                }
                            };
                            
                            string GetContentType(string filePath)
                            {
                                var extension = Path.GetExtension(filePath).ToLowerInvariant();
                                return extension switch
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
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to set virtual host mapping: {ex.Message}");
                        }
                    }
                    
                    webView.CoreWebView2.NavigationCompleted += async (sender, args) =>
                    {
                        if (args.IsSuccess)
                        {
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
                        }
                    };
                    
                    var apiUrl = WebAppConfig.GetApiUrl();
                    string urlToNavigate;
                    
                    if (WebUrl.StartsWith("http://app.localhost"))
                    {
                        var separator = WebUrl.Contains("?") ? "&" : "?";
                        urlToNavigate = $"{WebUrl}{separator}source=revit&api={Uri.EscapeDataString(apiUrl)}";
                    }
                    else
                    {
                        var separator = WebUrl.Contains("?") ? "&" : "?";
                        urlToNavigate = $"{WebUrl}{separator}source=revit&api={Uri.EscapeDataString(apiUrl)}";
                    }
                    
                    webView.CoreWebView2.Navigate(urlToNavigate);
                }
            }
            catch (Exception ex)
            {
                var errorText = new TextBlock
                {
                    Text = $"Error loading web page: {ex.Message}\n\nURL: {WebUrl}",
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(20)
                };
                _grid.Children.Clear();
                _grid.Children.Add(errorText);
            }
        };

        _grid.Children.Add(webView);
    }
}
