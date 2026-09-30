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
    from IfcTesterRevit.Writeback import IfcGuid, WritebackService

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
                        list(pre.Headers.GetValues('Access-Control-Allow-Headers'))), (200, ['*'], ['Content-Type']))

    status, body, response = post('/resolve-parameters',
                                  u'{"items":[{"key":"k1","globalId":"%s","facet":"property","propertySet":"Pset_WallCommon","name":"FireRating"},'
                                  u'{"key":"k2","globalId":"%s","facet":"property","propertySet":"X","name":"Comments"}]}' % (guid, guid))
    log('resolve ->', status, body)
    check('resolve status', status, 200)
    check('resolve CORS + content type', (list(response.Headers.GetValues('Access-Control-Allow-Origin')), response.Content.Headers.ContentType.MediaType), (['*'], 'application/json'))
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

    # mapping file of a saved export setup: named in the request, then taken from the last export
    brand = u'{"key":"b","globalId":"%s","facet":"property","propertySet":"Custom","name":"Brand"}' % guid
    status, body, response = post('/resolve-parameters', u'{"configuration":"WB Test Setup","items":[%s]}' % brand)
    log('resolve with configuration ->', status, body)
    check('setup named in the request', ('"source":"pset-mapping-file"' in body, '"configuration":"WB Test Setup"' in body, 'psets.txt"]' in body), (True, True, True))
    status, body, response = post('/resolve-parameters', u'{"items":[%s]}' % brand)
    check('no setup known -> no mapping, with a note', ('"candidates":[]' in body, '"mappingFiles":[]' in body, '"mappingNote":"No IFC export setup' in body), (True, True, True))
    server.GetType().GetField('_lastExportConfiguration', BindingFlags.NonPublic | BindingFlags.Instance).SetValue(server, 'WB Test Setup')
    status, body, response = post('/resolve-parameters', u'{"items":[%s]}' % brand)
    check('setup of the last export', ('"source":"pset-mapping-file"' in body, '"configuration":"WB Test Setup"' in body), (True, True))

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

    # /status: its config preload waits on an external event that cannot fire here, so it answers
    # after its own retries; only the new field is of interest.
    started = time.time()
    status_task = client.GetAsync(BASE + '/status')
    done = pump(status_task, 60)
    status_body = status_task.Result.Content.ReadAsStringAsync().Result if done else 'timed out'
    log('status after %.0fs ->' % (time.time() - started), status_body)
    check('status reports the writeback capability', '"capabilities":["writeback"]' in status_body, True)
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
