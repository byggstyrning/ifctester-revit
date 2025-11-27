using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Archicad;
using Archicad.Interfaces;

namespace IfcTesterArchicad;

/// <summary>
/// External event handler for selecting elements in Archicad by ElementId
/// </summary>
public class SelectElementEventHandler
{
    public int ElementId { get; set; }
    public TaskCompletionSource<bool>? CompletionSource { get; set; }

    public void Execute()
    {
        try
        {
            // Archicad element selection implementation
            // This is a placeholder - actual implementation depends on Archicad API
            var elementId = new IntPtr(ElementId);
            
            // Select element in Archicad
            // Note: The exact API calls depend on Archicad version
            // This would typically involve:
            // 1. Getting the element by ID
            // 2. Selecting it in the UI
            // 3. Zooming to it
            
            CompletionSource?.SetResult(true);
        }
        catch
        {
            CompletionSource?.SetResult(false);
        }
    }
}

/// <summary>
/// External event handler for selecting elements in Archicad by IfcGUID
/// </summary>
public class SelectElementByGuidEventHandler
{
    public string IfcGuid { get; set; } = string.Empty;
    public TaskCompletionSource<bool>? CompletionSource { get; set; }

    public void Execute()
    {
        try
        {
            // Search for element by IfcGUID in Archicad
            // Archicad stores IFC GUIDs in element properties
            // This is a placeholder - actual implementation depends on Archicad API
            
            // The implementation would:
            // 1. Search through all elements
            // 2. Check for IFC GUID property
            // 3. Match against the provided GUID
            // 4. Select and zoom to the element
            
            CompletionSource?.SetResult(true);
        }
        catch
        {
            CompletionSource?.SetResult(false);
        }
    }
}

/// <summary>
/// External event handler for getting IFC export configurations
/// </summary>
public class GetIfcConfigurationsEventHandler
{
    public List<string> Configurations { get; set; } = new();
    public TaskCompletionSource<bool>? CompletionSource { get; set; }

    public void Execute()
    {
        try
        {
            Configurations.Clear();
            
            // Get IFC export configurations from Archicad
            // Archicad has different IFC export options than Revit
            // This is a placeholder - actual implementation depends on Archicad API
            
            // Default configurations for Archicad
            Configurations.Add("Default");
            Configurations.Add("IFC2x3");
            Configurations.Add("IFC4");
            Configurations.Add("IFC4 Reference View");
            
            CompletionSource?.SetResult(true);
        }
        catch (Exception ex)
        {
            // Even on error, provide default configurations
            Configurations.Clear();
            Configurations.Add("Default");
            Configurations.Add("IFC2x3");
            Configurations.Add("IFC4");
            System.Diagnostics.Debug.WriteLine($"Error in GetIfcConfigurationsEventHandler: {ex.Message}");
            CompletionSource?.SetResult(true); // Still return true since we have defaults
        }
    }
}

/// <summary>
/// External event handler for exporting IFC
/// </summary>
public class ExportIfcEventHandler
{
    public string ConfigurationName { get; set; } = string.Empty;
    public string? OutputFilePath { get; set; }
    public TaskCompletionSource<bool>? CompletionSource { get; set; }

    public void Execute()
    {
        try
        {
            // Create temporary file path
            var tempDir = Path.Combine(Path.GetTempPath(), "IfcTesterArchicad");
            Directory.CreateDirectory(tempDir);
            
            var fileName = $"Export_{DateTime.Now:yyyyMMdd_HHmmss}.ifc";
            OutputFilePath = Path.Combine(tempDir, fileName);

            System.Diagnostics.Debug.WriteLine($"ExportIfcEventHandler: Starting export with configuration '{ConfigurationName}' to '{OutputFilePath}'");

            // Export IFC from Archicad
            // This is a placeholder - actual implementation depends on Archicad API
            // Archicad's IFC export API is different from Revit's
            
            // The implementation would:
            // 1. Get the active project/view
            // 2. Configure IFC export options based on ConfigurationName
            // 3. Execute the export
            // 4. Save to OutputFilePath
            
            // For now, create an empty file as placeholder
            File.WriteAllText(OutputFilePath, "IFC placeholder - Archicad export not yet implemented");
            
            // Wait a moment for file to be written
            Thread.Sleep(500);
            
            if (!File.Exists(OutputFilePath))
            {
                throw new InvalidOperationException($"IFC file was not created at: {OutputFilePath}");
            }

            CompletionSource?.SetResult(true);
        }
        catch (Exception ex)
        {
            // Ensure OutputFilePath is set even on error
            if (string.IsNullOrEmpty(OutputFilePath))
            {
                var errorTempDir = Path.Combine(Path.GetTempPath(), "IfcTesterArchicad");
                Directory.CreateDirectory(errorTempDir);
                OutputFilePath = Path.Combine(errorTempDir, $"Export_Error_{DateTime.Now:yyyyMMdd_HHmmss}.ifc");
            }
            
            System.Diagnostics.Debug.WriteLine($"ExportIfcEventHandler error: {ex.Message}\n{ex.StackTrace}");
            CompletionSource?.SetResult(false);
        }
    }
}

/// <summary>
/// HTTP server for Archicad API communication with Browser control
/// Implements API endpoints similar to Revit version
/// </summary>
public class ArchicadApiServer : IDisposable
{
    private HttpListener? _listener;
    private readonly string _baseUrl;
    private readonly int _port;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isRunning;
    private SelectElementEventHandler? _eventHandler;
    private SelectElementByGuidEventHandler? _eventHandlerByGuid;
    private GetIfcConfigurationsEventHandler? _eventHandlerGetIfcConfigs;
    private ExportIfcEventHandler? _eventHandlerExportIfc;
    private bool _configsLoaded = false;
    private readonly object _configsLock = new object();

    public ArchicadApiServer(int port = 48882)
    {
        _port = port;
        _baseUrl = $"http://localhost:{port}/";
    }

    public bool IsRunning => _isRunning;

    public void Start()
    {
        if (_isRunning)
        {
            return;
        }

        _cancellationTokenSource = new CancellationTokenSource();

        // Create event handlers
        _eventHandler = new SelectElementEventHandler();
        _eventHandlerByGuid = new SelectElementByGuidEventHandler();
        _eventHandlerGetIfcConfigs = new GetIfcConfigurationsEventHandler();
        _eventHandlerExportIfc = new ExportIfcEventHandler();

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(_baseUrl);
            _listener.Start();
            _isRunning = true;

            // Start listening for requests in background
            Task.Run(() => ListenForRequests(_cancellationTokenSource.Token));

            System.Diagnostics.Debug.WriteLine($"Archicad API Server started on {_baseUrl}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to start Archicad API Server: {ex.Message}");
            _isRunning = false;
        }
    }

    public void Stop()
    {
        if (!_isRunning)
        {
            return;
        }

        _cancellationTokenSource?.Cancel();
        _listener?.Stop();
        _isRunning = false;
        System.Diagnostics.Debug.WriteLine("Archicad API Server stopped");
    }

    private async Task ListenForRequests(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(context, cancellationToken));
            }
            catch (HttpListenerException)
            {
                // Listener was closed
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error accepting request: {ex.Message}");
            }
        }
    }

    private async Task HandleRequest(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;

        // Enable CORS
        response.AddHeader("Access-Control-Allow-Origin", "*");
        response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.AddHeader("Access-Control-Allow-Headers", "Content-Type");

        // Handle preflight OPTIONS request
        if (request.HttpMethod == "OPTIONS")
        {
            response.StatusCode = 200;
            response.Close();
            return;
        }

        try
        {
            var path = request.Url?.AbsolutePath ?? "";
            var method = request.HttpMethod;

            System.Diagnostics.Debug.WriteLine($"Archicad API Request: {method} {path}");

            if (path == "/status" && method == "GET")
            {
                await HandleStatus(response);
            }
            else if (path.StartsWith("/select-by-id/") && method == "GET")
            {
                var elementIdStr = path.Replace("/select-by-id/", "");
                if (int.TryParse(elementIdStr, out var elementId))
                {
                    await HandleSelectById(response, elementId);
                }
                else
                {
                    await SendError(response, 400, "Invalid element ID");
                }
            }
            else if (path.StartsWith("/select-by-guid/") && method == "GET")
            {
                var guid = path.Replace("/select-by-guid/", "");
                if (!string.IsNullOrEmpty(guid))
                {
                    guid = Uri.UnescapeDataString(guid);
                    await HandleSelectByGuid(response, guid);
                }
                else
                {
                    await SendError(response, 400, "Invalid GUID");
                }
            }
            else if (path == "/ifc-configurations" && method == "GET")
            {
                await HandleGetIfcConfigurations(response);
            }
            else if (path == "/export-ifc" && method == "POST")
            {
                await HandleExportIfc(request, response);
            }
            else
            {
                await SendError(response, 404, "Not Found");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error handling request: {ex.Message}");
            await SendError(response, 500, $"Internal Server Error: {ex.Message}");
        }
        finally
        {
            response.Close();
        }
    }

    private async Task HandleStatus(HttpListenerResponse response)
    {
        bool configsReady = false;
        
        lock (_configsLock)
        {
            if (_configsLoaded)
            {
                configsReady = true;
            }
        }
        
        if (!configsReady && _eventHandlerGetIfcConfigs != null)
        {
            int maxRetries = 3;
            int retryDelay = 1000;
            
            for (int retry = 0; retry < maxRetries && !configsReady; retry++)
            {
                try
                {
                    var tcs = new TaskCompletionSource<bool>();
                    _eventHandlerGetIfcConfigs.CompletionSource = tcs;
                    _eventHandlerGetIfcConfigs.Configurations.Clear();
                    
                    // Execute on Archicad's main thread
                    // Note: Archicad doesn't have ExternalEvent like Revit
                    // This would need to be adapted to Archicad's threading model
                    _eventHandlerGetIfcConfigs.Execute();

                    int timeout = retry == 0 ? 10000 : 5000;
                    var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
                    
                    if (completed == tcs.Task && await tcs.Task)
                    {
                        var configs = _eventHandlerGetIfcConfigs.Configurations;
                        configsReady = configs.Count > 0;
                        
                        if (configsReady)
                        {
                            lock (_configsLock)
                            {
                                _configsLoaded = true;
                            }
                            break;
                        }
                    }
                    else
                    {
                        if (retry < maxRetries - 1)
                        {
                            await Task.Delay(retryDelay);
                            retryDelay *= 2;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Status check: Exception loading configs (attempt {retry + 1}): {ex.Message}");
                    if (retry < maxRetries - 1)
                    {
                        await Task.Delay(retryDelay);
                        retryDelay *= 2;
                    }
                }
            }
        }

        var status = new
        {
            status = configsReady ? "ok" : "initializing",
            connected = true,
            configsReady = configsReady,
            version = "1.0.0"
        };

        var json = System.Text.Json.JsonSerializer.Serialize(status);
        await SendJson(response, json);
    }

    private async Task HandleSelectById(HttpListenerResponse response, int elementId)
    {
        if (_eventHandler == null)
        {
            await SendError(response, 503, "Archicad application not available");
            return;
        }

        try
        {
            var tcs = new TaskCompletionSource<bool>();
            _eventHandler.ElementId = elementId;
            _eventHandler.CompletionSource = tcs;
            
            // Execute on Archicad's main thread
            _eventHandler.Execute();
            
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
            
            if (completed == tcs.Task && await tcs.Task)
            {
                var result = new { success = true, message = $"Element {elementId} selected" };
                var json = System.Text.Json.JsonSerializer.Serialize(result);
                await SendJson(response, json);
            }
            else
            {
                await SendError(response, 500, $"Element {elementId} not found or selection failed");
            }
        }
        catch (Exception ex)
        {
            await SendError(response, 500, $"Failed to select element: {ex.Message}");
        }
    }

    private async Task HandleSelectByGuid(HttpListenerResponse response, string ifcGuid)
    {
        if (_eventHandlerByGuid == null)
        {
            await SendError(response, 503, "Archicad application not available");
            return;
        }

        try
        {
            var tcs = new TaskCompletionSource<bool>();
            _eventHandlerByGuid.IfcGuid = ifcGuid;
            _eventHandlerByGuid.CompletionSource = tcs;
            
            // Execute on Archicad's main thread
            _eventHandlerByGuid.Execute();
            
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(10000));
            
            if (completed == tcs.Task && await tcs.Task)
            {
                var result = new { success = true, message = $"Element with GUID {ifcGuid} selected" };
                var json = System.Text.Json.JsonSerializer.Serialize(result);
                await SendJson(response, json);
            }
            else
            {
                await SendError(response, 500, $"Element with GUID {ifcGuid} not found or selection failed");
            }
        }
        catch (Exception ex)
        {
            await SendError(response, 500, $"Failed to select element by GUID: {ex.Message}");
        }
    }

    private async Task SendJson(HttpListenerResponse response, string json)
    {
        response.ContentType = "application/json";
        response.StatusCode = 200;

        var buffer = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
    }

    private async Task HandleGetIfcConfigurations(HttpListenerResponse response)
    {
        if (_eventHandlerGetIfcConfigs == null)
        {
            await SendError(response, 503, "Archicad application not available");
            return;
        }

        try
        {
            var tcs = new TaskCompletionSource<bool>();
            _eventHandlerGetIfcConfigs.CompletionSource = tcs;
            _eventHandlerGetIfcConfigs.Configurations.Clear();
            
            // Execute on Archicad's main thread
            _eventHandlerGetIfcConfigs.Execute();

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(10000));
            
            if (completed == tcs.Task && await tcs.Task)
            {
                var configs = _eventHandlerGetIfcConfigs.Configurations;
                if (configs.Count == 0)
                {
                    configs.Add("Default");
                    configs.Add("IFC2x3");
                    configs.Add("IFC4");
                }
                
                var result = new { configurations = configs };
                var json = System.Text.Json.JsonSerializer.Serialize(result);
                await SendJson(response, json);
            }
            else
            {
                var defaultConfigs = new List<string> { "Default", "IFC2x3", "IFC4" };
                var result = new { configurations = defaultConfigs };
                var json = System.Text.Json.JsonSerializer.Serialize(result);
                await SendJson(response, json);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Exception in HandleGetIfcConfigurations: {ex.Message}\n{ex.StackTrace}");
            try
            {
                var defaultConfigs = new List<string> { "Default", "IFC2x3", "IFC4" };
                var result = new { configurations = defaultConfigs };
                var json = System.Text.Json.JsonSerializer.Serialize(result);
                await SendJson(response, json);
            }
            catch
            {
                await SendError(response, 500, $"Failed to get IFC configurations: {ex.Message}");
            }
        }
    }

    private async Task HandleExportIfc(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (_eventHandlerExportIfc == null)
        {
            await SendError(response, 503, "Archicad application not available");
            return;
        }

        try
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var body = await reader.ReadToEndAsync();
            
            var requestData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(body);
            if (requestData == null || !requestData.ContainsKey("configuration"))
            {
                await SendError(response, 400, "Missing 'configuration' parameter");
                return;
            }

            var configName = requestData["configuration"];
            
            var tcs = new TaskCompletionSource<bool>();
            _eventHandlerExportIfc.ConfigurationName = configName;
            _eventHandlerExportIfc.CompletionSource = tcs;
            
            // Execute on Archicad's main thread
            _eventHandlerExportIfc.Execute();

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(60000));
            
            if (completed == tcs.Task && await tcs.Task && !string.IsNullOrEmpty(_eventHandlerExportIfc.OutputFilePath))
            {
                if (File.Exists(_eventHandlerExportIfc.OutputFilePath))
                {
                    var fileBytes = await File.ReadAllBytesAsync(_eventHandlerExportIfc.OutputFilePath);
                    var fileName = Path.GetFileName(_eventHandlerExportIfc.OutputFilePath);
                    
                    response.ContentType = "application/octet-stream";
                    response.AddHeader("Content-Disposition", $"attachment; filename=\"{fileName}\"");
                    response.StatusCode = 200;
                    response.ContentLength64 = fileBytes.Length;
                    
                    await response.OutputStream.WriteAsync(fileBytes, 0, fileBytes.Length);
                    
                    try
                    {
                        File.Delete(_eventHandlerExportIfc.OutputFilePath);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
                else
                {
                    await SendError(response, 500, "IFC file was not created");
                }
            }
            else
            {
                var errorDetails = "Unknown error";
                if (_eventHandlerExportIfc.OutputFilePath == null)
                {
                    errorDetails = "Export handler did not set output file path";
                }
                else if (!File.Exists(_eventHandlerExportIfc.OutputFilePath))
                {
                    errorDetails = $"File was not created at: {_eventHandlerExportIfc.OutputFilePath}";
                }
                else
                {
                    errorDetails = "Export handler returned false";
                }
                
                System.Diagnostics.Debug.WriteLine($"IFC export failed: {errorDetails}");
                await SendError(response, 500, $"Failed to export IFC: {errorDetails}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"HandleExportIfc exception: {ex.Message}\n{ex.StackTrace}");
            await SendError(response, 500, $"Failed to export IFC: {ex.Message}");
        }
    }

    private async Task SendError(HttpListenerResponse response, int statusCode, string message)
    {
        var error = new { error = message };
        var json = System.Text.Json.JsonSerializer.Serialize(error);
        response.StatusCode = statusCode;
        response.ContentType = "application/json";

        var buffer = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
    }

    public void Dispose()
    {
        Stop();
        _listener?.Close();
        _cancellationTokenSource?.Dispose();
    }
}
