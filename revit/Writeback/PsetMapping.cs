namespace IfcTesterRevit.Writeback;

/// <summary>
/// One line of a mapping file: an IFC property and the Revit parameter the exporter reads it from.
/// </summary>
public sealed class MappedParameter
{
    public string PropertySet { get; set; } = "";
    public string PropertyName { get; set; } = "";

    /// <summary>Revit parameter name. Null when the line names a built-in parameter instead.</summary>
    public string? ParameterName { get; set; }

    /// <summary>The enum name from a "BuiltInParameter.XYZ" entry, without the prefix.</summary>
    public string? BuiltInParameterName { get; set; }

    /// <summary>The set lists at least one occurrence entity (IfcWall, IfcElement, ...).</summary>
    public bool OnInstance { get; set; }

    /// <summary>The set lists at least one type entity (IfcWallType, IfcTypeObject, ...).</summary>
    public bool OnType { get; set; }
}

/// <summary>
/// The IFC property name to Revit parameter name mappings of an IFC export setup, read from its
/// user-defined property set file and its parameter mapping table. Parsing follows the Revit IFC
/// exporter (Revit.IFC.Export PropertyMap.LoadUserDefinedPset / LoadParameterMap) line for line,
/// so a file the exporter accepts reads the same here. No Revit types.
/// </summary>
public sealed class PsetMapping
{
    /// <summary>
    /// A set with this name in the user-defined file maps IFC attributes (Name, Description, ...)
    /// instead of properties.
    /// </summary>
    public const string AttributeSetName = "Attribute Mapping";

    private readonly List<MappedParameter> _entries = new();

    public IReadOnlyList<MappedParameter> Entries => _entries;

    /// <summary>
    /// Reads a user-defined property set file:
    /// <code>
    /// PropertySet:	&lt;Pset name&gt;	I|T	&lt;entity list&gt;
    /// 	&lt;Property name&gt;	&lt;Data type&gt;	&lt;Revit parameter name, optional&gt;
    /// </code>
    /// Tab separated; empty columns are dropped, so extra tabs between columns do not matter.
    /// </summary>
    public void ReadUserDefinedPsets(IEnumerable<string> lines)
    {
        string? setName = null;
        var onInstance = false;
        var onType = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimStart(' ', '\t');
            if (line.Length == 0 || line[0] == '#') continue;

            var parts = line.Split(new[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4 && string.Equals(parts[0], "PropertySet:", StringComparison.OrdinalIgnoreCase))
            {
                setName = parts[1].Trim();
                var entities = parts[3].Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                // The exporter ignores the I/T column and applies a set to whatever entities it
                // lists, so the entity names decide between instance and type.
                onType = entities.Any(IsTypeEntity);
                onInstance = entities.Length == 0 || entities.Any(e => !IsTypeEntity(e));
            }
            else if (parts.Length >= 2 && setName != null)
            {
                var entry = new MappedParameter
                {
                    PropertySet = setName,
                    PropertyName = parts[0].Trim(),
                    OnInstance = onInstance,
                    OnType = onType
                };

                const string builtInPrefix = "BuiltInParameter.";
                var parameter = parts.Length >= 3 ? parts[2].Trim() : "";
                if (parameter.StartsWith(builtInPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    entry.BuiltInParameterName = parameter.Substring(builtInPrefix.Length).Trim();
                }
                else
                {
                    // Without a third column the exporter reads the parameter named like the property.
                    entry.ParameterName = parameter.Length > 0 ? parameter : entry.PropertyName;
                }

                _entries.Add(entry);
            }
        }
    }

    /// <summary>
    /// Reads a parameter mapping table: <c>&lt;Pset name&gt;	&lt;Property name&gt;	&lt;Revit parameter name&gt;</c>,
    /// exactly three tab-separated columns per line. It renames the parameter behind a property
    /// of a common property set.
    /// </summary>
    public void ReadParameterMappingTable(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            if (line.Length == 0 || line[0] == '#') continue;

            var parts = line.Split(new[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) continue;

            _entries.Add(new MappedParameter
            {
                PropertySet = parts[0].Trim(),
                PropertyName = parts[1].Trim(),
                ParameterName = parts[2].Trim(),
                OnInstance = true,
                OnType = true
            });
        }
    }

    /// <summary>
    /// Every mapping for the property, in file order. A property name repeated inside one set is
    /// a fallback chain in the exporter, so all of its lines are returned.
    /// </summary>
    public List<MappedParameter> Find(string? propertySet, string? propertyName)
    {
        var set = (propertySet ?? "").Trim();
        var name = (propertyName ?? "").Trim();
        return _entries
            .Where(e => string.Equals(e.PropertySet, set, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(e.PropertyName, name, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public List<MappedParameter> FindAttribute(string? attributeName) => Find(AttributeSetName, attributeName);

    private static bool IsTypeEntity(string entity)
    {
        return entity.EndsWith("Type", StringComparison.OrdinalIgnoreCase) ||
               entity.EndsWith("Style", StringComparison.OrdinalIgnoreCase) ||
               entity.StartsWith("IfcType", StringComparison.OrdinalIgnoreCase);
    }
}
