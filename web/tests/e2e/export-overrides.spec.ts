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

const CONFIGURATION = 'IFC4 Reference View';
const W2 = '3M0lsbuhnETg9eDScRBzRN';
const DEFAULT_FOLDER = '\\\\bim-byggp1.hogerklick.bim\\H29\\BIM-tools\\pset';
const SETUP_PSET = 'P:\\BIM\\pset\\Byggpartner.txt';
const OVERRIDE = `${DEFAULT_FOLDER}\\Byggpartner H29.txt`;
const SETUP_WARNING = `The setup's property set file was not found: ${SETUP_PSET}. The export has no user-defined property sets.`;

type Seen = { exportBodies: Record<string, unknown>[]; resolveBodies: Record<string, unknown>[]; listedDirs: string[] };

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

/** The add-in's export endpoints, with a setup whose own property set file is on an unreachable drive. */
async function mockRevit(page: Page, capabilities: string[]): Promise<Seen> {
    const seen: Seen = { exportBodies: [], resolveBodies: [], listedDirs: [] };
    const json = (route: Route, body: unknown, status = 200) =>
        route.fulfill({ status, headers: CORS, contentType: 'application/json', body: JSON.stringify(body) });

    await page.route(`${REVIT}/**`, async (route) => {
        const request = route.request();
        const url = new URL(request.url());
        const path = url.pathname;

        if (request.method() === 'OPTIONS') return route.fulfill({ status: 200, headers: CORS });
        if (path === '/status') return json(route, { status: 'ok', connected: true, configsReady: true, version: '1.4.0', capabilities });
        if (path === '/ifc-configurations') return json(route, { configurations: [CONFIGURATION] });
        if (path === '/ifc-configuration-files') {
            expect(url.searchParams.get('name')).toBe(CONFIGURATION);
            return json(route, {
                psetFile: SETUP_PSET,
                psetFileExists: false,
                psetFileIsOverride: false,
                parameterMappingFile: null,
                parameterMappingFileExists: false,
                parameterMappingFileIsOverride: false,
                warning: SETUP_WARNING,
            });
        }
        if (path === '/pset-files') {
            const dir = url.searchParams.get('dir') ?? '';
            seen.listedDirs.push(dir);
            return json(route, {
                files: [
                    { name: 'Byggpartner H29.txt', path: `${dir}\\Byggpartner H29.txt`, modified: '2026-09-30T12:00:00Z' },
                    { name: 'ParameterMapping.txt', path: `${dir}\\ParameterMapping.txt`, modified: '2026-09-01T08:00:00Z' },
                ],
            });
        }
        if (path === '/export-ifc') {
            seen.exportBodies.push(request.postDataJSON());
            return json(route, { jobId: 'job-1', status: 'running' });
        }
        if (path === '/export-status/job-1') {
            const body = seen.exportBodies.at(-1) ?? {};
            const psetFile = typeof body.psetFile === 'string' ? body.psetFile : null;
            const exportFiles = {
                psetFile: psetFile ?? SETUP_PSET,
                psetFileExists: psetFile != null,
                psetFileIsOverride: psetFile != null,
                parameterMappingFile: null,
                parameterMappingFileExists: false,
                parameterMappingFileIsOverride: false,
                warning: psetFile ? null : SETUP_WARNING,
            };
            return json(route, { jobId: 'job-1', status: 'complete', exportFiles, warning: exportFiles.warning });
        }
        if (path === '/export-file/job-1') {
            return route.fulfill({
                status: 200,
                headers: { ...CORS, 'content-disposition': 'attachment; filename="Export_H29.ifc"' },
                contentType: 'application/octet-stream',
                body: read('model-ifc4.ifc'),
            });
        }
        if (path === '/resolve-parameters') {
            const body = request.postDataJSON() as { items: { key: string }[] };
            seen.resolveBodies.push(body);
            return json(route, {
                items: body.items.map((item) => ({ key: item.key, found: false, candidates: [], message: 'Not in this model' })),
                configuration: CONFIGURATION,
                mappingFiles: [OVERRIDE],
                mappingNote: null,
            });
        }
        return json(route, { error: `Not mocked: ${path}` }, 404);
    });
    return seen;
}

/** Opens the page as the add-in serves it with the IFC4 regression IDS active, and no model loaded. */
async function openWithIds(page: Page) {
    await page.goto('/?source=revit');
    await page.evaluate(
        async ({ ids }) => {
            const wasm = (await import('/src/modules/wasm/index.ts')).default;
            const IDS = await import('/src/modules/api/ids.svelte.ts');
            await wasm.init();
            const docId = 'e2e-export-overrides';
            IDS.Module.documents[docId] = (await wasm.openIDS(ids)) as never;
            IDS.setDocumentState(docId, { viewMode: 'viewer' });
            IDS.Module.activeDocument = docId;
        },
        { ids: read('specs-ifc4.ids') },
    );
    await expect(page.getByRole('heading', { name: 'IFC4 regression' })).toBeVisible();
}

test.describe('property set file override for the Revit export', () => {
    test('the export sends the chosen pset file, reports it and labels the audit', async ({ page }) => {
        const revit = await mockRevit(page, ['writeback', 'export-overrides']);
        await openWithIds(page);
        await page.locator('select.config-select').selectOption(CONFIGURATION);

        // The setup's own file is shown, flagged as missing
        await expect(page.getByText('Setup\'s property set file: Byggpartner.txt')).toBeVisible();
        await expect(page.getByText('not found', { exact: true })).toBeVisible();

        // The default folder is listed and offered
        await expect.poll(() => revit.listedDirs).toEqual([DEFAULT_FOLDER]);
        const psetSelect = page.getByRole('combobox', { name: 'Property set file', exact: true });
        await psetSelect.selectOption({ label: 'Byggpartner H29.txt' });
        await expect(page.getByRole('textbox', { name: 'Property set file path' })).toHaveValue(OVERRIDE);

        await page.getByRole('button', { name: 'Export IFC' }).click();
        await expect.poll(() => revit.exportBodies.length).toBe(1);
        expect(revit.exportBodies[0]).toEqual({ configuration: CONFIGURATION, psetFile: OVERRIDE });

        // The status line, the model label and the audit header all name the override
        await expect(page.locator('.overrides').getByRole('status')).toHaveText('Exported with override property set file Byggpartner H29.txt', { timeout: 120_000 });
        await expect(page.locator('.override-label')).toHaveText('Pset override: Byggpartner H29.txt');
        await expect(page.locator('.override-note')).toHaveText('Pset override: Byggpartner H29.txt. Not the delivery export.', { timeout: 120_000 });

        // Write-back resolves against the same files the export read
        const card = page.locator('.specification-card').filter({ has: page.getByRole('heading', { name: 'wall fire rating property', exact: true }) });
        await card.getByRole('heading', { name: 'wall fire rating property', exact: true }).click();
        await card.locator('button.facet-header').click();
        await card.getByRole('button', { name: 'Add fix for W2' }).click();
        await page.getByRole('button', { name: 'Review changes' }).click();
        await expect.poll(() => revit.resolveBodies.length).toBe(1);
        expect(revit.resolveBodies[0]).toMatchObject({ configuration: CONFIGURATION, psetFile: OVERRIDE });
        expect(revit.resolveBodies[0]).not.toHaveProperty('parameterMappingFile');
        expect((revit.resolveBodies[0].items as { globalId: string }[])[0].globalId).toBe(W2);
    });

    test('without an override the export reports the missing setup file', async ({ page }) => {
        const revit = await mockRevit(page, ['writeback', 'export-overrides']);
        await page.goto('/?source=revit');
        await page.locator('select.config-select').selectOption(CONFIGURATION);
        await page.getByRole('button', { name: 'Export IFC' }).click();

        await expect(page.locator('.overrides').getByRole('status')).toHaveText(SETUP_WARNING, { timeout: 120_000 });
        expect(revit.exportBodies).toEqual([{ configuration: CONFIGURATION }]);
        await expect(page.locator('.override-label')).toHaveCount(0);
    });

    test('an add-in without the capability gets no override controls', async ({ page }) => {
        const revit = await mockRevit(page, ['writeback']);
        await page.goto('/?source=revit');
        await page.locator('select.config-select').selectOption(CONFIGURATION);
        await expect(page.getByRole('button', { name: 'Export IFC' })).toBeEnabled();
        await expect(page.getByText('Property set file')).toHaveCount(0);
        expect(revit.listedDirs).toEqual([]);
    });
});
