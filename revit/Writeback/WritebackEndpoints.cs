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

    /// <summary>
    /// POST /element-parameters: every parameter of one element and its type, and where each line
    /// of the export setup's mapping files reads its value on that element. Reads only.
    /// </summary>
    public Task<WritebackHttpResult> ElementParameters(string body)
    {
        return Handle<ElementParametersRequest, ElementParametersResponse>(body, r => !string.IsNullOrWhiteSpace(r.GlobalId) || r.ElementId != null, "globalId", (app, request) =>
        {
            var document = app.ActiveUIDocument?.Document;
            var settings = ExportRequestSettings.ForResolve(
                new ResolveRequest { Configuration = request.Configuration, PsetFile = request.PsetFile, ParameterMappingFile = request.ParameterMappingFile },
                _lastExport());
            var mapping = document == null
                ? ExportMapping.None
                : ExportMapping.Load(document, settings.Configuration, settings.PsetFile, settings.ParameterMappingFile);
            if (request.PsetFileContent != null &&
                (string.IsNullOrWhiteSpace(request.PsetFileContentFor) ||
                 string.Equals(request.PsetFileContentFor, mapping.PsetFile, StringComparison.OrdinalIgnoreCase)))
            {
                var name = mapping.PsetFile != null ? Path.GetFileName(mapping.PsetFile) : "pset file";
                mapping = mapping.WithPsetContent(request.PsetFileContent, $"{name} (unsaved edits)");
            }
            return ElementInspection.Inspect(document, request, mapping);
        });
    }

    /// <summary>
    /// GET /pset-files/read?path=: the text of an existing .txt property set file, for editing on
    /// the page. Refuses a file that is not valid UTF-8, because editing it here and saving it back
    /// as UTF-8 would garble its å, ä and ö. Plain file IO, not on the Revit thread.
    /// </summary>
    public static async Task<WritebackHttpResult> ReadPsetFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) ||
            !string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase))
        {
            return WritebackHttpResult.Error(400, "'path' must be the full path of a .txt file");
        }

        try
        {
            // A network folder can take long to answer; keep it off the listener thread
            var bytes = await Task.Run(() => File.Exists(path) ? File.ReadAllBytes(path) : null).ConfigureAwait(false);
            if (bytes == null) return WritebackHttpResult.Error(404, $"The file does not exist: {path}");

            var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            string content;
            try
            {
                content = new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
            }
            catch (System.Text.DecoderFallbackException)
            {
                return WritebackHttpResult.Error(422, $"{Path.GetFileName(path)} is not UTF-8 (probably ANSI), so it cannot be edited here without garbling its characters.");
            }

            return WritebackHttpResult.Ok(new PsetFileReadResponse { Path = path!, Content = content, Bom = bom });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return WritebackHttpResult.Error(500, $"The file could not be read: {ex.Message}");
        }
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
