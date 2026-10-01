using System.Text.Json;
using System.Text.Json.Serialization;

namespace IfcTesterRevit.Writeback;

// JSON bodies of POST /resolve-parameters and POST /apply-changes. The contract is written down
// in tasks/writeback.md; property names are camelCase on the wire.

public sealed class ResolveRequest
{
    public List<ResolveItem>? Items { get; set; }

    /// <summary>
    /// Optional: the IFC export setup whose mapping files to read. Without it the server uses
    /// the setup of the last export it ran.
    /// </summary>
    public string? Configuration { get; set; }

    /// <summary>
    /// Optional: the user-defined property set file and parameter mapping table the export was
    /// made with, when they replaced the setup's own. Without them the server uses the overrides
    /// of its last export, if that export used the same setup.
    /// </summary>
    public string? PsetFile { get; set; }
    public string? ParameterMappingFile { get; set; }
}

public sealed class ResolveItem
{
    public string? Key { get; set; }
    public string? GlobalId { get; set; }

    /// <summary>"property" or "attribute".</summary>
    public string? Facet { get; set; }

    public string? PropertySet { get; set; }
    public string? Name { get; set; }
}

public sealed class ResolveResponse
{
    public List<ResolveResult> Items { get; set; } = new();

    /// <summary>The IFC export setup the mapping files were taken from, if one was known.</summary>
    public string? Configuration { get; set; }

    /// <summary>The mapping files that were read. Empty when the setup uses none.</summary>
    public List<string> MappingFiles { get; set; } = new();

    /// <summary>Set when a mapping file the setup names could not be used.</summary>
    public string? MappingNote { get; set; }
}

public sealed class ResolveResult
{
    public string? Key { get; set; }
    public bool Found { get; set; }
    public long? ElementId { get; set; }
    public string? ElementName { get; set; }
    public string? Category { get; set; }

    /// <summary>The element's type, and how many elements are of it: what a type-scope fix changes.</summary>
    public string? TypeName { get; set; }
    public int? TypeInstanceCount { get; set; }

    public List<ParameterCandidate> Candidates { get; set; } = new();
    public string? Message { get; set; }
}

public sealed class ParameterCandidate
{
    public string Parameter { get; set; } = "";

    /// <summary>"instance" or "type".</summary>
    public string Scope { get; set; } = WritebackScope.Instance;

    /// <summary>"string", "integer", "double", "yesno" or "elementid".</summary>
    public string StorageType { get; set; } = "string";

    public string Value { get; set; } = "";
    public bool HasValue { get; set; }
    public bool ReadOnly { get; set; }

    /// <summary>"pset-mapping-file", "name-match" or "ifc-override".</summary>
    public string Source { get; set; } = WritebackSource.NameMatch;
}

public sealed class ApplyRequest
{
    public List<ChangeItem>? Changes { get; set; }
}

public sealed class ChangeItem
{
    public string? Key { get; set; }
    public string? GlobalId { get; set; }
    public string? Parameter { get; set; }
    public string? Scope { get; set; }

    /// <summary>A JSON string by contract; a number or a boolean is read as its text.</summary>
    public JsonElement Value { get; set; }

    [JsonIgnore]
    public string ValueText => Value.ValueKind switch
    {
        JsonValueKind.String => Value.GetString() ?? "",
        JsonValueKind.Number => Value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => ""
    };
}

public sealed class ApplyResponse
{
    public int Applied { get; set; }
    public int Failed { get; set; }
    public List<ChangeResult> Results { get; set; } = new();
}

public sealed class ChangeResult
{
    public string? Key { get; set; }
    public bool Ok { get; set; }
    public string? Message { get; set; }
    public string? NewValue { get; set; }
}

public static class WritebackScope
{
    public const string Instance = "instance";
    public const string Type = "type";
}

public static class WritebackSource
{
    public const string PsetMappingFile = "pset-mapping-file";
    public const string NameMatch = "name-match";
    public const string IfcOverride = "ifc-override";
}

public static class WritebackJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
