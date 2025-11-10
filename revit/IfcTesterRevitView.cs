using System;
using System.IO;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace IfcTesterRevit.Views;

public sealed class IfcTesterRevitView : UserControl
{
    private readonly string WebUrl = WebAppConfig.GetWebAppUrl();

    public IfcTesterRevitView()
    {
        var grid = new System.Windows.Controls.Grid();
        grid.Background = System.Windows.Media.Brushes.White;

        // Create WebView2 control
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
                    "IfcTesterRevit",
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
                            // This is needed for Pyodide to fetch wheel files from the virtual host
                            // We intercept requests for files that need CORS (wheel files, etc.)
                            // IMPORTANT: Add filter BEFORE setting up the handler
                            webView.CoreWebView2.AddWebResourceRequestedFilter("*app.localhost*", CoreWebView2WebResourceContext.All);
                            
                            webView.CoreWebView2.WebResourceRequested += (sender, args) =>
                            {
                                // Only handle app.localhost requests
                                if (args.Request != null && args.Request.Uri != null && args.Request.Uri.StartsWith("http://app.localhost"))
                                {
                                    try
                                    {
                                        var requestUri = args.Request.Uri;
                                        System.Diagnostics.Debug.WriteLine($"[CORS] Intercepted request: {requestUri}");
                                        
                                        var uri = new Uri(requestUri);
                                        // Decode the path to handle URL encoding (e.g., + becomes space, %2B becomes +)
                                        // But we need to be careful - + in filenames should stay as +
                                        var absolutePath = uri.AbsolutePath.TrimStart('/');
                                        var relativePath = absolutePath.Replace('/', Path.DirectorySeparatorChar);
                                        
                                        System.Diagnostics.Debug.WriteLine($"[CORS] Decoded relative path: {relativePath}");
                                        
                                        // Only intercept files that need CORS headers (wheel files, worker files)
                                        // Let other files use the virtual host mapping normally
                                        var needsCors = relativePath.EndsWith(".whl", StringComparison.OrdinalIgnoreCase) ||
                                                      relativePath.Contains("worker", StringComparison.OrdinalIgnoreCase) ||
                                                      relativePath.Contains("pyodide", StringComparison.OrdinalIgnoreCase);
                                        
                                        if (!needsCors)
                                        {
                                            // Let the request pass through to virtual host mapping
                                            System.Diagnostics.Debug.WriteLine($"[CORS] Passing through (no CORS needed): {relativePath}");
                                            return;
                                        }
                                        
                                        // Handle OPTIONS preflight requests
                                        if (args.Request.Method == "OPTIONS")
                                        {
                                            System.Diagnostics.Debug.WriteLine($"[CORS] Handling OPTIONS preflight for: {relativePath}");
                                            // Headers must be formatted as a single string with CRLF line endings
                                            var headers = "Access-Control-Allow-Origin: *\r\n" +
                                                         "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                                                         "Access-Control-Allow-Headers: Content-Type\r\n" +
                                                         "Access-Control-Max-Age: 86400\r\n";
                                            args.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(
                                                null, 200, "OK", headers
                                            );
                                            return;
                                        }
                                        
                                        // For GET requests, read the file and serve it with CORS headers
                                        var filePath = Path.Combine(webAppFolder, relativePath);
                                        
                                        // Normalize path separators
                                        filePath = Path.GetFullPath(filePath);
                                        
                                        System.Diagnostics.Debug.WriteLine($"[CORS] Looking for file: {filePath}");
                                        System.Diagnostics.Debug.WriteLine($"[CORS] Web app folder: {webAppFolder}");
                                        System.Diagnostics.Debug.WriteLine($"[CORS] File exists: {File.Exists(filePath)}");
                                        
                                        if (File.Exists(filePath))
                                        {
                                            System.Diagnostics.Debug.WriteLine($"[CORS] Serving file with CORS headers: {filePath}");
                                            // Read the file content as a stream
                                            var fileStream = File.OpenRead(filePath);
                                            var contentType = GetContentType(filePath);
                                            
                                            // Create response with CORS headers
                                            // Headers must be formatted as a single string with CRLF line endings
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
                                            // List available files for debugging
                                            var binDir = Path.GetDirectoryName(filePath);
                                            if (Directory.Exists(binDir))
                                            {
                                                var availableFiles = Directory.GetFiles(binDir, "*.whl");
                                                System.Diagnostics.Debug.WriteLine($"[CORS] Available .whl files in directory: {string.Join(", ", availableFiles.Select(f => Path.GetFileName(f)))}");
                                            }
                                            
                                            // Return 404 with CORS headers so the error is clear
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
                                        // Return error with CORS headers
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
                                            // If we can't create error response, let it pass through
                                        }
                                    }
                                }
                            };
                            
                            // Helper method to determine content type
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
                                        // Check if API URL is already set in URL params
                                        const urlParams = new URLSearchParams(window.location.search);
                                        if (!urlParams.get('api')) {{
                                            // Add API URL parameter pointing to localhost Revit API
                                            urlParams.set('api', '{apiUrl}');
                                            const newUrl = window.location.pathname + '?' + urlParams.toString();
                                            window.history.replaceState({{}}, '', newUrl);
                                            
                                            // Trigger a custom event to notify the app
                                            window.dispatchEvent(new CustomEvent('revitApiUrlSet', {{ 
                                                detail: {{ apiUrl: '{apiUrl}' }} 
                                            }}));
                                        }}
                                        
                                        // Also set a global variable for immediate access
                                        window.__REVIT_API_URL__ = '{apiUrl}';
                                        
                                        // If Revit module exists, update it
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
                    
                    // Navigate to the URL
                    // If using local files, add API parameter to URL
                    var apiUrl = WebAppConfig.GetApiUrl();
                    string urlToNavigate;
                    
                    if (WebUrl.StartsWith("http://app.localhost"))
                    {
                        // Local file serving - add API parameter
                        var separator = WebUrl.Contains("?") ? "&" : "?";
                        urlToNavigate = $"{WebUrl}{separator}api={Uri.EscapeDataString(apiUrl)}";
                    }
                    else
                    {
                        // Remote URL (dev server)
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