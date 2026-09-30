using System.IO;
using Autodesk.Revit.DB;

namespace IfcTesterRevit.Writeback;

/// <summary>
/// What an IFC export setup says about where property values come from: its user-defined
/// property set file and its parameter mapping table.
/// </summary>
public sealed class ExportMapping
{
    public PsetMapping Mapping { get; } = new();

    /// <summary>The setup's "Use type properties in instance property sets" option (the exporter spells it UseTypePropertiesInInstacePSets).</summary>
    public bool UseTypePropertiesInInstancePsets { get; set; }

    public string? Configuration { get; set; }
    public List<string> Files { get; } = new();
    public string? Note { get; set; }

    public static ExportMapping None => new();

    /// <summary>Reads mapping files directly. Either path may be null.</summary>
    public static ExportMapping FromFiles(string? userDefinedPsetFile, string? parameterMappingTable, bool useTypePropertiesInInstancePsets = false)
    {
        var mapping = new ExportMapping { UseTypePropertiesInInstancePsets = useTypePropertiesInInstancePsets };
        if (!string.IsNullOrEmpty(userDefinedPsetFile))
        {
            mapping.Mapping.ReadUserDefinedPsets(File.ReadAllLines(userDefinedPsetFile));
            mapping.Files.Add(userDefinedPsetFile!);
        }
        if (!string.IsNullOrEmpty(parameterMappingTable))
        {
            mapping.Mapping.ReadParameterMappingTable(File.ReadAllLines(parameterMappingTable));
            mapping.Files.Add(parameterMappingTable!);
        }
        return mapping;
    }

    /// <summary>
    /// Reads the mapping files of the named IFC export setup of the document. The setups live in
    /// the IFC exporter's UI assembly, which is reached by reflection like the rest of the add-in
    /// does. Never throws: a setup that cannot be read gives an empty mapping and a note.
    /// </summary>
    public static ExportMapping Load(Document document, string? configurationName)
    {
        var mapping = new ExportMapping { Configuration = configurationName };
        if (string.IsNullOrWhiteSpace(configurationName))
        {
            mapping.Note = "No IFC export setup is known for this audit, so no property set mapping file was read.";
            return mapping;
        }

        try
        {
            var configuration = FindConfiguration(document, configurationName!);
            if (configuration == null)
            {
                mapping.Note = $"The IFC export setup '{configurationName}' was not found, so no property set mapping file was read.";
                return mapping;
            }

            mapping.UseTypePropertiesInInstancePsets = GetBool(configuration, "UseTypePropertiesInInstacePSets");

            if (GetBool(configuration, "ExportUserDefinedPsets"))
            {
                // Same fallback as the exporter: a missing file is looked for as <setup name>.txt
                // next to the exporter.
                var file = GetString(configuration, "ExportUserDefinedPsetsFileName");
                if (string.IsNullOrEmpty(file) || !File.Exists(file))
                {
                    var fallback = Path.Combine(ExporterDirectory(), configurationName + ".txt");
                    if (File.Exists(fallback))
                    {
                        file = fallback;
                    }
                    else
                    {
                        mapping.Note = $"The user-defined property set file of '{configurationName}' was not found: {file}";
                        file = null;
                    }
                }

                if (file != null)
                {
                    mapping.Mapping.ReadUserDefinedPsets(File.ReadAllLines(file));
                    mapping.Files.Add(file);
                }
            }

            // The exporter reads the table whenever the setup names a file that exists; the
            // setup's checkbox for it is not consulted.
            var table = GetString(configuration, "ExportUserDefinedParameterMappingFileName");
            if (!string.IsNullOrEmpty(table) && File.Exists(table))
            {
                mapping.Mapping.ReadParameterMappingTable(File.ReadAllLines(table));
                mapping.Files.Add(table!);
            }
            else if (GetBool(configuration, "ExportUserDefinedParameterMapping"))
            {
                mapping.Note = $"The parameter mapping table of '{configurationName}' was not found: {table}";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ExportMapping.Load failed: {ex}");
            mapping.Note = $"The IFC export setup '{configurationName}' could not be read: {ex.Message}";
        }

        return mapping;
    }

    private static object? FindConfiguration(Document document, string name)
    {
        var mapType = IFCExportHelper.FindIFCExportConfigurationsMapType();
        if (mapType == null) return null;

        var map = IFCExportHelper.CreateAndInitializeConfigMap(mapType, document);
        if (map == null) return null;

        // Not the map's indexer: it throws for a name it does not have.
        if (mapType.GetProperty("Values")?.GetValue(map) is not System.Collections.IEnumerable values) return null;

        var all = values.Cast<object>().Where(c => c != null).ToList();
        return all.FirstOrDefault(c => string.Equals(GetString(c, "Name"), name, StringComparison.Ordinal))
               ?? all.FirstOrDefault(c => string.Equals(GetString(c, "Name"), name, StringComparison.OrdinalIgnoreCase));
    }

    private static string ExporterDirectory()
    {
        var exporter = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Revit.IFC.Export");
        var location = exporter?.Location;
        if (string.IsNullOrEmpty(location)) location = typeof(Document).Assembly.Location;
        return Path.GetDirectoryName(location) ?? "";
    }

    private static bool GetBool(object configuration, string property)
    {
        return configuration.GetType().GetProperty(property)?.GetValue(configuration) is true;
    }

    private static string? GetString(object configuration, string property)
    {
        return configuration.GetType().GetProperty(property)?.GetValue(configuration) as string;
    }
}
