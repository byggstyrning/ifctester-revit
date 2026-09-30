using System.Globalization;
using Autodesk.Revit.DB;

namespace IfcTesterRevit.Writeback;

/// <summary>
/// Finds the Revit parameters behind a failed IFC property or attribute of one element.
/// One instance serves one request: it keeps the parameter list of every element it has looked
/// at, because many failed elements share a type.
/// </summary>
public sealed class ParameterResolver
{
    private readonly Dictionary<ElementId, List<Parameter>> _parameters = new();

    /// <summary>IFC attribute to the override parameter the exporter reads it from.</summary>
    private static readonly Dictionary<string, string> AttributeOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Name"] = "IfcName",
        ["Description"] = "IfcDescription",
        ["ObjectType"] = "IfcObjectType",
        ["LongName"] = "IfcLongName"
    };

    public static bool IsSupportedAttribute(string? attribute)
    {
        return attribute != null && AttributeOverrides.ContainsKey(attribute.Trim());
    }

    /// <summary>
    /// Candidates for a property, best first: the export setup's mapping files, then a parameter
    /// named like the property on the instance, then on the type.
    /// </summary>
    public List<ParameterCandidate> ForProperty(Element element, Element? type, string? propertySet, string name, ExportMapping mapping)
    {
        var candidates = new CandidateList();

        foreach (var mapped in mapping.Mapping.Find(propertySet, name))
        {
            AddMapped(candidates, element, type, mapped, mapped.OnInstance, mapped.OnType || mapping.UseTypePropertiesInInstancePsets);
        }

        // The exporter reads a common property from a parameter called "<Pset>.<Property>" first
        // and from one called "<Property>" second, and on a type also accepts a "[Type]" suffix.
        var names = new List<string>();
        if (!string.IsNullOrWhiteSpace(propertySet)) names.Add($"{propertySet!.Trim()}.{name}");
        names.Add(name);

        foreach (var parameterName in names)
        {
            candidates.AddRange(FindByName(element, parameterName), WritebackScope.Instance, WritebackSource.NameMatch);
        }

        if (type != null)
        {
            foreach (var parameterName in names.Concat(names.Select(n => n + "[Type]")))
            {
                candidates.AddRange(FindByName(type, parameterName), WritebackScope.Type, WritebackSource.NameMatch);
            }
        }

        return candidates.Items;
    }

    /// <summary>
    /// Candidates for Name, Description, ObjectType or LongName: an "Attribute Mapping" set in the
    /// mapping file, then the Ifc&lt;Attribute&gt; override parameter.
    /// </summary>
    public List<ParameterCandidate> ForAttribute(Element element, Element? type, string attribute, ExportMapping mapping)
    {
        var candidates = new CandidateList();

        foreach (var mapped in mapping.Mapping.FindAttribute(attribute))
        {
            // Attributes are written on the occurrence; the exporter falls back to its type.
            AddMapped(candidates, element, type, mapped, onInstance: true, onType: true);
        }

        if (AttributeOverrides.TryGetValue(attribute.Trim(), out var overrideName))
        {
            candidates.AddRange(FindByName(element, overrideName), WritebackScope.Instance, WritebackSource.IfcOverride);

            // Only ObjectType is taken from the type when the instance has no override.
            if (type != null && overrideName == "IfcObjectType")
            {
                candidates.AddRange(FindByName(type, overrideName + "[Type]"), WritebackScope.Type, WritebackSource.IfcOverride);
            }
        }

        return candidates.Items;
    }

    /// <summary>
    /// The parameter a candidate named, looked up again by its exact name. With several of that
    /// name the first writable one is taken, in the order the candidates were listed.
    /// </summary>
    public Parameter? FindForWrite(Element target, string parameterName)
    {
        var matches = Ordered(target)
            .Where(p => string.Equals(p.Definition.Name, parameterName, StringComparison.Ordinal))
            .ToList();

        return matches.FirstOrDefault(p => !p.IsReadOnly && p.StorageType != StorageType.ElementId && p.StorageType != StorageType.None)
               ?? matches.FirstOrDefault();
    }

    public static ParameterCandidate Describe(Parameter parameter, string scope, string source)
    {
        return new ParameterCandidate
        {
            Parameter = parameter.Definition.Name,
            Scope = scope,
            StorageType = StorageTypeName(parameter),
            Value = DisplayValue(parameter),
            HasValue = HasValue(parameter),
            ReadOnly = parameter.IsReadOnly,
            Source = source
        };
    }

    public static string StorageTypeName(Parameter parameter)
    {
        switch (parameter.StorageType)
        {
            case StorageType.Integer:
                return IsYesNo(parameter) ? "yesno" : "integer";
            case StorageType.Double:
                return "double";
            case StorageType.ElementId:
                return "elementid";
            default:
                return "string";
        }
    }

    /// <summary>
    /// The value as the user should read and type it: project display units for doubles,
    /// Yes/No for yes/no parameters, the number itself for integers.
    /// </summary>
    public static string DisplayValue(Parameter parameter)
    {
        switch (parameter.StorageType)
        {
            case StorageType.String:
                return parameter.AsString() ?? "";
            case StorageType.Integer:
                if (!parameter.HasValue) return "";
                if (IsYesNo(parameter)) return parameter.AsInteger() != 0 ? "Yes" : "No";
                return parameter.AsInteger().ToString(CultureInfo.InvariantCulture);
            case StorageType.Double:
            case StorageType.ElementId:
                return parameter.HasValue ? parameter.AsValueString() ?? "" : "";
            default:
                return "";
        }
    }

    public static bool IsYesNo(Parameter parameter)
    {
        return parameter.StorageType == StorageType.Integer &&
               parameter.Definition.GetDataType() == SpecTypeId.Boolean.YesNo;
    }

    private static bool HasValue(Parameter parameter)
    {
        if (!parameter.HasValue) return false;
        return parameter.StorageType != StorageType.String || !string.IsNullOrEmpty(parameter.AsString());
    }

    private void AddMapped(CandidateList candidates, Element element, Element? type, MappedParameter mapped, bool onInstance, bool onType)
    {
        if (onInstance)
        {
            candidates.AddRange(FindMapped(element, mapped), WritebackScope.Instance, WritebackSource.PsetMappingFile);
        }

        if (type != null && onType)
        {
            candidates.AddRange(FindMapped(type, mapped), WritebackScope.Type, WritebackSource.PsetMappingFile);
        }
    }

    private IEnumerable<Parameter> FindMapped(Element target, MappedParameter mapped)
    {
        if (mapped.BuiltInParameterName != null)
        {
            if (Enum.TryParse<BuiltInParameter>(mapped.BuiltInParameterName, out var builtIn) && builtIn != BuiltInParameter.INVALID)
            {
                var parameter = target.get_Parameter(builtIn);
                return parameter != null ? new[] { parameter } : Array.Empty<Parameter>();
            }

            // A built-in name the exporter cannot read makes it fall back to the property name.
            return FindByName(target, mapped.PropertyName);
        }

        var found = FindByName(target, mapped.ParameterName ?? mapped.PropertyName);
        if (found.Count == 0 && target is ElementType)
        {
            found = FindByName(target, (mapped.ParameterName ?? mapped.PropertyName) + "[Type]");
        }
        return found;
    }

    /// <summary>
    /// Parameters matching a name the way the exporter matches them: spaces do not count and
    /// neither does case (Revit.IFC.Export ParameterUtil caches names through RemoveSpaces in a
    /// case-insensitive dictionary), so "Fire Rating" answers to "FireRating".
    /// </summary>
    private List<Parameter> FindByName(Element target, string name)
    {
        var key = name.Replace(" ", "");
        return Ordered(target)
            .Where(p => string.Equals(p.Definition.Name.Replace(" ", ""), key, StringComparison.InvariantCultureIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// The element's visible parameters: the IFC group first, then by parameter id, which is the
    /// order in which the exporter prefers one of two parameters with the same name.
    /// </summary>
    private List<Parameter> Ordered(Element target)
    {
        if (_parameters.TryGetValue(target.Id, out var cached)) return cached;

        var parameters = new List<Parameter>();
        foreach (Parameter parameter in target.Parameters)
        {
            var definition = parameter?.Definition;
            if (definition == null || string.IsNullOrWhiteSpace(definition.Name)) continue;
            if (definition is InternalDefinition { Visible: false }) continue;
            parameters.Add(parameter!);
        }

        var ordered = parameters
            .OrderBy(p => p.Definition.GetGroupTypeId() == GroupTypeId.Ifc ? 0 : 1)
            .ThenBy(p => p.Id.Value)
            .ToList();
        _parameters[target.Id] = ordered;
        return ordered;
    }

    private sealed class CandidateList
    {
        private readonly HashSet<(string Scope, long ParameterId)> _seen = new();

        public List<ParameterCandidate> Items { get; } = new();

        public void AddRange(IEnumerable<Parameter> parameters, string scope, string source)
        {
            foreach (var parameter in parameters)
            {
                if (_seen.Add((scope, parameter.Id.Value)))
                {
                    Items.Add(Describe(parameter, scope, source));
                }
            }
        }
    }
}
