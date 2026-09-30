// Console check of the write-back classes that need no Revit: GlobalId compression, the
// mapping file parsers, value parsing and the JSON contract. Exit code = number of failures.
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

// PsetMapping: parameter mapping table
var t = new PsetMapping();
t.ReadParameterMappingTable(new[] { "# c", "Pset_WallCommon\tFireRating\tBrandklass", "two\tcolumns", "", "a\tb\tc\td" });
Check("table", Show(t.Find("Pset_WallCommon", "FireRating")), "Brandklass i=True t=True");
Check("table count", t.Entries.Count, 1);

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

Console.WriteLine(failed == 0 ? "ALL PASSED" : $"{failed} FAILED");
return failed;
