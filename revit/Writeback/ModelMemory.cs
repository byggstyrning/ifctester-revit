using System.IO;
using System.Text.Json;

namespace IfcTesterRevit.Writeback;

/// <summary>What IfcTester last used for one Revit model. Paths only; the files are read again each time.</summary>
public sealed class RememberedChoices
{
    /// <summary>The IDS file last loaded through the add-in for this model.</summary>
    public string? IdsFile { get; set; }

    /// <summary>The IFC export setup of the last export.</summary>
    public string? Configuration { get; set; }

    /// <summary>The property set file override of the last export; null when it used the setup's own.</summary>
    public string? PsetFile { get; set; }

    /// <summary>The parameter mapping table override of the last export; null when it used the setup's own.</summary>
    public string? ParameterMappingFile { get; set; }

    /// <summary>When the entry last changed, UTC, ISO 8601.</summary>
    public string? Updated { get; set; }
}

/// <summary>The memory file: one entry per model, keyed by the central model path or the file path.</summary>
public sealed class ModelMemoryFile
{
    public int Version { get; set; } = 1;
    public Dictionary<string, RememberedChoices> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>POST /model-memory: remember the IDS file for the open model.</summary>
public sealed class ModelMemoryRequest
{
    public string? IdsFile { get; set; }
}

/// <summary>A remembered file and whether it can be read now.</summary>
public sealed class RememberedFile
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Exists { get; set; }

    /// <summary>Why the file could not be checked, e.g. a share that did not answer.</summary>
    public string? Error { get; set; }
}

/// <summary>The open model as the memory knows it.</summary>
public sealed class MemoryModel
{
    public string Key { get; set; } = "";
    public string? Title { get; set; }
    public bool Workshared { get; set; }
}

/// <summary>GET /model-memory and POST /model-memory.</summary>
public sealed class ModelMemoryResponse
{
    /// <summary>Null when no document is open or it has never been saved.</summary>
    public MemoryModel? Model { get; set; }

    /// <summary>Why nothing is remembered, when that is the case.</summary>
    public string? Message { get; set; }

    /// <summary>Null when nothing is remembered for the model.</summary>
    public RememberedState? Remembered { get; set; }
}

public sealed class RememberedState
{
    public string? Updated { get; set; }
    public string? Configuration { get; set; }
    public RememberedFile? IdsFile { get; set; }
    public RememberedFile? PsetFile { get; set; }
    public RememberedFile? ParameterMappingFile { get; set; }
}

/// <summary>GET /ids-files/read and POST /ids-files/pick.</summary>
public sealed class IdsFileResponse
{
    public bool Cancelled { get; set; }
    public string? Path { get; set; }
    public string? Name { get; set; }
    public string? Content { get; set; }
}

/// <summary>
/// The per-user memory of IfcTester choices per Revit model, in a JSON file next to the pset save
/// folder. It lives outside the model on purpose: writing it must never change a shared central
/// model, and the same file serves Revit 2025 and 2026. Every change re-reads the file under a lock
/// file, so two Revit sessions do not overwrite each other's entries.
/// </summary>
public sealed class ModelMemoryStore
{
    /// <summary>Models beyond this many are forgotten, oldest first.</summary>
    public const int MaxModels = 500;

    private static readonly JsonSerializerOptions WriteOptions = new(WritebackJson.Options) { WriteIndented = true };
    private readonly object _lock = new();

    public ModelMemoryStore(string path)
    {
        FilePath = path;
    }

    public string FilePath { get; }

    public static string DefaultPath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IfcTesterRevit", "model-memory.json");
    }

    /// <summary>The entry for the model, or null. Never throws: an unreadable file remembers nothing.</summary>
    public RememberedChoices? Get(string modelKey)
    {
        lock (_lock)
        {
            return Read().Models.TryGetValue(modelKey, out var choices) ? choices : null;
        }
    }

    public RememberedChoices RememberIds(string modelKey, string idsFile, DateTime utcNow)
    {
        return Change(modelKey, utcNow, choices => choices.IdsFile = idsFile);
    }

    /// <summary>The setup and the overrides of an export; a null override means the setup's own file was used.</summary>
    public RememberedChoices RememberExport(string modelKey, string configuration, string? psetFile, string? parameterMappingFile, DateTime utcNow)
    {
        return Change(modelKey, utcNow, choices =>
        {
            choices.Configuration = configuration;
            choices.PsetFile = string.IsNullOrWhiteSpace(psetFile) ? null : psetFile!.Trim();
            choices.ParameterMappingFile = string.IsNullOrWhiteSpace(parameterMappingFile) ? null : parameterMappingFile!.Trim();
        });
    }

    private RememberedChoices Change(string modelKey, DateTime utcNow, Action<RememberedChoices> change)
    {
        if (string.IsNullOrWhiteSpace(modelKey)) throw new ArgumentException("A model key is required", nameof(modelKey));
        lock (_lock)
        {
            var folder = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            using var fileLock = AcquireFileLock();

            var memory = Read(forChange: true);
            if (!memory.Models.TryGetValue(modelKey, out var choices))
            {
                choices = new RememberedChoices();
                memory.Models[modelKey] = choices;
            }
            change(choices);
            choices.Updated = utcNow.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

            foreach (var old in memory.Models.OrderByDescending(m => m.Value.Updated, StringComparer.Ordinal).Skip(MaxModels).Select(m => m.Key).ToList())
            {
                memory.Models.Remove(old);
            }

            Write(memory);
            return choices;
        }
    }

    // A file that cannot be opened right now reads as empty, but a change then fails rather than
    // writing the other entries away; a file that is not valid JSON is kept aside and replaced.
    private ModelMemoryFile Read(bool forChange = false)
    {
        string text;
        try
        {
            if (!File.Exists(FilePath)) return new ModelMemoryFile();
            text = File.ReadAllText(FilePath);
        }
        catch (Exception ex) when (!forChange && ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Model memory could not be opened: {ex.Message}");
            return new ModelMemoryFile();
        }

        try
        {
            var memory = JsonSerializer.Deserialize<ModelMemoryFile>(text, WritebackJson.Options);
            if (memory?.Models == null) return new ModelMemoryFile();
            // The deserialized dictionary compares keys with case; model paths do not
            memory.Models = new Dictionary<string, RememberedChoices>(memory.Models.Where(m => m.Value != null), StringComparer.OrdinalIgnoreCase);
            return memory;
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Model memory is not valid JSON, starting empty: {ex.Message}");
            KeepUnreadable(text);
            return new ModelMemoryFile();
        }
    }

    // A file that is not valid JSON is copied aside rather than lost, so it can still be looked at
    private void KeepUnreadable(string text)
    {
        try
        {
            File.WriteAllText(FilePath + ".unreadable", text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // Written next to the file and moved over it, so a crash never leaves half a file
    private void Write(ModelMemoryFile memory)
    {
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(memory, WriteOptions), new System.Text.UTF8Encoding(false));
        if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
        else File.Move(temp, FilePath);
    }

    // Revit 2025 and 2026 may both run; the lock file keeps their read-change-write cycles apart
    private FileStream AcquireFileLock()
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            try
            {
                return new FileStream(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }
        }
    }
}

/// <summary>Reading an IDS file the page may have: decoded by its byte order mark, else as UTF-8.</summary>
public static class IdsFiles
{
    /// <summary>Larger than any IDS seen in practice; keeps a wrong pick from loading a huge file into the page.</summary>
    public const long MaxBytes = 50L * 1024 * 1024;

    public static bool HasIdsExtension(string? path)
    {
        var extension = Path.GetExtension(path ?? "");
        return string.Equals(extension, ".ids", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase);
    }

    public static IdsFileResponse Read(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxBytes) throw new IOException($"{info.Name} is larger than {MaxBytes / (1024 * 1024)} MB, which is not an IDS file.");
        using var reader = new StreamReader(path, new System.Text.UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        return new IdsFileResponse { Path = info.FullName, Name = info.Name, Content = reader.ReadToEnd() };
    }
}
