using System.IO;
using System.Text.Json;

namespace IfcTesterRevit.Writeback;

/// <summary>An HTTP status and a JSON body for RevitApiServer to send.</summary>
public sealed class WritebackHttpResult
{
    public WritebackHttpResult(int statusCode, string json)
    {
        StatusCode = statusCode;
        Json = json;
    }

    public int StatusCode { get; }
    public string Json { get; }

    public static WritebackHttpResult Ok<T>(T body) => new(200, WritebackJson.Serialize(body));

    public static WritebackHttpResult Error(int statusCode, string message) => new(statusCode, JsonSerializer.Serialize(new { error = message }));
}

/// <summary>
/// POST /resolve-parameters, POST /apply-changes and the pset builder's endpoints: JSON in, a job
/// on the Revit thread (except saving a file), JSON out.
/// </summary>
public sealed class WritebackEndpoints
{
    // How long a request waits for Revit to become free before it gives up.
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

    private readonly RevitWorkQueue _queue;
    private readonly Func<ExportRequestSettings?> _lastExport;

    /// <param name="queue">Runs the work on the Revit thread.</param>
    /// <param name="lastExport">The IFC export setup and file overrides of the last export the server ran, if any.</param>
    public WritebackEndpoints(RevitWorkQueue queue, Func<ExportRequestSettings?> lastExport)
    {
        _queue = queue;
        _lastExport = lastExport;
    }

    public Task<WritebackHttpResult> ResolveParameters(string body)
    {
        return Handle<ResolveRequest, ResolveResponse>(body, r => r.Items != null, "items", (app, request) =>
        {
            var document = app.ActiveUIDocument?.Document;
            var settings = ExportRequestSettings.ForResolve(request, _lastExport());
            var mapping = document == null
                ? ExportMapping.None
                : ExportMapping.Load(document, settings.Configuration, settings.PsetFile, settings.ParameterMappingFile);
            return WritebackService.Resolve(document, request, mapping);
        });
    }

    // Apply writes the parameters resolve named, so it reads no mapping files and needs no overrides
    public Task<WritebackHttpResult> ApplyChanges(string body)
    {
        return Handle<ApplyRequest, ApplyResponse>(body, r => r.Changes != null, "changes", (app, request) =>
            WritebackService.Apply(app.ActiveUIDocument?.Document, request));
    }

    /// <summary>GET /model-parameters: a read-only scan of the active document's model elements and their types.</summary>
    public async Task<WritebackHttpResult> ModelParameters()
    {
        try
        {
            var response = await _queue.Run(app => ModelParameterScan.Scan(app.ActiveUIDocument?.Document), StartTimeout).ConfigureAwait(false);
            return WritebackHttpResult.Ok(response);
        }
        catch (RevitBusyException ex)
        {
            return WritebackHttpResult.Error(503, ex.Message);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Model parameter scan failed: {ex}");
            return WritebackHttpResult.Error(500, $"Revit failed to scan the model: {ex.Message}");
        }
    }

    /// <summary>POST /pset-suggestions: Revit parameters for IFC properties, by the exporter's name matching.</summary>
    public Task<WritebackHttpResult> PsetSuggestions(string body)
    {
        return Handle<PsetSuggestionRequest, PsetSuggestionResponse>(body, r => r.Items != null, "items", (app, request) =>
            ModelParameterScan.Suggest(app.ActiveUIDocument?.Document, request));
    }

    /// <summary>POST /pset-files/save: writes a generated pset file. Plain file IO, not on the Revit thread.</summary>
    public static async Task<WritebackHttpResult> SavePsetFile(string body, string defaultFolder)
    {
        PsetFileSaveRequest? request;
        try
        {
            request = WritebackJson.Deserialize<PsetFileSaveRequest>(body);
        }
        catch (JsonException ex)
        {
            return WritebackHttpResult.Error(400, $"Invalid JSON body: {ex.Message}");
        }
        if (request == null) return WritebackHttpResult.Error(400, "Missing body");

        try
        {
            // A network folder can take long to answer; keep it off the listener thread
            var saved = await Task.Run(() => PsetFileWriter.Save(request, defaultFolder)).ConfigureAwait(false);
            return WritebackHttpResult.Ok(saved);
        }
        catch (PsetFileSaveException ex)
        {
            return new WritebackHttpResult(ex.StatusCode, JsonSerializer.Serialize(new { error = ex.Message, path = ex.Path }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return WritebackHttpResult.Error(500, $"The file could not be written: {ex.Message}");
        }
    }

    private async Task<WritebackHttpResult> Handle<TRequest, TResponse>(
        string body,
        Func<TRequest, bool> isValid,
        string requiredProperty,
        Func<Autodesk.Revit.UI.UIApplication, TRequest, TResponse> work)
        where TRequest : class
    {
        TRequest? request;
        try
        {
            request = WritebackJson.Deserialize<TRequest>(body);
        }
        catch (JsonException ex)
        {
            return WritebackHttpResult.Error(400, $"Invalid JSON body: {ex.Message}");
        }

        if (request == null || !isValid(request))
        {
            return WritebackHttpResult.Error(400, $"Missing '{requiredProperty}' array");
        }

        try
        {
            var response = await _queue.Run(app => work(app, request), StartTimeout).ConfigureAwait(false);
            return WritebackHttpResult.Ok(response);
        }
        catch (RevitBusyException ex)
        {
            return WritebackHttpResult.Error(503, ex.Message);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Write-back request failed: {ex}");
            return WritebackHttpResult.Error(500, $"Revit failed to handle the request: {ex.Message}");
        }
    }
}
