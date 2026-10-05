# -*- coding: utf-8 -*-
# Check of the write-back HTTP endpoints inside a real Revit. Started by run.ps1 through
# `pyrevit run` after model_check.py, whose saved model it opens; not meant to be run by hand.
#
# Starts the add-in's RevitApiServer on a spare port and talks to it over real HTTP. External
# events do not fire while a script holds the Revit thread, so the write-back queue's Execute
# is called by hand where Revit would call it: everything but Revit's own dispatch of the
# external event is exercised. Results go to http-log-<year>.txt in the output folder.
import clr
import io
import os
import time
import traceback

import System
from System.Reflection import BindingFlags
from Autodesk.Revit.DB import *

clr.AddReference('System.Net.Http')
from System.Net.Http import HttpClient, HttpMethod, HttpRequestMessage, StringContent


def json_string(value):
    # IronPython's json.dumps fails on non-ASCII paths ('unknown' codec), so escape by hand.
    out = []
    for ch in value:
        if ch == '"' or ch == chr(92):
            out.append(chr(92) + ch)
        elif 32 <= ord(ch) < 127:
            out.append(ch)
        else:
            out.append(chr(92) + 'u%04x' % ord(ch))
    return '"' + ''.join(out) + '"'


def escape_query(value):
    # System.Uri is not reachable from pyRevit's IronPython on .NET 8 without an extra assembly reference.
    safe = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.~'
    return ''.join(ch if ch in safe else ''.join('%%%02X' % b for b in bytearray(ch.encode('utf-8'))) for ch in value)

OUT = os.environ['IFCTESTER_WB_OUT']
DLL = os.environ['IFCTESTER_WB_DLL']
YEAR = __revit__.Application.VersionNumber
LOG = os.path.join(OUT, 'http-log-%s.txt' % YEAR)
PORT = 48991
BASE = 'http://localhost:%d' % PORT

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


try:
    uiapp = __revit__
    uidoc = uiapp.ActiveUIDocument
    if uidoc is None:
        # pyrevit run cannot open a model saved through the API ("Can not detect the Revit version")
        uidoc = uiapp.OpenAndActivateDocument(os.path.join(OUT, 'wb-model-%s.rvt' % YEAR))
    log('Revit', uiapp.Application.VersionNumber, 'active document:', uidoc.Document.Title if uidoc else None)
    doc = uidoc.Document

    clr.AddReferenceToFileAndPath(DLL)
    from IfcTesterRevit import RevitApiServer
    from IfcTesterRevit.Writeback import ExportRequestSettings, IfcGuid, WritebackService

    import json
    ids = json.load(io.open(os.path.join(OUT, 'wb-model-%s.json' % YEAR), encoding='utf-8'))
    wall = doc.GetElement(ElementId(System.Int64(ids['w1'])))
    wall2 = doc.GetElement(ElementId(System.Int64(ids['w2'])))
    guid, guid2 = ids['g1'], ids['g2']
    check('GlobalId survives save and reopen', IfcGuid.FromGuid(ExportUtils.GetExportId(doc, wall.Id)), guid)
    comments = lambda w: w.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).AsString()

    server = RevitApiServer(PORT)
    server.Start(uiapp)
    check('server running', server.IsRunning, True)
    queue = server.GetType().GetField('_writebackQueue', BindingFlags.NonPublic | BindingFlags.Instance).GetValue(server)

    transactions = []

    def on_changed(sender, args):
        transactions.append(list(args.GetTransactionNames()))

    uiapp.Application.DocumentChanged += on_changed

    client = HttpClient()
    client.Timeout = System.TimeSpan.FromSeconds(120)

    def pump(task, seconds=20):
        # Stand in for Revit: run queued write-back jobs until the HTTP call returns.
        deadline = time.time() + seconds
        while not task.IsCompleted and time.time() < deadline:
            queue.Execute(uiapp)
            time.sleep(0.1)
        return task.IsCompleted

    def get(path):
        task = client.GetAsync(BASE + path)
        if not pump(task):
            return None, 'timed out'
        return int(task.Result.StatusCode), task.Result.Content.ReadAsStringAsync().Result

    def post(path, body):
        task = client.PostAsync(BASE + path, StringContent(body, System.Text.Encoding.UTF8, 'application/json'))
        if not pump(task):
            return None, 'timed out', None
        response = task.Result
        return int(response.StatusCode), response.Content.ReadAsStringAsync().Result, response

    # preflight
    options = HttpRequestMessage(HttpMethod.Options, BASE + '/apply-changes')
    options.Headers.Add('Origin', 'http://localhost:5173')
    options.Headers.Add('Access-Control-Request-Method', 'POST')
    options.Headers.Add('Access-Control-Request-Headers', 'content-type')
    pre = client.SendAsync(options).Result
    check('preflight', (int(pre.StatusCode),
                        list(pre.Headers.GetValues('Access-Control-Allow-Origin')),
                        list(pre.Headers.GetValues('Access-Control-Allow-Headers'))), (200, ['http://localhost:5173'], ['Content-Type']))
    options = HttpRequestMessage(HttpMethod.Options, BASE + '/apply-changes')
    options.Headers.Add('Origin', 'https://evil.example')
    options.Headers.Add('Access-Control-Request-Method', 'POST')
    pre = client.SendAsync(options).Result
    check('foreign preflight -> 403 without CORS', (int(pre.StatusCode), pre.Headers.Contains('Access-Control-Allow-Origin')), (403, False))

    status, body, response = post('/resolve-parameters',
                                  u'{"items":[{"key":"k1","globalId":"%s","facet":"property","propertySet":"Pset_WallCommon","name":"FireRating"},'
                                  u'{"key":"k2","globalId":"%s","facet":"property","propertySet":"X","name":"Comments"}]}' % (guid, guid))
    log('resolve ->', status, body)
    check('resolve status', status, 200)
    check('resolve without Origin: no CORS header + content type', (response.Headers.Contains('Access-Control-Allow-Origin'), response.Content.Headers.ContentType.MediaType), (False, 'application/json'))
    check('resolve body', ('"key":"k1","found":true' in body, '"parameter":"Fire Rating","scope":"type"' in body,
                           '"parameter":"Comments","scope":"instance"' in body, '"elementId":%d' % wall.Id.Value in body), (True, True, True, True))

    # a value outside ASCII must survive the body encoding
    status, body, response = post('/apply-changes',
                                  u'{"changes":[{"key":"c1","globalId":"%s","parameter":"Comments","scope":"instance","value":"Brandvägg åäö"},'
                                  u'{"key":"c2","globalId":"0000000000000000000000","parameter":"Comments","scope":"instance","value":"x"}]}' % guid)
    log('apply ->', status, body)
    check('apply status', status, 200)
    check('apply body', ('"applied":1,"failed":1' in body, '"key":"c1","ok":true' in body, '"key":"c2","ok":false' in body), (True, True, True))
    check('apply wrote to the active document', comments(wall), u'Brandvägg åäö')
    check('one named transaction', transactions, [[WritebackService.TransactionName]])

    # a simple POST from a website the user has open: refused before it reaches Revit
    foreign = HttpRequestMessage(HttpMethod.Post, BASE + '/apply-changes')
    foreign.Headers.Add('Origin', 'https://evil.example')
    foreign.Content = StringContent(u'{"changes":[{"key":"f","globalId":"%s","parameter":"Comments","scope":"instance","value":"foreign"}]}' % guid,
                                    System.Text.Encoding.UTF8, 'text/plain')
    sent = client.SendAsync(foreign)
    pump(sent)
    check('foreign origin POST -> 403 without CORS, nothing written',
          (int(sent.Result.StatusCode), sent.Result.Headers.Contains('Access-Control-Allow-Origin'), comments(wall)), (403, False, u'Brandvägg åäö'))
    allowed = HttpRequestMessage(HttpMethod.Get, BASE + '/pset-files?dir=' + escape_query(OUT))
    allowed.Headers.Add('Origin', 'http://localhost:%d' % PORT)
    sent = client.SendAsync(allowed).Result
    check('own origin -> CORS header for it', (int(sent.StatusCode), list(sent.Headers.GetValues('Access-Control-Allow-Origin'))), (200, ['http://localhost:%d' % PORT]))

    # mapping file of a saved export setup: named in the request, then taken from the last export
    brand = u'{"key":"b","globalId":"%s","facet":"property","propertySet":"Custom","name":"Brand"}' % guid
    status, body, response = post('/resolve-parameters', u'{"configuration":"WB Test Setup","items":[%s]}' % brand)
    log('resolve with configuration ->', status, body)
    check('setup named in the request', ('"source":"pset-mapping-file"' in body, '"configuration":"WB Test Setup"' in body, 'psets.txt"]' in body), (True, True, True))
    status, body, response = post('/resolve-parameters', u'{"items":[%s]}' % brand)
    check('no setup known -> no mapping, with a note', ('"candidates":[]' in body, '"mappingFiles":[]' in body, '"mappingNote":"No IFC export setup' in body), (True, True, True))
    last_export = server.GetType().GetField('_lastExport', BindingFlags.NonPublic | BindingFlags.Instance)
    last_export.SetValue(server, ExportRequestSettings('WB Test Setup', None, None))
    status, body, response = post('/resolve-parameters', u'{"items":[%s]}' % brand)
    check('setup of the last export', ('"source":"pset-mapping-file"' in body, '"configuration":"WB Test Setup"' in body), (True, True))

    # a property set file override: the setup's own file is missing, the override replaces it
    psets = os.path.join(OUT, 'psets.txt')
    status, body, response = post('/resolve-parameters', u'{"configuration":"WB Missing File","items":[%s]}' % brand)
    check('missing setup file -> no mapping, with a note', ('"mappingFiles":[]' in body, 'was not found' in body), (True, True))
    status, body, response = post('/resolve-parameters', u'{"configuration":"WB Missing File","psetFile":%s,"items":[%s]}' % (json.dumps(psets), brand))
    check('override named in the request', ('"source":"pset-mapping-file"' in body, '"mappingNote":null' in body, 'psets.txt"]' in body), (True, True, True))
    last_export.SetValue(server, ExportRequestSettings('WB Missing File', psets, None))
    status, body, response = post('/resolve-parameters', u'{"items":[%s]}' % brand)
    check('override of the last export', ('"source":"pset-mapping-file"' in body, '"configuration":"WB Missing File"' in body), (True, True))
    status, body, response = post('/resolve-parameters', u'{"configuration":"WB Test Setup","items":[%s]}' % brand)
    check('another setup drops the last override', ('"configuration":"WB Test Setup"' in body, 'psets.txt"]' in body), (True, True))

    # the files a setup exports with, and the folder listing for the override dropdown
    status, body = get('/ifc-configuration-files?name=WB%20Missing%20File')
    log('configuration files ->', status, body)
    check('setup files', (status, '"psetFileExists":false' in body, 'does-not-exist.txt' in body, 'The setup' in body), (200, True, True, True))
    status, body = get('/ifc-configuration-files?name=No%20Such%20Setup')
    check('unknown setup -> 404', status, 404)
    status, body = get('/pset-files?dir=' + escape_query(OUT))
    log('pset files ->', status, body)
    check('folder listing', (status, '"name":"psets.txt"' in body, '"modified":"' in body), (200, True, True))
    status, body = get('/pset-files?dir=' + escape_query(os.path.join(OUT, 'no-such-folder')))
    check('missing folder -> 400', status, 400)
    status, body = get('/pset-files')
    check('no folder -> 400', status, 400)

    # an override file that does not exist fails the export before Revit is asked to export
    status, body, response = post('/export-ifc', u'{"configuration":"WB Test Setup","psetFile":%s}' % json_string(os.path.join(OUT, u'saknas ' + unichr(0xe5) + unichr(0xe4) + unichr(0xf6) + u'.txt')))
    job = json.loads(body)['jobId'] if status == 200 else None
    status, body = get('/export-status/%s' % job)
    log('export with a missing override ->', status, body)
    # System.Text.Json escapes non-ASCII, so only the ASCII part of the name is looked for
    check('missing override fails the job', ('"status":"failed"' in body, 'saknas ' in body, 'Nothing was exported' in body), (True, True, True))

    # two overlapping requests keep their own results
    del transactions[:]
    first = client.PostAsync(BASE + '/resolve-parameters', StringContent(
        u'{"items":[{"key":"first","globalId":"%s","facet":"attribute","name":"Name"}]}' % guid, System.Text.Encoding.UTF8, 'application/json'))
    second = client.PostAsync(BASE + '/apply-changes', StringContent(
        u'{"changes":[{"key":"second","globalId":"%s","parameter":"Comments","scope":"instance","value":"queued"}]}' % guid2, System.Text.Encoding.UTF8, 'application/json'))
    time.sleep(1.5)
    check('both wait for Revit', (first.IsCompleted, second.IsCompleted), (False, False))
    check('overlapping requests complete', (pump(first), pump(second)), (True, True))
    body1 = first.Result.Content.ReadAsStringAsync().Result
    body2 = second.Result.Content.ReadAsStringAsync().Result
    check('overlapping results are not mixed', ('"key":"first"' in body1 and '"IfcName"' in body1, '"key":"second"' in body2 and '"applied":1' in body2), (True, True))
    check('queued apply wrote', comments(wall2), 'queued')

    # Revit busy: the request gives up after 30 s and its job must not run afterwards
    del transactions[:]
    started = time.time()
    busy = client.PostAsync(BASE + '/apply-changes', StringContent(
        u'{"changes":[{"key":"late","globalId":"%s","parameter":"Comments","scope":"instance","value":"too late"}]}' % guid2, System.Text.Encoding.UTF8, 'application/json'))
    check('busy request answers on its own', busy.Wait(45000), True)
    busy_body = busy.Result.Content.ReadAsStringAsync().Result
    log('busy after %.0fs ->' % (time.time() - started), int(busy.Result.StatusCode), busy_body)
    check('busy -> 503 with error', (int(busy.Result.StatusCode), '"error"' in busy_body), (503, True))
    queue.Execute(uiapp)
    check('withdrawn job did not run', (comments(wall2), transactions), ('queued', []))

    status, body, response = post('/apply-changes', u'{"changes": oops')
    check('bad json -> 400 with error', (status, '"error"' in body), (400, True))
    status, body, response = post('/resolve-parameters', u'{}')
    check('missing items -> 400', status, 400)
    log('   ', body)

    # pset builder: the model's parameters, suggestions and saving a file
    started = time.time()
    status, body = get('/model-parameters')
    log('model-parameters -> %s after %.0f ms, %d bytes' % (status, (time.time() - started) * 1000, len(body or '')))
    params = json.loads(body) if status == 200 else {}
    brand = [x for x in params.get('parameters', []) if x['name'] == 'Brandklass']
    check('model-parameters', (status, params.get('elementCount', 0) >= 6, [(x['origin'], x['scope'], x['instanceCount']) for x in brand]), (200, True, [('shared', 'instance', 6)]))
    log('   elapsedMs', params.get('elapsedMs'), 'document', params.get('document'))

    status, body, response = post('/pset-suggestions', u'{"items":[{"key":"b","propertySet":"Projekt","name":"Brandklass","entities":["IfcWall"]},'
                                                        u'{"key":"f","propertySet":"Pset_WallCommon","name":"FireRating","entities":["IfcWall"]},'
                                                        u'{"key":"n","propertySet":"X","name":"DoesNotExist","entities":["IfcWall"]}]}')
    log('pset-suggestions ->', status, body)
    suggested = dict((i['key'], i) for i in json.loads(body)['items']) if status == 200 else {}
    check('pset-suggestions', (status, [(s['parameter'], s['scope']) for s in suggested.get('b', {}).get('suggestions', [])][:1],
                               # FIRE_RATING and DOOR_FIRE_RATING are one enum value; the name Revit reports is either
                               [(s['parameter'], s['scope'], s['builtInParameter'] in ('FIRE_RATING', 'DOOR_FIRE_RATING')) for s in suggested.get('f', {}).get('suggestions', [])][:1],
                               suggested.get('n', {}).get('suggestions')),
          (200, [('Brandklass', 'instance')], [('Fire Rating', 'type', True)], []))
    status, body, response = post('/pset-suggestions', u'{}')
    check('pset-suggestions without items -> 400', status, 400)

    save_folder = os.path.join(OUT, 'psets-http-%s' % YEAR)
    if os.path.isdir(save_folder):
        for name in os.listdir(save_folder):
            os.remove(os.path.join(save_folder, name))
    server.PsetSaveFolder = save_folder
    content = u'PropertySet:\tProjekt\tI\tIfcWall\r\n\tBrandklass\tLabel\tBrandklass ' + unichr(0xe5) + unichr(0xe4) + unichr(0xf6) + u'\r\n'
    status, body, response = post('/pset-files/save', u'{"name":"Projekt test","content":%s}' % json_string(content))
    log('save ->', status, body)
    saved = os.path.join(save_folder, 'Projekt test.txt')
    raw = open(saved, 'rb').read() if os.path.isfile(saved) else b''
    check('save to the default folder', (status, '"overwritten":false' in body, os.path.isfile(saved)), (200, True, True))
    check('saved as UTF-8 without BOM, CRLF kept', (raw[:3] != b'\xef\xbb\xbf', raw.count(b'\r\n'), content.encode('utf-8') == raw), (True, 2, True))
    status, body, response = post('/pset-files/save', u'{"name":"Projekt test","content":"x"}')
    check('save refuses to overwrite -> 409 with the path', (status, '"error"' in body, 'Projekt test.txt' in body), (409, True, True))
    status, body, response = post('/pset-files/save', u'{"path":%s,"content":"y","overwrite":true}' % json_string(saved))
    check('save with overwrite', (status, '"overwritten":true' in body, open(saved, 'rb').read()), (200, True, b'y'))
    status, body, response = post('/pset-files/save', u'{"path":%s,"content":"y"}' % json_string(os.path.join(OUT, 'no-such-folder', 'a.txt')))
    check('save to a missing folder -> 400', status, 400)
    status, body, response = post('/pset-files/save', u'{"content": oops')
    check('save with bad json -> 400', status, 400)

    # reading a pset file: only files the add-in has named to the page or that are in the save folder
    status, body = get('/pset-files/read?path=' + escape_query(saved))
    check('read a saved file', (status, '"content":"y"' in body), (200, True))
    status, body = get('/pset-files/read?path=' + escape_query(psets))
    check('read the setup file the resolve named', (status, '"content":"' in body), (200, True))
    stray = os.path.join(OUT, 'not-named.txt')
    with io.open(stray, 'w', encoding='utf-8') as f:
        f.write(u'secret')
    status, body = get('/pset-files/read?path=' + escape_query(stray))
    check('read a file nobody named -> 403', (status, 'secret' in body), (403, False))
    status, body = get('/pset-files/read?path=' + escape_query(os.path.join(save_folder, '..', 'not-named.txt')))
    check('read out of the save folder with .. -> 403', status, 403)

    # model memory: keyed by the document's path (this model is not workshared), paths only
    from IfcTesterRevit.Writeback import ModelMemoryStore
    memory_file = os.path.join(OUT, 'model-memory-%s.json' % YEAR)
    if os.path.isfile(memory_file):
        os.remove(memory_file)
    server.ModelMemory = ModelMemoryStore(memory_file)
    status, body = get('/model-memory')
    log('model-memory ->', status, body)
    check('model memory: the open model, nothing remembered', (status, '"workshared":false' in body, '"remembered":null' in body, 'wb-model-%s.rvt' % YEAR in body), (200, True, True, True))
    ids_file = os.path.join(OUT, u'spec ' + unichr(0xe5) + unichr(0xe4) + unichr(0xf6) + u'.ids')
    with io.open(ids_file, 'w', encoding='utf-8') as f:
        f.write(u'<ids>V' + unichr(0xe5) + u'ning</ids>')
    status, body, response = post('/model-memory', u'{"idsFile":%s}' % json_string(ids_file))
    check('model memory: an IDS not opened through the add-in is refused', status, 403)
    status, body = get('/ids-files/read?path=' + escape_query(ids_file))
    check('ids read: not picked -> 403', status, 403)
    # what POST /ids-files/pick does after Revit's file dialog, which cannot be clicked in a script
    server.GetType().GetField('_readable', BindingFlags.NonPublic | BindingFlags.Instance).GetValue(server).Allow(ids_file)
    status, body, response = post('/model-memory', u'{"idsFile":%s}' % json_string(ids_file))
    log('model-memory remember ->', status, body)
    check('model memory: remembered, checked on disk', (status, '"exists":true' in body, '"name":"spec ' in body), (200, True, True))
    check('model memory: written to the file', os.path.isfile(memory_file), True)
    status, body = get('/ids-files/read?path=' + escape_query(ids_file))
    # System.Text.Json escapes < > and non-ASCII, so compare the parsed content
    check('ids read: picked file', (status, json.loads(body)['content'] if status == 200 else body), (200, u'<ids>V' + unichr(0xe5) + u'ning</ids>'))
    status, body = get('/ids-files/read?path=' + escape_query(stray))
    check('ids read: not an IDS -> 400', status, 400)

    status, body = get('/ifc-configuration-files?name=WB%20Test%20Setup')
    log('configuration files with schema ->', status, body)
    check('setup files name the IFC version', (status, '"ifcVersion":"' in body), (200, True))

    # /status: its config preload waits on an external event that cannot fire here, so it answers
    # after its own retries; only the new field is of interest.
    started = time.time()
    status_task = client.GetAsync(BASE + '/status')
    done = pump(status_task, 60)
    status_body = status_task.Result.Content.ReadAsStringAsync().Result if done else 'timed out'
    log('status after %.0fs ->' % (time.time() - started), status_body)
    check('status reports its capabilities', '"capabilities":["writeback","export-overrides","pset-builder","element-inspector","model-memory"]' in status_body, True)
    check('status keeps its other fields', ('"connected":true' in status_body, '"version":"1.4.0"' in status_body, '"configsReady":' in status_body), (True, True, True))

    uiapp.Application.DocumentChanged -= on_changed
    server.Stop()
    server.Dispose()
    log('FAILED: %s' % fails if fails else 'ALL PASSED')
except Exception:
    log('EXCEPTION', traceback.format_exc())
    log('FAILED so far: %s' % fails)
finally:
    out.close()
