using System;
using System.IO;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace IfcTesterArchicad.Views;

/// <summary>
/// Archicad view component that hosts the WebView2 browser control
/// Similar to Revit version but adapted for Archicad's browser control system
/// </summary>
public sealed class IfcTesterArchicadView : UserControl
{
    private readonly string WebUrl = WebAppConfig.GetWebAppUrl();

    public IfcTesterArchicadView()
    {
        var grid = new System.Windows.Controls.Grid();
        grid.Background = System.Windows.Media.Brushes.White;

        // Create WebView2 control
        // Note: Archicad also supports CEF (Chromium Embedded Framework) browser control
        // This implementation uses WebView2 for consistency with Revit version
        var webView = new WebView2
        {
            VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
        };

        // Navigate to the URL when the control is loaded
        webView.Loaded += async (sender, e) =>
        {
            try
            {
                // Create user data folder path in AppData
                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IfcTesterArchicad",
                    "WebView2"
                );

                // Ensure directory exists
                Directory.CreateDirectory(userDataFolder);

                // Create environment options
                var environmentOptions = new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments = "--disable-web-security --allow-running-insecure-content"
                };

                // Create environment with user data folder
                var environment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: userDataFolder,
                    options: environmentOptions
                );

                // Set the environment before ensuring CoreWebView2
                await webView.EnsureCoreWebView2Async(environment);

                // Configure settings to allow local network access
                if (webView.CoreWebView2 != null)
                {
                    webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
                    webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
                    webView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = true;
                    
                    // Set up virtual host mapping for local file serving
                    var webAppFolder = WebAppConfig.GetWebAppFolder();
                    if (webAppFolder != null && Directory.Exists(webAppFolder))
                    {
                        try
                        {
                            // Map virtual host to local folder for serving built web app
                            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                                "app.localhost",
                                webAppFolder,
                                CoreWebView2HostResourceAccessKind.Allow
                            );
                            System.Diagnostics.Debug.WriteLine($"Mapped app.localhost to {webAppFolder}");
                            
                            // Add CORS headers to responses from app.localhost
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
                                        
                                        var needsCors = relativePath.EndsWith(".whl", StringComparison.OrdinalIgnoreCase) ||
                                                      relativePath.Contains("worker", StringComparison.OrdinalIgnoreCase) ||
                                                      relativePath.Contains("pyodide", StringComparison.OrdinalIgnoreCase);
                                        
                                        if (!needsCors)
                                        {
                                            return;
                                        }
                                        
                                        if (args.Request.Method == "OPTIONS")
                                        {
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
                                        
                                        if (File.Exists(filePath))
                                        {
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
                    
                    // Set up navigation completed handler to inject API URL
                    webView.CoreWebView2.NavigationCompleted += async (sender, args) =>
                    {
                        if (args.IsSuccess)
                        {
                            try
                            {
                                // Inject JavaScript to set the API URL parameter
                                var apiUrl = WebAppConfig.GetApiUrl();
                                var script = $@"
                                    (function() {{
                                        const urlParams = new URLSearchParams(window.location.search);
                                        if (!urlParams.get('api')) {{
                                            urlParams.set('api', '{apiUrl}');
                                            const newUrl = window.location.pathname + '?' + urlParams.toString();
                                            window.history.replaceState({{}}, '', newUrl);
                                            
                                            window.dispatchEvent(new CustomEvent('archicadApiUrlSet', {{ 
                                                detail: {{ apiUrl: '{apiUrl}' }} 
                                            }}));
                                        }}
                                        
                                        window.__ARCHICAD_API_URL__ = '{apiUrl}';
                                        
                                        if (window.Archicad) {{
                                            window.Archicad.apiUrl = '{apiUrl}';
                                            window.Archicad.enabled = true;
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
                    
                    // Navigate to the URL
                    var apiUrl = WebAppConfig.GetApiUrl();
                    string urlToNavigate;
                    
                    if (WebUrl.StartsWith("http://app.localhost"))
                    {
                        var separator = WebUrl.Contains("?") ? "&" : "?";
                        urlToNavigate = $"{WebUrl}{separator}api={Uri.EscapeDataString(apiUrl)}";
                    }
                    else
                    {
                        var separator = WebUrl.Contains("?") ? "&" : "?";
                        urlToNavigate = $"{WebUrl}{separator}api={Uri.EscapeDataString(apiUrl)}";
                    }
                    
                    webView.CoreWebView2.Navigate(urlToNavigate);
                }
            }
            catch (System.Exception ex)
            {
                // If WebView2 fails, show error message
                var errorText = new TextBlock
                {
                    Text = $"Error loading web page: {ex.Message}\n\nURL: {WebUrl}",
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    TextWrapping = System.Windows.TextWrapping.Wrap,
                    Margin = new System.Windows.Thickness(20)
                };
                grid.Children.Clear();
                grid.Children.Add(errorText);
            }
        };

        Content = grid;
        grid.Children.Add(webView);
    }
}
