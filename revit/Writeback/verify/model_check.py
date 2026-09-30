# -*- coding: utf-8 -*-
# Check of IfcTesterRevit.Writeback inside a real Revit. Started by run.ps1 through
# `pyrevit run`; not meant to be run by hand.
#
# Builds a blank project with a few walls, exports it to IFC and compares the GlobalIds the
# exporter wrote with the ones the add-in derives, then drives resolve and apply directly.
# The project is saved as wb-model-<year>.rvt in the output folder for http_check.py.
# Results go to model-log-<year>.txt there; the last line is ALL PASSED or FAILED: [...].
import clr
import io
import os
import re
import sys
import traceback

import System
from System import Int64
from Autodesk.Revit.DB import *

OUT = os.environ['IFCTESTER_WB_OUT']
DLL = os.environ['IFCTESTER_WB_DLL']
YEAR = __revit__.Application.VersionNumber
LOG = os.path.join(OUT, 'model-log-%s.txt' % YEAR)

out = io.open(LOG, 'w', encoding='utf-8')
fails = []


def log(*parts):
    out.write(u' '.join([unicode(p) for p in parts]) + u'\n')
    out.flush()


def check(what, actual, expected):
    ok = actual == expected
    if not ok:
        fails.append(what)
    log(u'ok  ' if ok else u'FAIL', what, u'=', actual, u'' if ok else u'(expected %s)' % (expected,))


def name_of(el):
    return Element.Name.__get__(el)


try:
    uiapp = __revit__
    app = uiapp.Application
    log('Revit', app.VersionNumber, app.VersionBuild, 'dll', DLL)

    clr.AddReferenceToFileAndPath(DLL)
    from IfcTesterRevit.Writeback import (
        ApplyRequest, ExportMapping, GlobalIdIndex, IfcGuid, ResolveRequest, WritebackJson, WritebackService)
    from IfcTesterRevit import IFCExportHelper

    doc = app.NewProjectDocument(UnitSystem.Metric)

    # ---- model -------------------------------------------------------------------------
    spfile = os.path.join(OUT, 'shared-params-%s.txt' % YEAR)
    open(spfile, 'w').close()
    app.SharedParametersFilename = spfile
    group = app.OpenSharedParameterFile().Groups.Create('wb')

    t = Transaction(doc, 'setup')
    t.Start()

    walls_cat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Walls)

    def shared(name, spec, instance, param_group):
        definition = group.Definitions.Create(ExternalDefinitionCreationOptions(name, spec))
        cats = app.Create.NewCategorySet()
        cats.Insert(walls_cat)
        binding = app.Create.NewInstanceBinding(cats) if instance else app.Create.NewTypeBinding(cats)
        return doc.ParameterBindings.Insert(definition, binding, param_group)

    check('bind IfcName', shared('IfcName', SpecTypeId.String.Text, True, GroupTypeId.Ifc), True)
    check('bind IfcDescription', shared('IfcDescription', SpecTypeId.String.Text, True, GroupTypeId.Ifc), True)
    check('bind IfcObjectType[Type]', shared('IfcObjectType[Type]', SpecTypeId.String.Text, False, GroupTypeId.Ifc), True)
    check('bind Brandklass', shared('Brandklass', SpecTypeId.String.Text, True, GroupTypeId.Data), True)
    check('bind Antal', shared('Antal', SpecTypeId.Int.Integer, True, GroupTypeId.Data), True)
    check('bind Pset_WallCommon.AcousticRating', shared('Pset_WallCommon.AcousticRating', SpecTypeId.String.Text, False, GroupTypeId.Data), True)

    level = FilteredElementCollector(doc).OfClass(Level).FirstElement()
    if level is None:
        level = Level.Create(doc, 0.0)

    def wall(row):
        y = row * 10.0
        return Wall.Create(doc, Line.CreateBound(XYZ(0, y, 0), XYZ(10, y, 0)), level.Id, False)

    w1, w2, w3, w4, w5, w6 = [wall(i) for i in range(6)]
    base_type = doc.GetElement(w1.GetTypeId())
    type2 = base_type.Duplicate('WB Type 2')
    w3.ChangeTypeId(type2.Id)

    shared_guid = IfcGuid.FromGuid(System.Guid.Parse('60f91daf-3dd7-4283-a86d-24137b73f3da'))
    own_guid = IfcGuid.FromGuid(System.Guid.Parse('0f8fad5b-d9cb-469f-a165-70867728950e'))
    for w, g in ((w4, shared_guid), (w5, shared_guid), (w6, own_guid)):
        p = w.get_Parameter(BuiltInParameter.IFC_GUID)
        log('IFC_GUID param on', w.Id.Value, 'readonly', p.IsReadOnly, 'set ->', p.Set(g))
    check('commit setup', t.Commit(), TransactionStatus.Committed)

    # ---- GlobalId: the index against a real export ---------------------------------------
    clr.AddReference('RevitAPIIFC')
    from Autodesk.Revit.DB.IFC import ExporterIFCUtils
    for w in (w1, w6):
        log('wall', w.Id.Value,
            'derived', IfcGuid.FromGuid(ExportUtils.GetExportId(doc, w.Id)),
            'CreateAlternateGUID', ExporterIFCUtils.CreateAlternateGUID(w),
            'stored', w.get_Parameter(BuiltInParameter.IFC_GUID).AsString())
    check('derived == ExporterIFCUtils.CreateAlternateGUID (w1)',
          IfcGuid.FromGuid(ExportUtils.GetExportId(doc, w1.Id)), ExporterIFCUtils.CreateAlternateGUID(w1))

    ifc_name = 'wb-%s.ifc' % YEAR
    ifc_path = os.path.join(OUT, ifc_name)
    if os.path.exists(ifc_path):
        os.remove(ifc_path)
    t = Transaction(doc, 'export')
    t.Start()
    options = IFCExportOptions()
    options.FileVersion = IFCVersion.IFC4
    log('doc.Export ->', doc.Export(OUT, ifc_name, options))
    t.RollBack()

    exported = {}  # element id -> GlobalId
    for line in io.open(ifc_path, encoding='utf-8', errors='replace'):
        m = re.match(r"#\d+=\s*IFCWALL(?:TYPE)?\('([^']{22})'", line)
        if m:
            tags = re.findall(r"'(\d+)'", line)
            exported[int(tags[-1])] = m.group(1)
    log('exported walls', exported)
    check('exported walls + wall types', len(exported), 8)

    index = GlobalIdIndex.Build(doc)
    for w in (w1, w2, w3, w6):
        el, msg = index.Find(exported[w.Id.Value])
        check('index finds exported GlobalId of %s' % w.Id.Value, el.Id.Value if el else msg, w.Id.Value)
    check('stored guid is what the exporter used (w6)', exported[w6.Id.Value], own_guid)
    log('exporter gave the two walls sharing a stored guid:', exported[w4.Id.Value], exported[w5.Id.Value], 'stored', shared_guid)
    el, msg = index.Find(shared_guid)
    check('shared stored guid is ambiguous', el, None)
    log('   message:', msg)
    el, msg = index.Find('0000000000000000000000')
    check('unknown guid not found', el, None)
    log('   message:', msg)
    el, msg = index.Find(exported[base_type.Id.Value])
    check('a type GlobalId is not a target, and the message says why', (el, 'is the type' in msg and '5 instances' in msg), (None, True))
    log('   message:', msg)
    check('instances of base type', index.InstanceCount(base_type.Id), 5)
    check('instances of type 2', index.InstanceCount(type2.Id), 1)

    g1, g2, g3 = exported[w1.Id.Value], exported[w2.Id.Value], exported[w3.Id.Value]

    # ---- resolve ---------------------------------------------------------------------------
    psets = os.path.join(OUT, 'psets.txt')
    with io.open(psets, 'w', encoding='utf-8') as f:
        f.write(u'PropertySet:\tCustom\tI\tIfcWall\n'
                u'\tBrand\tText\tBrandklass\n'
                u'\tMarkX\tText\tBuiltInParameter.ALL_MODEL_MARK\n'
                u'PropertySet:\tAttribute Mapping\tI\tIfcWall\n'
                u'\tDescription\tText\tComments\n')
    mapping = ExportMapping.FromFiles(psets, None, False)

    def item(key, guid, facet, name, pset=None):
        s = u'{"key":"%s","globalId":"%s","facet":"%s","name":"%s"' % (key, guid, facet, name)
        if pset:
            s += u',"propertySet":"%s"' % pset
        return s + u'}'

    items = [
        item('fire', g1, 'property', 'FireRating', 'Pset_WallCommon'),
        item('brand', g1, 'property', 'Brand', 'Custom'),
        item('markx', g1, 'property', 'MarkX', 'Custom'),
        item('name', g1, 'attribute', 'Name'),
        item('desc', g1, 'attribute', 'Description'),
        item('objtype', g1, 'attribute', 'ObjectType'),
        item('tag', g1, 'attribute', 'Tag'),
        item('comments', g1, 'property', 'Comments', 'X'),
        item('offset', g1, 'property', 'BaseOffset', 'X'),
        item('roombounding', g1, 'property', 'RoomBounding', 'X'),
        item('constraint', g1, 'property', 'BaseConstraint', 'X'),
        item('length', g1, 'property', 'Length', 'X'),
        item('acoustic', g1, 'property', 'AcousticRating', 'Pset_WallCommon'),
        item('antal', g1, 'property', 'antal', 'X'),
        item('missing', g1, 'property', 'DoesNotExist', 'X'),
        item('material', g1, 'material', 'Concrete'),
        item('unknown', '0000000000000000000000', 'property', 'FireRating', 'Pset_WallCommon'),
        item('ambiguous', shared_guid, 'property', 'FireRating', 'Pset_WallCommon'),
        item('stored', own_guid, 'property', 'Comments', 'X'),
    ]
    request = WritebackJson.Deserialize[ResolveRequest](u'{"items":[%s]}' % u','.join(items))
    response = WritebackService.Resolve(doc, request, mapping)
    log('--- resolve response')
    by_key = {}
    for r in response.Items:
        by_key[r.Key] = r
        log(WritebackJson.Serialize(r))

    def cands(key):
        return [(c.Parameter, c.Scope, c.StorageType, c.Source, c.ReadOnly) for c in by_key[key].Candidates]

    check('resolve fire', cands('fire'), [('Fire Rating', 'type', 'string', 'name-match', False)])
    check('resolve brand', cands('brand'), [('Brandklass', 'instance', 'string', 'pset-mapping-file', False)])
    check('resolve markx', cands('markx'), [('Mark', 'instance', 'string', 'pset-mapping-file', False)])
    check('resolve name', cands('name'), [('IfcName', 'instance', 'string', 'ifc-override', False)])
    check('resolve desc', cands('desc'), [('Comments', 'instance', 'string', 'pset-mapping-file', False),
                                          ('IfcDescription', 'instance', 'string', 'ifc-override', False)])
    check('resolve objtype', cands('objtype'), [('IfcObjectType[Type]', 'type', 'string', 'ifc-override', False)])
    check('resolve tag', (cands('tag'), by_key['tag'].Found, by_key['tag'].Message is not None), ([], True, True))
    check('resolve comments', cands('comments'), [('Comments', 'instance', 'string', 'name-match', False)])
    check('resolve offset', cands('offset'), [('Base Offset', 'instance', 'double', 'name-match', False)])
    check('resolve roombounding', cands('roombounding'), [('Room Bounding', 'instance', 'yesno', 'name-match', False)])
    check('resolve constraint', [c[:3] for c in cands('constraint')], [('Base Constraint', 'instance', 'elementid')])
    check('resolve length', cands('length'), [('Length', 'instance', 'double', 'name-match', True)])
    check('resolve acoustic', cands('acoustic'), [('Pset_WallCommon.AcousticRating', 'type', 'string', 'name-match', False)])
    check('resolve antal', cands('antal'), [('Antal', 'instance', 'integer', 'name-match', False)])
    check('resolve missing', (cands('missing'), by_key['missing'].Message is not None), ([], True))
    check('resolve material', (cands('material'), by_key['material'].Message is not None), ([], True))
    check('resolve unknown', (by_key['unknown'].Found, by_key['unknown'].ElementId), (False, None))
    check('resolve ambiguous', by_key['ambiguous'].Found, False)
    check('resolve stored', (by_key['stored'].Found, by_key['stored'].ElementId), (True, w6.Id.Value))
    check('resolve element', (by_key['fire'].ElementId, by_key['fire'].Category), (w1.Id.Value, 'Walls'))
    check('resolve type', (by_key['fire'].TypeName, by_key['fire'].TypeInstanceCount), (name_of(base_type), 5))
    check('resolve type of a not-found item', (by_key['unknown'].TypeName, by_key['unknown'].TypeInstanceCount), (None, None))
    log('elementName:', by_key['fire'].ElementName, '| mappingFiles:', list(response.MappingFiles))

    # ---- apply -----------------------------------------------------------------------------
    transactions = []

    def on_changed(sender, args):
        transactions.append(list(args.GetTransactionNames()))

    app.DocumentChanged += on_changed

    def change(key, guid, parameter, scope, value):
        return u'{"key":"%s","globalId":"%s","parameter":"%s","scope":"%s","value":%s}' % (key, guid, parameter, scope, value)

    changes = [
        change('brand', g1, 'Brandklass', 'instance', '"EI60"'),
        change('fire1', g1, 'Fire Rating', 'type', '"EI30"'),
        change('fire-conflict', g2, 'Fire Rating', 'type', '"EI90"'),
        change('fire-same', g2, 'Fire Rating', 'type', '"EI30"'),
        change('offset', g1, 'Base Offset', 'instance', '"500"'),
        change('offset-bad', g2, 'Base Offset', 'instance', '"abc"'),
        change('roombounding', g1, 'Room Bounding', 'instance', '"no"'),
        change('roombounding-bad', g2, 'Room Bounding', 'instance', '"maybe"'),
        change('antal', g1, 'Antal', 'instance', '"12"'),
        change('antal-bad', g2, 'Antal', 'instance', '"1.5"'),
        change('antal-number', g3, 'Antal', 'instance', '7'),
        change('length', g1, 'Length', 'instance', '"5"'),
        change('constraint', g1, 'Base Constraint', 'instance', '"Level 1"'),
        change('unknown', '0000000000000000000000', 'Comments', 'instance', '"x"'),
        change('noparam', g1, 'Nope', 'instance', '"x"'),
        change('ifcname', g3, 'IfcName', 'instance', '"Wall A"'),
        change('badscope', g1, 'Comments', 'bogus', '"x"'),
        change('type2', g3, 'Fire Rating', 'type', '"EI15"'),
    ]
    apply_request = WritebackJson.Deserialize[ApplyRequest](u'{"changes":[%s]}' % u','.join(changes))
    applied = WritebackService.Apply(doc, apply_request)
    log('--- apply response')
    log(WritebackJson.Serialize(applied))
    res = dict((r.Key, r) for r in applied.Results)
    expected_ok = ['brand', 'fire1', 'fire-same', 'offset', 'roombounding', 'antal', 'antal-number', 'ifcname', 'type2']
    check('apply ok keys', sorted([r.Key for r in applied.Results if r.Ok]), sorted(expected_ok))
    check('apply counts', (applied.Applied, applied.Failed), (9, 9))
    check('every failure has a message', [r.Key for r in applied.Results if not r.Ok and not r.Message], [])
    check('one transaction, named', transactions, [[WritebackService.TransactionName]])
    check('type message names the instance count', res['fire1'].Message is not None and ' 5 instances' in res['fire1'].Message, True)
    check('type2 message names the instance count', res['type2'].Message is not None and ' 1 instances' in res['type2'].Message, True)
    check('instance ok has no message', res['brand'].Message, None)

    check('value Brandklass', w1.LookupParameter('Brandklass').AsString(), 'EI60')
    check('value Fire Rating (base type)', base_type.get_Parameter(BuiltInParameter.FIRE_RATING).AsString(), 'EI30')
    check('value Fire Rating (type 2)', type2.get_Parameter(BuiltInParameter.FIRE_RATING).AsString(), 'EI15')
    offset = w1.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)
    check('value Base Offset in mm', round(UnitUtils.ConvertFromInternalUnits(offset.AsDouble(), UnitTypeId.Millimeters), 6), 500.0)
    check('newValue Base Offset', res['offset'].NewValue, offset.AsValueString())
    check('value w2 Base Offset untouched', w2.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET).AsDouble(), 0.0)
    check('value Room Bounding', (w1.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING).AsInteger(), res['roombounding'].NewValue), (0, 'No'))
    check('value Antal', (w1.LookupParameter('Antal').AsInteger(), w3.LookupParameter('Antal').AsInteger()), (12, 7))
    check('value IfcName', w3.LookupParameter('IfcName').AsString(), 'Wall A')

    # nothing applies -> rolled back, no transaction at all
    del transactions[:]
    nothing = WritebackService.Apply(doc, WritebackJson.Deserialize[ApplyRequest](
        u'{"changes":[%s,%s]}' % (change('a', '0000000000000000000000', 'Comments', 'instance', '"x"'),
                                   change('b', g1, 'Length', 'instance', '"1"'))))
    check('nothing applied', (nothing.Applied, nothing.Failed), (0, 2))
    check('no transaction when nothing applied', transactions, [])
    empty = WritebackService.Apply(doc, WritebackJson.Deserialize[ApplyRequest](u'{"changes":[]}'))
    check('empty batch', (empty.Applied, empty.Failed, transactions), (0, 0, []))
    no_doc = WritebackService.Apply(None, WritebackJson.Deserialize[ApplyRequest](u'{"changes":[%s]}' % change('a', g1, 'Comments', 'instance', '"x"')))
    check('no document', (no_doc.Applied, no_doc.Failed, no_doc.Results[0].Message), (0, 1, 'No model is open in Revit.'))

    # the fix shows up in a new export
    t = Transaction(doc, 'export 2')
    t.Start()
    doc.Export(OUT, 'wb2-%s.ifc' % YEAR, options)
    t.RollBack()
    text = io.open(os.path.join(OUT, 'wb2-%s.ifc' % YEAR), encoding='utf-8', errors='replace').read()
    line3 = [l for l in text.splitlines() if re.match(r"#\d+=\s*IFCWALL\w*\('%s'" % re.escape(g3), l)]
    log('re-exported wall 3:', line3)
    check('IfcName override reaches the IFC, same GlobalId', len(line3) == 1 and "'Wall A'" in line3[0], True)

    # ---- export setup -> mapping files ---------------------------------------------------
    map_type = IFCExportHelper.FindIFCExportConfigurationsMapType()
    log('config map type:', map_type)
    config_map = IFCExportHelper.CreateAndInitializeConfigMap(map_type, doc)
    names = [c.Name for c in config_map.Values]
    log('export setups:', names)
    builtin = ExportMapping.Load(doc, names[0])
    check('Load built-in setup', (list(builtin.Files), builtin.Note), ([], None))
    missing = ExportMapping.Load(doc, 'No Such Setup')
    check('Load unknown setup has a note, no files', (list(missing.Files), missing.Note is not None), ([], True))
    log('   note:', missing.Note)
    none = ExportMapping.Load(doc, None)
    check('Load without a name has a note', (list(none.Files), none.Note is not None), ([], True))
    try:
        initial = type(config_map)(config_map)
        custom = config_map[names[0]].Duplicate('WB Test Setup')
        custom.ExportUserDefinedPsets = True
        custom.ExportUserDefinedPsetsFileName = psets
        config_map.AddOrReplace(custom)
        gone = config_map[names[0]].Duplicate('WB Missing File')
        gone.ExportUserDefinedPsets = True
        gone.ExportUserDefinedPsetsFileName = os.path.join(OUT, 'does-not-exist.txt')
        config_map.AddOrReplace(gone)
        config_map.UpdateSavedConfigurations(initial)
        saved = ExportMapping.Load(doc, 'WB Test Setup')
        check('Load saved setup with a pset file', (list(saved.Files), saved.Note, saved.Mapping.Entries.Count), ([psets], None, 3))
        check('Load is case-insensitive on the setup name', list(ExportMapping.Load(doc, 'wb test setup').Files), [psets])
        lost = ExportMapping.Load(doc, 'WB Missing File')
        check('Load saved setup with a missing pset file', (list(lost.Files), lost.Note is not None), ([], True))
        log('   note:', lost.Note)
    except Exception:
        fails.append('saved setup')
        log('FAIL saved setup:', traceback.format_exc())

    # The HTTP layer needs an active document: see wb_http.py, which opens this model.
    model = os.path.join(OUT, 'wb-model-%s.rvt' % YEAR)
    if os.path.exists(model):
        os.remove(model)
    doc.SaveAs(model)
    log('saved', model)
    io.open(os.path.join(OUT, 'wb-model-%s.json' % YEAR), 'w', encoding='utf-8').write(
        u'{"g1":"%s","g2":"%s","g3":"%s","w1":%d,"w2":%d}' % (g1, g2, g3, w1.Id.Value, w2.Id.Value))

    # ---- workshared document: the ownership check runs -----------------------------------
    try:
        doc.EnableWorksharing('Shared Levels and Grids', 'Workset1')
        log('workshared:', doc.IsWorkshared)
        ws = WritebackService.Apply(doc, WritebackJson.Deserialize[ApplyRequest](
            u'{"changes":[%s]}' % change('ws', g1, 'Comments', 'instance', '"workshared"')))
        log(WritebackJson.Serialize(ws))
        check('apply in a workshared document', (doc.IsWorkshared, ws.Applied), (True, 1))
    except Exception:
        log('workshared check not run:', traceback.format_exc())

    app.DocumentChanged -= on_changed
    doc.Close(False)
    log('FAILED: %s' % fails if fails else 'ALL PASSED')
except Exception:
    log('EXCEPTION', traceback.format_exc())
    log('FAILED so far: %s' % fails)
finally:
    out.close()
