using System.IO;

namespace IfcTesterRevit.Writeback;

/// <summary>The file settings an IFC export setup holds, read from the exporter's configuration object.</summary>
public sealed class ExportFileSettings
{
    public bool ExportUserDefinedPsets { get; set; }
    public string? UserDefinedPsetsFileName { get; set; }
    public bool ExportUserDefinedParameterMapping { get; set; }
    public string? ParameterMappingFileName { get; set; }
}

/// <summary>
/// Which property set file and parameter mapping table an export reads: the setup's own, the
/// exporter's fallback, or an override from the page. Reported by GET /export-status and
/// GET /ifc-configuration-files; ExportMapping reads the same files for write-back.
/// </summary>
public sealed class ExportFileSelection
{
    /// <summary>The user-defined property set file the export reads, or the missing file the setup names. Null when the setup exports none.</summary>
    public string? PsetFile { get; set; }
    public bool PsetFileExists { get; set; }
    public bool PsetFileIsOverride { get; set; }

    /// <summary>The parameter mapping table the export reads, or the missing table the setup names. Null when there is none.</summary>
    public string? ParameterMappingFile { get; set; }
    public bool ParameterMappingFileExists { get; set; }
    public bool ParameterMappingFileIsOverride { get; set; }

    /// <summary>Set when a file the setup names is missing, so the export lacks what it maps.</summary>
    public string? Warning { get; set; }
}

/// <summary>The setup and file overrides an export ran with; write-back reads the same files.</summary>
public sealed class ExportRequestSettings
{
    public ExportRequestSettings(string? configuration, string? psetFile, string? parameterMappingFile)
    {
        Configuration = configuration;
        PsetFile = string.IsNullOrWhiteSpace(psetFile) ? null : psetFile!.Trim();
        ParameterMappingFile = string.IsNullOrWhiteSpace(parameterMappingFile) ? null : parameterMappingFile!.Trim();
    }

    public string? Configuration { get; }
    public string? PsetFile { get; }
    public string? ParameterMappingFile { get; }

    /// <summary>
    /// The setup and overrides a resolve reads. What the request names wins. An override it does
    /// not name comes from the last export, but only when that export used the same setup:
    /// another setup's override would describe a different export.
    /// </summary>
    public static ExportRequestSettings ForResolve(ResolveRequest request, ExportRequestSettings? lastExport)
    {
        var configuration = string.IsNullOrWhiteSpace(request.Configuration) ? lastExport?.Configuration : request.Configuration;
        var sameExport = lastExport != null && string.Equals(configuration, lastExport.Configuration, StringComparison.Ordinal);
        return new ExportRequestSettings(
            configuration,
            string.IsNullOrWhiteSpace(request.PsetFile) && sameExport ? lastExport!.PsetFile : request.PsetFile,
            string.IsNullOrWhiteSpace(request.ParameterMappingFile) && sameExport ? lastExport!.ParameterMappingFile : request.ParameterMappingFile);
    }
}

public static class ExportFiles
{
    /// <summary>
    /// Applies the exporter's rules for which files an export reads. An override replaces the
    /// setup's file. Without one, a missing property set file is looked for as
    /// &lt;setup name&gt;.txt next to the exporter, and the parameter mapping table is read
    /// whenever the setup names a file that exists, whatever its checkbox says.
    /// </summary>
    public static ExportFileSelection Select(
        string? configurationName,
        ExportFileSettings setup,
        string? psetOverride,
        string? mappingOverride,
        string exporterDirectory,
        Func<string, bool> fileExists)
    {
        var selection = new ExportFileSelection();
        var warnings = new List<string>();

        if (!string.IsNullOrWhiteSpace(psetOverride))
        {
            selection.PsetFile = psetOverride!.Trim();
            selection.PsetFileIsOverride = true;
            selection.PsetFileExists = fileExists(selection.PsetFile);
            if (!selection.PsetFileExists)
            {
                warnings.Add($"The property set file override was not found: {selection.PsetFile}.");
            }
        }
        else if (setup.ExportUserDefinedPsets)
        {
            var file = setup.UserDefinedPsetsFileName;
            if (!string.IsNullOrEmpty(file) && fileExists(file!))
            {
                selection.PsetFile = file;
                selection.PsetFileExists = true;
            }
            else
            {
                var fallback = string.IsNullOrEmpty(configurationName) ? null : Path.Combine(exporterDirectory, configurationName + ".txt");
                if (fallback != null && fileExists(fallback))
                {
                    selection.PsetFile = fallback;
                    selection.PsetFileExists = true;
                }
                else
                {
                    selection.PsetFile = string.IsNullOrEmpty(file) ? null : file;
                    warnings.Add($"The setup's property set file was not found: {(string.IsNullOrEmpty(file) ? "(no file named)" : file)}. The export has no user-defined property sets.");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(mappingOverride))
        {
            selection.ParameterMappingFile = mappingOverride!.Trim();
            selection.ParameterMappingFileIsOverride = true;
            selection.ParameterMappingFileExists = fileExists(selection.ParameterMappingFile);
            if (!selection.ParameterMappingFileExists)
            {
                warnings.Add($"The parameter mapping table override was not found: {selection.ParameterMappingFile}.");
            }
        }
        else if (!string.IsNullOrEmpty(setup.ParameterMappingFileName))
        {
            selection.ParameterMappingFile = setup.ParameterMappingFileName;
            selection.ParameterMappingFileExists = fileExists(setup.ParameterMappingFileName!);
            if (!selection.ParameterMappingFileExists && setup.ExportUserDefinedParameterMapping)
            {
                warnings.Add($"The setup's parameter mapping table was not found: {setup.ParameterMappingFileName}.");
            }
        }
        else if (setup.ExportUserDefinedParameterMapping)
        {
            warnings.Add("The setup uses a parameter mapping table but names no file.");
        }

        selection.Warning = warnings.Count == 0 ? null : string.Join(" ", warnings);
        return selection;
    }
}

/// <summary>JSON body of POST /export-ifc. The two files are optional overrides of the setup's own.</summary>
public sealed class ExportIfcRequest
{
    public string? Configuration { get; set; }
    public string? PsetFile { get; set; }
    public string? ParameterMappingFile { get; set; }
}

/// <summary>One entry of GET /pset-files.</summary>
public sealed class PsetFileEntry
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";

    /// <summary>Last write time, ISO 8601 in UTC.</summary>
    public string Modified { get; set; } = "";
}
