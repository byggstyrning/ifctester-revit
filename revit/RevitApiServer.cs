using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace IfcTesterRevit;

/// <summary>
/// Helper class for IFC export configuration operations
/// </summary>
public static class IFCExportHelper
{
    /// <summary>
    /// Finds the IFCExportConfigurationsMap type using reflection
    /// </summary>
    public static Type? FindIFCExportConfigurationsMapType()
    {
        var allAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        Type? configMapType = null;
        
        // Search all loaded assemblies for IFC-related types
        foreach (var assembly in allAssemblies)
        {
            try
            {
                var assemblyName = assembly.GetName().Name ?? "";
                if (assemblyName.Contains("IFC", StringComparison.OrdinalIgnoreCase) || 
                    assemblyName.Contains("BIM", StringComparison.OrdinalIgnoreCase))
                {
                    var possibleTypes = new[] {
                        "BIM.IFC.Export.UI.IFCExportConfigurationsMap",
                        "Autodesk.Revit.DB.IFC.IFCExportConfigurationsMap",
                        "Revit.IFC.Export.UI.IFCExportConfigurationsMap",
                        "BIM.IFC.Export.IFCExportConfigurationsMap"
                    };
                    
                    foreach (var typeName in possibleTypes)
                    {
                        configMapType = assembly.GetType(typeName);
                        if (configMapType != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"Found IFCExportConfigurationsMap: {typeName} in {assemblyName}");
                            return configMapType;
                        }
                    }
                }
            }
            catch { }
        }
        
        // If still not found, try loading IFCExporterUIOverride.dll explicitly
        try
        {
            var revitPath = System.IO.Path.GetDirectoryName(typeof(Document).Assembly.Location);
            if (!string.IsNullOrEmpty(revitPath))
            {
                var ifcExporterPath = System.IO.Path.Combine(revitPath, "IFCExporterUIOverride.dll");
                if (System.IO.File.Exists(ifcExporterPath))
                {
                    System.Diagnostics.Debug.WriteLine($"Loading IFC exporter DLL: {ifcExporterPath}");
                    var ifcAssembly = System.Reflection.Assembly.LoadFile(ifcExporterPath);
                    configMapType = ifcAssembly.GetType("BIM.IFC.Export.UI.IFCExportConfigurationsMap") ??
                                   ifcAssembly.GetType("Revit.IFC.Export.UI.IFCExportConfigurationsMap");
                    if (configMapType != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Found IFCExportConfigurationsMap in IFCExporterUIOverride.dll");
                        return configMapType;
                    }
                }
            }
        }
        catch (Exception loadEx)
        {
            System.Diagnostics.Debug.WriteLine($"Could not load IFC exporter assembly: {loadEx.Message}");
        }
        
        return null;
    }
    
    /// <summary>
    /// Sets IFCCommandOverrideApplication.TheDocument if available
    /// </summary>
    public static void SetIFCCommandOverrideDocument(Document doc)
    {
        try
        {
            var ifcCommandOverrideType = typeof(Document).Assembly.GetType("BIM.IFC.Export.UI.IFCCommandOverrideApplication") ??
                                         AppDomain.CurrentDomain.GetAssemblies()
                                             .Select(a => a.GetType("BIM.IFC.Export.UI.IFCCommandOverrideApplication"))
                                             .FirstOrDefault(t => t != null);
            
            if (ifcCommandOverrideType != null)
            {
                var theDocumentProperty = ifcCommandOverrideType.GetProperty("TheDocument", 
                    BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
                if (theDocumentProperty != null)
                {
                    theDocumentProperty.SetValue(null, doc);
                    System.Diagnostics.Debug.WriteLine("Set IFCCommandOverrideApplication.TheDocument");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error setting IFCCommandOverrideApplication.TheDocument: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Creates and initializes an IFCExportConfigurationsMap instance
    /// </summary>
    public static object? CreateAndInitializeConfigMap(Type configMapType, Document doc)
    {
        try
        {
            object? configMap = null;
            var createMethod = configMapType.GetMethod("Create", new[] { typeof(Document) });
            if (createMethod != null && createMethod.IsStatic)
            {
                configMap = createMethod.Invoke(null, new object[] { doc });
                System.Diagnostics.Debug.WriteLine("Created IFCExportConfigurationsMap using Create(Document)");
            }
            else
            {
                configMap = Activator.CreateInstance(configMapType);
                System.Diagnostics.Debug.WriteLine("Created IFCExportConfigurationsMap using constructor");
            }

            if (configMap != null)
            {
                // Add built-in configurations
                var addBuiltInMethod = configMapType.GetMethod("AddBuiltInConfigurations");
                if (addBuiltInMethod != null)
                {
                    addBuiltInMethod.Invoke(configMap, null);
                    System.Diagnostics.Debug.WriteLine("Called AddBuiltInConfigurations()");
                }

                // Add saved configurations
                var addSavedMethodNoParams = configMapType.GetMethod("AddSavedConfigurations", Type.EmptyTypes);
                var addSavedMethodWithDoc = configMapType.GetMethod("AddSavedConfigurations", new[] { typeof(Document) });
                
                if (addSavedMethodNoParams != null)
                {
                    SetIFCCommandOverrideDocument(doc);
                    addSavedMethodNoParams.Invoke(configMap, null);
                    System.Diagnostics.Debug.WriteLine("Called AddSavedConfigurations() (parameterless)");
                }
                else if (addSavedMethodWithDoc != null)
                {
                    addSavedMethodWithDoc.Invoke(configMap, new object[] { doc });
                    System.Diagnostics.Debug.WriteLine("Called AddSavedConfigurations(Document)");
                }
            }
            
            return configMap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error creating/configuring IFCExportConfigurationsMap: {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// External event handler for selecting elements in Revit by ElementId
/// </summary>
public class SelectElementEventHandler : IExternalEventHandler
{
    public int ElementId { get; set; }
    public TaskCompletionSource<bool>? CompletionSource { get; set; }

    public void Execute(UIApplication app)
    {
        try
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null)
            {
                CompletionSource?.SetResult(false);
                return;
            }

            var doc = uidoc.Document;
            var elementId = new ElementId((long)ElementId);
            var element = doc.GetElement(elementId);
            if (element == null)
            {
                CompletionSource?.SetResult(false);
                return;
            }

            var selection = uidoc.Selection;
            selection.SetElementIds(new[] { elementId });
            uidoc.ShowElements(new[] { elementId });

            CompletionSource?.SetResult(true);
        }
        catch
        {
            CompletionSource?.SetResult(false);
        }
    }

    public string GetName() => "SelectElement";
}

/// <summary>
/// External event handler for selecting elements in Revit by IfcGUID
/// </summary>
public class SelectElementByGuidEventHandler : IExternalEventHandler
{
    public string IfcGuid { get; set; } = string.Empty;
    public TaskCompletionSource<bool>? CompletionSource { get; set; }

    public void Execute(UIApplication app)
    {
        try
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null)
            {
                CompletionSource?.SetResult(false);
                return;
            }

            var doc = uidoc.Document;
            
            // Search for element by IfcGUID parameter
            Element? foundElement = null;
            
            // Try to find element by IfcGUID parameter
            using (var collector = new FilteredElementCollector(doc))
            {
                var allElements = collector.WhereElementIsNotElementType().ToElements();
                
                foreach (var element in allElements)
                {
                    try
                    {
                        // Check IfcGUID parameter (built-in parameter)
                        var ifcGuidParam = element.get_Parameter(BuiltInParameter.IFC_GUID);
                        if (ifcGuidParam != null && ifcGuidParam.HasValue)
                        {
                            var guidValue = ifcGuidParam.AsString();
                            if (!string.IsNullOrEmpty(guidValue) && 
                                string.Equals(guidValue, IfcGuid, StringComparison.OrdinalIgnoreCase))
                            {
                                foundElement = element;
                                break;
                            }
                        }
                        
                        // Check if there's a shared parameter called "IfcGUID" or "GlobalId"
                        foreach (Parameter param in element.Parameters)
                        {
                            if (param != null && param.HasValue)
                            {
                                var paramName = param.Definition?.Name ?? "";
                                if (string.Equals(paramName, "IfcGUID", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(paramName, "GlobalId", StringComparison.OrdinalIgnoreCase))
                                {
                                    var guidValue = param.AsString();
                                    if (!string.IsNullOrEmpty(guidValue) && 
                                        string.Equals(guidValue, IfcGuid, StringComparison.OrdinalIgnoreCase))
                                    {
                                        foundElement = element;
                                        break;
                                    }
                                }
                            }
                        }
                        
                        if (foundElement != null) break;
                    }
                    catch
                    {
                        // Continue searching if parameter access fails
                        continue;
                    }
                }
            }
            
            if (foundElement == null)
            {
                CompletionSource?.SetResult(false);
                return;
            }

            var selection = uidoc.Selection;
            selection.SetElementIds(new[] { foundElement.Id });
            uidoc.ShowElements(new[] { foundElement.Id });

            CompletionSource?.SetResult(true);
        }
        catch
        {
            CompletionSource?.SetResult(false);
        }
    }

    public string GetName() => "SelectElementByGuid";
}

/// <summary>
/// External event handler for getting IFC export configurations
/// </summary>
public class GetIfcConfigurationsEventHandler : IExternalEventHandler
{
    public List<string> Configurations { get; set; } = new();
    public TaskCompletionSource<bool>? CompletionSource { get; set; }

    public void Execute(UIApplication app)
    {
        try
        {
            Configurations.Clear();
            
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
            {
                // Fallback to defaults if no document
            Configurations.Add("Default");
            Configurations.Add("IFC2x3");
            Configurations.Add("IFC4");
                CompletionSource?.SetResult(true);
                return;
            }

            // FIRST: Try IFCExportOptionsManager (Revit 2021+) - this is faster and more reliable
            System.Diagnostics.Debug.WriteLine("GetIfcConfigurationsEventHandler: Starting configuration retrieval...");
            try
            {
                System.Diagnostics.Debug.WriteLine("Trying IFCExportOptionsManager first...");
                var optionsManagerType = typeof(Document).Assembly.GetType("Autodesk.Revit.DB.IFC.IFCExportOptionsManager");
                if (optionsManagerType != null)
                {
                    System.Diagnostics.Debug.WriteLine("Found IFCExportOptionsManager type");
                    var getManagerMethod = optionsManagerType.GetMethod("GetIFCExportOptionsManager", 
                        BindingFlags.Public | BindingFlags.Static, 
                        null, 
                        new[] { typeof(Document) }, 
                        null);
                    
                    if (getManagerMethod != null)
                    {
                        System.Diagnostics.Debug.WriteLine("Found GetIFCExportOptionsManager method");
                        var manager = getManagerMethod.Invoke(null, new object[] { doc });
                        if (manager != null)
                        {
                            System.Diagnostics.Debug.WriteLine("Got IFCExportOptionsManager instance");
                            var getSetupNamesMethod = optionsManagerType.GetMethod("GetSetupNames");
                            if (getSetupNamesMethod != null)
                            {
                                System.Diagnostics.Debug.WriteLine("Found GetSetupNames method");
                                var setupNames = getSetupNamesMethod.Invoke(manager, null) as IList<string>;
                                if (setupNames != null && setupNames.Count > 0)
                                {
                                    System.Diagnostics.Debug.WriteLine($"GetSetupNames returned {setupNames.Count} names");
                                    foreach (var name in setupNames)
                                    {
                                        if (!string.IsNullOrEmpty(name))
                                        {
                                            Configurations.Add(name);
                                        }
                                    }
                                    System.Diagnostics.Debug.WriteLine($"Found {Configurations.Count} IFC export configurations via IFCExportOptionsManager");
                                    CompletionSource?.SetResult(true);
                                    return; // Success! Return early
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine("GetSetupNames returned null or empty list");
                                }
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine("GetSetupNames method not found");
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("GetIFCExportOptionsManager returned null");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("GetIFCExportOptionsManager method not found");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("IFCExportOptionsManager type not found");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"IFCExportOptionsManager error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            // SECOND: Try accessing IFC export setups through document methods
            if (Configurations.Count == 0)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("Trying to access IFC export setups through document methods...");
                    
                    // Try GetIFCExportSetups method if it exists
                    var getSetupsMethod = doc.GetType().GetMethod("GetIFCExportSetups", 
                        BindingFlags.Public | BindingFlags.Instance);
                    
                    if (getSetupsMethod != null)
                    {
                        System.Diagnostics.Debug.WriteLine("Found GetIFCExportSetups method");
                        var setups = getSetupsMethod.Invoke(doc, null);
                        if (setups != null)
                        {
                            var enumerable = setups as System.Collections.IEnumerable;
                            if (enumerable != null)
                            {
                                foreach (var setup in enumerable)
                                {
                                    try
                                    {
                                        var nameProperty = setup.GetType().GetProperty("Name");
                                        var name = nameProperty?.GetValue(setup) as string;
                                        if (!string.IsNullOrEmpty(name))
                                        {
                                            Configurations.Add(name);
                                        }
                                    }
                                    catch { }
                                }
                                if (Configurations.Count > 0)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Found {Configurations.Count} IFC export setups via GetIFCExportSetups");
                                    CompletionSource?.SetResult(true);
                                    return;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"GetIFCExportSetups error: {ex.Message}");
                }
            }

            // THIRD: Try IFCExportConfigurationsMap (official IFC exporter approach) - only if previous methods failed
            if (Configurations.Count == 0)
            {
                try
                {
                    var configMapType = IFCExportHelper.FindIFCExportConfigurationsMapType();
                    if (configMapType != null)
                    {
                        var configMap = IFCExportHelper.CreateAndInitializeConfigMap(configMapType, doc);
                        if (configMap != null)
                        {
                            var valuesProperty = configMapType.GetProperty("Values");
                            if (valuesProperty != null)
                            {
                                var values = valuesProperty.GetValue(configMap) as System.Collections.IEnumerable;
                                if (values != null)
                                {
                                    foreach (var config in values)
                                    {
                                        try
                                        {
                                            var nameProperty = config.GetType().GetProperty("Name");
                                            var name = nameProperty?.GetValue(config) as string;
                                            if (!string.IsNullOrEmpty(name) && !Configurations.Contains(name))
                                            {
                                                Configurations.Add(name);
                                            }
                                        }
                                        catch { }
                                    }
                                    System.Diagnostics.Debug.WriteLine($"Found {Configurations.Count} IFC export configurations via IFCExportConfigurationsMap");
                                    if (Configurations.Count > 0)
                                    {
                                        CompletionSource?.SetResult(true);
                                        return; // Success!
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"IFCExportConfigurationsMap error: {ex.Message}");
                }
            }

            // If still no configurations found, add standard defaults
            if (Configurations.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("No IFC configurations found, using defaults");
                Configurations.Add("Default");
                Configurations.Add("IFC2x3 Coordination View");
                Configurations.Add("IFC2x3 Coordination View 2.0");
                Configurations.Add("IFC4 Reference View");
                Configurations.Add("IFC4 Design Transfer View");
            }

            CompletionSource?.SetResult(true);
        }
        catch (Exception ex)
        {
            // Even on error, provide default configurations
            Configurations.Clear();
            Configurations.Add("Default");
            Configurations.Add("IFC2x3 Coordination View");
            Configurations.Add("IFC4 Reference View");
            System.Diagnostics.Debug.WriteLine($"Error in GetIfcConfigurationsEventHandler: {ex.Message}");
            CompletionSource?.SetResult(true); // Still return true since we have defaults
        }
    }

    public string GetName() => "GetIfcConfigurations";
}

/// <summary>
/// External event handler for exporting IFC
/// </summary>
public class ExportIfcEventHandler : IExternalEventHandler
{
    public string ConfigurationName { get; set; } = string.Empty;
    public string? OutputFilePath { get; set; }
    public TaskCompletionSource<bool>? CompletionSource { get; set; }

    public void Execute(UIApplication app)
    {
        try
        {
            // Create temporary file path - ALWAYS set this first
            var tempDir = Path.Combine(Path.GetTempPath(), "IfcTesterRevit");
            Directory.CreateDirectory(tempDir);
            
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null)
            {
                // Set output path even on error so we can report it
                OutputFilePath = Path.Combine(tempDir, $"Export_Error_{DateTime.Now:yyyyMMdd_HHmmss}.ifc");
                CompletionSource?.SetResult(false);
                return;
            }

            var doc = uidoc.Document;
            var view = uidoc.ActiveView;

            var fileName = $"Export_{doc.Title}_{DateTime.Now:yyyyMMdd_HHmmss}.ifc";
            OutputFilePath = Path.Combine(tempDir, fileName);

            System.Diagnostics.Debug.WriteLine($"ExportIfcEventHandler: Starting export with configuration '{ConfigurationName}' to '{OutputFilePath}'");

            // Get IFC export options from the named configuration
            IFCExportOptions? options = null;
            
            try
            {
                var configMapType = IFCExportHelper.FindIFCExportConfigurationsMapType();
                if (configMapType != null)
                {
                    var configMap = IFCExportHelper.CreateAndInitializeConfigMap(configMapType, doc);
                    if (configMap != null)
                    {
                        // Get the configuration by name (using indexer)
                        var indexer = configMapType.GetProperty("Item", new[] { typeof(string) });
                        if (indexer != null)
                        {
                            object? selectedConfig = indexer.GetValue(configMap, new object[] { ConfigurationName });
                            
                            // Try case-insensitive search if direct lookup fails
                            if (selectedConfig == null)
                            {
                                var valuesProperty = configMapType.GetProperty("Values");
                                if (valuesProperty != null)
                                {
                                    var allConfigs = valuesProperty.GetValue(configMap) as System.Collections.IEnumerable;
                                    if (allConfigs != null)
                                    {
                                        foreach (var config in allConfigs)
                                        {
                                            try
                                            {
                                                var nameProperty = config.GetType().GetProperty("Name");
                                                var name = nameProperty?.GetValue(config) as string;
                                                if (!string.IsNullOrEmpty(name) && 
                                                    string.Equals(name, ConfigurationName, StringComparison.OrdinalIgnoreCase))
                                                {
                                                    selectedConfig = config;
                                                    break;
                                                }
                                            }
                                            catch { }
                                        }
                                    }
                                }
                            }
                            
                            if (selectedConfig != null)
                            {
                                // Duplicate the configuration
                                var duplicateMethod = selectedConfig.GetType().GetMethod("Duplicate", new[] { typeof(string) });
                                object? duplicatedConfig = null;
                                
                                if (duplicateMethod != null)
                                {
                                    // Duplicate method requires a name parameter
                                    duplicatedConfig = duplicateMethod.Invoke(selectedConfig, new object[] { "TempExportConfig" });
                                }
                                else
                                {
                                    // Try parameterless version as fallback
                                    var duplicateMethodNoParams = selectedConfig.GetType().GetMethod("Duplicate", Type.EmptyTypes);
                                    if (duplicateMethodNoParams != null)
                                    {
                                        duplicatedConfig = duplicateMethodNoParams.Invoke(selectedConfig, null);
                                    }
                                    else
                                    {
                                        // If no Duplicate method exists, use the config directly
                                        duplicatedConfig = selectedConfig;
                                    }
                                }
                                
                                if (duplicatedConfig != null)
                                {
                                    // Set VisibleElementsOfCurrentView to true
                                    var visibleProperty = duplicatedConfig.GetType().GetProperty("VisibleElementsOfCurrentView");
                                    if (visibleProperty != null && visibleProperty.CanWrite)
                                    {
                                        visibleProperty.SetValue(duplicatedConfig, true);
                                    }
                                    
                                    // Update options with the view
                                    var updateOptionsMethod = duplicatedConfig.GetType().GetMethod("UpdateOptions", new[] { typeof(IFCExportOptions), typeof(ElementId) });
                                    if (updateOptionsMethod != null)
                                    {
                                        options = new IFCExportOptions();
                                        updateOptionsMethod.Invoke(duplicatedConfig, new object[] { options, view.Id });
                                        System.Diagnostics.Debug.WriteLine($"Retrieved and applied IFC export configuration '{ConfigurationName}' via IFCExportConfigurationsMap");
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception configEx)
            {
                System.Diagnostics.Debug.WriteLine($"Error retrieving configuration '{ConfigurationName}': {configEx.Message}");
            }
            
            // Fallback: Try IFCExportOptionsManager
            if (options == null)
            {
                try
                {
                    var optionsManagerType = typeof(Document).Assembly.GetType("Autodesk.Revit.DB.IFC.IFCExportOptionsManager");
                    if (optionsManagerType != null)
                    {
                        var getManagerMethod = optionsManagerType.GetMethod("GetIFCExportOptionsManager", 
                            BindingFlags.Public | BindingFlags.Static, 
                            null, 
                            new[] { typeof(Document) }, 
                            null);
                        
                        if (getManagerMethod != null)
                        {
                            var manager = getManagerMethod.Invoke(null, new object[] { doc });
                            if (manager != null)
                            {
                                var getOptionsMethod = optionsManagerType.GetMethod("GetIFCExportOptions", 
                                    new[] { typeof(string) });
                                
                                if (getOptionsMethod != null)
                                {
                                    var configOptions = getOptionsMethod.Invoke(manager, new object[] { ConfigurationName });
                                    if (configOptions != null && configOptions is IFCExportOptions)
                                    {
                                        options = new IFCExportOptions((IFCExportOptions)configOptions);
                                        options.FilterViewId = view.Id;
                                        System.Diagnostics.Debug.WriteLine($"Retrieved IFC export configuration '{ConfigurationName}' via IFCExportOptionsManager");
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Could not get IFC configuration via IFCExportOptionsManager: {ex.Message}");
                }
            }

            // If still no configuration found, create default options based on name
            if (options == null)
            {
                System.Diagnostics.Debug.WriteLine($"Configuration '{ConfigurationName}' not found, creating default options");
                options = new IFCExportOptions();
                
                // Apply configuration based on name patterns
            if (ConfigurationName.Contains("IFC2x3", StringComparison.OrdinalIgnoreCase))
            {
                options.FileVersion = IFCVersion.IFC2x3;
            }
            else if (ConfigurationName.Contains("IFC4", StringComparison.OrdinalIgnoreCase))
            {
                options.FileVersion = IFCVersion.IFC4;
            }
                else if (ConfigurationName.Contains("IFC2X2", StringComparison.OrdinalIgnoreCase))
                {
                    options.FileVersion = IFCVersion.IFC2x2;
                }
                
                // Set the filter view to the active view
                options.FilterViewId = view.Id;
            }
            else if (options.FilterViewId == ElementId.InvalidElementId)
            {
                // Ensure FilterViewId is set if not already set by the configuration
                options.FilterViewId = view.Id;
            }

            // Export IFC
            // NOTE: The exact IFC export API may vary by Revit version and IFC exporter add-in
            // This implementation uses a basic approach that should work with standard Revit installations
            // For production use, you may need to adjust this based on your specific Revit/IFC exporter version
            
            var directory = Path.GetDirectoryName(OutputFilePath)!;
            var filename = Path.GetFileName(OutputFilePath)!;
            
            // Try to export using reflection to find the correct method signature
            // The IFC export API varies by Revit version
            bool exportSucceeded = false;
            Exception? lastException = null;
            
            // First, try to find all Export methods to see what's available
            var allExportMethods = doc.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == "Export")
                .ToList();
            
            System.Diagnostics.Debug.WriteLine($"Found {allExportMethods.Count} Export methods");
            foreach (var method in allExportMethods)
            {
                var paramTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();
                System.Diagnostics.Debug.WriteLine($"Export method with parameters: {string.Join(", ", paramTypes.Select(t => t.Name))}");
            }
            
            // Try method signature: Export(string directory, string filename, IList<ElementId> viewIds, IFCExportOptions options)
            // This is the most common signature for IFC export
            if (!exportSucceeded)
            {
                try
                {
                    var viewIds = new List<ElementId> { view.Id };
                    var exportMethod = doc.GetType().GetMethod("Export", new[] { typeof(string), typeof(string), typeof(IList<ElementId>), typeof(IFCExportOptions) });
                    if (exportMethod != null)
                    {
                        System.Diagnostics.Debug.WriteLine("Trying Export(string, string, IList<ElementId>, IFCExportOptions)");
                        exportMethod.Invoke(doc, new object[] { directory, filename, viewIds, options });
                        // Wait a moment for file to be written
                        System.Threading.Thread.Sleep(500);
                        exportSucceeded = File.Exists(OutputFilePath);
                        if (exportSucceeded)
                        {
                            System.Diagnostics.Debug.WriteLine("IFC export succeeded using Export(string, string, IList<ElementId>, IFCExportOptions)");
                        }
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    System.Diagnostics.Debug.WriteLine($"Export method with IList<ElementId> failed: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Inner exception: {ex.InnerException.Message}");
                    }
                }
            }
            
            // Try method signature: Export(string directory, string filename, List<ElementId> viewIds, IFCExportOptions options)
            if (!exportSucceeded)
            {
                try
                {
                    var viewIds = new List<ElementId> { view.Id };
                    var exportMethod = doc.GetType().GetMethod("Export", new[] { typeof(string), typeof(string), typeof(List<ElementId>), typeof(IFCExportOptions) });
                    if (exportMethod != null)
                    {
                        System.Diagnostics.Debug.WriteLine("Trying Export(string, string, List<ElementId>, IFCExportOptions)");
                        exportMethod.Invoke(doc, new object[] { directory, filename, viewIds, options });
                        System.Threading.Thread.Sleep(500);
                        exportSucceeded = File.Exists(OutputFilePath);
                        if (exportSucceeded)
                        {
                            System.Diagnostics.Debug.WriteLine("IFC export succeeded using Export(string, string, List<ElementId>, IFCExportOptions)");
                        }
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    System.Diagnostics.Debug.WriteLine($"Export method with List<ElementId> failed: {ex.Message}");
                }
            }
            
            // Try method signature: Export(string fullPath, IFCExportOptions options)
            if (!exportSucceeded)
            {
                try
                {
                    var exportMethod = doc.GetType().GetMethod("Export", new[] { typeof(string), typeof(IFCExportOptions) });
                    if (exportMethod != null)
                    {
                        System.Diagnostics.Debug.WriteLine("Trying Export(string, IFCExportOptions)");
                        exportMethod.Invoke(doc, new object[] { OutputFilePath, options });
                        System.Threading.Thread.Sleep(500);
                        exportSucceeded = File.Exists(OutputFilePath);
                        if (exportSucceeded)
                        {
                            System.Diagnostics.Debug.WriteLine("IFC export succeeded using Export(string, IFCExportOptions)");
                        }
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    System.Diagnostics.Debug.WriteLine($"Export method with string+IFCExportOptions failed: {ex.Message}");
                }
            }
            
            // Try method signature: Export(string directory, string filename, IFCExportOptions options)
            if (!exportSucceeded)
            {
                try
                {
                    var exportMethod = doc.GetType().GetMethod("Export", new[] { typeof(string), typeof(string), typeof(IFCExportOptions) });
                    if (exportMethod != null)
                    {
                        System.Diagnostics.Debug.WriteLine("Trying Export(string, string, IFCExportOptions)");
                        System.Diagnostics.Debug.WriteLine($"Directory: {directory}");
                        System.Diagnostics.Debug.WriteLine($"Filename: {filename}");
                        System.Diagnostics.Debug.WriteLine($"IFCExportOptions FileVersion: {options.FileVersion}");
                        System.Diagnostics.Debug.WriteLine($"IFCExportOptions FilterViewId: {options.FilterViewId}");
                        
                        // Ensure directory exists
                        if (!Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                            System.Diagnostics.Debug.WriteLine($"Created directory: {directory}");
                        }
                        
                        // Document.Export requires a transaction (even though it's read-only)
                        using (var trans = new Transaction(doc, "IFC Export"))
                        {
                            trans.Start();
                            try
                            {
                        exportMethod.Invoke(doc, new object[] { directory, filename, options });
                                trans.Commit();
                                System.Diagnostics.Debug.WriteLine("IFC export transaction committed successfully");
                            }
                            catch (Exception transEx)
                            {
                                trans.RollBack();
                                System.Diagnostics.Debug.WriteLine($"IFC export transaction rolled back: {transEx.Message}");
                                throw;
                            }
                        }
                        
                        System.Threading.Thread.Sleep(1000); // Increased wait time
                        exportSucceeded = File.Exists(OutputFilePath);
                        if (exportSucceeded)
                        {
                            System.Diagnostics.Debug.WriteLine("IFC export succeeded using Export(string, string, IFCExportOptions)");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Export method returned but file not found at: {OutputFilePath}");
                            // Check if any IFC files were created in the directory
                            var ifcFiles = Directory.GetFiles(directory, "*.ifc");
                            System.Diagnostics.Debug.WriteLine($"Found {ifcFiles.Length} IFC files in directory");
                            foreach (var file in ifcFiles)
                            {
                                System.Diagnostics.Debug.WriteLine($"  - {file}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    System.Diagnostics.Debug.WriteLine($"Export method with string+string+IFCExportOptions failed: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"Exception type: {ex.GetType().FullName}");
                    if (ex.InnerException != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Inner exception: {ex.InnerException.Message}");
                        System.Diagnostics.Debug.WriteLine($"Inner exception type: {ex.InnerException.GetType().FullName}");
                        if (ex.InnerException.StackTrace != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"Inner exception stack trace: {ex.InnerException.StackTrace}");
                        }
                    }
                    if (ex.StackTrace != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Outer exception stack trace: {ex.StackTrace}");
                    }
                }
            }
            
            if (!exportSucceeded)
            {
                var errorMsg = lastException != null 
                    ? $"IFC export failed: {lastException.Message}. Please ensure IFC exporter add-in is installed."
                    : "IFC export method not found. Please ensure IFC exporter add-in is installed.";
                throw new InvalidOperationException(errorMsg);
            }
            
            // Verify file was created
            if (!File.Exists(OutputFilePath))
            {
                throw new InvalidOperationException($"IFC file was not created at: {OutputFilePath}");
            }

            CompletionSource?.SetResult(true);
        }
        catch (Exception ex)
        {
            // Ensure OutputFilePath is set even on error
            if (string.IsNullOrEmpty(OutputFilePath))
            {
                var errorTempDir = Path.Combine(Path.GetTempPath(), "WebAecRevit");
                Directory.CreateDirectory(errorTempDir);
                OutputFilePath = Path.Combine(errorTempDir, $"Export_Error_{DateTime.Now:yyyyMMdd_HHmmss}.ifc");
            }
            
            System.Diagnostics.Debug.WriteLine($"ExportIfcEventHandler error: {ex.Message}\n{ex.StackTrace}");
            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"Inner exception: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}");
            }
            CompletionSource?.SetResult(false);
        }
    }

    public string GetName() => "ExportIfc";
}

/// <summary>
/// HTTP server for Revit API communication with WebView2
/// Implements SelectServer API endpoints
/// </summary>
public class RevitApiServer : IDisposable
{
    private HttpListener? _listener;
    private readonly string _baseUrl;
    private readonly int _port;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isRunning;
    private UIApplication? _uiApplication;
    private ExternalEvent? _externalEvent;
    private SelectElementEventHandler? _eventHandler;
    private ExternalEvent? _externalEventByGuid;
    private SelectElementByGuidEventHandler? _eventHandlerByGuid;
    private ExternalEvent? _externalEventGetIfcConfigs;
    private GetIfcConfigurationsEventHandler? _eventHandlerGetIfcConfigs;
    private ExternalEvent? _externalEventExportIfc;
    private ExportIfcEventHandler? _eventHandlerExportIfc;
    private bool _configsLoaded = false;
    private readonly object _configsLock = new object();

    public RevitApiServer(int port = 48881)
    {
        _port = port;
        _baseUrl = $"http://localhost:{port}/";
    }

    public bool IsRunning => _isRunning;

    public void Start(UIApplication uiApplication)
    {
        if (_isRunning)
        {
            return;
        }

        _uiApplication = uiApplication;
        _cancellationTokenSource = new CancellationTokenSource();

        // Create external events for Revit API calls
        _eventHandler = new SelectElementEventHandler();
        _externalEvent = ExternalEvent.Create(_eventHandler);
        
        _eventHandlerByGuid = new SelectElementByGuidEventHandler();
        _externalEventByGuid = ExternalEvent.Create(_eventHandlerByGuid);
        
        _eventHandlerGetIfcConfigs = new GetIfcConfigurationsEventHandler();
        _externalEventGetIfcConfigs = ExternalEvent.Create(_eventHandlerGetIfcConfigs);
        
        _eventHandlerExportIfc = new ExportIfcEventHandler();
        _externalEventExportIfc = ExternalEvent.Create(_eventHandlerExportIfc);

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(_baseUrl);
            _listener.Start();
            _isRunning = true;

            // Start listening for requests in background
            Task.Run(() => ListenForRequests(_cancellationTokenSource.Token));

            System.Diagnostics.Debug.WriteLine($"Revit API Server started on {_baseUrl}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to start Revit API Server: {ex.Message}");
            _isRunning = false;
        }
    }

    public void Stop()
    {
        if (!_isRunning)
        {
            return;
        }

        _cancellationTokenSource?.Cancel();
        _listener?.Stop();
        _isRunning = false;
        System.Diagnostics.Debug.WriteLine("Revit API Server stopped");
    }

    private async Task ListenForRequests(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(context, cancellationToken));
            }
            catch (HttpListenerException)
            {
                // Listener was closed
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error accepting request: {ex.Message}");
            }
        }
    }

    private async Task HandleRequest(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;

        // Enable CORS
        response.AddHeader("Access-Control-Allow-Origin", "*");
        response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.AddHeader("Access-Control-Allow-Headers", "Content-Type");

        // Handle preflight OPTIONS request
        if (request.HttpMethod == "OPTIONS")
        {
            response.StatusCode = 200;
            response.Close();
            return;
        }

        try
        {
            var path = request.Url?.AbsolutePath ?? "";
            var method = request.HttpMethod;

            System.Diagnostics.Debug.WriteLine($"Revit API Request: {method} {path}");

            if (path == "/status" && method == "GET")
            {
                await HandleStatus(response);
            }
            else if (path.StartsWith("/select-by-id/") && method == "GET")
            {
                var elementIdStr = path.Replace("/select-by-id/", "");
                if (int.TryParse(elementIdStr, out var elementId))
                {
                    await HandleSelectById(response, elementId);
                }
                else
                {
                    await SendError(response, 400, "Invalid element ID");
                }
            }
            else if (path.StartsWith("/select-by-guid/") && method == "GET")
            {
                var guid = path.Replace("/select-by-guid/", "");
                if (!string.IsNullOrEmpty(guid))
                {
                    // URL decode the GUID
                    guid = Uri.UnescapeDataString(guid);
                    await HandleSelectByGuid(response, guid);
                }
                else
                {
                    await SendError(response, 400, "Invalid GUID");
                }
            }
            else if (path == "/ifc-configurations" && method == "GET")
            {
                await HandleGetIfcConfigurations(response);
            }
            else if (path == "/export-ifc" && method == "POST")
            {
                await HandleExportIfc(request, response);
            }
            else
            {
                await SendError(response, 404, "Not Found");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error handling request: {ex.Message}");
            await SendError(response, 500, $"Internal Server Error: {ex.Message}");
        }
        finally
        {
            response.Close();
        }
    }

    private async Task HandleStatus(HttpListenerResponse response)
    {
        // Preload IFC configurations to ensure they're ready when status returns OK
        bool configsReady = false;
        
        lock (_configsLock)
        {
            // If configs are already loaded, return immediately
            if (_configsLoaded)
            {
                configsReady = true;
            }
        }
        
        // If not loaded yet, try to load them with retry logic
        if (!configsReady && _uiApplication != null && _externalEventGetIfcConfigs != null && _eventHandlerGetIfcConfigs != null)
        {
            int maxRetries = 3;
            int retryDelay = 1000; // Start with 1 second delay
            
            for (int retry = 0; retry < maxRetries && !configsReady; retry++)
            {
                try
                {
                    // Try to load configurations
                    var tcs = new TaskCompletionSource<bool>();
                    _eventHandlerGetIfcConfigs.CompletionSource = tcs;
                    _eventHandlerGetIfcConfigs.Configurations.Clear();
                    _externalEventGetIfcConfigs.Raise();

                    // Use longer timeout for first attempt (IFC assemblies may need to load)
                    int timeout = retry == 0 ? 10000 : 5000; // 10 seconds for first attempt, 5 for retries
                    var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
                    
                    if (completed == tcs.Task && await tcs.Task)
                    {
                        var configs = _eventHandlerGetIfcConfigs.Configurations;
                        configsReady = configs.Count > 0;
                        
                        if (configsReady)
                        {
                            lock (_configsLock)
                            {
                                _configsLoaded = true;
                            }
                            
                            System.Diagnostics.Debug.WriteLine($"Status check: Configs loaded successfully ({configs.Count} configurations) after {retry + 1} attempt(s)");
                            break;
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Status check: Config loading attempt {retry + 1} timed out (timeout: {timeout}ms)");
                        
                        // Wait before retry (exponential backoff)
                        if (retry < maxRetries - 1)
                        {
                            await Task.Delay(retryDelay);
                            retryDelay *= 2; // Exponential backoff
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Status check: Exception loading configs (attempt {retry + 1}): {ex.Message}");
                    
                    // Wait before retry
                    if (retry < maxRetries - 1)
                    {
                        await Task.Delay(retryDelay);
                        retryDelay *= 2;
                    }
                }
            }
            
            if (!configsReady)
            {
                System.Diagnostics.Debug.WriteLine("Status check: All config loading attempts failed or timed out");
            }
        }
        else if (!configsReady)
        {
            // If handlers aren't ready yet, wait a bit
            await Task.Delay(500);
            configsReady = false; // Return initializing status
        }

        var status = new
        {
            status = configsReady ? "ok" : "initializing",
            connected = true,
            configsReady = configsReady,
            version = "1.0.0"
        };

        var json = System.Text.Json.JsonSerializer.Serialize(status);
        await SendJson(response, json);
    }

    private async Task HandleSelectById(HttpListenerResponse response, int elementId)
    {
        if (_uiApplication == null || _externalEvent == null || _eventHandler == null)
        {
            await SendError(response, 503, "Revit application not available");
            return;
        }

        try
        {
            // Use a task completion source to handle async Revit API call
            var tcs = new TaskCompletionSource<bool>();
            
            // Set up the event handler
            _eventHandler.ElementId = elementId;
            _eventHandler.CompletionSource = tcs;
            
            // Raise the external event (this will execute on Revit's main thread)
            _externalEvent.Raise();
            
            // Wait for the event to complete (with timeout)
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
            
            if (completed == tcs.Task && await tcs.Task)
            {
                var result = new { success = true, message = $"Element {elementId} selected" };
                var json = System.Text.Json.JsonSerializer.Serialize(result);
                await SendJson(response, json);
            }
            else
            {
                await SendError(response, 500, $"Element {elementId} not found or selection failed");
            }
        }
        catch (Exception ex)
        {
            await SendError(response, 500, $"Failed to select element: {ex.Message}");
        }
    }

    private async Task HandleSelectByGuid(HttpListenerResponse response, string ifcGuid)
    {
        if (_uiApplication == null || _externalEventByGuid == null || _eventHandlerByGuid == null)
        {
            await SendError(response, 503, "Revit application not available");
            return;
        }

        try
        {
            // Use a task completion source to handle async Revit API call
            var tcs = new TaskCompletionSource<bool>();
            
            // Set up the event handler
            _eventHandlerByGuid.IfcGuid = ifcGuid;
            _eventHandlerByGuid.CompletionSource = tcs;
            
            // Raise the external event (this will execute on Revit's main thread)
            _externalEventByGuid.Raise();
            
            // Wait for the event to complete (with timeout)
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(10000)); // Longer timeout for GUID search
            
            if (completed == tcs.Task && await tcs.Task)
            {
                var result = new { success = true, message = $"Element with GUID {ifcGuid} selected" };
                var json = System.Text.Json.JsonSerializer.Serialize(result);
                await SendJson(response, json);
            }
            else
            {
                await SendError(response, 500, $"Element with GUID {ifcGuid} not found or selection failed");
            }
        }
        catch (Exception ex)
        {
            await SendError(response, 500, $"Failed to select element by GUID: {ex.Message}");
        }
    }

    private async Task SendJson(HttpListenerResponse response, string json)
    {
        response.ContentType = "application/json";
        response.StatusCode = 200;

        var buffer = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
    }

    private async Task HandleGetIfcConfigurations(HttpListenerResponse response)
    {
        if (_uiApplication == null || _externalEventGetIfcConfigs == null || _eventHandlerGetIfcConfigs == null)
        {
            await SendError(response, 503, "Revit application not available");
            return;
        }

        try
        {
            var tcs = new TaskCompletionSource<bool>();
            _eventHandlerGetIfcConfigs.CompletionSource = tcs;
            _eventHandlerGetIfcConfigs.Configurations.Clear(); // Clear before raising event
            _externalEventGetIfcConfigs.Raise();

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(10000)); // Increased timeout to 10 seconds
            
            if (completed == tcs.Task && await tcs.Task)
            {
                // Always return configurations, even if empty (shouldn't happen due to defaults)
                var configs = _eventHandlerGetIfcConfigs.Configurations;
                if (configs.Count == 0)
                {
                    // Fallback: provide defaults if somehow empty
                    configs.Add("Default");
                    configs.Add("IFC2x3");
                    configs.Add("IFC4");
                }
                
                var result = new { configurations = configs };
                var json = System.Text.Json.JsonSerializer.Serialize(result);
                await SendJson(response, json);
            }
            else
            {
                // Timeout or failure - still return default configurations
                System.Diagnostics.Debug.WriteLine("IFC configurations request timed out or failed, returning defaults");
                var defaultConfigs = new List<string> { "Default", "IFC2x3", "IFC4" };
                var result = new { configurations = defaultConfigs };
                var json = System.Text.Json.JsonSerializer.Serialize(result);
                await SendJson(response, json);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Exception in HandleGetIfcConfigurations: {ex.Message}\n{ex.StackTrace}");
            // Even on exception, return default configurations
            try
            {
                var defaultConfigs = new List<string> { "Default", "IFC2x3", "IFC4" };
                var result = new { configurations = defaultConfigs };
                var json = System.Text.Json.JsonSerializer.Serialize(result);
                await SendJson(response, json);
            }
            catch
            {
                await SendError(response, 500, $"Failed to get IFC configurations: {ex.Message}");
            }
        }
    }

    private async Task HandleExportIfc(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (_uiApplication == null || _externalEventExportIfc == null || _eventHandlerExportIfc == null)
        {
            await SendError(response, 503, "Revit application not available");
            return;
        }

        try
        {
            // Read request body
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var body = await reader.ReadToEndAsync();
            
            var requestData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(body);
            if (requestData == null || !requestData.ContainsKey("configuration"))
            {
                await SendError(response, 400, "Missing 'configuration' parameter");
                return;
            }

            var configName = requestData["configuration"];
            
            var tcs = new TaskCompletionSource<bool>();
            _eventHandlerExportIfc.ConfigurationName = configName;
            _eventHandlerExportIfc.CompletionSource = tcs;
            _externalEventExportIfc.Raise();

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(60000)); // 60 second timeout for export
            
            if (completed == tcs.Task && await tcs.Task && !string.IsNullOrEmpty(_eventHandlerExportIfc.OutputFilePath))
            {
                // Read the exported file and send it back
                if (File.Exists(_eventHandlerExportIfc.OutputFilePath))
                {
                    var fileBytes = await File.ReadAllBytesAsync(_eventHandlerExportIfc.OutputFilePath);
                    var fileName = Path.GetFileName(_eventHandlerExportIfc.OutputFilePath);
                    
                    response.ContentType = "application/octet-stream";
                    response.AddHeader("Content-Disposition", $"attachment; filename=\"{fileName}\"");
                    response.StatusCode = 200;
                    response.ContentLength64 = fileBytes.Length;
                    
                    await response.OutputStream.WriteAsync(fileBytes, 0, fileBytes.Length);
                    
                    // Clean up temporary file
                    try
                    {
                        File.Delete(_eventHandlerExportIfc.OutputFilePath);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
                else
                {
                    await SendError(response, 500, "IFC file was not created");
                }
            }
            else
            {
                var errorDetails = "Unknown error";
                if (_eventHandlerExportIfc.OutputFilePath == null)
                {
                    errorDetails = "Export handler did not set output file path";
                }
                else if (!File.Exists(_eventHandlerExportIfc.OutputFilePath))
                {
                    errorDetails = $"File was not created at: {_eventHandlerExportIfc.OutputFilePath}";
                }
                else
                {
                    errorDetails = "Export handler returned false";
                }
                
                System.Diagnostics.Debug.WriteLine($"IFC export failed: {errorDetails}");
                await SendError(response, 500, $"Failed to export IFC: {errorDetails}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"HandleExportIfc exception: {ex.Message}\n{ex.StackTrace}");
            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }
            await SendError(response, 500, $"Failed to export IFC: {ex.Message}");
        }
    }

    private async Task SendError(HttpListenerResponse response, int statusCode, string message)
    {
        var error = new { error = message };
        var json = System.Text.Json.JsonSerializer.Serialize(error);
        response.StatusCode = statusCode;
        response.ContentType = "application/json";

        var buffer = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
    }

    public void Dispose()
    {
        Stop();
        _listener?.Close();
        _cancellationTokenSource?.Dispose();
    }
}

