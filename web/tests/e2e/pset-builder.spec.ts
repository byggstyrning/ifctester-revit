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
const CONFIGURATION = 'Projekt IFC4';
const SAVED = 'C:\\psets\\exists.txt';

// Same package-download escape hatch as audit.spec.ts, for sandboxes whose browser cannot reach PyPI.
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

type Body = Record<string, unknown>;
type Seen = { suggestionBodies: Body[]; saveBodies: Body[]; exportBodies: Body[]; files: Set<string> };

const suggestion = (parameter: string, scope: string, origin: string, counts: [number, number, number], extra: Body = {}) => ({
    parameter, scope, rule: 'name', origin, storageType: 'string', readOnly: false,
    elementsWithParameter: counts[0], elementsWithValue: counts[1], elementsScanned: counts[2], ...extra,
});

const SUGGESTIONS: Record<string, unknown[]> = {
    Brandklass: [suggestion('Brandklass', 'instance', 'shared', [10, 8, 12]), suggestion('Brandklass', 'type', 'shared', [2, 2, 12])],
    FireRating: [suggestion('Fire Rating', 'type', 'built-in', [10, 10, 10], { builtInParameter: 'FIRE_RATING' })],
    Volym: [suggestion('Volume', 'instance', 'built-in', [10, 10, 10], { readOnly: true, storageType: 'double', builtInParameter: 'HOST_VOLUME_COMPUTED' })],
    Rumsnamn: [suggestion('Rumsnamn', 'instance', 'project', [4, 4, 4])],
    Typbeteckning: [suggestion('Typbeteckning', 'type', 'shared', [1, 1, 6])],
    Bärande: [],
};

const MODEL_PARAMETERS = [
    { name: 'Bärande', scope: 'instance', origin: 'shared', storageType: 'yesno', dataType: 'boolean', readOnly: false, instanceCount: 12, typeCount: 0, elementCount: 12, withValueCount: 12, categories: ['Walls'] },
    { name: 'Structural', scope: 'instance', origin: 'built-in', builtInParameter: 'WALL_STRUCTURAL_SIGNIFICANT', storageType: 'yesno', dataType: 'boolean', readOnly: false, instanceCount: 12, typeCount: 0, elementCount: 12, withValueCount: 12, categories: ['Walls'] },
    { name: 'Fire Rating', scope: 'type', origin: 'built-in', builtInParameter: 'FIRE_RATING', storageType: 'string', dataType: 'string', readOnly: false, instanceCount: 0, typeCount: 10, elementCount: 10, withValueCount: 10, categories: ['Walls'] },
];

/** The add-in with the pset builder endpoints, or an older one without them. */
async function mockRevit(page: Page, capabilities: string[]): Promise<Seen> {
    const seen: Seen = { suggestionBodies: [], saveBodies: [], exportBodies: [], files: new Set([SAVED]) };
    const json = (route: Route, body: unknown, status = 200) =>
        route.fulfill({ status, headers: CORS, contentType: 'application/json', body: JSON.stringify(body) });

    await page.route(`${REVIT}/**`, async (route) => {
        const request = route.request();
        const path = new URL(request.url()).pathname;
        if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS });
        if (path === '/status') return json(route, { status: 'ok', connected: true, configsReady: true, version: '1.4.0', capabilities });
        if (path === '/ifc-configurations') return json(route, { configurations: [CONFIGURATION] });
        if (path === '/ifc-configuration-files') {
            return json(route, {
                psetFile: null, psetFileExists: false, psetFileIsOverride: false,
                parameterMappingFile: null, parameterMappingFileExists: false, parameterMappingFileIsOverride: false,
                warning: null, ifcVersion: 'IFC4',
            });
        }
        if (path === '/pset-files') return json(route, { files: [] });
        if (!capabilities.includes('pset-builder') && /^\/(model-parameters|pset-suggestions|pset-files\/save)$/.test(path)) {
            throw new Error(`The page called ${path} on an add-in without the pset builder`);
        }
        if (path === '/model-parameters') {
            return json(route, { document: 'Projekt A', elementCount: 40, typeCount: 9, elapsedMs: 12, parameters: MODEL_PARAMETERS, message: null });
        }
        if (path === '/pset-suggestions') {
            const body = request.postDataJSON() as { items: { key: string; name: string }[] };
            seen.suggestionBodies.push(body);
            return json(route, {
                elementCount: 40,
                scopeMethod: 'Elements were matched to the entities by their IFC class.',
                elapsedMs: 20,
                items: body.items.map((item) => ({ key: item.key, scope: 'entities', elementsScanned: 12, note: null, suggestions: SUGGESTIONS[item.name] ?? [] })),
                message: null,
            });
        }
        if (path === '/pset-files/save') {
            const body = request.postDataJSON() as { path?: string; name?: string; overwrite?: boolean };
            seen.saveBodies.push(body);
            const target = body.path ?? `C:\\Users\\u\\AppData\\Local\\IfcTesterRevit\\psets\\${body.name}`;
            if (seen.files.has(target) && !body.overwrite) {
                return json(route, { error: `The file already exists: ${target}. Send overwrite: true to replace it.`, path: target }, 409);
            }
            const overwritten = seen.files.has(target);
            seen.files.add(target);
            return json(route, { path: target, overwritten, bytes: 100 });
        }
        if (path === '/export-ifc') {
            seen.exportBodies.push(request.postDataJSON());
            return json(route, { jobId: 'job-1', status: 'running' });
        }
        if (path === '/export-status/job-1') return json(route, { jobId: 'job-1', status: 'failed', error: 'Mocked export', exportFiles: null, warning: null });
        return json(route, { error: `Not mocked: ${path}` }, 404);
    });
    return seen;
}

async function openWithIds(page: Page) {
    await page.goto('/?source=revit');
    await page.evaluate(
        async ({ ids }) => {
            const wasm = (await import('/src/modules/wasm/index.ts')).default;
            const IDS = await import('/src/modules/api/ids.svelte.ts');
            await wasm.init();
            const docId = 'e2e-pset-builder';
            IDS.Module.documents[docId] = (await wasm.openIDS(ids)) as never;
            IDS.setDocumentState(docId, { viewMode: 'viewer' });
            IDS.Module.activeDocument = docId;
        },
        { ids: read('pset-builder.ids') },
    );
    await expect(page.getByRole('heading', { name: 'Pset builder test' })).toBeVisible();
}

const row = (page: Page, key: string) => page.locator(`tr[data-row="${key}"]`);
const preview = (page: Page) => page.getByLabel('Pset file preview');

test.describe('pset builder', () => {
    test('generator: schema casing, instance and type sets, fallbacks, data types, round trip', async ({ page }) => {
        await page.goto('/');
        const result = await page.evaluate(
            async ({ ids }) => {
                const wasm = (await import('/src/modules/wasm/index.ts')).default;
                const P = await import('/src/modules/psetBuilder/psetFile.ts');
                await wasm.init();
                const ifc2x3 = P.makeSchema('IFC2X3', await wasm.getEntityTree('IFC2X3'));
                const ifc4 = P.makeSchema('IFC4', await wasm.getEntityTree('IFC4'));
                const doc = await wasm.openIDS(ids);
                const { rows, notCovered, ifcVersions } = P.rowsFromIds(doc as never, ifc2x3);
                type Row = { name: string; dataType: string; dataTypeNote: string | null; entities: string[]; specs: string[]; parameters: string[] };
                const byName = (name: string): Row => {
                    const found = rows.find((r: Row) => r.name === name);
                    if (!found) throw new Error(`No row ${name}`);
                    return found;
                };
                const before = rows.map((r: Row) => ({ name: r.name, dataType: r.dataType, note: r.dataTypeNote, entities: [...r.entities], specs: [...r.specs] }));

                byName('Brandklass').parameters = ['Brandklass'];
                byName('Volym').parameters = ['Volume'];
                P.switchScope(byName('Typbeteckning'), 'type', ifc2x3);
                byName('Typbeteckning').parameters = ['Type Mark', 'Type Name'];
                byName('Rumsnamn').parameters = ['Name', 'BuiltInParameter.ROOM_NAME'];
                P.switchScope(byName('FireRating'), 'type', ifc2x3);
                byName('FireRating').parameters = ['Fire Rating'];
                const text = P.generatePsetFile(rows, { title: 'Pset builder test', version: '2', date: '2026-10-01', schema: ifc2x3 });

                // The file read back into fresh rows gives the same file
                const again = P.rowsFromIds(doc as never, ifc2x3).rows;
                const applied = P.applyParsed(again, P.parsePsetFile(text), ifc2x3);
                const roundTrip = P.generatePsetFile(again, { title: 'Pset builder test', version: '2', date: '2026-10-01', schema: ifc2x3 });

                return {
                    before, notCovered, ifcVersions, text, roundTrip, applied,
                    doorType4: P.typeEntityOf('IFCDOOR', ifc4),
                    doorType2: P.typeEntityOf('IfcDoor', ifc2x3),
                    standardCaseType: P.typeEntityOf('IFCWALLSTANDARDCASE', ifc2x3),
                    storeyType: P.typeEntityOf('IfcBuildingStorey', ifc2x3),
                    proxy: P.canonicalEntity('IFCBUILDINGELEMENTPROXY', ifc2x3),
                    bogus: P.canonicalEntity('IFCWALLTHING', ifc2x3),
                    dataTypes: ['IFCLABEL', 'IFCPOSITIVELENGTHMEASURE', 'IFCTHERMALTRANSMITTANCEMEASURE', 'IFCAREAMEASURE', 'IFCCOUNTMEASURE', 'IFCREAL', 'IFCIDENTIFIER', 'IFCDATE', 'IFCPHMEASURE', 'IFCBINARY', undefined].map((t) => P.dataTypeFromIds(t)),
                    issues: P.entityIssues({ ...byName('FireRating'), scope: 'type', entities: ['IfcWall', 'IfcWallThing'] }, ifc2x3),
                };
            },
            { ids: read('pset-builder.ids') },
        );

        expect(result.before).toEqual([
            { name: 'Brandklass', dataType: 'Label', note: null, entities: ['IfcWall', 'IfcWallStandardCase', 'IfcDoor'], specs: ['Walls', 'Doors'] },
            { name: 'Bärande', dataType: 'Boolean', note: null, entities: ['IfcWall', 'IfcWallStandardCase'], specs: ['Walls'] },
            { name: 'Volym', dataType: 'Volume', note: null, entities: ['IfcWall', 'IfcWallStandardCase'], specs: ['Walls'] },
            { name: 'Typbeteckning', dataType: 'Label', note: 'The IDS gives no data type; Label assumed', entities: ['IfcDoor'], specs: ['Doors'] },
            { name: 'Rumsnamn', dataType: 'Text', note: null, entities: ['IfcSpace'], specs: ['Spaces'] },
            { name: 'FireRating', dataType: 'Label', note: null, entities: ['IfcWall', 'IfcWallStandardCase'], specs: ['Walls'] },
        ]);
        expect(result.ifcVersions).toEqual(['IFC2X3', 'IFC4']);
        expect(result.notCovered.map((n: { spec: string; what: string }) => `${n.spec}: ${n.what}`)).toEqual([
            'Walls: Requirement: attribute Name',
            'Walls: Requirement: material',
            'Doors: Applicability: predefined type DOOR',
            'Doors: Requirement: classification',
            'Spaces: Applicability: property',
            'Spaces: Requirement: property Projekt.(restriction)',
        ]);

        const blocks = result.text.split('\r\n').filter((line: string) => !line.startsWith('# ') && line !== '#');
        expect(blocks).toEqual([
            '',
            'PropertySet:\tProjekt\tI\tIfcWall, IfcWallStandardCase, IfcDoor',
            '\tBrandklass\tLabel\tBrandklass',
            '',
            'PropertySet:\tProjekt\tI\tIfcWall, IfcWallStandardCase',
            '#\tBärande\tBoolean\t(no Revit parameter)',
            '\tVolym\tVolume\tVolume',
            '',
            'PropertySet:\tProjekt\tT\tIfcDoorStyle',
            '\tTypbeteckning\tLabel\tType Mark',
            '\tTypbeteckning\tLabel\tType Name',
            '',
            'PropertySet:\tProjekt\tI\tIfcSpace',
            '\tRumsnamn\tText\tName',
            '\tRumsnamn\tText\tBuiltInParameter.ROOM_NAME',
            '',
            'PropertySet:\tPset_WallCommon\tT\tIfcWallType',
            '\tFireRating\tLabel\tFire Rating',
            '',
        ]);
        expect(result.text).toContain('# Generated from Pset builder test (version 2) on 2026-10-01 by ifc-tester (pset builder), IFC2X3 entity names.');
        expect(result.text).not.toMatch(/[^\r]\n/);
        expect(result.roundTrip).toBe(result.text);
        expect(result.applied).toEqual({ updated: 6, added: 0 });

        expect([result.doorType4, result.doorType2, result.standardCaseType, result.storeyType]).toEqual(['IfcDoorType', 'IfcDoorStyle', 'IfcWallType', null]);
        expect([result.proxy, result.bogus]).toEqual(['IfcBuildingElementProxy', null]);
        expect(result.dataTypes.map((d: { dataType: string; note: string | null }) => `${d.dataType}${d.note ? '*' : ''}`)).toEqual([
            'Label', 'PositiveLength', 'ThermalTransmittance', 'Area', 'Count', 'Real', 'Identifier', 'Date', 'PH', 'Label*', 'Label*',
        ]);
        expect(result.issues).toEqual(['IfcWall is an occurrence: a type parameter needs IfcWallType', 'IfcWallThing is not an entity of IFC2X3']);
    });

    test('rows from the IDS, suggestions, editing, save and use for the next export', async ({ page }) => {
        const revit = await mockRevit(page, ['writeback', 'export-overrides', 'pset-builder']);
        await openWithIds(page);
        // The setup's schema (IFC4) is the builder's default
        await page.locator('select.config-select').selectOption(CONFIGURATION);
        await page.getByRole('button', { name: 'Pset builder', exact: true }).click();
        await expect(page.getByRole('heading', { name: 'Pset builder', exact: true })).toBeVisible();
        await expect(page.getByLabel('IFC schema')).toHaveValue('IFC4');
        await expect(page.getByText('Not covered (6)')).toBeVisible();
        await expect(page.locator('tr[data-row]')).toHaveCount(6);
        await expect(page.locator('tr.unmapped')).toHaveCount(6);
        await expect(page.getByText('6 unmapped')).toBeVisible();

        await page.getByRole('button', { name: 'Suggest from model' }).click();
        await expect.poll(() => revit.suggestionBodies.length).toBe(1);
        const items = (revit.suggestionBodies[0].items as { key: string; propertySet: string; name: string; entities: string[] }[]);
        expect(items.find((i) => i.name === 'Brandklass')).toEqual({ key: 'Projekt|Brandklass', propertySet: 'Projekt', name: 'Brandklass', entities: ['IfcWall', 'IfcWallStandardCase', 'IfcDoor'] });

        await expect(page.getByLabel('Revit parameter of Brandklass')).toHaveValue('Brandklass');
        await expect(row(page, 'Projekt|Brandklass').getByText('10/12 (8 with value)')).toBeVisible();
        // A type parameter moves the row to the type entity
        await expect(page.getByLabel('Scope of FireRating')).toHaveText('T');
        await expect(page.getByLabel('Entities of FireRating')).toHaveValue('IfcWallType');
        await expect(page.getByLabel('Entities of Typbeteckning')).toHaveValue('IfcDoorType');
        await expect(page.locator('tr.unmapped')).toHaveCount(1);
        await expect(row(page, 'Projekt|Bärande')).toHaveClass(/unmapped/);
        await expect(page.getByLabel('Warnings for Rumsnamn')).toHaveAttribute('title', /project parameter: Revit 2025 and 2026 export these/);
        await expect(page.getByLabel('Warnings for Volym')).toHaveAttribute('title', /read-only/);
        await expect(page.getByLabel('Warnings for Typbeteckning')).toHaveAttribute('title', /Low coverage: 1 of 6/);
        await expect(page.getByLabel('Warnings for Brandklass')).toHaveAttribute('title', /2 of 10 elements have no value/);
        await expect(page.getByText('40 elements and 9 types of Projekt A')).toBeVisible();

        // Map the unmapped row by hand with a fallback, and go back to IFC2X3
        await page.getByLabel('Revit parameter of Bärande').fill('Bärande');
        await page.getByLabel('Revit parameter of Bärande').press('Tab');
        await row(page, 'Projekt|Bärande').getByRole('button', { name: '+ fallback' }).click();
        await page.getByLabel('Fallback 1 of Bärande').fill('Structural');
        await page.getByLabel('Fallback 1 of Bärande').press('Tab');
        await expect(page.locator('tr.unmapped')).toHaveCount(0);
        await page.getByLabel('IFC schema').selectOption('IFC2X3');
        await expect(page.getByLabel('Entities of Typbeteckning')).toHaveValue('IfcDoorStyle');
        await page.getByLabel('Scope of Volym').click();
        await expect(page.getByLabel('Entities of Volym')).toHaveValue('IfcWallType');
        await expect(page.getByLabel('Warnings for Bärande')).toHaveCount(0);

        const text = (await preview(page).textContent())?.replace(/\r/g, '');
        expect(text).toContain('PropertySet:\tProjekt\tI\tIfcWall, IfcWallStandardCase\n\tBärande\tBoolean\tBärande\n\tBärande\tBoolean\tStructural\n');
        expect(text).toContain('PropertySet:\tProjekt\tT\tIfcWallType\n\tVolym\tVolume\tVolume\n');
        expect(text).toContain('PropertySet:\tPset_WallCommon\tT\tIfcWallType\n\tFireRating\tLabel\tFire Rating\n');
        expect(text).toContain('PropertySet:\tProjekt\tT\tIfcDoorStyle\n\tTypbeteckning\tLabel\tTypbeteckning\n');

        // Save to a file that exists: asked first, then replaced
        await page.getByLabel('Save path').fill(SAVED);
        await page.getByRole('button', { name: 'Save', exact: true }).click();
        await expect(page.getByRole('alert').filter({ hasText: 'already exists' })).toBeVisible();
        await page.getByRole('button', { name: 'Overwrite' }).click();
        await expect.poll(() => revit.saveBodies.length).toBe(2);
        expect(revit.saveBodies.map((b) => [b.path, b.overwrite])).toEqual([[SAVED, false], [SAVED, true]]);
        const content = String(revit.saveBodies[1].content);
        expect(content).toContain('\r\nPropertySet:\tProjekt\tI\tIfcWall, IfcWallStandardCase\r\n\tBärande\tBoolean\tBärande\r\n');
        expect(content).not.toMatch(/[^\r]\n/);

        // "Use for next export" saves again (its own file: no question) and sets the export override
        await page.getByRole('button', { name: 'Use for next export' }).click();
        await expect.poll(() => revit.saveBodies.length).toBe(3);
        expect([revit.saveBodies[2].path, revit.saveBodies[2].overwrite]).toEqual([SAVED, true]);
        await expect(page.getByText('Next export uses exists.txt')).toBeVisible();
        await expect(page.getByRole('textbox', { name: 'Property set file path' })).toHaveValue(SAVED);
        await page.getByRole('button', { name: 'Export IFC' }).click();
        await expect.poll(() => revit.exportBodies.length).toBe(1);
        expect(revit.exportBodies[0]).toEqual({ configuration: CONFIGURATION, psetFile: SAVED });

        // The work is kept for this IDS
        await openWithIds(page);
        await page.getByRole('button', { name: 'Pset builder', exact: true }).click();
        await expect(page.getByLabel('Fallback 1 of Bärande')).toHaveValue('Structural');
        await expect(page.getByLabel('IFC schema')).toHaveValue('IFC2X3');
        await expect(page.getByLabel('Save path')).toHaveValue(SAVED);
    });

    test('an add-in without the pset builder: drafting, opening a file and downloading work', async ({ page }) => {
        const errors: string[] = [];
        page.on('pageerror', (err) => errors.push(err.message));
        page.on('console', (msg) => {
            if (msg.type() === 'error') errors.push(msg.text());
        });
        await mockRevit(page, ['writeback', 'export-overrides']);
        await openWithIds(page);
        await page.getByRole('button', { name: 'Pset builder', exact: true }).click();
        await expect(page.getByRole('heading', { name: 'Pset builder', exact: true })).toBeVisible();
        await expect(page.getByText('Drafting: suggestions and saving next to Revit need')).toBeVisible();
        await expect(page.getByRole('button', { name: 'Suggest from model' })).toHaveCount(0);
        await expect(page.getByRole('button', { name: 'Save', exact: true })).toHaveCount(0);
        await expect(page.getByRole('button', { name: 'Use for next export' })).toHaveCount(0);
        await expect(page.getByLabel('IFC schema')).toHaveValue('IFC2X3');

        // An existing pset file: its mapping fills the matching row, a property the IDS lacks is added
        const chooser = page.waitForEvent('filechooser');
        await page.getByRole('button', { name: 'Open pset file...' }).click();
        await (await chooser).setFiles({
            name: 'old.txt',
            mimeType: 'text/plain',
            buffer: Buffer.from('# old file\r\nPropertySet:\tProjekt\tT\tIfcDoorStyle\r\n\tTypbeteckning\tLABEL\tType Mark\r\n\tTypbeteckning\tLABEL\tType Name\r\n\tMMI\tLABEL\tMMI\r\n', 'utf8'),
        });
        await expect(page.getByLabel('Revit parameter of Typbeteckning')).toHaveValue('Type Mark');
        await expect(page.getByLabel('Fallback 1 of Typbeteckning')).toHaveValue('Type Name');
        await expect(page.getByLabel('Scope of Typbeteckning')).toHaveText('T');
        await expect(row(page, 'Projekt|MMI').getByText('file')).toBeVisible();

        await page.getByLabel('Revit parameter of Brandklass').fill('Brandklass');
        await page.getByLabel('Revit parameter of Brandklass').press('Tab');
        const download = page.waitForEvent('download');
        await page.getByRole('button', { name: 'Download' }).click();
        const file = await download;
        expect(file.suggestedFilename()).toBe('Pset builder test pset.txt');
        const text = readFileSync(await file.path(), 'utf8');
        expect(text).toContain('PropertySet:\tProjekt\tT\tIfcDoorStyle\r\n\tTypbeteckning\tLabel\tType Mark\r\n\tTypbeteckning\tLabel\tType Name\r\n\tMMI\tLabel\tMMI\r\n');
        expect(text).toContain('PropertySet:\tProjekt\tI\tIfcWall, IfcWallStandardCase, IfcDoor\r\n\tBrandklass\tLabel\tBrandklass\r\n');
        expect(text.charCodeAt(0)).toBe('#'.charCodeAt(0));
        expect(errors).toEqual([]);
    });
});
