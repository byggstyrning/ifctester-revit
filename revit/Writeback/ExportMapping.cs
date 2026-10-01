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
    /// does. An override path replaces the setup's file of that kind and keeps the setup's other
    /// settings. Never throws: a setup that cannot be read gives an empty mapping and a note.
    /// </summary>
    public static ExportMapping Load(Document document, string? configurationName, string? psetOverride = null, string? mappingOverride = null)
    {
        var mapping = new ExportMapping { Configuration = configurationName };
        try
        {
            var configuration = string.IsNullOrWhiteSpace(configurationName) ? null : FindConfiguration(document, configurationName!);
            if (configuration == null)
            {
                mapping.Note = string.IsNullOrWhiteSpace(configurationName)
                    ? "No IFC export setup is known for this audit"
                    : $"The IFC export setup '{configurationName}' was not found";
                mapping.Note += string.IsNullOrWhiteSpace(psetOverride) && string.IsNullOrWhiteSpace(mappingOverride)
                    ? ", so no property set mapping file was read."
                    : ", so only the override files were read.";
                // The overrides still say where the exported values came from
                var overrides = ExportFiles.Select(null, new ExportFileSettings(), psetOverride, mappingOverride, "", File.Exists);
                mapping.Read(overrides);
                if (overrides.Warning != null) mapping.Note += " " + overrides.Warning;
                return mapping;
            }

            mapping.UseTypePropertiesInInstancePsets = GetBool(configuration, "UseTypePropertiesInInstacePSets");
            var selection = DescribeFiles(configuration, configurationName!, psetOverride, mappingOverride);
            mapping.Read(selection);
            mapping.Note = selection.Warning;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ExportMapping.Load failed: {ex}");
            mapping.Note = $"The IFC export setup '{configurationName}' could not be read: {ex.Message}";
        }

        return mapping;
    }

    /// <summary>The files the named setup of the document exports with, or null when there is no such setup.</summary>
    public static ExportFileSelection? DescribeFiles(Document document, string configurationName)
    {
        var configuration = FindConfiguration(document, configurationName);
        return configuration == null ? null : DescribeFiles(configuration, configurationName, null, null);
    }

    /// <summary>The files an export with this exporter configuration object and these overrides reads.</summary>
    public static ExportFileSelection DescribeFiles(object configuration, string configurationName, string? psetOverride, string? mappingOverride)
    {
        return ExportFiles.Select(configurationName, ReadFileSettings(configuration), psetOverride, mappingOverride, ExporterDirectory(), File.Exists);
    }

    private static ExportFileSettings ReadFileSettings(object configuration)
    {
        return new ExportFileSettings
        {
            ExportUserDefinedPsets = GetBool(configuration, "ExportUserDefinedPsets"),
            UserDefinedPsetsFileName = GetString(configuration, "ExportUserDefinedPsetsFileName"),
            ExportUserDefinedParameterMapping = GetBool(configuration, "ExportUserDefinedParameterMapping"),
            ParameterMappingFileName = GetString(configuration, "ExportUserDefinedParameterMappingFileName")
        };
    }

    private void Read(ExportFileSelection selection)
    {
        if (selection.PsetFile != null && selection.PsetFileExists)
        {
            Mapping.ReadUserDefinedPsets(File.ReadAllLines(selection.PsetFile));
            Files.Add(selection.PsetFile);
        }
        if (selection.ParameterMappingFile != null && selection.ParameterMappingFileExists)
        {
            Mapping.ReadParameterMappingTable(File.ReadAllLines(selection.ParameterMappingFile));
            Files.Add(selection.ParameterMappingFile);
        }
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
