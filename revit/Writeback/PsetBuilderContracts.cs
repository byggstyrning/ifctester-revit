using System.IO;
using System.Text;

namespace IfcTesterRevit.Writeback;

// JSON bodies of the pset builder endpoints: GET /model-parameters, POST /pset-suggestions and
// POST /pset-files/save. Property names are camelCase on the wire. No Revit types here, so
// purecheck can run them.

/// <summary>Where a parameter comes from, as far as the pset file is concerned.</summary>
public static class ParameterOrigin
{
    public const string BuiltIn = "built-in";
    public const string Shared = "shared";

    /// <summary>A non-shared parameter bound to categories in the project (Manage > Project Parameters).</summary>
    public const string Project = "project";

    /// <summary>A non-shared parameter that comes with a loaded family.</summary>
    public const string Family = "family";
}

/// <summary>GET /model-parameters.</summary>
public sealed class ModelParametersResponse
{
    public string? Document { get; set; }

    /// <summary>Model elements scanned (not element types).</summary>
    public int ElementCount { get; set; }

    /// <summary>Distinct element types of those elements.</summary>
    public int TypeCount { get; set; }

    public long ElapsedMs { get; set; }
    public List<ModelParameterInfo> Parameters { get; set; } = new();
    public string? Message { get; set; }
}

public sealed class ModelParameterInfo
{
    public string Name { get; set; } = "";

    /// <summary>"instance", "type" or "both".</summary>
    public string Scope { get; set; } = WritebackScope.Instance;

    /// <summary>One of <see cref="ParameterOrigin"/>.</summary>
    public string Origin { get; set; } = ParameterOrigin.BuiltIn;

    /// <summary>The BuiltInParameter enum name, for a built-in parameter.</summary>
    public string? BuiltInParameter { get; set; }

    /// <summary>The shared parameter GUID, for a shared parameter.</summary>
    public string? Guid { get; set; }

    /// <summary>"string", "integer", "double", "yesno" or "elementid".</summary>
    public string StorageType { get; set; } = "string";

    /// <summary>The parameter's data type, e.g. "length" or "text"; empty when Revit gives none.</summary>
    public string DataType { get; set; } = "";

    /// <summary>Read-only wherever it was seen.</summary>
    public bool ReadOnly { get; set; }

    /// <summary>Elements that have it on themselves.</summary>
    public int InstanceCount { get; set; }

    /// <summary>Elements whose type has it.</summary>
    public int TypeCount { get; set; }

    /// <summary>Elements that have it on themselves or on their type.</summary>
    public int ElementCount { get; set; }

    /// <summary>Elements where it has a value, on the element or on its type.</summary>
    public int WithValueCount { get; set; }

    public List<string> Categories { get; set; } = new();
}

/// <summary>POST /pset-suggestions.</summary>
public sealed class PsetSuggestionRequest
{
    public List<PsetSuggestionItem>? Items { get; set; }
}

public sealed class PsetSuggestionItem
{
    public string? Key { get; set; }
    public string? PropertySet { get; set; }
    public string? Name { get; set; }

    /// <summary>IFC entity names (IfcWall, IFCWALL, IfcWallType); case does not matter here.</summary>
    public List<string>? Entities { get; set; }
}

public sealed class PsetSuggestionResponse
{
    public int ElementCount { get; set; }

    /// <summary>How elements were matched to the entities, so the page can say what "scanned" means.</summary>
    public string ScopeMethod { get; set; } = "";

    public long ElapsedMs { get; set; }
    public List<PsetSuggestionResult> Items { get; set; } = new();
    public string? Message { get; set; }
}

public sealed class PsetSuggestionResult
{
    public string? Key { get; set; }
    public string? PropertySet { get; set; }
    public string? Name { get; set; }

    /// <summary>"entities" when only elements of the item's entities were scanned, "all" when every model element was.</summary>
    public string Scope { get; set; } = "entities";

    public int ElementsScanned { get; set; }
    public string? Note { get; set; }

    /// <summary>Best first. Empty when no parameter matches by name.</summary>
    public List<PsetSuggestion> Suggestions { get; set; } = new();
}

public sealed class PsetSuggestion
{
    public string Parameter { get; set; } = "";

    /// <summary>"instance" or "type".</summary>
    public string Scope { get; set; } = WritebackScope.Instance;

    /// <summary>"name" (named like the property) or "pset-name" (named "&lt;Pset&gt;.&lt;Property&gt;").</summary>
    public string Rule { get; set; } = PsetSuggestionRule.Name;

    public string Origin { get; set; } = ParameterOrigin.BuiltIn;
    public string? BuiltInParameter { get; set; }
    public string StorageType { get; set; } = "string";
    public string DataType { get; set; } = "";
    public bool ReadOnly { get; set; }

    /// <summary>Scanned elements that have the parameter (on themselves for instance, on their type for type).</summary>
    public int ElementsWithParameter { get; set; }

    public int ElementsWithValue { get; set; }
    public int ElementsScanned { get; set; }
}

public static class PsetSuggestionRule
{
    public const string Name = "name";
    public const string PsetName = "pset-name";
}

/// <summary>POST /pset-files/save.</summary>
public sealed class PsetFileSaveRequest
{
    /// <summary>Full path. Empty: %LOCALAPPDATA%\IfcTesterRevit\psets\&lt;name&gt;.txt.</summary>
    public string? Path { get; set; }

    /// <summary>File name used when no path is given.</summary>
    public string? Name { get; set; }

    public string? Content { get; set; }
    public bool Overwrite { get; set; }
}

public sealed class PsetFileSaveResponse
{
    public string Path { get; set; } = "";
    public bool Overwritten { get; set; }
    public long Bytes { get; set; }
}

public sealed class PsetFileSaveException : Exception
{
    public PsetFileSaveException(int statusCode, string message, string? path = null)
        : base(message)
    {
        StatusCode = statusCode;
        Path = path;
    }

    public int StatusCode { get; }
    public string? Path { get; }
}

public static class PsetNames
{
    /// <summary>
    /// A name as the exporter compares parameter names: without spaces, upper case
    /// (Revit.IFC.Export ParameterUtil keys its cache by RemoveSpaces in a case-insensitive dictionary).
    /// </summary>
    public static string ParameterKey(string name) => name.Replace(" ", "").ToUpperInvariant();

    /// <summary>
    /// An IFC entity name reduced to the occurrence class it stands for: upper case, without a
    /// Type or Style suffix and without the StandardCase / ElementedCase subclass, so
    /// IfcWallType, IFCWALLSTANDARDCASE and IfcWall all give IFCWALL.
    /// </summary>
    public static string OccurrenceKey(string entity)
    {
        var key = entity.Trim().ToUpperInvariant();
        foreach (var suffix in new[] { "STANDARDCASE", "ELEMENTEDCASE", "TYPE", "STYLE" })
        {
            if (key.Length > suffix.Length + 3 && key.EndsWith(suffix, StringComparison.Ordinal))
            {
                key = key.Substring(0, key.Length - suffix.Length);
                break;
            }
        }
        return key;
    }

    /// <summary>The IFC class part of an IfcExportAs value: "IfcWall.SHEAR" gives "IfcWall".</summary>
    public static string ExportAsClass(string value)
    {
        var text = value.Trim();
        var dot = text.IndexOf('.');
        return dot >= 0 ? text.Substring(0, dot).Trim() : text;
    }
}

/// <summary>Writes a generated pset file. No Revit types.</summary>
public static class PsetFileWriter
{
    /// <summary>UTF-8 without a byte order mark, like the project's pset files on the shared pset folder (checked 2026-10-01).</summary>
    public static readonly Encoding FileEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static string DefaultFolder()
    {
        return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IfcTesterRevit", "psets");
    }

    /// <summary>The full path a request saves to.</summary>
    public static string ResolvePath(PsetFileSaveRequest request, string defaultFolder)
    {
        if (!string.IsNullOrWhiteSpace(request.Path))
        {
            var path = request.Path!.Trim();
            if (!System.IO.Path.IsPathRooted(path))
            {
                throw new PsetFileSaveException(400, $"The path must be a full path: {path}");
            }
            return System.IO.Path.GetFullPath(path);
        }

        var name = SafeFileName(string.IsNullOrWhiteSpace(request.Name) ? "pset" : request.Name!);
        if (!name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) name += ".txt";
        return System.IO.Path.Combine(defaultFolder, name);
    }

    /// <summary>
    /// Writes the content. Refuses to replace an existing file unless the request says so (409),
    /// and refuses a path whose folder does not exist unless it is the default folder, which is
    /// created.
    /// </summary>
    public static PsetFileSaveResponse Save(PsetFileSaveRequest request, string defaultFolder)
    {
        if (request.Content == null)
        {
            throw new PsetFileSaveException(400, "Missing 'content'");
        }

        var path = ResolvePath(request, defaultFolder);
        var folder = System.IO.Path.GetDirectoryName(path) ?? "";
        if (!Directory.Exists(folder))
        {
            if (string.Equals(System.IO.Path.GetFullPath(folder).TrimEnd('\\'), System.IO.Path.GetFullPath(defaultFolder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(folder);
            }
            else
            {
                throw new PsetFileSaveException(400, $"The folder does not exist: {folder}", path);
            }
        }

        var exists = File.Exists(path);
        if (exists && !request.Overwrite)
        {
            throw new PsetFileSaveException(409, $"The file already exists: {path}. Send overwrite: true to replace it.", path);
        }

        var bytes = FileEncoding.GetBytes(request.Content);
        File.WriteAllBytes(path, bytes);
        return new PsetFileSaveResponse { Path = path, Overwritten = exists, Bytes = bytes.Length };
    }

    private static string SafeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim('.', ' ');
        return cleaned.Length == 0 ? "pset" : cleaned;
    }
}
