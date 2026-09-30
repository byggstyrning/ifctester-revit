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
/// POST /resolve-parameters and POST /apply-changes: JSON in, a job on the Revit thread, JSON out.
/// </summary>
public sealed class WritebackEndpoints
{
    // How long a request waits for Revit to become free before it gives up.
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

    private readonly RevitWorkQueue _queue;
    private readonly Func<string?> _lastExportConfiguration;

    /// <param name="queue">Runs the work on the Revit thread.</param>
    /// <param name="lastExportConfiguration">The IFC export setup of the last export the server ran, if any.</param>
    public WritebackEndpoints(RevitWorkQueue queue, Func<string?> lastExportConfiguration)
    {
        _queue = queue;
        _lastExportConfiguration = lastExportConfiguration;
    }

    public Task<WritebackHttpResult> ResolveParameters(string body)
    {
        return Handle<ResolveRequest, ResolveResponse>(body, r => r.Items != null, "items", (app, request) =>
        {
            var document = app.ActiveUIDocument?.Document;
            var configuration = string.IsNullOrWhiteSpace(request.Configuration) ? _lastExportConfiguration() : request.Configuration;
            var mapping = document == null ? ExportMapping.None : ExportMapping.Load(document, configuration);
            return WritebackService.Resolve(document, request, mapping);
        });
    }

    public Task<WritebackHttpResult> ApplyChanges(string body)
    {
        return Handle<ApplyRequest, ApplyResponse>(body, r => r.Changes != null, "changes", (app, request) =>
            WritebackService.Apply(app.ActiveUIDocument?.Document, request));
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
