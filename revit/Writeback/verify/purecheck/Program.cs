// Console check of the write-back classes that need no Revit: GlobalId compression, the
// mapping file parsers, value parsing, the JSON contract and the API's origin and file access
// rules. Exit code = number of failures.
using System.Text;
using IfcTesterRevit.Writeback;

var failed = 0;
void Check(string what, object? actual, object? expected)
{
    var ok = Equals(actual, expected);
    if (!ok) failed++;
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}: {actual}{(ok ? "" : $"  (expected {expected})")}");
}

// IfcGuid: reference values from ifcopenshell.guid.compress (ifcopenshell 0.8.5)
Check("guid 1", IfcGuid.FromGuid(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e")), "0FZwrRsSj6dw5bS8PtA9KE");
Check("guid 2", IfcGuid.FromGuid(Guid.Parse("60f91daf-3dd7-4283-a86d-24137b73f3da")), "1W_HslFTT2WwXj91DxS$FQ");
Check("guid zero", IfcGuid.FromGuid(Guid.Empty), "0000000000000000000000");
Check("guid max", IfcGuid.FromGuid(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")), "3$$$$$$$$$$$$$$$$$$$$$");
Check("valid", IfcGuid.IsValid("2O2Fr$t4X7Zf8NOew3FLOH"), true);
Check("invalid first char", IfcGuid.IsValid("4O2Fr$t4X7Zf8NOew3FLOH"), false);
Check("invalid length", IfcGuid.IsValid("2O2Fr$t4X7Zf8NOew3FLO"), false);
Check("invalid char", IfcGuid.IsValid("2O2Fr-t4X7Zf8NOew3FLOH"), false);
Check("invalid null", IfcGuid.IsValid(null), false);

// PsetMapping: user-defined property set file
var file = string.Join("\n", new[]
{
    "# comment line",
    "PropertySet:\tExample\tI\tIfcSpace",
    "#\tEgenskapsnamn\t\t\tDatatyp (Revit)\tRevit egenskapsnamn",
    "\tEtapp\t\tLABEL\tPhase",
    "",
    "PropertySet:\tExample\tI\tIfcElement, IfcElementType",
    "\tEtapp\t\tLABEL\tPhase Created",
    "\tStoreyName\tLabel\tLevel",
    "\tStoreyName\tLabel\tReference Level",
    "\tNoThirdColumn\tText",
    "\tMark\tText\tBuiltInParameter.ALL_MODEL_MARK",
    "PropertySet:\tTypeOnly\tT\tIfcWallType,IfcDoorStyle",
    "\tFire\tLabel\tFire Rating",
    "PropertySet:\tAttribute Mapping\tI\tIfcElement",
    "\tDescription\tText\tComments",
    "PropertySet:\tTooFewColumns\tI",
    "\tOrphan\tText\tShould attach to the previous set",
}).Split('\n');
var m = new PsetMapping();
m.ReadUserDefinedPsets(file);
string Show(IEnumerable<MappedParameter> l) => string.Join(" | ", l.Select(e => $"{e.ParameterName ?? "BIP:" + e.BuiltInParameterName} i={e.OnInstance} t={e.OnType}"));
Check("per-entity mapping", Show(m.Find("Example", "Etapp")), "Phase i=True t=False | Phase Created i=True t=True");
Check("fallback chain", Show(m.Find("Example", "StoreyName")), "Level i=True t=True | Reference Level i=True t=True");
Check("no third column", Show(m.Find("Example", "NoThirdColumn")), "NoThirdColumn i=True t=True");
Check("built-in", Show(m.Find("Example", "Mark")), "BIP:ALL_MODEL_MARK i=True t=True");
Check("type-only set", Show(m.Find("TypeOnly", "Fire")), "Fire Rating i=False t=True");
Check("case-insensitive find", m.Find("example", "etapp").Count, 2);
Check("attribute mapping", Show(m.FindAttribute("Description")), "Comments i=True t=False");
Check("unknown", m.Find("Example", "Nope").Count, 0);
Check("header with 3 columns is not a set", Show(m.Find("Attribute Mapping", "Orphan")), "Should attach to the previous set i=True t=False");
Check("entry count (a 3-column header line reads as a property line, as in the exporter)", m.Entries.Count, 10);
Check("set entities", string.Join(",", m.Find("Example", "StoreyName")[0].Entities), "IfcElement,IfcElementType");
Check("set entities per set", string.Join(",", m.Find("TypeOnly", "Fire")[0].Entities), "IfcWallType,IfcDoorStyle");
Check("pset file origin", m.Find("Example", "Mark")[0].Origin, MappingOrigin.PsetFile);
Check("line, header and data type", string.Join(" | ", m.Find("Example", "Etapp").Select(e => $"{e.LineNumber}/{e.HeaderLineNumber} {e.DataType}")), "4/2 LABEL | 7/6 LABEL");
Check("data type without third column", m.Find("Example", "NoThirdColumn")[0].DataType, "Text");

// PsetMapping: parameter mapping table
var t = new PsetMapping();
t.ReadParameterMappingTable(new[] { "# c", "Pset_WallCommon\tFireRating\tBrandklass", "two\tcolumns", "", "a\tb\tc\td" });
Check("table", Show(t.Find("Pset_WallCommon", "FireRating")), "Brandklass i=True t=True");
Check("table count", t.Entries.Count, 1);
Check("table origin and no entities", $"{t.Entries[0].Origin} {t.Entries[0].Entities.Count}", $"{MappingOrigin.MappingTable} 0");

// ValueParsing
Check("int", ValueParsing.TryParseInteger(" 42 ", out var i) && i == 42, true);
Check("int negative", ValueParsing.TryParseInteger("-7", out i) && i == -7, true);
Check("int decimal rejected", ValueParsing.TryParseInteger("1.5", out _), false);
Check("int thousands rejected", ValueParsing.TryParseInteger("1,000", out _), false);
Check("int empty rejected", ValueParsing.TryParseInteger("", out _), false);
foreach (var (text, expected) in new[] { ("true", true), ("Yes", true), ("1", true), ("FALSE", false), ("no", false), ("0", false) })
    Check($"yesno {text}", ValueParsing.TryParseYesNo(text, out var b) && b == expected, true);
Check("yesno rejected", ValueParsing.TryParseYesNo("maybe", out _), false);
Check("yesno null rejected", ValueParsing.TryParseYesNo(null, out _), false);

// JSON contract
var resolve = WritebackJson.Deserialize<ResolveRequest>("""{ "items": [ { "key": "any-client-string", "globalId": "2O2Fr$t4X7Zf8NOew3FLOH", "facet": "property", "propertySet": "Pset_WallCommon", "name": "FireRating" }, { "key": "k2", "globalId": "x", "facet": "attribute", "name": "Name" } ] }""")!;
Check("resolve items", resolve.Items!.Count, 2);
Check("resolve item", $"{resolve.Items[0].Key}/{resolve.Items[0].GlobalId}/{resolve.Items[0].Facet}/{resolve.Items[0].PropertySet}/{resolve.Items[0].Name}", "any-client-string/2O2Fr$t4X7Zf8NOew3FLOH/property/Pset_WallCommon/FireRating");
Check("attribute has no pset", resolve.Items[1].PropertySet, null);
var apply = WritebackJson.Deserialize<ApplyRequest>("""{ "changes": [ { "key": "a", "globalId": "g", "parameter": "FireRating", "scope": "instance", "value": "EI60" }, { "key": "b", "value": 12.5 }, { "key": "c", "value": true }, { "key": "d" } ] }""")!;
Check("apply values", string.Join(",", apply.Changes!.Select(c => c.ValueText)), "EI60,12.5,true,");
Check("missing array", WritebackJson.Deserialize<ApplyRequest>("{}")!.Changes, null);
Check("resolve response json", WritebackJson.Serialize(new ResolveResponse { Items = { new ResolveResult { Key = "k", Found = true, ElementId = 123456, ElementName = "Basic Wall: Generic - 200mm", Category = "Walls", Candidates = { new ParameterCandidate { Parameter = "FireRating" } } } } }),
    """{"items":[{"key":"k","found":true,"elementId":123456,"elementName":"Basic Wall: Generic - 200mm","category":"Walls","typeName":null,"typeInstanceCount":null,"candidates":[{"parameter":"FireRating","scope":"instance","storageType":"string","value":"","hasValue":false,"readOnly":false,"source":"name-match"}],"message":null}],"configuration":null,"mappingFiles":[],"mappingNote":null}""");
Check("apply response json", WritebackJson.Serialize(new ApplyResponse { Applied = 1, Results = { new ChangeResult { Key = "k", Ok = true, NewValue = "EI60" } } }),
    """{"applied":1,"failed":0,"results":[{"key":"k","ok":true,"message":null,"newValue":"EI60"}]}""");

// ExportFiles: which property set file and mapping table an export reads
var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\x\override.txt", @"C:\exp\H29.txt", @"C:\x\table.txt" };
bool Exists(string path) => existing.Contains(path);
var psetSetup = new ExportFileSettings { ExportUserDefinedPsets = true, UserDefinedPsetsFileName = @"P:\pset\Byggpartner.txt" };
string Sel(ExportFileSelection s) => $"{s.PsetFile}|{s.PsetFileExists}|{s.PsetFileIsOverride}|{s.ParameterMappingFile}|{s.ParameterMappingFileExists}|{s.ParameterMappingFileIsOverride}";

var missingSetupFile = ExportFiles.Select("Other", psetSetup, null, null, @"C:\exp", Exists);
Check("setup file missing", Sel(missingSetupFile), @"P:\pset\Byggpartner.txt|False|False||False|False");
Check("setup file missing warns", missingSetupFile.Warning, @"The setup's property set file was not found: P:\pset\Byggpartner.txt. The export has no user-defined property sets.");
Check("exporter fallback <setup>.txt", Sel(ExportFiles.Select("H29", psetSetup, null, null, @"C:\exp", Exists)), @"C:\exp\H29.txt|True|False||False|False");
Check("exporter fallback no warning", ExportFiles.Select("H29", psetSetup, null, null, @"C:\exp", Exists).Warning, null);
var overridden = ExportFiles.Select("Other", psetSetup, @" C:\x\override.txt ", @"C:\x\table.txt", @"C:\exp", Exists);
Check("override replaces missing setup file", Sel(overridden), @"C:\x\override.txt|True|True|C:\x\table.txt|True|True");
Check("override no warning", overridden.Warning, null);
Check("missing override warns", ExportFiles.Select("Other", psetSetup, @"C:\x\gone.txt", null, @"C:\exp", Exists).Warning, @"The property set file override was not found: C:\x\gone.txt.");
Check("override on a setup without psets", Sel(ExportFiles.Select("Other", new ExportFileSettings(), @"C:\x\override.txt", null, @"C:\exp", Exists)), @"C:\x\override.txt|True|True||False|False");
var noPsets = ExportFiles.Select("Other", new ExportFileSettings { UserDefinedPsetsFileName = @"P:\unused.txt" }, null, null, @"C:\exp", Exists);
Check("psets off: no file, no warning", $"{Sel(noPsets)}|{noPsets.Warning}", "|False|False||False|False|");
Check("table read without its checkbox", Sel(ExportFiles.Select("Other", new ExportFileSettings { ParameterMappingFileName = @"C:\x\table.txt" }, null, null, @"C:\exp", Exists)), @"|False|False|C:\x\table.txt|True|False");
Check("missing table warns only when checked", ExportFiles.Select("Other", new ExportFileSettings { ExportUserDefinedParameterMapping = true, ParameterMappingFileName = @"P:\t.txt" }, null, null, @"C:\exp", Exists).Warning, @"The setup's parameter mapping table was not found: P:\t.txt.");
Check("missing unchecked table is quiet", ExportFiles.Select("Other", new ExportFileSettings { ParameterMappingFileName = @"P:\t.txt" }, null, null, @"C:\exp", Exists).Warning, null);

// ExportRequestSettings.ForResolve: the files a resolve reads
var last = new ExportRequestSettings("H29", @"C:\x\override.txt", null);
string Res(ExportRequestSettings s) => $"{s.Configuration}|{s.PsetFile}|{s.ParameterMappingFile}";
Check("resolve: nothing named, last export used", Res(ExportRequestSettings.ForResolve(new ResolveRequest(), last)), @"H29|C:\x\override.txt|");
Check("resolve: same setup keeps last override", Res(ExportRequestSettings.ForResolve(new ResolveRequest { Configuration = "H29" }, last)), @"H29|C:\x\override.txt|");
Check("resolve: other setup drops last override", Res(ExportRequestSettings.ForResolve(new ResolveRequest { Configuration = "Other" }, last)), "Other||");
Check("resolve: request override wins", Res(ExportRequestSettings.ForResolve(new ResolveRequest { Configuration = "H29", PsetFile = @"C:\y.txt", ParameterMappingFile = " " }, last)), @"H29|C:\y.txt|");
Check("resolve: no last export", Res(ExportRequestSettings.ForResolve(new ResolveRequest { PsetFile = @"C:\y.txt" }, null)), @"|C:\y.txt|");
Check("resolve request json", Res(ExportRequestSettings.ForResolve(WritebackJson.Deserialize<ResolveRequest>("""{ "items": [], "configuration": "H29", "psetFile": "\\\\srv\\pset\\Byggpartner åäö.txt", "parameterMappingFile": "" }""")!, null)), @"H29|\\srv\pset\Byggpartner åäö.txt|");
var exportRequest = WritebackJson.Deserialize<ExportIfcRequest>("""{ "configuration": "H29", "psetFile": "C:\\x\\override.txt" }""")!;
Check("export request json", $"{exportRequest.Configuration}|{exportRequest.PsetFile}|{exportRequest.ParameterMappingFile}", @"H29|C:\x\override.txt|");
Check("export files json", WritebackJson.Serialize(overridden),
    """{"psetFile":"C:\\x\\override.txt","psetFileExists":true,"psetFileIsOverride":true,"parameterMappingFile":"C:\\x\\table.txt","parameterMappingFileExists":true,"parameterMappingFileIsOverride":true,"warning":null,"ifcVersion":null}""");

// Pset builder: name keys as the exporter compares them, entity keys for matching elements
Check("parameter key", PsetNames.ParameterKey("Fire Rating"), "FIRERATING");
Check("parameter key pset name", PsetNames.ParameterKey("Pset_WallCommon.Acoustic Rating"), "PSET_WALLCOMMON.ACOUSTICRATING");
foreach (var (entity, key) in new[] { ("IfcWall", "IFCWALL"), ("IFCWALL", "IFCWALL"), ("IfcWallType", "IFCWALL"), ("IfcWallStandardCase", "IFCWALL"), ("IfcDoorStyle", "IFCDOOR"),
             ("IfcSlabElementedCase", "IFCSLAB"), ("IfcBuildingElementProxyType", "IFCBUILDINGELEMENTPROXY"), (" IfcSpace ", "IFCSPACE"), ("IfcTypeObject", "IFCTYPEOBJECT") })
    Check($"occurrence key {entity}", PsetNames.OccurrenceKey(entity), key);
Check("export-as class", PsetNames.ExportAsClass("IfcWall.SHEAR"), "IfcWall");
Check("export-as class plain", PsetNames.ExportAsClass(" IfcBeam "), "IfcBeam");

var suggestionRequest = WritebackJson.Deserialize<PsetSuggestionRequest>("""{ "items": [ { "key": "r1", "propertySet": "Projekt", "name": "Brandklass", "entities": ["IfcWall", "IFCSLAB"] } ] }""")!;
Check("suggestion request json", $"{suggestionRequest.Items![0].Key}|{suggestionRequest.Items[0].PropertySet}|{suggestionRequest.Items[0].Name}|{string.Join(",", suggestionRequest.Items[0].Entities!)}", "r1|Projekt|Brandklass|IfcWall,IFCSLAB");

// Pset file save: default folder, refusal to overwrite, UTF-8 without BOM, CRLF kept
var saveRoot = Path.Combine(Path.GetTempPath(), "ifctester-purecheck-" + Guid.NewGuid().ToString("N"));
var saveDefault = Path.Combine(saveRoot, "psets");
try
{
    var content = "# Generated\r\nPropertySet:\tProjekt\tI\tIfcWall\r\n\tBrandklass\tLabel\tBrandklass åäö\r\n";
    var saved = PsetFileWriter.Save(new PsetFileSaveRequest { Name = "Projekt A: draft", Content = content }, saveDefault);
    Check("save default path", saved.Path, Path.Combine(saveDefault, "Projekt A_ draft.txt"));
    Check("save created the default folder", Directory.Exists(saveDefault), true);
    var bytes = File.ReadAllBytes(saved.Path);
    Check("save has no BOM", bytes.Length > 2 && bytes[0] == 0xEF && bytes[1] == 0xBB, false);
    Check("save is UTF-8 with CRLF", Encoding.UTF8.GetString(bytes), content);
    Check("save reports bytes", saved.Bytes, (long)bytes.Length);
    Check("save first time not overwritten", saved.Overwritten, false);
    int Status(Action action) { try { action(); return 200; } catch (PsetFileSaveException ex) { return ex.StatusCode; } }
    Check("save refuses to overwrite", Status(() => PsetFileWriter.Save(new PsetFileSaveRequest { Name = "Projekt A: draft", Content = "x" }, saveDefault)), 409);
    Check("refused save left the file", File.ReadAllText(saved.Path), content);
    var again = PsetFileWriter.Save(new PsetFileSaveRequest { Path = saved.Path, Content = "y", Overwrite = true }, saveDefault);
    Check("save with overwrite", $"{again.Overwritten}|{File.ReadAllText(saved.Path)}", "True|y");
    Check("save to a missing folder", Status(() => PsetFileWriter.Save(new PsetFileSaveRequest { Path = Path.Combine(saveRoot, "nope", "a.txt"), Content = "x" }, saveDefault)), 400);
    Check("save relative path", Status(() => PsetFileWriter.Save(new PsetFileSaveRequest { Path = "a.txt", Content = "x" }, saveDefault)), 400);
    Check("save without content", Status(() => PsetFileWriter.Save(new PsetFileSaveRequest { Name = "b" }, saveDefault)), 400);
    Check("save keeps a given extension", PsetFileWriter.ResolvePath(new PsetFileSaveRequest { Name = "x.TXT" }, saveDefault), Path.Combine(saveDefault, "x.TXT"));
    Check("save without a name", PsetFileWriter.ResolvePath(new PsetFileSaveRequest(), saveDefault), Path.Combine(saveDefault, "pset.txt"));
    Check("default folder", PsetFileWriter.DefaultFolder(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IfcTesterRevit", "psets"));
}
finally
{
    if (Directory.Exists(saveRoot)) Directory.Delete(saveRoot, true);
}

// OriginPolicy: CORS only for the add-in's own page and the dev servers; a foreign origin is refused
var origins = OriginPolicy.ForServer(48881, " http://localhost:5199/ ; https://Tool.Example.com:8443");
string Origin(string? origin) { var d = origins.Check(origin); return $"{d.Refuse}|{d.AllowOrigin}"; }
Check("origin: none (same-origin GET, curl)", Origin(null), "False|");
Check("origin: the add-in's own page", Origin("http://localhost:48881"), "False|http://localhost:48881");
Check("origin: vite dev", Origin("http://localhost:5173"), "False|http://localhost:5173");
Check("origin: vite preview", Origin("http://localhost:4173"), "False|http://localhost:4173");
Check("origin: extra from the variable", Origin("http://localhost:5199"), "False|http://localhost:5199");
Check("origin: extra https, case and port", Origin("https://tool.example.com:8443"), "False|https://tool.example.com:8443");
Check("origin: a website", Origin("https://evil.example"), "True|");
Check("origin: localhost on another port", Origin("http://localhost:8080"), "True|");
Check("origin: 127.0.0.1 is another origin", Origin("http://127.0.0.1:48881"), "True|");
Check("origin: https on the add-in's port", Origin("https://localhost:48881"), "True|");
Check("origin: null (file://, sandboxed frame)", Origin("null"), "True|");
Check("origin: empty header", Origin(""), "True|");
Check("origin: lookalike host", Origin("http://localhost.evil.example:48881"), "True|");
Check("origin normalize", OriginPolicy.Normalize("HTTP://LocalHost:80/"), "http://localhost");
Check("origins without the variable", string.Join(",", OriginPolicy.ForServer(48881, null).Allowed.OrderBy(o => o)), "http://localhost:4173,http://localhost:48881,http://localhost:5173");

// ReadablePaths: only files named to the page, picked through the add-in, or directly in an allowed folder
var readRoot = Path.Combine(Path.GetTempPath(), "ifctester-readable");
var readable = new ReadablePaths();
readable.Allow(Path.Combine(readRoot, "setup", "Byggpartner.txt"));
readable.AllowAll(new[] { null, "", "relative.txt", @"\\srv\share\pset\H29 åäö.txt" });
readable.AllowFolder(Path.Combine(readRoot, "psets"));
Check("readable: named file", readable.IsAllowed(Path.Combine(readRoot, "setup", "Byggpartner.txt")), true);
Check("readable: case and surrounding spaces", readable.IsAllowed("  " + Path.Combine(readRoot, "SETUP", "byggpartner.TXT") + " "), true);
Check("readable: UNC path", readable.IsAllowed(@"\\srv\share\pset\h29 ÅÄÖ.txt"), true);
Check("readable: another file in the same folder", readable.IsAllowed(Path.Combine(readRoot, "setup", "secrets.txt")), false);
Check("readable: dot-dot back to a named file", readable.IsAllowed(Path.Combine(readRoot, "psets", "..", "setup", "Byggpartner.txt")), true);
Check("readable: dot-dot out of the folder", readable.IsAllowed(Path.Combine(readRoot, "psets", "..", "other.txt")), false);
Check("readable: file in the allowed folder", readable.IsAllowed(Path.Combine(readRoot, "psets", "Projekt.txt")), true);
Check("readable: subfolder of the allowed folder", readable.IsAllowed(Path.Combine(readRoot, "psets", "sub", "Projekt.txt")), false);
Check("readable: the folder itself", readable.IsAllowed(Path.Combine(readRoot, "psets")), false);
Check("readable: relative path", readable.IsAllowed("relative.txt"), false);
Check("readable: empty", readable.IsAllowed(" "), false);
Check("readable: a user's file", readable.IsAllowed(@"C:\Users\someone\Documents\passwords.txt"), false);
Check("readable: malformed", readable.IsAllowed("C:\\a<>|b.txt"), false);

// ModelMemoryStore: per-model choices in one JSON file, paths only
var memoryRoot = Path.Combine(Path.GetTempPath(), "ifctester-memory-" + Guid.NewGuid().ToString("N"));
try
{
    var memoryPath = Path.Combine(memoryRoot, "IfcTesterRevit", "model-memory.json");
    var store = new ModelMemoryStore(memoryPath);
    var t0 = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
    const string central = @"\\srv\H29\K-20-V-100-6300-000.rvt";
    Check("memory: nothing for a new model", store.Get(central), null);
    store.RememberIds(central, @"\\srv\H29\ids\BS-100-63 åäö.ids", t0);
    Check("memory: file created with its folder", File.Exists(memoryPath), true);
    store.RememberExport(central, "IFC HUS 29X", @" \\srv\H29\pset\BS-100-63-Pset-K.txt ", " ", t0.AddMinutes(1));
    string Mem(RememberedChoices? c) => c == null ? "null" : $"{c.IdsFile}|{c.Configuration}|{c.PsetFile}|{c.ParameterMappingFile}|{c.Updated}";
    Check("memory: IDS and export kept together", Mem(store.Get(central)), @"\\srv\H29\ids\BS-100-63 åäö.ids|IFC HUS 29X|\\srv\H29\pset\BS-100-63-Pset-K.txt||2026-10-05T12:01:00Z");
    Check("memory: key ignores case", store.Get(central.ToUpperInvariant())?.Configuration, "IFC HUS 29X");
    Check("memory: read back by a new store (another Revit)", Mem(new ModelMemoryStore(memoryPath).Get(central)), Mem(store.Get(central)));
    store.RememberExport(central, "IFC4 Reference View", null, null, t0.AddMinutes(2));
    Check("memory: an export with the setup's own files clears the overrides", Mem(store.Get(central)), @"\\srv\H29\ids\BS-100-63 åäö.ids|IFC4 Reference View|||2026-10-05T12:02:00Z");
    store.RememberIds(@"C:\Models\Other.rvt", @"C:\ids\other.ids", t0);
    Check("memory: models apart", $"{store.Get(@"C:\Models\Other.rvt")?.IdsFile}|{store.Get(@"C:\Models\Other.rvt")?.Configuration}", @"C:\ids\other.ids|");
    var json = File.ReadAllText(memoryPath);
    Check("memory: JSON keeps the model path as key", json.Contains("\"\\\\\\\\srv\\\\H29\\\\K-20-V-100-6300-000.rvt\": {"), true);
    Check("memory: JSON camelCase, no temp or lock file left", (json.Contains("\"idsFile\"") && json.Contains("\"version\": 1"), File.Exists(memoryPath + ".tmp"), File.Exists(memoryPath + ".lock")), (true, false, false));

    // Two Revit sessions writing at once: neither loses the other's model
    Parallel.For(0, 40, n => new ModelMemoryStore(memoryPath).RememberIds($@"C:\Models\M{n}.rvt", $@"C:\ids\{n}.ids", t0.AddSeconds(n)));
    Check("memory: concurrent writers keep every model", Enumerable.Range(0, 40).Count(n => store.Get($@"C:\Models\M{n}.rvt")?.IdsFile == $@"C:\ids\{n}.ids"), 40);
    Check("memory: earlier entries survive the concurrent writes", store.Get(central)?.Configuration, "IFC4 Reference View");

    // A broken file is kept aside and the memory starts again instead of failing the page
    File.WriteAllText(memoryPath, "{ not json");
    Check("memory: unreadable file remembers nothing", store.Get(central), null);
    Check("memory: unreadable file kept aside", File.ReadAllText(memoryPath + ".unreadable"), "{ not json");
    store.RememberIds(central, @"C:\ids\new.ids", t0);
    Check("memory: writes again after a broken file", store.Get(central)?.IdsFile, @"C:\ids\new.ids");

    // Oldest models are forgotten beyond the limit
    var many = new ModelMemoryFile();
    for (var n = 0; n < ModelMemoryStore.MaxModels + 5; n++) many.Models[$@"C:\Models\Old{n:D4}.rvt"] = new RememberedChoices { IdsFile = "x.ids", Updated = t0.AddMinutes(-1000 + n).ToString("yyyy-MM-ddTHH:mm:ssZ") };
    File.WriteAllText(memoryPath, WritebackJson.Serialize(many));
    store.RememberIds(@"C:\Models\Newest.rvt", @"C:\ids\n.ids", t0);
    Check("memory: capped, oldest forgotten", (store.Get(@"C:\Models\Old0000.rvt"), store.Get(@"C:\Models\Old0005.rvt"), store.Get(@"C:\Models\Old0006.rvt")?.IdsFile, store.Get(@"C:\Models\Newest.rvt")?.IdsFile), ((RememberedChoices?)null, (RememberedChoices?)null, "x.ids", @"C:\ids\n.ids"));
    Check("memory: default path", ModelMemoryStore.DefaultPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IfcTesterRevit", "model-memory.json"));
    Check("memory request json", WritebackJson.Deserialize<ModelMemoryRequest>("""{ "idsFile": "C:\\ids\\a.ids" }""")!.IdsFile, @"C:\ids\a.ids");
    Check("memory response json", WritebackJson.Serialize(new ModelMemoryResponse { Model = new MemoryModel { Key = @"C:\m.rvt", Title = "m" }, Remembered = new RememberedState { Configuration = "S", IdsFile = new RememberedFile { Path = @"C:\a.ids", Name = "a.ids", Exists = true } } }),
        """{"model":{"key":"C:\\m.rvt","title":"m","workshared":false},"message":null,"remembered":{"updated":null,"configuration":"S","idsFile":{"path":"C:\\a.ids","name":"a.ids","exists":true,"error":null},"psetFile":null,"parameterMappingFile":null}}""");

    // IDS files: extension, encoding by byte order mark, size
    Check("ids extension", (IdsFiles.HasIdsExtension(@"C:\a.IDS"), IdsFiles.HasIdsExtension(@"C:\a.xml"), IdsFiles.HasIdsExtension(@"C:\a.txt"), IdsFiles.HasIdsExtension(null)), (true, true, false, false));
    var idsPath = Path.Combine(memoryRoot, "spec åäö.ids");
    File.WriteAllText(idsPath, "<ids>Våning</ids>", new UTF8Encoding(true));
    Check("ids read UTF-8 with BOM", $"{IdsFiles.Read(idsPath).Name}|{IdsFiles.Read(idsPath).Content}", "spec åäö.ids|<ids>Våning</ids>");
    File.WriteAllText(idsPath, "<ids>Våning</ids>", new UTF8Encoding(false));
    Check("ids read UTF-8 without BOM", IdsFiles.Read(idsPath).Content, "<ids>Våning</ids>");
    File.WriteAllText(idsPath, "<ids>Våning</ids>", Encoding.Unicode);
    Check("ids read UTF-16 by its BOM", IdsFiles.Read(idsPath).Content, "<ids>Våning</ids>");
    using (new FileStream(idsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
    {
        Check("ids read while an editor holds it open for writing", IdsFiles.Read(idsPath).Content, "<ids>Våning</ids>");
    }
}
finally
{
    if (Directory.Exists(memoryRoot)) Directory.Delete(memoryRoot, true);
}

Console.WriteLine(failed == 0 ? "ALL PASSED" : $"{failed} FAILED");
return failed;
