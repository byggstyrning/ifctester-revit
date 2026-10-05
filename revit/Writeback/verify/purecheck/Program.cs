// Console check of the write-back classes that need no Revit: GlobalId compression, the
// mapping file parsers, value parsing and the JSON contract. Exit code = number of failures.
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

Console.WriteLine(failed == 0 ? "ALL PASSED" : $"{failed} FAILED");
return failed;
