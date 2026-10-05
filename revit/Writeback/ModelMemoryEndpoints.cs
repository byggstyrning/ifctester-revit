using System.IO;
using System.Text.Json;
using Autodesk.Revit.DB;

namespace IfcTesterRevit.Writeback;

/// <summary>
/// GET/POST /model-memory, POST /ids-files/pick and GET /ids-files/read: what IfcTester last used
/// for the open model, and loading an IDS by path so that choice can be remembered. The model is
/// identified on the Revit thread; the memory file and the remembered files are read off it.
/// </summary>
public sealed class ModelMemoryEndpoints
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

    // A remembered file on a share that does not answer must not hold the page
    private static readonly TimeSpan FileCheckTimeout = TimeSpan.FromSeconds(10);

    private readonly RevitWorkQueue _queue;
    private readonly Func<ModelMemoryStore> _store;
    private readonly ReadablePaths _readable;

    /// <param name="queue">Runs the work on the Revit thread.</param>
    /// <param name="store">The memory file; a function so a test can point it elsewhere.</param>
    /// <param name="readable">The files the page may read; picked and remembered files that exist are added to it.</param>
    public ModelMemoryEndpoints(RevitWorkQueue queue, Func<ModelMemoryStore> store, ReadablePaths readable)
    {
        _queue = queue;
        _store = store;
        _readable = readable;
    }

    /// <summary>
    /// The key the memory files a model under: the central model's user-visible path for a
    /// workshared model, so every local copy shares one entry, else the document's own path.
    /// Null for a document that has never been saved.
    /// </summary>
    public static MemoryModel? ModelOf(Document? document)
    {
        if (document == null || document.IsFamilyDocument) return null;
        if (document.IsWorkshared)
        {
            try
            {
                var central = document.GetWorksharingCentralModelPath();
                var path = central == null ? null : ModelPathUtils.ConvertModelPathToUserVisiblePath(central);
                if (!string.IsNullOrWhiteSpace(path)) return new MemoryModel { Key = path!, Title = document.Title, Workshared = true };
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                System.Diagnostics.Debug.WriteLine($"No central model path: {ex.Message}");
            }
        }
        return string.IsNullOrWhiteSpace(document.PathName) ? null : new MemoryModel { Key = document.PathName, Title = document.Title };
    }

    /// <summary>Remembers the setup and overrides of an export of the model. Never throws: the export has already succeeded.</summary>
    public void RememberExport(string? modelKey, ExportRequestSettings settings)
    {
        if (string.IsNullOrWhiteSpace(modelKey) || string.IsNullOrWhiteSpace(settings.Configuration)) return;
        try
        {
            _store().RememberExport(modelKey!, settings.Configuration!, settings.PsetFile, settings.ParameterMappingFile, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"The export could not be remembered for {modelKey}: {ex}");
        }
    }

    /// <summary>GET /model-memory</summary>
    public async Task<WritebackHttpResult> Get()
    {
        try
        {
            var model = await OpenModel().ConfigureAwait(false);
            return WritebackHttpResult.Ok(await Describe(model).ConfigureAwait(false));
        }
        catch (RevitBusyException ex)
        {
            return WritebackHttpResult.Error(503, ex.Message);
        }
    }

    /// <summary>POST /model-memory { idsFile }: remembers an IDS the user loaded through the add-in for the open model.</summary>
    public async Task<WritebackHttpResult> Post(string body)
    {
        ModelMemoryRequest? request;
        try
        {
            request = WritebackJson.Deserialize<ModelMemoryRequest>(body);
        }
        catch (JsonException ex)
        {
            return WritebackHttpResult.Error(400, $"Invalid JSON body: {ex.Message}");
        }
        if (request == null || string.IsNullOrWhiteSpace(request.IdsFile)) return WritebackHttpResult.Error(400, "Missing 'idsFile'");

        // Only a file the user chose through the add-in, so the page cannot plant a path to read later
        var idsFile = ReadablePaths.Normalize(request.IdsFile);
        if (idsFile == null || !IdsFiles.HasIdsExtension(idsFile) || !_readable.IsAllowed(idsFile))
        {
            return WritebackHttpResult.Error(403, $"Only an IDS file opened through the add-in can be remembered: {request.IdsFile}");
        }

        try
        {
            var model = await OpenModel().ConfigureAwait(false);
            if (model == null) return WritebackHttpResult.Ok(await Describe(null).ConfigureAwait(false));
            await Task.Run(() => _store().RememberIds(model.Key, idsFile, DateTime.UtcNow)).ConfigureAwait(false);
            return WritebackHttpResult.Ok(await Describe(model).ConfigureAwait(false));
        }
        catch (RevitBusyException ex)
        {
            return WritebackHttpResult.Error(503, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return WritebackHttpResult.Error(500, $"The choice could not be remembered: {ex.Message}");
        }
    }

    /// <summary>
    /// POST /ids-files/pick: Revit's own file dialog for an IDS, so the page gets the file's path
    /// (a browser file picker gives none). Starts in the folder of the IDS remembered for the model.
    /// </summary>
    public async Task<WritebackHttpResult> Pick()
    {
        string? picked;
        try
        {
            picked = await _queue.Run(app =>
            {
                var model = ModelOf(app.ActiveUIDocument?.Document);
                var remembered = model == null ? null : _store().Get(model.Key)?.IdsFile;
                return ShowOpenDialog(app.MainWindowHandle, remembered);
            }, StartTimeout).ConfigureAwait(false);
        }
        catch (RevitBusyException ex)
        {
            return WritebackHttpResult.Error(503, ex.Message);
        }

        if (picked == null) return WritebackHttpResult.Ok(new IdsFileResponse { Cancelled = true });
        _readable.Allow(picked);
        return await ReadIds(picked).ConfigureAwait(false);
    }

    /// <summary>GET /ids-files/read?path=: an IDS picked through the add-in or remembered for the open model.</summary>
    public async Task<WritebackHttpResult> Read(string? path)
    {
        if (ReadablePaths.Normalize(path) is not { } full || !IdsFiles.HasIdsExtension(full))
        {
            return WritebackHttpResult.Error(400, "'path' must be the full path of an .ids or .xml file");
        }
        if (!_readable.IsAllowed(full))
        {
            return WritebackHttpResult.Error(403, $"IfcTester reads only an IDS picked through the add-in or remembered for the open model: {path}");
        }
        return await ReadIds(full).ConfigureAwait(false);
    }

    private async Task<WritebackHttpResult> ReadIds(string path)
    {
        try
        {
            // A network folder can take long to answer; keep it off the listener thread
            var read = await Task.Run(() => File.Exists(path) ? IdsFiles.Read(path) : null).ConfigureAwait(false);
            return read == null ? WritebackHttpResult.Error(404, $"The file does not exist: {path}") : WritebackHttpResult.Ok(read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return WritebackHttpResult.Error(500, $"The file could not be read: {ex.Message}");
        }
    }

    private Task<MemoryModel?> OpenModel()
    {
        return _queue.Run(app => ModelOf(app.ActiveUIDocument?.Document), StartTimeout);
    }

    // The remembered choices with each file checked now; the files that exist become readable for the page
    private async Task<ModelMemoryResponse> Describe(MemoryModel? model)
    {
        if (model == null)
        {
            return new ModelMemoryResponse { Message = "No saved model is open in Revit, so IfcTester remembers nothing for it." };
        }

        RememberedChoices? choices;
        try
        {
            choices = await Task.Run(() => _store().Get(model.Key)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Model memory read failed: {ex}");
            choices = null;
        }
        if (choices == null) return new ModelMemoryResponse { Model = model };

        var files = await Task.WhenAll(Check(choices.IdsFile), Check(choices.PsetFile), Check(choices.ParameterMappingFile)).ConfigureAwait(false);
        foreach (var file in files)
        {
            if (file is { Exists: true }) _readable.Allow(file.Path);
        }
        return new ModelMemoryResponse
        {
            Model = model,
            Remembered = new RememberedState
            {
                Updated = choices.Updated,
                Configuration = choices.Configuration,
                IdsFile = files[0],
                PsetFile = files[1],
                ParameterMappingFile = files[2]
            }
        };
    }

    private static async Task<RememberedFile?> Check(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var file = new RememberedFile { Path = path!, Name = Path.GetFileName(path!) };
        var exists = Task.Run(() => File.Exists(path));
        if (await Task.WhenAny(exists, Task.Delay(FileCheckTimeout)).ConfigureAwait(false) != exists)
        {
            file.Error = "The folder did not answer in time.";
            return file;
        }
        file.Exists = await exists.ConfigureAwait(false);
        return file;
    }

    // On the Revit thread. A small topmost owner keeps the dialog above the browser the page runs in.
    private static string? ShowOpenDialog(IntPtr revitWindow, string? remembered)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open IDS for IfcTester",
            Filter = "IDS files (*.ids;*.xml)|*.ids;*.xml|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        try
        {
            var folder = string.IsNullOrWhiteSpace(remembered) ? null : Path.GetDirectoryName(remembered);
            if (folder != null && Directory.Exists(folder)) dialog.InitialDirectory = folder;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
        }

        var owner = new System.Windows.Window
        {
            Width = 0,
            Height = 0,
            WindowStyle = System.Windows.WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = true,
            Topmost = true,
            AllowsTransparency = true,
            Opacity = 0
        };
        if (revitWindow != IntPtr.Zero) new System.Windows.Interop.WindowInteropHelper(owner).Owner = revitWindow;
        try
        {
            owner.Show();
            owner.Activate();
            return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
        }
        finally
        {
            owner.Close();
        }
    }
}
