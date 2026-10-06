using System.Diagnostics;
using Autodesk.Revit.DB;

namespace IfcTesterRevit.Writeback;

/// <summary>
/// Read-only scans of the parameters on the model elements of a document and on their types,
/// for the pset builder: GET /model-parameters and POST /pset-suggestions. One pass over the
/// elements; per-parameter facts (name, origin, storage type) are worked out once per parameter
/// id and per-type parameter lists once per type. No view is touched.
/// </summary>
public sealed class ModelParameterScan
{
    private readonly Document _document;
    private readonly HashSet<long> _boundDefinitions = new();
    private readonly Dictionary<long, ParameterMeta?> _meta = new();
    private readonly Dictionary<long, List<ParameterHit>> _typeParameters = new();

    private ModelParameterScan(Document document)
    {
        _document = document;

        // Shared and project parameters bound to categories; a non-shared parameter outside this
        // map comes from a family.
        var iterator = document.ParameterBindings.ForwardIterator();
        while (iterator.MoveNext())
        {
            if (iterator.Key is InternalDefinition definition && definition.Id != ElementId.InvalidElementId)
            {
                _boundDefinitions.Add(definition.Id.Value);
            }
        }
    }

    /// <summary>
    /// GET /model-parameters: every distinct parameter of the model elements and their types, plus
    /// the shared and project parameters bound to categories that no scanned element carries yet.
    /// </summary>
    public static ModelParametersResponse Scan(Document? document)
    {
        if (document == null) return new ModelParametersResponse { Message = "No model is open in Revit." };

        var watch = Stopwatch.StartNew();
        var scan = new ModelParameterScan(document);
        var elements = ModelElements(document);
        var entries = new Dictionary<string, Aggregate>(StringComparer.Ordinal);
        var types = new HashSet<long>();

        for (var index = 0; index < elements.Count; index++)
        {
            var element = elements[index];
            var categoryId = element.Category?.Id.Value ?? 0;

            foreach (var hit in scan.InstanceParameters(element))
            {
                entries.TryGetValue(hit.Meta.AggregateKey, out var entry);
                entry ??= entries[hit.Meta.AggregateKey] = new Aggregate(hit.Meta);
                entry.Add(index, categoryId, hit, onType: false);
            }

            var typeId = element.GetTypeId();
            if (typeId == ElementId.InvalidElementId) continue;
            types.Add(typeId.Value);
            foreach (var hit in scan.TypeParameters(typeId))
            {
                entries.TryGetValue(hit.Meta.AggregateKey, out var entry);
                entry ??= entries[hit.Meta.AggregateKey] = new Aggregate(hit.Meta);
                entry.Add(index, categoryId, hit, onType: true);
            }
        }

        var categoryNames = CategoryNames(document);
        var response = new ModelParametersResponse
        {
            Document = document.Title,
            ElementCount = elements.Count,
            TypeCount = types.Count,
            Parameters = entries.Values
                .Select(e => e.ToInfo(categoryNames))
                .Concat(scan.BoundWithoutElements())
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Origin, StringComparer.Ordinal)
                .ToList()
        };
        response.ElapsedMs = watch.ElapsedMilliseconds;
        return response;
    }

    /// <summary>
    /// POST /pset-suggestions: for each IFC property, the Revit parameters the exporter would read
    /// it from by name, best first. Never guesses beyond the exporter's own name matching.
    /// </summary>
    public static PsetSuggestionResponse Suggest(Document? document, PsetSuggestionRequest request)
    {
        var items = request.Items ?? new List<PsetSuggestionItem>();
        if (document == null)
        {
            return new PsetSuggestionResponse { Message = "No model is open in Revit.", ScopeMethod = "" };
        }

        var watch = Stopwatch.StartNew();
        var scan = new ModelParameterScan(document);
        var elements = ModelElements(document);
        var classifier = new IfcClassifier(document);
        var classes = elements.Select(classifier.ClassKey).ToArray();

        // Which items each IFC class serves, and which items find no element and scan everything
        var itemKeys = items.Select(i => new HashSet<string>((i.Entities ?? new List<string>())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(PsetNames.OccurrenceKey), StringComparer.Ordinal)).ToList();
        var matched = new int[items.Count];
        foreach (var classKey in classes)
        {
            if (classKey == null) continue;
            for (var i = 0; i < items.Count; i++)
            {
                if (itemKeys[i].Contains(classKey)) matched[i]++;
            }
        }
        var scanAll = Enumerable.Range(0, items.Count).Where(i => matched[i] == 0).ToList();
        var present = new HashSet<string>(classes.Where(c => c != null).Select(c => c!), StringComparer.Ordinal);
        var itemsByClass = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < items.Count; i++)
        {
            if (matched[i] == 0) continue;
            foreach (var key in itemKeys[i])
            {
                if (!itemsByClass.TryGetValue(key, out var list)) itemsByClass[key] = list = new List<int>();
                list.Add(i);
            }
        }

        // Parameter names wanted, as the exporter compares them, and which item and rule each serves
        var wanted = new Dictionary<string, List<(int Item, string Rule)>>(StringComparer.Ordinal);
        void Want(string name, int item, string rule)
        {
            var key = PsetNames.ParameterKey(name);
            if (!wanted.TryGetValue(key, out var list)) wanted[key] = list = new List<(int, string)>();
            if (!list.Contains((item, rule))) list.Add((item, rule));
        }
        for (var i = 0; i < items.Count; i++)
        {
            var name = (items[i].Name ?? "").Trim();
            if (name.Length == 0) continue;
            Want(name, i, PsetSuggestionRule.Name);
            var pset = (items[i].PropertySet ?? "").Trim();
            if (pset.Length > 0) Want($"{pset}.{name}", i, PsetSuggestionRule.PsetName);
        }

        var scanned = new int[items.Count];
        var found = new Dictionary<string, SuggestionAggregate>[items.Count];
        for (var i = 0; i < items.Count; i++) found[i] = new Dictionary<string, SuggestionAggregate>(StringComparer.Ordinal);

        var relevant = new List<int>();
        for (var index = 0; index < elements.Count; index++)
        {
            relevant.Clear();
            relevant.AddRange(scanAll);
            if (classes[index] != null && itemsByClass.TryGetValue(classes[index]!, out var forClass)) relevant.AddRange(forClass);
            if (relevant.Count == 0) continue;
            foreach (var i in relevant) scanned[i]++;

            // Values are read only for parameters whose name is wanted
            var element = elements[index];
            foreach (var (parameter, meta) in scan.Visible(element))
            {
                if (Targets(meta, WritebackScope.Instance) is { } targets)
                {
                    Add(new ParameterHit(meta, HasValue(parameter, meta.Storage)), targets, WritebackScope.Instance);
                }
            }
            var typeId = element.GetTypeId();
            if (typeId == ElementId.InvalidElementId) continue;
            foreach (var hit in scan.TypeParameters(typeId))
            {
                if (Targets(hit.Meta, WritebackScope.Type) is { } targets) Add(hit, targets, WritebackScope.Type);
            }

            void Add(ParameterHit hit, List<(int Item, string Rule)> targets, string scope)
            {
                foreach (var (item, rule) in targets)
                {
                    if (!relevant.Contains(item)) continue;
                    var aggregateKey = $"{scope}|{rule}|{hit.Meta.AggregateKey}";
                    if (!found[item].TryGetValue(aggregateKey, out var aggregate))
                    {
                        found[item][aggregateKey] = aggregate = new SuggestionAggregate(hit.Meta, scope, rule);
                    }
                    aggregate.Add(index, hit);
                }
            }
        }

        List<(int Item, string Rule)>? Targets(ParameterMeta meta, string scope)
        {
            // A type parameter named "<Name>[Type]" is read for the type, as the resolver does
            var key = meta.ExporterKey;
            if (scope == WritebackScope.Type && key.EndsWith("[TYPE]", StringComparison.Ordinal) && !wanted.ContainsKey(key))
            {
                key = key.Substring(0, key.Length - "[TYPE]".Length);
            }
            return wanted.TryGetValue(key, out var targets) ? targets : null;
        }

        var response = new PsetSuggestionResponse
        {
            ElementCount = elements.Count,
            ScopeMethod = classifier.Available
                ? "Elements were matched to the entities by their IFC class: the IfcExportAs parameter of the element or its type, otherwise the active IFC category mapping of the document. Items whose entities matched no element were checked against every model element."
                : "The IFC category mapping could not be read, so elements were matched by IfcExportAs only; items whose entities matched no element were checked against every model element."
        };
        for (var i = 0; i < items.Count; i++)
        {
            var result = new PsetSuggestionResult
            {
                Key = items[i].Key,
                PropertySet = items[i].PropertySet,
                Name = items[i].Name,
                Scope = matched[i] == 0 ? "all" : "entities",
                ElementsScanned = scanned[i],
                Suggestions = found[i].Values
                    .Select(a => a.ToSuggestion(scanned[i]))
                    .OrderBy(s => s.Rule == PsetSuggestionRule.Name ? 0 : 1)
                    .ThenByDescending(s => s.ElementsWithParameter)
                    .ThenBy(s => s.Scope == WritebackScope.Instance ? 0 : 1)
                    .ThenBy(s => s.Parameter, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
            if (itemKeys[i].Count == 0)
            {
                result.Note = "No entities given, so every model element was checked.";
            }
            else if (matched[i] == 0)
            {
                result.Note = $"No model element was found as {string.Join(", ", items[i].Entities!)}, so every model element was checked.";
            }
            else
            {
                var missing = items[i].Entities!.Where(e => !string.IsNullOrWhiteSpace(e) && !present.Contains(PsetNames.OccurrenceKey(e))).ToList();
                if (missing.Count > 0) result.Note = $"No model element was found as {string.Join(", ", missing)}.";
            }
            if (string.IsNullOrWhiteSpace(items[i].Name)) result.Note = "The item names no property.";
            response.Items.Add(result);
        }

        response.ElapsedMs = watch.ElapsedMilliseconds;
        return response;
    }

    /// <summary>
    /// The elements an IFC export can write with property sets: model categories, levels and the
    /// project information, without element types and view-specific elements. Elements of
    /// internal categories (not in the document's category list) and the definitions Revit keeps
    /// as model elements (pipe and duct segments, HVAC zones, materials) are left out.
    /// </summary>
    public static List<Element> ModelElements(Document document)
    {
        var categories = new HashSet<long>();
        foreach (Category category in document.Settings.Categories)
        {
            if (category.CategoryType == CategoryType.Model) categories.Add(category.Id.Value);
        }

        return new FilteredElementCollector(document)
            .WhereElementIsNotElementType()
            .WhereElementIsViewIndependent()
            .Where(e => e is Level || e is ProjectInfo ||
                        (e.Category != null && categories.Contains(e.Category.Id.Value) &&
                         e is not Material && e is not PropertySetElement && e is not SketchPlane &&
                         e is not Segment && e is not Autodesk.Revit.DB.Mechanical.Zone))
            .ToList();
    }

    // Every Revit API call per parameter counts on a big model: name, origin, storage type and
    // read-only are read once per parameter id, so a parameter costs its id and its value only.

    private IEnumerable<ParameterHit> InstanceParameters(Element element)
    {
        foreach (var (parameter, meta) in Visible(element))
        {
            yield return new ParameterHit(meta, HasValue(parameter, meta.Storage));
        }
    }

    private List<ParameterHit> TypeParameters(ElementId typeId)
    {
        if (_typeParameters.TryGetValue(typeId.Value, out var cached)) return cached;
        var type = _document.GetElement(typeId);
        var hits = type == null ? new List<ParameterHit>() : Visible(type).Select(v => new ParameterHit(v.Meta, HasValue(v.Parameter, v.Meta.Storage))).ToList();
        _typeParameters[typeId.Value] = hits;
        return hits;
    }

    private IEnumerable<(Parameter Parameter, ParameterMeta Meta)> Visible(Element element)
    {
        foreach (Parameter parameter in element.Parameters)
        {
            var meta = Meta(parameter);
            if (meta != null) yield return (parameter, meta);
        }
    }

    private ParameterMeta? Meta(Parameter? parameter)
    {
        if (parameter == null) return null;
        var id = parameter.Id.Value;
        if (_meta.TryGetValue(id, out var meta)) return meta;

        var definition = parameter.Definition;
        // The exporter skips hidden built-ins and nameless parameters; so does the builder
        if (definition == null || string.IsNullOrWhiteSpace(definition.Name) || definition is InternalDefinition { Visible: false })
        {
            _meta[id] = null;
            return null;
        }

        string origin;
        string? builtIn = null;
        string? guid = null;
        if (id < 0)
        {
            origin = ParameterOrigin.BuiltIn;
            builtIn = (definition as InternalDefinition)?.BuiltInParameter.ToString();
        }
        else if (parameter.IsShared)
        {
            origin = ParameterOrigin.Shared;
            guid = parameter.GUID.ToString();
        }
        else
        {
            origin = _boundDefinitions.Contains(id) ? ParameterOrigin.Project : ParameterOrigin.Family;
        }

        meta = new ParameterMeta(definition.Name, origin, builtIn, guid, parameter.StorageType, ParameterResolver.StorageTypeName(parameter), DataTypeName(definition), parameter.IsReadOnly);
        _meta[id] = meta;
        return meta;
    }

    /// <summary>
    /// Bound parameters the element pass did not meet: bound to categories with no element yet, or
    /// only to categories the scan leaves out. Counts are zero; the categories are the binding's.
    /// </summary>
    private IEnumerable<ModelParameterInfo> BoundWithoutElements()
    {
        var iterator = _document.ParameterBindings.ForwardIterator();
        while (iterator.MoveNext())
        {
            if (iterator.Key is not InternalDefinition definition || definition.Id == ElementId.InvalidElementId) continue;
            if (string.IsNullOrWhiteSpace(definition.Name)) continue;
            if (_meta.TryGetValue(definition.Id.Value, out var seen) && seen != null) continue;

            var categories = new List<string>();
            if (iterator.Current is ElementBinding { Categories: { } bound })
            {
                foreach (Category category in bound) categories.Add(category.Name);
            }
            var shared = _document.GetElement(definition.Id) as SharedParameterElement;
            yield return new ModelParameterInfo
            {
                Name = definition.Name,
                Scope = iterator.Current is TypeBinding ? WritebackScope.Type : WritebackScope.Instance,
                Origin = shared != null ? ParameterOrigin.Shared : ParameterOrigin.Project,
                Guid = shared?.GuidValue.ToString(),
                StorageType = StorageTypeName(definition),
                DataType = DataTypeName(definition),
                ReadOnly = false,
                Categories = categories.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
            };
        }
    }

    /// <summary>The storage type a parameter of this definition would have, without a parameter to ask.</summary>
    private static string StorageTypeName(Definition definition)
    {
        try
        {
            var spec = definition.GetDataType();
            if (spec == SpecTypeId.Boolean.YesNo) return "yesno";
            if (spec == SpecTypeId.Int.Integer) return "integer";
            if (spec == SpecTypeId.Reference.Material || Category.IsBuiltInCategory(spec)) return "elementid";
            if (spec == SpecTypeId.Number || UnitUtils.IsMeasurableSpec(spec)) return "double";
            return "string";
        }
        catch (Exception)
        {
            return "string";
        }
    }

    private static string DataTypeName(Definition definition)
    {
        try
        {
            // "autodesk.spec.aec:length-2.0.0" gives "length", "autodesk.spec:spec.string-2.0.0" gives "string"
            var typeId = definition.GetDataType()?.TypeId ?? "";
            var name = typeId.Substring(typeId.LastIndexOf(':') + 1);
            var dash = name.IndexOf('-');
            if (dash >= 0) name = name.Substring(0, dash);
            return name.StartsWith("spec.", StringComparison.Ordinal) ? name.Substring(5) : name;
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static bool HasValue(Parameter parameter, StorageType storage)
    {
        if (!parameter.HasValue) return false;
        switch (storage)
        {
            case StorageType.String:
                return !string.IsNullOrEmpty(parameter.AsString());
            case StorageType.ElementId:
                return parameter.AsElementId() != ElementId.InvalidElementId;
            default:
                return true;
        }
    }

    private static Dictionary<long, string> CategoryNames(Document document)
    {
        var names = new Dictionary<long, string>();
        foreach (Category category in document.Settings.Categories)
        {
            names[category.Id.Value] = category.Name;
        }
        return names;
    }

    private sealed class ParameterMeta
    {
        public ParameterMeta(string name, string origin, string? builtIn, string? guid, StorageType storage, string storageType, string dataType, bool readOnly)
        {
            Name = name;
            Origin = origin;
            BuiltIn = builtIn;
            Guid = guid;
            Storage = storage;
            StorageType = storageType;
            ReadOnly = readOnly;
            DataType = dataType;
            ExporterKey = PsetNames.ParameterKey(name);
            AggregateKey = $"{name}|{origin}|{builtIn ?? guid ?? ""}";
        }

        public string Name { get; }
        public string Origin { get; }
        public string? BuiltIn { get; }
        public string? Guid { get; }
        public StorageType Storage { get; }
        public string StorageType { get; }
        public string DataType { get; }

        /// <summary>As the first parameter with this id said; the scan does not ask every element again.</summary>
        public bool ReadOnly { get; }
        public string ExporterKey { get; }
        public string AggregateKey { get; }
    }

    private readonly record struct ParameterHit(ParameterMeta Meta, bool HasValue);

    private sealed class Aggregate
    {
        private readonly ParameterMeta _meta;
        private readonly HashSet<long> _categories = new();
        private int _instance;
        private int _type;
        private int _elements;
        private int _withValue;
        private bool _readOnly = true;
        private int _lastElement = -1;
        private int _lastValue = -1;
        private int _lastInstance = -1;
        private int _lastType = -1;

        public Aggregate(ParameterMeta meta)
        {
            _meta = meta;
        }

        public void Add(int element, long categoryId, ParameterHit hit, bool onType)
        {
            if (onType)
            {
                if (_lastType != element) { _lastType = element; _type++; }
            }
            else if (_lastInstance != element)
            {
                _lastInstance = element;
                _instance++;
            }
            if (_lastElement != element)
            {
                _lastElement = element;
                _elements++;
                if (categoryId != 0) _categories.Add(categoryId);
            }
            if (hit.HasValue && _lastValue != element)
            {
                _lastValue = element;
                _withValue++;
            }
            _readOnly &= hit.Meta.ReadOnly;
        }

        public ModelParameterInfo ToInfo(Dictionary<long, string> categoryNames)
        {
            return new ModelParameterInfo
            {
                Name = _meta.Name,
                Scope = _instance > 0 && _type > 0 ? "both" : _type > 0 ? WritebackScope.Type : WritebackScope.Instance,
                Origin = _meta.Origin,
                BuiltInParameter = _meta.BuiltIn,
                Guid = _meta.Guid,
                StorageType = _meta.StorageType,
                DataType = _meta.DataType,
                ReadOnly = _readOnly,
                InstanceCount = _instance,
                TypeCount = _type,
                ElementCount = _elements,
                WithValueCount = _withValue,
                Categories = _categories
                    .Select(id => categoryNames.TryGetValue(id, out var name) ? name : id.ToString())
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
        }
    }

    private sealed class SuggestionAggregate
    {
        private readonly ParameterMeta _meta;
        private readonly string _scope;
        private readonly string _rule;
        private int _elements;
        private int _withValue;
        private bool _readOnly = true;
        private int _last = -1;

        public SuggestionAggregate(ParameterMeta meta, string scope, string rule)
        {
            _meta = meta;
            _scope = scope;
            _rule = rule;
        }

        public void Add(int element, ParameterHit hit)
        {
            _readOnly &= hit.Meta.ReadOnly;
            if (_last == element) return;
            _last = element;
            _elements++;
            if (hit.HasValue) _withValue++;
        }

        public PsetSuggestion ToSuggestion(int scanned)
        {
            return new PsetSuggestion
            {
                Parameter = _meta.Name,
                Scope = _scope,
                Rule = _rule,
                Origin = _meta.Origin,
                BuiltInParameter = _meta.BuiltIn,
                StorageType = _meta.StorageType,
                DataType = _meta.DataType,
                ReadOnly = _readOnly,
                ElementsWithParameter = _elements,
                ElementsWithValue = _withValue,
                ElementsScanned = scanned
            };
        }
    }

    /// <summary>
    /// The IFC class an element exports as, reduced by <see cref="PsetNames.OccurrenceKey"/>: the
    /// IfcExportAs parameter of the element, then of its type, then the document's active IFC
    /// category mapping (walls by their function, as the exporter does). Null when unknown.
    /// </summary>
    private sealed class IfcClassifier
    {
        private readonly Document _document;
        private readonly IFCCategoryTemplate? _template;
        private readonly Dictionary<(long, CustomSubCategoryId), string?> _byCategory = new();
        private readonly Dictionary<long, string?> _typeExportAs = new();

        public IfcClassifier(Document document)
        {
            _document = document;
            try
            {
                _template = IFCCategoryTemplate.GetActiveTemplate(document) ?? IFCCategoryTemplate.GetOrCreateInSessionTemplate(document);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"IFC category mapping not available: {ex.Message}");
                _template = null;
            }
        }

        public bool Available => _template != null;

        public string? ClassKey(Element element)
        {
            if (element is Level) return "IFCBUILDINGSTOREY";
            if (element is ProjectInfo) return "IFCPROJECT";

            var exportAs = ExportAs(element.get_Parameter(BuiltInParameter.IFC_EXPORT_ELEMENT_AS));
            if (exportAs != null) return exportAs;

            var typeId = element.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
            {
                if (!_typeExportAs.TryGetValue(typeId.Value, out var typeClass))
                {
                    var type = _document.GetElement(typeId);
                    typeClass = type == null ? null
                        : ExportAs(type.get_Parameter(BuiltInParameter.IFC_EXPORT_ELEMENT_TYPE_AS)) ?? ExportAs(type.get_Parameter(BuiltInParameter.IFC_EXPORT_ELEMENT_AS));
                    _typeExportAs[typeId.Value] = typeClass;
                }
                if (typeClass != null) return typeClass;
            }

            var category = element.Category;
            if (category == null || _template == null) return null;

            var subCategory = CustomSubCategoryId.None;
            if (element is Wall wall && wall.WallType != null && wall.WallType.Kind == WallKind.Basic)
            {
                subCategory = wall.WallType.Function switch
                {
                    WallFunction.Exterior => CustomSubCategoryId.ExteriorWall,
                    WallFunction.Interior => CustomSubCategoryId.InteriorWall,
                    WallFunction.Foundation => CustomSubCategoryId.FoundationWall,
                    WallFunction.Retaining => CustomSubCategoryId.RetainingWall,
                    WallFunction.Soffit => CustomSubCategoryId.Soffit,
                    WallFunction.Coreshaft => CustomSubCategoryId.Coreshaft,
                    _ => CustomSubCategoryId.None
                };
            }

            var key = (category.Id.Value, subCategory);
            if (_byCategory.TryGetValue(key, out var cached)) return cached;

            string? result = null;
            try
            {
                var info = _template.GetMappingInfoById(_document, category.Id, subCategory);
                if ((info == null || string.IsNullOrWhiteSpace(info.IFCEntityName)) && subCategory != CustomSubCategoryId.None)
                {
                    info = _template.GetMappingInfoById(_document, category.Id, CustomSubCategoryId.None);
                }
                if (info != null && info.IFCExportFlag && !string.IsNullOrWhiteSpace(info.IFCEntityName))
                {
                    result = PsetNames.OccurrenceKey(info.IFCEntityName);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"IFC category mapping of {category.Name}: {ex.Message}");
            }
            _byCategory[key] = result;
            return result;
        }

        private static string? ExportAs(Parameter? parameter)
        {
            var value = parameter?.StorageType == StorageType.String ? parameter.AsString() : null;
            if (string.IsNullOrWhiteSpace(value)) return null;
            var ifcClass = PsetNames.ExportAsClass(value!);
            return ifcClass.Length == 0 ? null : PsetNames.OccurrenceKey(ifcClass);
        }
    }
}
