import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { type Page, type Route, expect, test } from '@playwright/test';

const FIX = join(import.meta.dirname, '..', 'fixtures');
const read = (name: string) => readFileSync(join(FIX, name), 'utf8');

const REVIT = 'http://localhost:48881';
const CORS = {
    'access-control-allow-origin': '*',
    'access-control-allow-methods': 'GET, POST, OPTIONS',
    'access-control-allow-headers': 'Content-Type',
};

// In the IFC4 fixture W1 carries Pset_WallCommon (FireRating EI60, IsExternal TRUE); W2 has no sets.
const W2 = '3M0lsbuhnETg9eDScRBzRN';

test.beforeEach(async ({ page }) => {
    if (!process.env.E2E_ROUTE_NETWORK) return;
    await page.route(/^https:\/\/(pypi\.org|files\.pythonhosted\.org|cdn\.jsdelivr\.net)\//, async (route) => {
        const r = await fetch(route.request().url());
        const headers = Object.fromEntries(
            [...r.headers].filter(([k]) => !/^(content-encoding|content-length|transfer-encoding)$/i.test(k)),
        );
        await route.fulfill({ status: r.status, headers: { ...headers, 'access-control-allow-origin': '*' }, body: Buffer.from(await r.arrayBuffer()) });
    });
});

const candidate = (parameter: string, scope: 'instance' | 'type', value: string) => ({
    parameter,
    scope,
    storageType: 'string',
    value,
    hasValue: value !== '',
    readOnly: false,
    source: 'pset-mapping-file',
});

const line = (propertySet: string, propertyName: string, parameterName: string, entities: string[], candidates: unknown[]) => ({
    propertySet,
    propertyName,
    parameterName,
    builtInParameter: null,
    entities,
    origin: 'pset-file',
    onInstance: true,
    onType: false,
    candidates,
});

/** The add-in's /status and POST /element-parameters for a wall; records the request bodies. */
async function mockRevit(page: Page, capabilities: string[]) {
    const bodies: Record<string, unknown>[] = [];
    const json = (route: Route, body: unknown) => route.fulfill({ status: 200, headers: CORS, contentType: 'application/json', body: JSON.stringify(body) });

    await page.route(`${REVIT}/**`, async (route) => {
        const request = route.request();
        const path = new URL(request.url()).pathname;
        if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS });
        if (path === '/status') return json(route, { status: 'ok', connected: true, configsReady: true, version: '1.4.0', capabilities });
        if (path === '/ifc-configurations') return json(route, { configurations: ['IFC4 Reference View'] });
        if (path === '/element-parameters') {
            const body = request.postDataJSON() as Record<string, unknown>;
            bodies.push(body);
            return json(route, {
                found: true,
                message: null,
                elementId: 123456,
                elementName: 'Generic - 200mm',
                category: 'Walls',
                typeId: 777,
                typeName: 'Basic Wall: Generic - 200mm',
                parameters: [
                    { name: 'Mark', scope: 'instance', group: 'Identity Data', storageType: 'string', value: body.globalId === W2 ? 'W2' : 'W1', hasValue: true, readOnly: false, shared: false, builtIn: 'ALL_MODEL_MARK' },
                    { name: 'Comments', scope: 'instance', group: 'Identity Data', storageType: 'string', value: '', hasValue: false, readOnly: false, shared: false, builtIn: 'ALL_MODEL_INSTANCE_COMMENTS' },
                    { name: 'Fire Rating', scope: 'type', group: 'Identity Data', storageType: 'string', value: '', hasValue: false, readOnly: false, shared: true, builtIn: null },
                ],
                mappings: [
                    line('Attribute Mapping', 'Name', 'Mark', ['IfcElement'], [candidate('Mark', 'instance', body.globalId === W2 ? 'W2' : 'W1')]),
                    line('Pset_WallCommon', 'FireRating', 'Fire Rating', ['IfcWall'], [candidate('Fire Rating', 'type', '')]),
                    line('Byggpartner', 'TypeID', 'TypeID', ['IFCWALL'], []),
                    line('Byggpartner', 'Ljudklass', 'Ljudklass', ['IfcWall'], []),
                    line('Byggpartner', 'Littera', 'Littera', ['IfcWall'], [candidate('Littera', 'type', 'V01')]),
                    line('Doors', 'Width', 'Width', ['IfcDoor'], []),
                ],
                configuration: 'IFC4 Reference View',
                mappingFiles: ['C:\\psets\\BS-Pset-A.txt'],
                mappingNote: null,
                useTypePropertiesInInstancePsets: false,
            });
        }
        return route.fulfill({ status: 404, headers: CORS, contentType: 'application/json', body: JSON.stringify({ error: `Not mocked: ${path}` }) });
    });
    return bodies;
}

async function openAuditedFixture(page: Page) {
    await page.goto('/?source=revit');
    await page.evaluate(
        async ({ ifc, ids }) => {
            const wasm = (await import('/src/modules/wasm/index.ts')).default;
            const IDS = await import('/src/modules/api/ids.svelte.ts');
            const API = await import('/src/modules/api/api.svelte.ts');

            await wasm.init();
            const docId = 'e2e-inspector';
            IDS.Module.documents[docId] = (await wasm.openIDS(ids)) as never;
            IDS.setDocumentState(docId, { viewMode: 'viewer' });
            IDS.Module.activeDocument = docId;

            await API.loadIfc(new File([ifc], 'model.ifc'));
            await API.runAudit();
        },
        { ifc: read('model-ifc4.ifc'), ids: read('specs-ifc4.ids') },
    );
    await expect(page.getByRole('heading', { name: 'IFC4 regression' })).toBeVisible();
}

async function expandRequirement(page: Page, name: string) {
    const card = page.locator('.specification-card').filter({ has: page.getByRole('heading', { name, exact: true }) });
    await card.getByRole('heading', { name, exact: true }).click();
    await card.locator('button.facet-header').click();
    return card;
}

test.describe('element inspector', () => {
    test('a failed element shows its IFC properties next to the Revit parameters the pset file maps', async ({ page }) => {
        const bodies = await mockRevit(page, ['writeback', 'element-inspector']);
        await openAuditedFixture(page);

        const card = await expandRequirement(page, 'wall fire rating property');
        await card.locator('.entity-table-section.fail').getByRole('button', { name: 'Inspect element' }).click();

        const inspector = page.getByRole('complementary', { name: 'Element inspector' });
        await expect(inspector.getByRole('heading', { name: 'W2' })).toBeVisible();
        await expect(inspector.getByText('IfcWall', { exact: true })).toBeVisible();
        await expect(inspector.getByText('BS-Pset-A.txt')).toBeVisible();

        // The Tag of a Revit export is the element id; this fixture has none, so only the GlobalId is sent
        expect(bodies).toHaveLength(1);
        expect(bodies[0]).toMatchObject({ globalId: W2 });
        expect(bodies[0].elementId).toBeUndefined();

        // The requirement's property: mapped to an empty type parameter, so absent from the IFC
        const fireRating = inspector.locator('[data-row="pset_wallcommon|firerating"]');
        await expect(fireRating).toHaveClass(/focused/);
        await expect(fireRating).toHaveClass(/status-empty/);
        await expect(fireRating.getByText('not in the IFC')).toBeVisible();
        await expect(fireRating.getByText('"Fire Rating" (type) has no value in Revit.')).toBeVisible();

        // An attribute reached through a supertype in the set's entity list (IfcWall is an IfcElement)
        const name = inspector.locator('[data-row="attributes|name"]');
        await expect(name).toHaveClass(/status-exported/);
        await expect(name.getByText('Mark')).toBeVisible();

        // A set whose entity name only matches ignoring case is what the exporter skips
        const typeId = inspector.locator('[data-row="byggpartner|typeid"]');
        await expect(typeId).toHaveClass(/status-wrong-case/);
        await expect(typeId.getByText(/The set lists "IFCWALL".*write it "IfcWall"/)).toBeVisible();

        // The status names the cause on the Revit side: no such parameter, or a value the IFC lacks
        const ljudklass = inspector.locator('[data-row="byggpartner|ljudklass"]');
        await expect(ljudklass).toHaveClass(/status-no-parameter/);
        await expect(ljudklass.getByText('No parameter in Revit')).toBeVisible();
        const littera = inspector.locator('[data-row="byggpartner|littera"]');
        await expect(littera).toHaveClass(/status-not-exported/);
        await expect(littera.getByText('Not exported')).toBeVisible();
        await expect(fireRating.getByText('Empty in Revit')).toBeVisible();

        // A set for another class does not show
        await expect(inspector.locator('[data-row="doors|width"]')).toHaveCount(0);

        // The Revit side shows at the same time, lists every parameter and says which IFC property each one feeds
        const revit = inspector.getByRole('region', { name: 'Revit parameters' });
        await expect(inspector.getByRole('region', { name: 'IFC properties' })).toBeVisible();
        await expect(revit.locator('.param.used')).toHaveCount(2);
        const fireRatingParam = revit.locator('.param', { hasText: 'Fire Rating' });
        await expect(fireRatingParam.getByText('→ Pset_WallCommon.FireRating')).toBeVisible();
        await expect(revit.locator('.param', { hasText: 'Comments' })).not.toHaveClass(/used/);

        // Hovering one side lights up its partner on the other
        await fireRating.hover();
        await expect(fireRatingParam).toHaveClass(/lit/);
        await revit.locator('.param', { hasText: 'Mark' }).hover();
        await expect(name).toHaveClass(/lit/);
        await expect(fireRatingParam).not.toHaveClass(/lit/);

        // Mapped only narrows the Revit side to the parameters the pset file reads
        await revit.getByLabel('Mapped only').check();
        await expect(revit.locator('.param')).toHaveCount(2);

        await inspector.getByRole('button', { name: 'Close inspector' }).click();
        await expect(inspector).toHaveCount(0);
    });

    test('without the add-in capability the IFC side still shows', async ({ page }) => {
        await mockRevit(page, ['writeback']);
        await openAuditedFixture(page);

        const card = await expandRequirement(page, 'wall fire rating property');
        await card.locator('.entity-table-section.pass').getByRole('button', { name: 'Inspect element' }).click();

        const inspector = page.getByRole('complementary', { name: 'Element inspector' });
        await expect(inspector.getByRole('heading', { name: 'W1' })).toBeVisible();
        await expect(inspector.getByText('cannot list element parameters')).toBeVisible();

        const fireRating = inspector.locator('[data-row="pset_wallcommon|firerating"]');
        await expect(fireRating.getByText('EI60')).toBeVisible();
        await expect(inspector.locator('[data-row="pset_wallcommon|isexternal"]').getByText('TRUE')).toBeVisible();
        await expect(inspector.getByRole('region', { name: 'Revit parameters' }).getByText('cannot list element parameters')).toBeVisible();
    });

    test('linking rewrites lines of the pset file, which is saved and used for the next export', async ({ page }) => {
        const revit = await mockLinkingRevit(page);
        await openAuditedFixture(page);

        const card = await expandRequirement(page, 'wall fire rating property');
        await card.locator('.entity-table-section.fail').getByRole('button', { name: 'Inspect element' }).click();
        const inspector = page.getByRole('complementary', { name: 'Element inspector' });
        const ifcSide = inspector.getByRole('region', { name: 'IFC properties' });
        const revitSide = inspector.getByRole('region', { name: 'Revit parameters' });
        const linkBar = inspector.getByRole('region', { name: 'Link' });
        const lastContent = () => splitLines(revit.bodies.at(-1)?.psetFileContent as string);

        // Remap: Ljudklass reads a parameter the wall lacks; link it to Ljud
        const ljudklass = ifcSide.locator('[data-row="byggpartner|ljudklass"]');
        await expect(ljudklass).toHaveClass(/status-no-parameter/);
        await ljudklass.locator('.prop').click();
        await expect(linkBar.getByText('Pick a Revit parameter on the right')).toBeVisible();
        await revitSide.locator('.param', { hasText: 'Ljud' }).locator('.prop').click();
        await expect(linkBar.getByText(/Line 5 of BS-Pset-A\.txt/)).toBeVisible();
        await expect(linkBar.getByText(/The set applies to IfcElement/)).toBeVisible();
        await linkBar.getByRole('button', { name: 'Link' }).click();

        await expect(ljudklass).toHaveClass(/status-not-exported/);
        expect(revit.bodies.at(-1)?.psetFileContentFor).toBe(PSET_PATH);
        // Only the parameter column changes; the tabs, the other lines and the CRLF line ends stay
        expect(lastContent()[4]).toBe('\tLjudklass\t\tLabel\tLjud');
        expect(revit.bodies.at(-1)?.psetFileContent).toBe(PSET_FILE.replace('\tLjudklass\t\tLabel\tLjudklass', '\tLjudklass\t\tLabel\tLjud'));
        await expect(inspector.getByText('1 unsaved change')).toBeVisible();

        // A set whose entity is in the wrong case is fixed in its header
        const tjocklek = ifcSide.locator('[data-row="skikt|tjocklek"]');
        await tjocklek.getByRole('button', { name: 'Fix case: IFCWALL → IfcWall' }).click();
        await expect(tjocklek).toHaveClass(/status-no-parameter/);
        expect(lastContent()[5]).toBe('PropertySet:\tSkikt\tI\tIfcWall');

        // An attribute with no "Attribute Mapping" set has no line to change or set to add one to
        await ifcSide.locator('[data-row="attributes|name"] .prop').click();
        await revitSide.locator('.param', { hasText: 'Mark' }).locator('.prop').click();
        await expect(linkBar.getByText('The pset file has no set "Attribute Mapping" that reaches IfcWall, so there is no set to add the line to.')).toBeVisible();
        await expect(linkBar.getByRole('button', { name: 'Link' })).toBeDisabled();
        await linkBar.getByRole('button', { name: 'Cancel' }).click();

        // Add: W1 has Pset_WallCommon.IsExternal with no line; a line is added to the set, data type picked
        await page.locator('.entity-table-section.pass').getByRole('button', { name: 'Inspect element' }).click();
        await expect(inspector.getByRole('heading', { name: 'W1' })).toBeVisible();
        await ifcSide.locator('[data-row="pset_wallcommon|isexternal"] .prop').click();
        await revitSide.locator('.param', { hasText: 'Mark' }).locator('.prop').click();
        await expect(linkBar.getByText(/New line 4 in set "Pset_WallCommon"/)).toBeVisible();
        await linkBar.getByLabel('Data type').selectOption('Boolean');
        await linkBar.getByRole('button', { name: 'Link' }).click();
        await expect(inspector.getByText('3 unsaved changes')).toBeVisible();
        expect(lastContent()[3]).toBe('\tIsExternal\tBoolean\tBuiltInParameter.ALL_MODEL_MARK');

        // Review lists the changes; Undo takes the last one back
        await inspector.getByRole('button', { name: 'Review' }).click();
        await expect(inspector.locator('.changes li')).toHaveCount(3);
        await inspector.getByRole('button', { name: 'Undo' }).click();
        await expect(inspector.getByText('2 unsaved changes')).toBeVisible();
        await expect.poll(() => lastContent().some((line) => line.includes('IsExternal'))).toBe(false);

        // Save next to the original; it exists, so replacing it is confirmed first
        await expect(inspector.getByLabel('Save as')).toHaveValue('C:\\psets\\BS-Pset-A-edited.txt');
        await inspector.getByRole('button', { name: 'Save', exact: true }).click();
        await inspector.getByRole('button', { name: 'Replace existing file' }).click();
        await expect(inspector.getByText('Saved as')).toBeVisible();
        expect(revit.saves.map((s) => s.overwrite)).toEqual([false, true]);
        expect(revit.saves[1].path).toBe('C:\\psets\\BS-Pset-A-edited.txt');
        expect(revit.saves[1].content).toBe(
            PSET_FILE.replace('\tLjudklass\t\tLabel\tLjudklass', '\tLjudklass\t\tLabel\tLjud').replace('Skikt\tI\tIFCWALL', 'Skikt\tI\tIfcWall'),
        );
        const override = await page.evaluate(async () => (await import('/src/modules/api/revit.svelte.js')).Revit.exportOverrides.psetFile);
        expect(override).toBe('C:\\psets\\BS-Pset-A-edited.txt');
    });

    test('a link can be kept to the element class by splitting the set, or every set split per class at once', async ({ page }) => {
        const revit = await mockLinkingRevit(page);
        await openAuditedFixture(page);

        const card = await expandRequirement(page, 'wall fire rating property');
        await card.locator('.entity-table-section.fail').getByRole('button', { name: 'Inspect element' }).click();
        const inspector = page.getByRole('complementary', { name: 'Element inspector' });
        const ifcSide = inspector.getByRole('region', { name: 'IFC properties' });
        const revitSide = inspector.getByRole('region', { name: 'Revit parameters' });
        const linkBar = inspector.getByRole('region', { name: 'Link' });
        const lastContent = () => splitLines(revit.bodies.at(-1)?.psetFileContent as string);

        // Material lists IfcWall, IfcSlab and IfcBeam: by default only IfcWall changes
        const klass = ifcSide.locator('[data-row="material|klass"]');
        await klass.locator('.prop').click();
        await revitSide.locator('.param', { hasText: 'Ljud' }).locator('.prop').click();
        await expect(linkBar.getByLabel('Only IfcWall')).toBeChecked();
        await expect(linkBar.getByText(/IfcWall leaves its header and gets its own copy of the set \(2 lines\)/)).toBeVisible();
        await linkBar.getByRole('button', { name: 'Link' }).click();

        await expect(klass).toHaveClass(/status-not-exported/);
        expect(lastContent().slice(7)).toEqual([
            'PropertySet:\tMaterial\tI\tIfcSlab, IfcBeam',
            '\tMaterialnamn\tLabel\tMaterial',
            '\tKlass\tLabel\tKlass',
            '',
            '# Material for IfcWall only, split from the set on line 8 by IfcTester',
            'PropertySet:\tMaterial\tI\tIfcWall',
            '\tMaterialnamn\tLabel\tMaterial',
            '\tKlass\tLabel\tLjud',
            '',
        ]);

        // A set that reaches the wall through IfcElement cannot leave the wall out
        await ifcSide.locator('[data-row="byggpartner|ljudklass"] .prop').click();
        await revitSide.locator('.param', { hasText: 'Mark' }).locator('.prop').click();
        await expect(linkBar.getByLabel('Only IfcWall')).toHaveCount(0);
        await expect(linkBar.getByText(/IfcWall is in the set through IfcElement/)).toBeVisible();
        await linkBar.getByRole('button', { name: 'Cancel' }).click();

        // One set per class rewrites the whole file; single-class sets and comments stay
        await inspector.getByRole('button', { name: 'Discard' }).click();
        await inspector.getByRole('button', { name: 'One set per class' }).click();
        await expect(inspector.getByText('1 unsaved change')).toBeVisible();
        expect(lastContent()).toEqual([
            '# Test pset file',
            'PropertySet:\tPset_WallCommon\tI\tIfcWall',
            '\tFireRating\tLabel\tFire Rating',
            'PropertySet:\tByggpartner\tI\tIfcElement',
            '\tLjudklass\t\tLabel\tLjudklass',
            'PropertySet:\tSkikt\tI\tIFCWALL',
            '\tTjocklek\tLength\tWidth',
            'PropertySet:\tMaterial\tI\tIfcWall',
            '\tMaterialnamn\tLabel\tMaterial',
            '\tKlass\tLabel\tKlass',
            '',
            'PropertySet:\tMaterial\tI\tIfcSlab',
            '\tMaterialnamn\tLabel\tMaterial',
            '\tKlass\tLabel\tKlass',
            '',
            'PropertySet:\tMaterial\tI\tIfcBeam',
            '\tMaterialnamn\tLabel\tMaterial',
            '\tKlass\tLabel\tKlass',
            '',
        ]);

        // After it, a link needs no split
        await klass.locator('.prop').click();
        await revitSide.locator('.param', { hasText: 'Ljud' }).locator('.prop').click();
        await expect(linkBar.getByLabel('Only IfcWall')).toHaveCount(0);
        await expect(linkBar.getByText(/Line 10 of BS-Pset-A\.txt/)).toBeVisible();
    });
});

// ---- A Revit that reads the pset file (or the page's draft of it) like the add-in does ----

const PSET_PATH = 'C:\\psets\\BS-Pset-A.txt';
const PSET_FILE = [
    '# Test pset file',
    'PropertySet:\tPset_WallCommon\tI\tIfcWall',
    '\tFireRating\tLabel\tFire Rating',
    'PropertySet:\tByggpartner\tI\tIfcElement',
    '\tLjudklass\t\tLabel\tLjudklass',
    'PropertySet:\tSkikt\tI\tIFCWALL',
    '\tTjocklek\tLength\tWidth',
    'PropertySet:\tMaterial\tI\tIfcWall, IfcSlab, IfcBeam',
    '\tMaterialnamn\tLabel\tMaterial',
    '\tKlass\tLabel\tKlass',
    '',
].join('\r\n');

const splitLines = (text: string) => (text ?? '').split(/\r?\n/);

type Param = { name: string; scope: 'instance' | 'type'; builtIn: string | null; value: string; shared: boolean };

function parsePset(content: string, parameters: Param[]) {
    const mappings: unknown[] = [];
    let set: { name: string; entities: string[]; header: number } | null = null;
    splitLines(content).forEach((raw, index) => {
        const text = raw.trimStart();
        if (!text || text.startsWith('#')) return;
        const columns = text.split('\t').filter((c) => c.trim() !== '');
        if (columns[0] === 'PropertySet:') {
            set = { name: columns[1], entities: columns[3].split(/[,; ]+/).filter(Boolean), header: index + 1 };
            return;
        }
        if (!set) return;
        const reference = columns[2] ?? columns[0];
        const found = parameters.filter((p) => p.name === reference || (p.builtIn && `BuiltInParameter.${p.builtIn}` === reference));
        mappings.push({
            ...line(set.name, columns[0], reference, set.entities, found.map((p) => candidate(p.name, p.scope, p.value))),
            lineNumber: index + 1,
            headerLineNumber: set.header,
            dataType: columns[1],
        });
    });
    return mappings;
}

async function mockLinkingRevit(page: Page) {
    const seen = { bodies: [] as Record<string, unknown>[], saves: [] as { path: string; content: string; overwrite: boolean }[] };
    const json = (route: Route, body: unknown, status = 200) =>
        route.fulfill({ status, headers: CORS, contentType: 'application/json', body: JSON.stringify(body) });

    await page.route(`${REVIT}/**`, async (route) => {
        const request = route.request();
        const url = new URL(request.url());
        if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS });
        if (url.pathname === '/status') return json(route, { status: 'ok', connected: true, configsReady: true, version: '1.4.0', capabilities: ['writeback', 'element-inspector'] });
        if (url.pathname === '/ifc-configurations') return json(route, { configurations: ['IFC4 Reference View'] });
        if (url.pathname === '/pset-files/read') {
            if (url.searchParams.get('path') !== PSET_PATH) return json(route, { error: 'unexpected path' }, 404);
            return json(route, { path: PSET_PATH, content: PSET_FILE, bom: false });
        }
        if (url.pathname === '/pset-files/save') {
            const body = request.postDataJSON() as { path: string; content: string; overwrite: boolean };
            seen.saves.push(body);
            if (!body.overwrite) return json(route, { error: 'The file already exists', path: body.path }, 409);
            return json(route, { path: body.path, overwritten: true, bytes: body.content.length });
        }
        if (url.pathname === '/element-parameters') {
            const body = request.postDataJSON() as Record<string, unknown>;
            seen.bodies.push(body);
            const mark = body.globalId === W2 ? 'W2' : 'W1';
            const parameters: Param[] = [
                { name: 'Mark', scope: 'instance', builtIn: 'ALL_MODEL_MARK', value: mark, shared: false },
                { name: 'Comments', scope: 'instance', builtIn: 'ALL_MODEL_INSTANCE_COMMENTS', value: '', shared: false },
                { name: 'Ljud', scope: 'instance', builtIn: null, value: 'R35', shared: true },
                { name: 'Fire Rating', scope: 'type', builtIn: null, value: '', shared: true },
            ];
            const content = typeof body.psetFileContent === 'string' ? body.psetFileContent : PSET_FILE;
            return json(route, {
                found: true,
                message: null,
                elementId: 123456,
                elementName: 'Generic - 200mm',
                category: 'Walls',
                typeId: 777,
                typeName: 'Basic Wall: Generic - 200mm',
                parameters: parameters.map((p) => ({ ...p, group: 'Identity Data', storageType: 'string', hasValue: p.value !== '', readOnly: false })),
                mappings: parsePset(content, parameters),
                configuration: 'IFC4 Reference View',
                mappingFiles: [PSET_PATH],
                mappingNote: null,
                useTypePropertiesInInstancePsets: false,
                psetFile: PSET_PATH,
            });
        }
        return json(route, { error: `Not mocked: ${url.pathname}` }, 404);
    });
    return seen;
}
