using Autodesk.Revit.DB;

namespace IfcTesterRevit.Writeback;

// JSON bodies of POST /element-parameters. Property names are camelCase on the wire.

public sealed class ElementParametersRequest
{
    public string? GlobalId { get; set; }

    /// <summary>
    /// Optional: the element id the exporter wrote as the entity's Tag. Tried first, and kept only
    /// when its GlobalId matches, so the whole-document GlobalId scan is skipped in the usual case.
    /// </summary>
    public long? ElementId { get; set; }

    /// <summary>The IFC export setup and file overrides whose mapping files to read, as for /resolve-parameters.</summary>
    public string? Configuration { get; set; }
    public string? PsetFile { get; set; }
    public string? ParameterMappingFile { get; set; }

    /// <summary>
    /// Optional: a draft of the property set file, not saved yet, read in place of the file the
    /// setup or override names. Lets the page show what an edit of the file would change.
    /// </summary>
    public string? PsetFileContent { get; set; }

    /// <summary>The file the draft was made from. The draft is used only when the setup reads that file.</summary>
    public string? PsetFileContentFor { get; set; }
}

public sealed class ElementParametersResponse
{
    public bool Found { get; set; }
    public string? Message { get; set; }

    public long? ElementId { get; set; }
    public string? ElementName { get; set; }
    public string? Category { get; set; }
    public long? TypeId { get; set; }
    public string? TypeName { get; set; }

    /// <summary>Instance parameters first, then type parameters, each in the exporter's preference order.</summary>
    public List<InspectedParameter> Parameters { get; set; } = new();

    /// <summary>Every line of the mapping files, each with the parameters it reads on this element.</summary>
    public List<InspectedMapping> Mappings { get; set; } = new();

    public string? Configuration { get; set; }
    public List<string> MappingFiles { get; set; } = new();
    public string? MappingNote { get; set; }
    public bool UseTypePropertiesInInstancePsets { get; set; }

    /// <summary>The user-defined property set file the mappings were read from (the file a draft replaces).</summary>
    public string? PsetFile { get; set; }
}

/// <summary>Body of GET /pset-files/read.</summary>
public sealed class PsetFileReadResponse
{
    public string Path { get; set; } = "";
    public string Content { get; set; } = "";

    /// <summary>The file starts with a UTF-8 byte order mark.</summary>
    public bool Bom { get; set; }
}

public sealed class InspectedParameter
{
    public string Name { get; set; } = "";

    /// <summary>"instance" or "type".</summary>
    public string Scope { get; set; } = WritebackScope.Instance;

    /// <summary>The parameter group as Revit labels it in the properties palette.</summary>
    public string Group { get; set; } = "";

    public string StorageType { get; set; } = "string";
    public string Value { get; set; } = "";
    public bool HasValue { get; set; }
    public bool ReadOnly { get; set; }
    public bool Shared { get; set; }

    /// <summary>The BuiltInParameter name, for a built-in parameter.</summary>
    public string? BuiltIn { get; set; }
}

public sealed class InspectedMapping
{
    public string PropertySet { get; set; } = "";
    public string PropertyName { get; set; } = "";
    public string? ParameterName { get; set; }
    public string? BuiltInParameter { get; set; }
    public List<string> Entities { get; set; } = new();

    /// <summary>"pset-file" or "mapping-table".</summary>
    public string Origin { get; set; } = MappingOrigin.PsetFile;

    public bool OnInstance { get; set; }
    public bool OnType { get; set; }

    /// <summary>The line and its set header in the file, 1-based, and the data type column.</summary>
    public int LineNumber { get; set; }
    public int HeaderLineNumber { get; set; }
    public string DataType { get; set; } = "";

    /// <summary>The parameters the line reads) on this element and its type, best first. Empty when none exists.</summary>
    public List<ParameterCandidate> Candidates { get; set; } = new();
}

/// <summary>
/// One element as Revit holds it and as the export setup's mapping files read it. Runs on the
/// Revit thread; reads only.
/// </summary>
public static class ElementInspection
{
    public static ElementParametersResponse Inspect(Document? document, ElementParametersRequest request, ExportMapping mapping)
    {
        var response = new ElementParametersResponse
        {
            Configuration = mapping.Configuration,
            MappingFiles = mapping.Files.ToList(),
            MappingNote = mapping.Note,
            UseTypePropertiesInInstancePsets = mapping.UseTypePropertiesInInstancePsets,
            PsetFile = mapping.PsetFile
        };

        if (document == null)
        {
            response.Message = "No model is open in Revit.";
            return response;
        }

        var element = Find(document, request, out var message);
        response.Message = message;
        if (element == null) return response;

        var typeId = element.GetTypeId();
        var type = typeId == ElementId.InvalidElementId ? null : document.GetElement(typeId);

        response.Found = true;
        response.ElementId = element.Id.Value;
        response.ElementName = element.Name;
        response.Category = element.Category?.Name;
        if (type != null)
        {
            response.TypeId = type.Id.Value;
            response.TypeName = type is ElementType elementType && !string.IsNullOrEmpty(elementType.FamilyName)
                ? $"{elementType.FamilyName}: {elementType.Name}"
                : type.Name;
        }

        var resolver = new ParameterResolver();
        response.Parameters.AddRange(resolver.VisibleParameters(element).Select(p => Describe(p, WritebackScope.Instance)));
        if (type != null)
        {
            response.Parameters.AddRange(resolver.VisibleParameters(type).Select(p => Describe(p, WritebackScope.Type)));
        }

        foreach (var mapped in mapping.Mapping.Entries)
        {
            response.Mappings.Add(new InspectedMapping
            {
                PropertySet = mapped.PropertySet,
                PropertyName = mapped.PropertyName,
                ParameterName = mapped.ParameterName,
                BuiltInParameter = mapped.BuiltInParameterName,
                Entities = mapped.Entities.ToList(),
                Origin = mapped.Origin,
                OnInstance = mapped.OnInstance,
                OnType = mapped.OnType,
                LineNumber = mapped.LineNumber,
                HeaderLineNumber = mapped.HeaderLineNumber,
                DataType = mapped.DataType,
                Candidates = resolver.ForMappedLine(element, type, mapped, mapping.UseTypePropertiesInInstancePsets)
            });
        }

        return response;
    }

    /// <summary>
    /// The element by its Tag when that one carries the GlobalId, otherwise by GlobalId. When only
    /// the Tag finds an element, that element is returned with a message saying the ids disagree.
    /// </summary>
    private static Element? Find(Document document, ElementParametersRequest request, out string? message)
    {
        message = null;
        var globalId = request.GlobalId?.Trim();

        Element? byTag = null;
        if (request.ElementId is long id)
        {
            byTag = document.GetElement(new ElementId(id));
            if (byTag != null && (string.IsNullOrEmpty(globalId) || HasGlobalId(document, byTag, globalId!)))
            {
                return byTag;
            }
        }

        if (!string.IsNullOrEmpty(globalId))
        {
            var found = GlobalIdIndex.Build(document).Find(globalId, out message);
            if (found != null) return found;
        }

        if (byTag != null)
        {
            message = $"No element in '{document.Title}' carries the GlobalId {globalId}; showing element {byTag.Id.Value}, " +
                      "the entity's Tag. The audited IFC may come from an older export.";
            return byTag;
        }

        return null;
    }

    private static bool HasGlobalId(Document document, Element element, string globalId)
    {
        try
        {
            var stored = element.get_Parameter(BuiltInParameter.IFC_GUID)?.AsString();
            if (IfcGuid.IsValid(stored) && string.Equals(stored, globalId, StringComparison.Ordinal)) return true;
            return string.Equals(IfcGuid.FromGuid(ExportUtils.GetExportId(document, element.Id)), globalId, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static InspectedParameter Describe(Parameter parameter, string scope)
    {
        var candidate = ParameterResolver.Describe(parameter, scope, "");
        var builtIn = (parameter.Definition as InternalDefinition)?.BuiltInParameter ?? BuiltInParameter.INVALID;
        return new InspectedParameter
        {
            Name = candidate.Parameter,
            Scope = scope,
            Group = GroupLabel(parameter.Definition),
            StorageType = candidate.StorageType,
            Value = candidate.Value,
            HasValue = candidate.HasValue,
            ReadOnly = candidate.ReadOnly,
            Shared = parameter.IsShared,
            BuiltIn = builtIn == BuiltInParameter.INVALID ? null : builtIn.ToString()
        };
    }

    private static string GroupLabel(Definition definition)
    {
        try
        {
            var group = definition.GetGroupTypeId();
            return group == null || group.Empty() ? "Other" : LabelUtils.GetLabelForGroup(group);
        }
        catch
        {
            return "Other";
        }
    }
}
